using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

namespace OpenDirectoryDownloader.Hosting;

/// <summary>
/// Finds the native libraries of the packages (SQLite, HttpCloak) when .NET itself can't. A single file build carries
/// them as resources (see the project file) and extracts one only when it is first needed: SQLite on the first scan,
/// HttpCloak only with --http-cloak. They go to %TEMP%\.net\OpenDirectoryDownloader\{build} (or
/// DOTNET_BUNDLE_EXTRACT_BASE_DIR, like .NET's own extraction) and are reused by later runs of the same build.
/// It also covers HttpCloak looking for its library relative to the current directory instead of the application's.
/// </summary>
internal static class EmbeddedNativeLibraries
{
	private const string ResourcePrefix = "native/";

	private static readonly Assembly ResourceAssembly = typeof(EmbeddedNativeLibraries).Assembly;
	private static readonly Lock ExtractLock = new();
	private static int Registered;

	public static void Register()
	{
		if (Interlocked.Exchange(ref Registered, 1) == 0)
		{
			// Only called when the normal lookup (and a library's own resolver) found nothing
			AssemblyLoadContext.Default.ResolvingUnmanagedDll += Resolve;
		}
	}

	private static nint Resolve(Assembly assembly, string libraryName)
	{
		foreach (string fileName in FileNames(libraryName))
		{
			foreach (string path in new[]
			{
				Path.Combine(AppContext.BaseDirectory, fileName),
				Path.Combine(AppContext.BaseDirectory, "runtimes", RuntimeInformation.RuntimeIdentifier, "native", fileName)
			})
			{
				if (File.Exists(path) && NativeLibrary.TryLoad(path, out nint handle))
				{
					return handle;
				}
			}

			if (TryExtract(fileName, out string extractedPath) && NativeLibrary.TryLoad(extractedPath, out nint extractedHandle))
			{
				return extractedHandle;
			}
		}

		return 0;
	}

	/// <summary>The file names a DllImport name can have on this platform</summary>
	private static IEnumerable<string> FileNames(string libraryName)
	{
		string extension = OperatingSystem.IsWindows() ? ".dll" : OperatingSystem.IsMacOS() ? ".dylib" : ".so";

		// HttpCloak names its library after the platform: libhttpcloak-windows-amd64.dll
		if (libraryName == "httpcloak")
		{
			string operatingSystem = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "darwin" : "linux";
			string architecture = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "arm64" : "amd64";

			yield return $"lib{libraryName}-{operatingSystem}-{architecture}{extension}";
			yield break;
		}

		yield return $"{libraryName}{extension}";

		if (!OperatingSystem.IsWindows())
		{
			yield return $"lib{libraryName}{extension}";
		}
	}

	private static bool TryExtract(string fileName, out string path)
	{
		path = null;

		using Stream resource = ResourceAssembly.GetManifestResourceStream(ResourcePrefix + fileName);

		if (resource is null)
		{
			return false;
		}

		string directory = Path.Combine(
			Environment.GetEnvironmentVariable("DOTNET_BUNDLE_EXTRACT_BASE_DIR") is { Length: > 0 } baseDirectory ? baseDirectory : Path.GetTempPath(),
			".net",
			"OpenDirectoryDownloader",
			ResourceAssembly.ManifestModule.ModuleVersionId.ToString("N"));

		path = Path.Combine(directory, fileName);

		lock (ExtractLock)
		{
			// Extracted before, by this or an earlier run of the same build
			if (File.Exists(path) && new FileInfo(path).Length == resource.Length)
			{
				return true;
			}

			Directory.CreateDirectory(directory);

			// Written next to it and then moved: another process never sees half a file
			string temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";

			try
			{
				using (FileStream file = new(temporaryPath, FileMode.CreateNew, FileAccess.Write))
				{
					resource.CopyTo(file);
				}

				File.Move(temporaryPath, path, overwrite: false);
			}
			catch (IOException) when (File.Exists(path) && new FileInfo(path).Length == resource.Length)
			{
				// Another process extracted it at the same time
			}
			finally
			{
				File.Delete(temporaryPath);
			}
		}

		return true;
	}
}
