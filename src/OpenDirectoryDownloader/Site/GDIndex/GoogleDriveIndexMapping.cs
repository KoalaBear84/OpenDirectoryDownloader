namespace OpenDirectoryDownloader.Site.GDIndex;

public class GoogleDriveIndexMapping
{
	public const string BhadooIndex = "Bhadoo";
	public const string GoIndex = "Go";
	public const string Go2Index = "Go2";
	public const string GdIndex = "Gd";

	public class KeyValueList<TKey, TValue> : List<KeyValuePair<TKey, TValue>>
	{
		public void Add(TKey key, TValue value) => Add(new KeyValuePair<TKey, TValue>(key, value));
	}

	public static readonly KeyValueList<string, string> SiteMapping = new()
	{
		// Order is important!
		{ "goindex-theme-acrou", Go2Index },
		{ "savemydinar/esmailtv", Go2Index },

		{ "5MayRain/goIndex-theme-nexmoe", GoIndex },

		{ "Bhadoo-Drive-Index", BhadooIndex },
		{ "/AjmalShajahan97/goindex", BhadooIndex },
		{ "/cheems/GDIndex", BhadooIndex },
		{ "/cheems/goindex-extended", BhadooIndex },
		{ "/goIndex-theme-nexmoe", BhadooIndex },
		{ "/@googledrive/index", BhadooIndex },
		{ "/LeeluPradhan/G-Index", BhadooIndex },
		{ "/K-E-N-W-A-Y/GD-Index-Dark",  BhadooIndex },
		{ "/ParveenBhadooOfficial/Google-Drive-Index", BhadooIndex },
		{ "/ParveenBhadooOfficial/BhadooJS", BhadooIndex },
		{ "/RemixDev/goindex", BhadooIndex },
		{ "/sawankumar/Google-Drive-Index-III", BhadooIndex },
		{ "/Virusia/Fia-Terminal", BhadooIndex },
		{ "/yanzai/goindex", BhadooIndex },

		{ "/go2index/", Go2Index },
		{ "/alx-xlx/goindex", Go2Index },
		{ "/setnomJ/goindexv", Go2Index },

		{ "goindex", GoIndex },

		{ "gdindex", GdIndex }
	};

	public static string GetGoogleDriveIndexType(string scriptUrl)
	{
		foreach (KeyValuePair<string, string> siteMapping in SiteMapping)
		{
			if (scriptUrl.Contains(siteMapping.Key, StringComparison.InvariantCultureIgnoreCase))
			{
				return siteMapping.Value;
			}
		}

		return null;
	}

	private static readonly Dictionary<string, string> NativeMimeTypeNames = new()
	{
		{ "application/vnd.google-apps.document", "Doc" },
		{ "application/vnd.google-apps.spreadsheet", "Sheet" },
		{ "application/vnd.google-apps.presentation", "Slide" },
		{ "application/vnd.google-apps.drawing", "Drawing" },
		{ "application/vnd.google-apps.form", "Form" },
		{ "application/vnd.google-apps.script", "Script" },
		{ "application/vnd.google-apps.site", "Site" },
		{ "application/vnd.google-apps.jam", "Jamboard" },
		{ "application/vnd.google-apps.map", "My Map" },
		{ "application/vnd.google-apps.shortcut", "Shortcut" },
		{ "application/vnd.google-apps.fusiontable", "Fusion Table" },
	};

	/// <summary>
	/// A short, human-readable label for a Google Drive native file's type (e.g. "Slide", "Doc", "Sheet") -
	/// these files don't have a normal file extension, so the mime type is the only way to tell what they
	/// are. Returns null for anything else (a real file with its own extension, which already speaks for
	/// itself - no need to override WebFile.Description for those).
	/// </summary>
	public static string GetFriendlyMimeTypeName(string mimeType) =>
		mimeType is not null && NativeMimeTypeNames.TryGetValue(mimeType, out string name) ? name : null;
}