using PixelFlow.Core.Projects;
using PixelFlow.Core.Runner;

namespace PixelFlow.Runner;

/// <summary>
/// Mocked resolve/execute for P05 IPC demos (no real UI automation yet).
/// Wait steps delay; other steps succeed immediately.
/// </summary>
internal sealed class MockedStepServices : ITargetResolver, IStepVerifier, IStepExecutor
{
    private readonly IRunnerDelay _delay;

    public MockedStepServices(IRunnerDelay? delay = null)
    {
        _delay = delay ?? new SystemRunnerDelay();
    }

    public Task<ResolveResult> ResolveAsync(ScriptStep step, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new ResolveResult(Found: true, CandidateId: $"mock:{step.Id}"));
    }

    public Task<bool> VerifyBeforeExecuteAsync(ScriptStep step, ResolveResult candidate, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(true);
    }

    public Task<bool> VerifyAfterExecuteAsync(ScriptStep step, ResolveResult candidate, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(true);
    }

    public async Task ExecuteAsync(ScriptStep step, ResolveResult candidate, CancellationToken cancellationToken)
    {
        if (string.Equals(step.Type, "Wait", StringComparison.OrdinalIgnoreCase))
        {
            var ms = step.WaitMs ?? 0;
            if (ms > 0)
            {
                Console.WriteLine($"[runner] Wait {ms}ms (step {step.Id})");
                await _delay.DelayAsync(ms, cancellationToken).ConfigureAwait(false);
            }

            return;
        }

        Console.WriteLine($"[runner] Mock execute {step.Type} (step {step.Id})");
        await Task.CompletedTask.ConfigureAwait(false);
    }
}
