using OpenDirectoryDownloader.Shared.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace OpenDirectoryDownloader.Tests;

public class DirectoryParserTests
{
	private static readonly Regex TestMethodRegex = new Regex(@"<Test(\w+)Async>");

	public static string GetSample()
	{
		// Ugly, but it works
		string fileName = TestMethodRegex.Match(new StackTrace().GetFrame(1).GetMethod().DeclaringType.Name).Groups[1].Value;

		return File.ReadAllText($"Samples{Path.DirectorySeparatorChar}{fileName}.html.dat");
	}

	public static void CleanWebDirectory(WebDirectory webDirectory, Uri testedUri)
	{
		List<WebDirectory> newWebDirectories = new List<WebDirectory>();

		foreach (WebDirectory subdirectory in webDirectory.Subdirectories)
		{
			if (subdirectory.Uri.Host == testedUri.Host && subdirectory.Uri.LocalPath.StartsWith(testedUri.LocalPath))
			{
				newWebDirectories.Add(subdirectory);
			}
		}

		if (newWebDirectories.Count != webDirectory.Subdirectories.Count)
		{
			webDirectory.Subdirectories.Clear();
			webDirectory.Subdirectories.AddRange(newWebDirectories);
		}

		webDirectory.Files.Where(f =>
		{
			Uri uri = new Uri(f.Url);
			return (uri.Scheme != "https" && uri.Scheme != "http" && uri.Scheme != "ftp") || (uri.Host != testedUri.Host || !uri.LocalPath.StartsWith(testedUri.LocalPath));
		}).ToList().ForEach(wd => webDirectory.Files.Remove(wd));
	}

	public static async Task<WebDirectory> ParseHtml(string html, string url = "http://localhost/", bool checkParents = true)
	{
		return await DirectoryParser.ParseHtml(new WebDirectory(null) { Url = url }, html, checkParents: checkParents);
	}
}
