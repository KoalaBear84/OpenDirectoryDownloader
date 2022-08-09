using System.IO;
using System.Text.RegularExpressions;

namespace OpenDirectoryDownloader.Helpers;

public static class PathHelper
{
	private static readonly Regex Regex = new(string.Format(@"^(CON|PRN|AUX|NUL|CLOCK\$|COM[1-9]|LPT[1-9])(?=\..|$)|(^(\.+|\s+)$)|((\.+|\s+)$)|([{0}])", Regex.Escape(new string(Path.GetInvalidFileNameChars()))), RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.CultureInvariant);

	public static string GetValidPath(string url)
	{
		return Regex.Replace(url, "_");
	}
}
