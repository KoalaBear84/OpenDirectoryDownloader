using Microsoft.Extensions.Logging;
using OpenDirectoryDownloader.Models;
using OpenDirectoryDownloader.Urls;

namespace OpenDirectoryDownloader.Parsing.Listings;

/// <summary>
/// The clean-up every parse result goes through: strips sort parameters, fragments, entries outside the directory,
/// well-known system directories and symlink loops. The order of the steps matters (fragments are moved to the end
/// of the list, before the parent check sees them).
/// </summary>
public static class ListingPostProcessor
{
	private static readonly HashSet<string> UsrSubdirectoriesToRemove = ["bin", "include", "lib", "lib32", "share", "src"];

	private static readonly HashSet<string> GoodUriSchemes =
	[
		Constants.UriScheme.Https,
		Constants.UriScheme.Http,
		Constants.UriScheme.Ftp,
		Constants.UriScheme.Ftps
	];

	private static readonly HashSet<string> SkipHosts =
	[
		Constants.GoogleDriveDomain,
		Constants.BlitzfilesTechDomain
	];

	public static void CheckParsedResults(WebDirectory webDirectory, string baseUrl, bool checkParents, ILogger logger = null)
	{
		if (webDirectory.Subdirectories.Count == 0 && webDirectory.Files.Count == 0)
		{
			return;
		}

		foreach (WebDirectory webDirectorySub in webDirectory.Subdirectories)
		{
			webDirectorySub.Url = StripUrl(webDirectorySub.Url);
		}

		if (webDirectory.Uri.Scheme != Constants.UriScheme.Ftp && webDirectory.Uri.Scheme != Constants.UriScheme.Ftps)
		{
			CleanFragments(webDirectory);
		}

		if (checkParents)
		{
			CheckParents(webDirectory, baseUrl);
		}

		CleanDynamicEntries(webDirectory);

		CheckSymlinks(webDirectory, logger);
	}

	/// <summary>Remove common linux folders</summary>
	private static void CleanDynamicEntries(WebDirectory webDirectory)
	{
		if (webDirectory.Files.Any(f => f.FileName == "core"))
		{
			webDirectory.Files = [.. webDirectory.Files.Where(f => f.FileName != "core")];
		}

		switch (webDirectory.Name)
		{
			case "dev":
				ClearWhen(webDirectory, subdirectory => subdirectory.Name is "bus" or "cpu" or "disk");
				break;
			case "lib":
				ClearWhen(webDirectory, subdirectory => subdirectory.Name is "firmware" or "modules");
				break;
			case "proc":
				ClearWhen(webDirectory, subdirectory => subdirectory.Name.All(char.IsDigit));
				break;
			case "run":
				ClearWhen(webDirectory, subdirectory => subdirectory.Name is "sudo" or "user");
				break;
			case "snap":
				ClearWhen(webDirectory, subdirectory => subdirectory.Name == "bin");
				break;
			case "sys":
				ClearWhen(webDirectory, subdirectory => subdirectory.Name is "dev" or "kernel");
				break;
			case "usr":
				if (webDirectory.Subdirectories.Any(d => UsrSubdirectoriesToRemove.Contains(d.Name)))
				{
					webDirectory.Subdirectories = [.. webDirectory.Subdirectories.Where(d => !UsrSubdirectoriesToRemove.Contains(d.Name))];
				}

				break;
			case "var":
				ClearWhen(webDirectory, subdirectory => subdirectory.Name is "lib" or "run");
				break;
		}
	}

	private static void ClearWhen(WebDirectory webDirectory, Func<WebDirectory, bool> isSystemDirectory)
	{
		if (webDirectory.Subdirectories.Any(isSystemDirectory))
		{
			webDirectory.Subdirectories.Clear();
			webDirectory.Files.Clear();
		}
	}

	private static void CheckParents(WebDirectory webDirectory, string baseUrl)
	{
		Uri baseUri = new(baseUrl);
		UriParts baseParts = new(baseUri);

		bool IsBadSubdirectory(WebDirectory d)
		{
			Uri uri = new(d.Url);

			return !GoodUriSchemes.Contains(uri.Scheme) || uri.Host != baseUri.Host || SkipHosts.Contains(uri.Host) || !SameHostAndDirectoryDirectory(baseParts, new UriParts(uri));
		}

		bool IsBadFile(WebFile f)
		{
			Uri uri = new(f.Url);

			return !GoodUriSchemes.Contains(uri.Scheme) || uri.Host != baseUri.Host || SkipHosts.Contains(uri.Host) || !SameHostAndDirectoryFile(new UriParts(uri), baseParts);
		}

		// One pass, one Uri parse per item
		List<WebDirectory> goodSubdirectories = KeepGood(webDirectory.Subdirectories, IsBadSubdirectory);

		if (goodSubdirectories is not null)
		{
			webDirectory.Subdirectories = goodSubdirectories;
		}

		List<WebFile> goodFiles = KeepGood(webDirectory.Files, IsBadFile);

		if (goodFiles is not null)
		{
			webDirectory.Files = goodFiles;
		}
	}

	/// <summary>The items for which isBad is false, in order - or null when none is bad, so callers can leave the original list alone.</summary>
	private static List<T> KeepGood<T>(List<T> items, Func<T, bool> isBad)
	{
		List<T> good = null;

		for (int index = 0; index < items.Count; index++)
		{
			if (isBad(items[index]))
			{
				// First bad item: everything before it was good
				good ??= items.GetRange(0, index);
			}
			else
			{
				good?.Add(items[index]);
			}
		}

		return good;
	}

	/// <summary>A Uri plus the strings SameHostAndDirectory* derive from it, computed once instead of on every comparison.</summary>
	private sealed class UriParts(Uri uri)
	{
		public string Host { get; } = uri.Host;
		public string LocalPath { get; } = uri.LocalPath;
		public string Text { get; } = uri.ToString();

		private string _localPathWithoutDefaultFilenames;
		public string LocalPathWithoutDefaultFilenames => _localPathWithoutDefaultFilenames ??= ReplaceCommonDefaultFilenames(LocalPath);

		private string _fileName;
		public string FileName => _fileName ??= Path.GetFileName(Text);

		private string _withoutFileName;
		/// <summary>LocalPath minus the file name, for the 'base' side of the file comparison</summary>
		public string LocalPathWithoutFileName => _withoutFileName ??= string.IsNullOrWhiteSpace(FileName) ? LocalPath : LocalPath.Replace(FileName, string.Empty);

		private string _defaultFilenamesAndFileNameRemoved;
		/// <summary>LocalPath minus default filenames and the file name, for the 'check' side of the file comparison</summary>
		public string LocalPathWithoutDefaultFilenamesAndFileName => _defaultFilenamesAndFileNameRemoved ??= string.IsNullOrWhiteSpace(FileName) ? LocalPathWithoutDefaultFilenames : LocalPathWithoutDefaultFilenames.Replace(FileName, string.Empty);
	}

	/// <summary>Same logic as the public overload, on precomputed parts. Note CheckParents deliberately passes the item first and the page second.</summary>
	private static bool SameHostAndDirectoryFile(UriParts baseUri, UriParts checkUri)
	{
		return baseUri.Text == checkUri.Text || (baseUri.Host == checkUri.Host && (
			checkUri.LocalPath.StartsWith(baseUri.LocalPath) ||
			checkUri.LocalPath.StartsWith(baseUri.LocalPathWithoutFileName) ||
			baseUri.LocalPath.StartsWith(checkUri.LocalPathWithoutDefaultFilenamesAndFileName)
		));
	}

	private static bool SameHostAndDirectoryDirectory(UriParts baseUri, UriParts checkUri)
	{
		if (baseUri.Text == checkUri.Text)
		{
			return true;
		}

		if (baseUri.Host != checkUri.Host)
		{
			return false;
		}

		return checkUri.LocalPathWithoutDefaultFilenames.StartsWith(baseUri.LocalPathWithoutDefaultFilenames);
	}

	private static void CleanFragments(WebDirectory webDirectory)
	{
		// Directories
		if (webDirectory.Subdirectories.Any(wd => wd.Url.Contains('#')))
		{
			List<WebDirectory> directoriesWithFragments = webDirectory.Subdirectories.Where(wd => wd.Url.Contains('#')).ToList();

			webDirectory.Subdirectories = [.. webDirectory.Subdirectories.Where(wd => !wd.Url.Contains('#'))];

			foreach (WebDirectory directoryWithFragments in directoriesWithFragments)
			{
				Uri uri = new(directoryWithFragments.Url);
				directoryWithFragments.Url = uri.GetLeftPart(UriPartial.Query);

				if (webDirectory.Subdirectories.All(wd => wd.Url != directoryWithFragments.Url))
				{
					webDirectory.Subdirectories.Add(directoryWithFragments);
				}
			}
		}

		// Files
		if (!webDirectory.Files.Any(wf => wf.Url.Contains('#')))
		{
			return;
		}

		List<WebFile> filesWithFragments = webDirectory.Files.Where(wf => wf.Url.Contains('#')).ToList();

		webDirectory.Files = [.. webDirectory.Files.Where(wf => !wf.Url.Contains('#'))];

		foreach (WebFile fileWithFragment in filesWithFragments)
		{
			Uri uri = new(fileWithFragment.Url);
			fileWithFragment.Url = uri.GetLeftPart(UriPartial.Query);

			if (webDirectory.Files.All(wf => wf.Url != fileWithFragment.Url))
			{
				webDirectory.Files.Add(fileWithFragment);
			}
		}
	}

	/// <summary>Removes the default sort parameters some servers add to every link.</summary>
	public static string StripUrl(string url)
	{
		// Only exactly two query parameters can be a sort pair; most URLs have none, so skip the parsing for them
		if (url.IndexOfAny(['&', ';']) < 0)
		{
			return url;
		}

		UrlEncodingParser urlEncodingParser = new(url);

		if (urlEncodingParser.Count != 2)
		{
			return url;
		}

		if (urlEncodingParser.Get("C") != null && urlEncodingParser.Get("O") != null)
		{
			// Remove the default C (column) and O (order) parameters
			urlEncodingParser.Remove("C");
			urlEncodingParser.Remove("O");
		}

		if (urlEncodingParser.Get("sort") != null && urlEncodingParser.Get("order") != null)
		{
			// Remove the default sort and order parameters for https://github.com/TheWaWaR/simple-http-server/
			urlEncodingParser.Remove("sort");
			urlEncodingParser.Remove("order");
		}

		return urlEncodingParser.ToString();
	}

	private static void CheckSymlinks(WebDirectory webDirectory, ILogger logger)
	{
		WebDirectory parentWebDirectory = webDirectory.ParentDirectory;

		// Computed once (direct children only) and reused for every ancestor level below
		UInt128? webDirectoryFingerprint = null;

		for (int level = 1; level <= 8; level++)
		{
			if (webDirectory.Uri.Segments.Length <= level || parentWebDirectory == null)
			{
				continue;
			}

			if (webDirectory.Subdirectories.Count != 0 || webDirectory.Files.Count != 0)
			{
				webDirectoryFingerprint ??= webDirectory.ComputeContentFingerprint();

				if (CheckDirectoryTheSame(webDirectory, webDirectoryFingerprint, parentWebDirectory))
				{
					logger?.LogError("Possible virtual directory or symlink detected (level {level})! SKIPPING! Url: {url}", level, webDirectory.Url);

					webDirectory.Subdirectories = [];
					webDirectory.Files = [];
					webDirectory.Error = true;
					break;
				}
			}

			parentWebDirectory = parentWebDirectory?.ParentDirectory;
		}
	}

	private static bool CheckDirectoryTheSame(WebDirectory webDirectory, UInt128? webDirectoryFingerprint, WebDirectory parentWebDirectory)
	{
		if (webDirectory.Files.Count != parentWebDirectory.Files.Count ||
		    webDirectory.Subdirectories.Count != parentWebDirectory.Subdirectories.Count)
		{
			return false;
		}

		// Ancestors are already finished by the time a descendant is parsed, so their fingerprint is normally cached
		UInt128 parentFingerprint = parentWebDirectory.ContentFingerprint ?? parentWebDirectory.ComputeContentFingerprint();

		return webDirectoryFingerprint == parentFingerprint;
	}

	public static string ReplaceCommonDefaultFilenames(string input)
	{
		input = input.Replace("index.shtml", string.Empty);
		input = input.Replace("index.php", string.Empty);
		input = input.Replace("DirectoryList.asp", string.Empty);

		return input;
	}

	/// <summary>Whether <paramref name="checkUri"/> is (in) the directory of <paramref name="baseUri"/>, for a file.</summary>
	public static bool SameHostAndDirectoryFile(Uri baseUri, Uri checkUri)
	{
		string checkUrlWithoutFileName = checkUri.LocalPath;
		checkUrlWithoutFileName = ReplaceCommonDefaultFilenames(checkUrlWithoutFileName);
		string checkUrlFileName = Path.GetFileName(checkUri.ToString());

		if (!string.IsNullOrWhiteSpace(checkUrlFileName))
		{
			checkUrlWithoutFileName = checkUrlWithoutFileName.Replace(checkUrlFileName, string.Empty);
		}

		string baseUrlWithoutFileName = baseUri.LocalPath;
		string baseUrlFileName = Path.GetFileName(baseUri.ToString());

		if (!string.IsNullOrWhiteSpace(baseUrlFileName))
		{
			baseUrlWithoutFileName = baseUri.LocalPath.Replace(baseUrlFileName, string.Empty);
		}

		return baseUri.ToString() == checkUri.ToString() || (baseUri.Host == checkUri.Host && (
			checkUri.LocalPath.StartsWith(baseUri.LocalPath) ||
			checkUri.LocalPath.StartsWith(baseUrlWithoutFileName) ||
			baseUri.LocalPath.StartsWith(checkUrlWithoutFileName)
		));
	}

	/// <summary>Whether <paramref name="checkUri"/> is (below) the directory <paramref name="baseUri"/>.</summary>
	public static bool SameHostAndDirectoryDirectory(Uri baseUri, Uri checkUri)
	{
		if (baseUri.ToString() == checkUri.ToString())
		{
			return true;
		}

		if (baseUri.Host != checkUri.Host)
		{
			return false;
		}

		return ReplaceCommonDefaultFilenames(checkUri.LocalPath).StartsWith(ReplaceCommonDefaultFilenames(baseUri.LocalPath));
	}
}
