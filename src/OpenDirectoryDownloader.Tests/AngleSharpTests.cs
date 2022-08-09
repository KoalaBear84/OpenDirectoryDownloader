using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using Xunit;

namespace OpenDirectoryDownloader.Tests;

public class AngleSharpTests
{
	/// <summary>
	/// Test 1
	/// </summary>
	/// <returns>Nothing</returns>
	[Fact]
	public async System.Threading.Tasks.Task Test01Async()
	{
		string html = @"<table>
  <thead>
    <tr>
      <th>Month</th>
      <th>Savings</th>
    </tr>
  </thead>
  <tbody>
    <tr>
      <td>January</td>
      <td>$100</td>
    </tr>
    <tr>
      <td>February</td>
      <td>$80</td>
    </tr>
  </tbody>
  <tfoot>
    <tr>
      <td>Sum</td>
      <td>$180</td>
    </tr>
  </tfoot>
</table>";
		HtmlParser htmlParser = new();
		IHtmlDocument htmlDocument = await htmlParser.ParseDocumentAsync(html);

		Assert.Equal("TABLE", htmlDocument.QuerySelector("tbody tr").Parent("table").TagName);
	}
}
