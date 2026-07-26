namespace PixelFlow.Core.Runner;

/// <summary>
/// Outcome of a (mocked or real) target resolution attempt.
/// </summary>
public sealed record ResolveResult(
    bool Found,
    string? CandidateId = null,
    string? FailureReason = null,
    ScreenRect BoundingRect = default,
    string? AutomationId = null,
    string? Name = null,
    string? ControlType = null,
    int ProcessId = 0)
{
    public static ResolveResult NotFound(string reason) =>
        new(Found: false, FailureReason: reason);
}
