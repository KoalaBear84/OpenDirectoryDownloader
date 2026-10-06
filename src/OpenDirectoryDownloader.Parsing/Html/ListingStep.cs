using OpenDirectoryDownloader.Models;

namespace OpenDirectoryDownloader.Parsing.Html;

/// <summary>One check of the listing pipeline: recognizes a kind of page and parses it, or lets the next step try.</summary>
public sealed record ListingStep(string Name, Func<ParseContext, ValueTask<StepResult>> RunAsync)
{
	public ListingStep(string name, Func<ParseContext, StepResult> run) : this(name, context => ValueTask.FromResult(run(context)))
	{
	}
}

public enum StepOutcome
{
	/// <summary>Not this kind of page (or nothing found), the next step tries</summary>
	Continue,

	/// <summary>The page is parsed, <see cref="StepResult.Result"/> is the answer</summary>
	Done,

	/// <summary>The raw &lt;pre&gt; shortcut did not work out: parse the page again from the start, the normal way</summary>
	RestartWithoutRawPre
}

public readonly record struct StepResult(StepOutcome Outcome, WebDirectory Result = null)
{
	public static readonly StepResult Continue = new(StepOutcome.Continue);

	public static readonly StepResult RestartWithoutRawPre = new(StepOutcome.RestartWithoutRawPre);

	public static StepResult Done(WebDirectory result) => new(StepOutcome.Done, result);
}
