using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

namespace OpenDirectoryDownloader.Protocols.Http;

/// <summary>
/// HttpCloak looks for its native library next to Assembly.Location, which is empty in a single file app, so it ends up
/// relative to the current directory and only works when started from the application folder. When that fails, this
/// loads the library (libhttpcloak-{os}-{arch}) from the application folder, or its runtimes folder.
/// </summary>
internal static class HttpCloakNativeLibrary
{
	private const string LibraryName = "httpcloak";

	private static int Registered;

	public static void EnsureResolvable()
	{
		if (Interlocked.Exchange(ref Registered, 1) == 0)
		{
			AssemblyLoadContext.Default.ResolvingUnmanagedDll += Resolve;
		}
	}

	private static nint Resolve(Assembly assembly, string libraryName)
	{
		if (libraryName != LibraryName)
		{
			return 0;
		}

		string fileName = $"lib{LibraryName}-{OperatingSystemName()}-{ArchitectureName()}{Extension()}";

		foreach (string path in new[]
		{
			Path.Combine(AppContext.BaseDirectory, fileName),
			Path.Combine(AppContext.BaseDirectory, "runtimes", RuntimeInformation.RuntimeIdentifier, "native", fileName),
		})
		{
			if (File.Exists(path) && NativeLibrary.TryLoad(path, out nint handle))
			{
				return handle;
			}
		}

		return 0;
	}

	private static string OperatingSystemName() =>
		OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "darwin" : "linux";

	private static string ArchitectureName() => RuntimeInformation.ProcessArchitecture switch
	{
		Architecture.Arm64 => "arm64",
		_ => "amd64",
	};

	private static string Extension() =>
		OperatingSystem.IsWindows() ? ".dll" : OperatingSystem.IsMacOS() ? ".dylib" : ".so";
}
