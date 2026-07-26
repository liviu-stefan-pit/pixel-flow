using PixelFlow.Core.Projects;

namespace PixelFlow.Core.Runner;

/// <summary>
/// Section 7 state machine. Resolves/executes via injected collaborators (mocked in P04).
/// </summary>
public sealed class RunnerEngine
{
    private readonly ITargetResolver _resolver;
    private readonly IStepVerifier _verifier;
    private readonly IStepExecutor _executor;
    private readonly IRunnerDelay _delay;
    private readonly List<RunnerState> _transitions = [];
    private readonly object _gate = new();
    private volatile bool _abortRequested;
    private volatile bool _pauseRequested;
    private CancellationTokenSource? _runCts;
    private RunnerState _state = RunnerState.Idle;

    public RunnerEngine(
        ITargetResolver resolver,
        IStepVerifier verifier,
        IStepExecutor executor,
        IRunnerDelay? delay = null)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _verifier = verifier ?? throw new ArgumentNullException(nameof(verifier));
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _delay = delay ?? new SystemRunnerDelay();
        _transitions.Add(RunnerState.Idle);
    }

    public RunnerState State
    {
        get { lock (_gate) { return _state; } }
    }

    public IReadOnlyList<RunnerState> TransitionLog
    {
        get { lock (_gate) { return _transitions.ToArray(); } }
    }

    /// <summary>Raised whenever <see cref="State"/> changes (including the initial Idle from construction is not raised).</summary>
    public event Action<RunnerState>? StateChanged;

    /// <summary>Emergency stop: transitions to Aborted from Resolving/Executing (and cooperative cancel elsewhere).</summary>
    public void RequestAbort()
    {
        _abortRequested = true;
        _pauseRequested = false;
        try
        {
            _runCts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // run already finishing
        }
    }

    /// <summary>
    /// Request pause. Honored between steps (P05 IPC surface; P10 tightens mid-run boundaries).
    /// </summary>
    public void RequestPause()
    {
        _pauseRequested = true;
    }

    public void RequestResume()
    {
        _pauseRequested = false;
    }

    public bool IsPauseRequested => _pauseRequested;

    public async Task RunAsync(ProjectDocument project, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        _abortRequested = false;
        // Intentionally do not clear _pauseRequested: a pause requested just before/at start
        // is honored between steps (including before the first step).

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _runCts = linked;
        try
        {
            var ct = linked.Token;

            foreach (var step in project.Steps)
            {
                if (_abortRequested || ct.IsCancellationRequested)
                {
                    TransitionTo(RunnerState.Aborted);
                    return;
                }

                await WaitWhilePausedAsync(linked).ConfigureAwait(false);
                if (_abortRequested || ct.IsCancellationRequested)
                {
                    TransitionTo(RunnerState.Aborted);
                    return;
                }

                var outcome = await RunStepAsync(step, project.Defaults, linked).ConfigureAwait(false);
                if (outcome == StepOutcome.Aborted)
                {
                    return;
                }

                if (outcome == StepOutcome.Failed)
                {
                    // P04: no recovery configuration yet -> Aborted after FailedStep.
                    TransitionTo(RunnerState.Aborted);
                    return;
                }
            }

            TransitionTo(RunnerState.Idle);
        }
        finally
        {
            _runCts = null;
        }
    }

    private async Task WaitWhilePausedAsync(CancellationTokenSource linked)
    {
        if (!_pauseRequested)
        {
            return;
        }

        TransitionTo(RunnerState.Paused);
        while (_pauseRequested && !_abortRequested && !linked.IsCancellationRequested)
        {
            try
            {
                await _delay.DelayAsync(50, linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task<StepOutcome> RunStepAsync(
        ScriptStep step,
        ProjectDefaults defaults,
        CancellationTokenSource linked)
    {
        var maxAttempts = step.Retry?.MaxAttempts ?? defaults.Retry.MaxAttempts;
        if (maxAttempts < 1)
        {
            maxAttempts = 1;
        }

        var backoffMs = step.Retry?.BackoffMs ?? defaults.Retry.BackoffMs;
        if (backoffMs < 0)
        {
            backoffMs = 0;
        }

        var attempt = 0;
        while (true)
        {
            attempt++;

            TransitionTo(RunnerState.Resolving);
            if (ShouldAbort(linked))
            {
                TransitionTo(RunnerState.Aborted);
                return StepOutcome.Aborted;
            }

            ResolveResult candidate;
            try
            {
                candidate = await _resolver.ResolveAsync(step, linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_abortRequested || linked.IsCancellationRequested)
            {
                TransitionTo(RunnerState.Aborted);
                return StepOutcome.Aborted;
            }

            if (ShouldAbort(linked))
            {
                TransitionTo(RunnerState.Aborted);
                return StepOutcome.Aborted;
            }

            if (!candidate.Found)
            {
                if (attempt >= maxAttempts)
                {
                    TransitionTo(RunnerState.FailedStep);
                    return StepOutcome.Failed;
                }

                TransitionTo(RunnerState.Retrying);
                await _delay.DelayAsync(backoffMs, linked.Token).ConfigureAwait(false);
                continue;
            }

            TransitionTo(RunnerState.Verifying);
            bool preOk;
            try
            {
                preOk = await _verifier.VerifyBeforeExecuteAsync(step, candidate, linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_abortRequested || linked.IsCancellationRequested)
            {
                TransitionTo(RunnerState.Aborted);
                return StepOutcome.Aborted;
            }

            if (!preOk)
            {
                if (attempt >= maxAttempts)
                {
                    TransitionTo(RunnerState.FailedStep);
                    return StepOutcome.Failed;
                }

                TransitionTo(RunnerState.Retrying);
                await _delay.DelayAsync(backoffMs, linked.Token).ConfigureAwait(false);
                continue;
            }

            TransitionTo(RunnerState.Executing);
            if (ShouldAbort(linked))
            {
                TransitionTo(RunnerState.Aborted);
                return StepOutcome.Aborted;
            }

            try
            {
                await _executor.ExecuteAsync(step, candidate, linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_abortRequested || linked.IsCancellationRequested)
            {
                TransitionTo(RunnerState.Aborted);
                return StepOutcome.Aborted;
            }

            if (ShouldAbort(linked))
            {
                TransitionTo(RunnerState.Aborted);
                return StepOutcome.Aborted;
            }

            TransitionTo(RunnerState.PostCheck);
            bool postOk;
            try
            {
                postOk = await _verifier.VerifyAfterExecuteAsync(step, candidate, linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_abortRequested || linked.IsCancellationRequested)
            {
                TransitionTo(RunnerState.Aborted);
                return StepOutcome.Aborted;
            }

            if (!postOk)
            {
                TransitionTo(RunnerState.FailedStep);
                return StepOutcome.Failed;
            }

            TransitionTo(RunnerState.Idle);
            return StepOutcome.Succeeded;
        }
    }

    private bool ShouldAbort(CancellationTokenSource linked)
    {
        if (!_abortRequested)
        {
            return false;
        }

        linked.Cancel();
        return true;
    }

    private void TransitionTo(RunnerState next)
    {
        Action<RunnerState>? handlers;
        lock (_gate)
        {
            if (_state == next && next is RunnerState.Idle or RunnerState.Aborted or RunnerState.Paused)
            {
                // Allow Idle->Idle between steps to be recorded once per return; skip duplicate terminal/paused.
                if (next is RunnerState.Aborted or RunnerState.Paused
                    && _transitions.Count > 0
                    && _transitions[^1] == next)
                {
                    return;
                }
            }

            _state = next;
            _transitions.Add(next);
            handlers = StateChanged;
        }

        handlers?.Invoke(next);
    }

    private enum StepOutcome
    {
        Succeeded,
        Failed,
        Aborted,
    }
}