using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace OpenDirectoryDownloader.Helpers
{
	/// <summary>
	/// Modified version of https://stackoverflow.com/a/41401824/951001
	/// No, this isn't great, but it works for the current purpose
	/// </summary>
	public static class JavaScriptHelper
	{
		private static readonly Regex JavaScriptName = new Regex(@"function (?<Name>.*?)\(.*\)");

		public struct Function
		{
			public int StartIndex { get; set; }
			public int EndIndex { get; set; }

			public string Name { get; set; }
			public string Body { get; set; }
		}

		/// <summary>
		/// Source: https://stackoverflow.com/questions/12765819/more-efficient-way-to-get-all-indexes-of-a-character-in-a-string
		/// </summary>
		public static List<int> AllIndexesOf(this string str, string value)
		{
			if (string.IsNullOrEmpty(value))
			{
				throw new ArgumentException("the string to find may not be empty", nameof(value));
			}

			List<int> indexes = new List<int>();

			for (int index = 0; ; index += value.Length)
			{
				index = str.IndexOf(value, index);

				if (index == -1)
				{
					return indexes;
				}

				indexes.Add(index);
			}
		}

		public static List<Function> Parse(string file)
		{
			List<Function> functions = new List<Function>();

			List<int> allFuncIndices = file.AllIndexesOf("function ");
			List<int> allOpeningBraceIndices = file.AllIndexesOf("{");
			List<int> allClosingBraceIndices = file.AllIndexesOf("}");

			for (int i = 0; i < allFuncIndices.Count; i++)
			{
				int thisIndex = allFuncIndices[i];
				bool functionBoundaryFound = false;

				int testFuncIndex = i;
				int lastIndex = file.Length - 1;

				while (!functionBoundaryFound)
				{
					// Find the next function index or last position if this is the last function definition
					int nextIndex = (testFuncIndex < (allFuncIndices.Count - 1)) ? allFuncIndices[testFuncIndex + 1] : lastIndex;

					IEnumerable<int> q1 = from c in allOpeningBraceIndices where c > thisIndex && c <= nextIndex select c;

					// Skip the first element as it is the opening brace for this function
					IEnumerable<int> qTemp = q1.Skip(1);

					IEnumerable<int> q2 = from c in allClosingBraceIndices where c > thisIndex && c <= nextIndex select c;

					int q1Count = qTemp.Count();
					int q2Count = q2.Count();

					if (q1Count == q2Count && nextIndex < lastIndex)
					{
						// Next function is a nested function, move on to the one after this
						functionBoundaryFound = false;
					}
					else if (q2Count > q1Count)
					{
						// We found the function boundary... just need to find the closest unbalanced closing brace 
						Function funcLim = new Function();
						funcLim.StartIndex = thisIndex;
						funcLim.EndIndex = q2.ElementAt(q1Count) + 1;
						funcLim.Body = file[thisIndex..funcLim.EndIndex];
						funcLim.Name = JavaScriptName.Match(funcLim.Body).Groups["Name"].Value;
						functions.Add(funcLim);

						functionBoundaryFound = true;
					}

					testFuncIndex++;
				}
			}

			return functions;
		}
	}
}
