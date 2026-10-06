using Microsoft.Extensions.Logging;
using OpenDirectoryDownloader.Models;
using OpenDirectoryDownloader.Parsing.Listings;

namespace OpenDirectoryDownloader.Parsing.Html;

/// <summary>Parses a directory page by running the steps of <see cref="DefaultListingPipeline"/> in order.</summary>
public static class HtmlListingParser
{
	/// <summary>
	/// Parses the HTML of <paramref name="webDirectory"/> into a new, detached WebDirectory with its subdirectories
	/// and files, in the order the page lists them.
	/// </summary>
	/// <param name="response">The response the HTML came from, so sites can be recognized by their headers</param>
	/// <param name="checkParents">Drop entries outside the directory (links up, to other sites, ...)</param>
	public static Task<WebDirectory> ParseAsync(ParserServices services, WebDirectory webDirectory, string html, HttpResponseMessage response = null, bool checkParents = true)
	{
		return RunAsync(new ParseContext(services, webDirectory, html, response, checkParents, allowRawPre: true));
	}

	private static async Task<WebDirectory> RunAsync(ParseContext context)
	{
		try
		{
			foreach (ListingStep step in DefaultListingPipeline.Steps)
			{
				StepResult result = await step.RunAsync(context);

				switch (result.Outcome)
				{
					case StepOutcome.Done:
						return result.Result;
					case StepOutcome.RestartWithoutRawPre:
						return await RunAsync(new ParseContext(context.Services, context.WebDirectory, context.Html, context.Response, context.CheckParents, allowRawPre: false));
				}
			}

			return context.Parsed;
		}
		catch (FriendlyException ex)
		{
			context.Logger.LogError("{error} (when parsing {url})", ex.Message, context.Parsed.Url);

			context.Parsed.Error = true;
		}
		catch (Exception ex)
		{
			context.Logger.LogError(ex, "Exception when parsing {url}", context.Parsed.Url);

			context.Parsed.Error = true;
		}

		ListingPostProcessor.CheckParsedResults(context.Parsed, context.BaseUrl, context.CheckParents, context.Logger);

		return context.Parsed;
	}
}
