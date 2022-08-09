using System.Collections.Generic;

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

	public static KeyValueList<string, string> SiteMapping = new()
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

		{ "goindex", GoIndex },

		{ "gdindex", GdIndex }
	};

	public static string GetGoogleDriveIndexType(string scriptUrl)
	{
		foreach (KeyValuePair<string, string> siteMapping in SiteMapping)
		{
			if (scriptUrl.ToLower().Contains(siteMapping.Key.ToLower()))
			{
				return siteMapping.Value;
			}
		}

		return null;
	}
}