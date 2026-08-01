namespace PixelFlow.Core.Diagnostics;

/// <summary>
/// One structured run-report event (architecture Section 8). Serialized as a single JSONL line.
/// </summary>
public sealed class RunReportEvent
{
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Event kind: runStarted, stepStarted, resolveAttempt, stepFinished, runFinished.
    /// </summary>
    public string Event { get; set; } = "";

    public string? RunId { get; set; }

    public string? ProjectName { get; set; }

    public string? StepId { get; set; }

    public string? StepType { get; set; }

    public int? Attempt { get; set; }

    public int? Attempts { get; set; }

    public bool? Found { get; set; }

    public string? MatchedLayer { get; set; }

    public double? Confidence { get; set; }

    /// <summary>Succeeded | Failed | Aborted</summary>
    public string? Outcome { get; set; }

    public string? FinalState { get; set; }

    public string? FailureReason { get; set; }

    /// <summary>Relative file name under the run report folder (e.g. failure-click-1.png).</summary>
    public string? Screenshot { get; set; }
}

public static class RunReportEventNames
{
    public const string RunStarted = "runStarted";
    public const string StepStarted = "stepStarted";
    public const string ResolveAttempt = "resolveAttempt";
    public const string StepFinished = "stepFinished";
    public const string RunFinished = "runFinished";
}

public static class RunReportOutcomes
{
    public const string Succeeded = "Succeeded";
    public const string Failed = "Failed";
    public const string Aborted = "Aborted";
}
