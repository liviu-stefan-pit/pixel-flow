namespace PixelFlow.Core.Runner;

/// <summary>
/// Stub-friendly delay used for retry backoff and per-attempt resolve polling.
/// </summary>
public interface IRunnerDelay
{
    Task DelayAsync(int milliseconds, CancellationToken cancellationToken);
}

public sealed class SystemRunnerDelay : IRunnerDelay
{
    public Task DelayAsync(int milliseconds, CancellationToken cancellationToken) =>
        Task.Delay(milliseconds, cancellationToken);
}

/// <summary>No-op delay for unit tests.</summary>
public sealed class ImmediateRunnerDelay : IRunnerDelay
{
    public Task DelayAsync(int milliseconds, CancellationToken cancellationToken) =>
        cancellationToken.IsCancellationRequested
            ? Task.FromCanceled(cancellationToken)
            : Task.CompletedTask;
}