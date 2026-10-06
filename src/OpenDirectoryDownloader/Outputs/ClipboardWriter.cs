using System.Diagnostics;
using System.Runtime.InteropServices;
using TextCopy;

namespace OpenDirectoryDownloader.Outputs;

/// <summary>Copies text to the clipboard: the built in way (Windows, macOS, Linux X11 through xclip), else wl-copy (Linux Wayland).</summary>
public static class ClipboardWriter
{
	/// <returns>Whether it worked as far as can be told</returns>
	public static bool TrySetText(string value)
	{
		ArgumentNullException.ThrowIfNull(value);

		try
		{
			new Clipboard().SetText(value);

			return true;
		}
		catch
		{
			// Try the next way
		}

		if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
		{
			return false;
		}

		try
		{
			using Process clipboardExecutable = new()
			{
				StartInfo = new ProcessStartInfo
				{
					RedirectStandardInput = true,
					FileName = "wl-copy",
				}
			};

			clipboardExecutable.Start();
			clipboardExecutable.StandardInput.Write(value);
			clipboardExecutable.StandardInput.Close();

			return true;
		}
		catch
		{
			return false;
		}
	}
}
