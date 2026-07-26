namespace PixelFlow.Core.Runner;

/// <summary>
/// Explicit runner states from architecture Section 7.
/// </summary>
public enum RunnerState
{
    Idle,
    Resolving,
    Retrying,
    Verifying,
    Executing,
    PostCheck,
    FailedStep,
    Paused,
    Aborted,
}