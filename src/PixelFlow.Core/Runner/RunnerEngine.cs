using PixelFlow.Core.Diagnostics;
using PixelFlow.Core.Projects;

namespace PixelFlow.Core.Runner;

/// <summary>
/// Section 7 state machine. Resolves/executes via injected collaborators (mocked in P04).
/// Optional <see cref="IRunReporter"/> writes JSONL diagnostics (P21/P22).
/// </summary>
public sealed class RunnerEngine
{
    private readonly ITargetResolver _resolver;
    private readonly IStepVerifier _verifier;
    private readonly IStepExecutor _executor;
    private readonly IRunnerDelay _delay;
    private readonly IUserInterferenceDetector _interference;
    private readonly IRunReporter? _reporter;
    private readonly IFailureScreenshotCapture? _screenshotCapture;
    private readonly List<RunnerState> _transitions = [];
    private readonly object _gate = new();
    private volatile bool _abortRequested;
    private volatile bool _pauseRequested;
    private string? _activePauseReason;
    private CancellationTokenSource? _runCts;
    private RunnerState _state = RunnerState.Idle;

    public RunnerEngine(
        ITargetResolver resolver,
        IStepVerifier verifier,
        IStepExecutor executor,
        IRunnerDelay? delay = null,
        IRunReporter? reporter = null,
        IFailureScreenshotCapture? screenshotCapture = null,
        IUserInterferenceDetector? interferenceDetector = null)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _verifier = verifier ?? throw new ArgumentNullException(nameof(verifier));
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _delay = delay ?? new SystemRunnerDelay();
        _interference = interferenceDetector ?? NullUserInterferenceDetector.Instance;
        _reporter = reporter;
        _screenshotCapture = screenshotCapture;
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
        _activePauseReason = null;
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
    /// Request pause. Studio/IPC pause is honored between steps (never mid-input / mid-Wait).
    /// User-interference pause is requested from inside a step before synthetic input and holds
    /// immediately (no click is sent) until resume/abort.
    /// </summary>
    public void RequestPause(string? reason = null)
    {
        _activePauseReason = reason ?? PauseReasons.UserRequested;
        _pauseRequested = true;
    }

    public void RequestResume()
    {
        _pauseRequested = false;
        _activePauseReason = null;
    }

    public bool IsPauseRequested => _pauseRequested;

    /// <summary>Why the engine is (or last became) paused; cleared on resume/abort.</summary>
    public string? ActivePauseReason => _activePauseReason;

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
            var steps = project.Steps;

            // Ignore ambient input from before this run; only activity during the run counts.
            _interference.NoteSyntheticInput();

            Report(new RunReportEvent
            {
                Event = RunReportEventNames.RunStarted,
                ProjectName = project.Name,
            });

            for (var index = 0; index < steps.Count;)
            {
                if (_abortRequested || ct.IsCancellationRequested)
                {
                    TransitionTo(RunnerState.Aborted);
                    ReportRunFinished(RunReportOutcomes.Aborted);
                    return;
                }

                await WaitWhilePausedAsync(linked).ConfigureAwait(false);
                if (_abortRequested || ct.IsCancellationRequested)
                {
                    TransitionTo(RunnerState.Aborted);
                    ReportRunFinished(RunReportOutcomes.Aborted);
                    return;
                }

                var step = steps[index];
                var outcome = await RunStepAsync(step, project.Defaults, linked).ConfigureAwait(false);
                if (outcome == StepOutcome.Aborted)
                {
                    ReportRunFinished(RunReportOutcomes.Aborted);
                    return;
                }

                if (outcome == StepOutcome.Failed)
                {
                    if (!TryApplyRecovery(step, steps, ref index))
                    {
                        ReportRunFinished(RunReportOutcomes.Failed);
                        return;
                    }

                    continue;
                }

                index++;
            }

            TransitionTo(RunnerState.Idle);
            ReportRunFinished(RunReportOutcomes.Succeeded);
        }
        finally
        {
            _runCts = null;
        }
    }

    /// <summary>
    /// After <see cref="RunnerState.FailedStep"/>, apply skip/jump/abort recovery.
    /// Returns false when the run should stop (Aborted). On true, <paramref name="index"/>
    /// is the next step to run; caller should <c>continue</c> without incrementing.
    /// </summary>
    private bool TryApplyRecovery(ScriptStep failedStep, IReadOnlyList<ScriptStep> steps, ref int index)
    {
        var action = NormalizeRecoveryAction(failedStep.Recovery?.Action);

        if (string.Equals(action, StepRecoveryActions.Skip, StringComparison.Ordinal))
        {
            // FailedStep -> Idle (between steps), then continue with the next step.
            TransitionTo(RunnerState.Idle);
            index++;
            return true;
        }

        if (string.Equals(action, StepRecoveryActions.Jump, StringComparison.Ordinal))
        {
            var jumpTo = failedStep.Recovery?.JumpTo?.Trim();
            if (string.IsNullOrEmpty(jumpTo))
            {
                TransitionTo(RunnerState.Aborted);
                return false;
            }

            var target = FindStepIndexById(steps, jumpTo);
            if (target < 0)
            {
                TransitionTo(RunnerState.Aborted);
                return false;
            }

            TransitionTo(RunnerState.Idle);
            index = target;
            return true;
        }

        // Abort (explicit or missing/unknown recovery) — architecture: no recovery → Aborted.
        TransitionTo(RunnerState.Aborted);
        return false;
    }

    private static string NormalizeRecoveryAction(string? action)
    {
        if (string.IsNullOrWhiteSpace(action))
        {
            return StepRecoveryActions.Abort;
        }

        if (string.Equals(action, StepRecoveryActions.Skip, StringComparison.OrdinalIgnoreCase))
        {
            return StepRecoveryActions.Skip;
        }

        if (string.Equals(action, StepRecoveryActions.Jump, StringComparison.OrdinalIgnoreCase))
        {
            return StepRecoveryActions.Jump;
        }

        if (string.Equals(action, StepRecoveryActions.Abort, StringComparison.OrdinalIgnoreCase))
        {
            return StepRecoveryActions.Abort;
        }

        return StepRecoveryActions.Abort;
    }

    private static int FindStepIndexById(IReadOnlyList<ScriptStep> steps, string id)
    {
        for (var i = 0; i < steps.Count; i++)
        {
            if (string.Equals(steps[i].Id, id, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
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

        // Per-attempt resolve budget: poll until found or TimeoutMs elapses, then count as one failed attempt.
        var timeoutMs = step.TimeoutMs ?? defaults.TimeoutMs;
        if (timeoutMs < 0)
        {
            timeoutMs = 0;
        }

        Report(new RunReportEvent
        {
            Event = RunReportEventNames.StepStarted,
            StepId = step.Id,
            StepType = step.Type,
        });

        string? lastLayer = null;
        double? lastConfidence = null;
        string? lastFailureReason = null;
        var attempt = 0;
        while (true)
        {
            attempt++;

            TransitionTo(RunnerState.Resolving);
            if (ShouldAbort(linked))
            {
                TransitionTo(RunnerState.Aborted);
                ReportStepFinished(step, RunReportOutcomes.Aborted, attempt, lastLayer, lastConfidence, "Aborted");
                return StepOutcome.Aborted;
            }

            ResolveResult candidate;
            try
            {
                candidate = await ResolveWithTimeoutAsync(step, timeoutMs, linked).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_abortRequested || linked.IsCancellationRequested)
            {
                TransitionTo(RunnerState.Aborted);
                ReportStepFinished(step, RunReportOutcomes.Aborted, attempt, lastLayer, lastConfidence, "Aborted");
                return StepOutcome.Aborted;
            }

            Report(new RunReportEvent
            {
                Event = RunReportEventNames.ResolveAttempt,
                StepId = step.Id,
                StepType = step.Type,
                Attempt = attempt,
                Found = candidate.Found,
                MatchedLayer = candidate.MatchedLayer,
                Confidence = candidate.Found ? candidate.Confidence : null,
                FailureReason = candidate.Found ? null : candidate.FailureReason,
            });

            if (candidate.Found)
            {
                lastLayer = candidate.MatchedLayer;
                lastConfidence = candidate.Confidence;
                lastFailureReason = null;
            }
            else
            {
                lastFailureReason = candidate.FailureReason;
            }

            if (ShouldAbort(linked))
            {
                TransitionTo(RunnerState.Aborted);
                ReportStepFinished(step, RunReportOutcomes.Aborted, attempt, lastLayer, lastConfidence, "Aborted");
                return StepOutcome.Aborted;
            }

            if (!candidate.Found)
            {
                if (attempt >= maxAttempts)
                {
                    TransitionTo(RunnerState.FailedStep);
                    ReportStepFinished(
                        step,
                        RunReportOutcomes.Failed,
                        attempt,
                        lastLayer,
                        lastConfidence,
                        lastFailureReason ?? "Resolve budget exhausted",
                        defaults);
                    return StepOutcome.Failed;
                }

                TransitionTo(RunnerState.Retrying);
                try
                {
                    await _delay.DelayAsync(backoffMs, linked.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (_abortRequested || linked.IsCancellationRequested)
                {
                    TransitionTo(RunnerState.Aborted);
                    ReportStepFinished(step, RunReportOutcomes.Aborted, attempt, lastLayer, lastConfidence, "Aborted");
                    return StepOutcome.Aborted;
                }

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
                ReportStepFinished(step, RunReportOutcomes.Aborted, attempt, lastLayer, lastConfidence, "Aborted");
                return StepOutcome.Aborted;
            }

            if (!preOk)
            {
                lastFailureReason = "Pre-execute verification failed";
                if (attempt >= maxAttempts)
                {
                    TransitionTo(RunnerState.FailedStep);
                    ReportStepFinished(
                        step,
                        RunReportOutcomes.Failed,
                        attempt,
                        lastLayer,
                        lastConfidence,
                        lastFailureReason,
                        defaults);
                    return StepOutcome.Failed;
                }

                TransitionTo(RunnerState.Retrying);
                try
                {
                    await _delay.DelayAsync(backoffMs, linked.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (_abortRequested || linked.IsCancellationRequested)
                {
                    TransitionTo(RunnerState.Aborted);
                    ReportStepFinished(step, RunReportOutcomes.Aborted, attempt, lastLayer, lastConfidence, "Aborted");
                    return StepOutcome.Aborted;
                }

                continue;
            }

            // P24: brief observe window immediately before synthetic input.
            if (IsSyntheticInputStep(step))
            {
                _interference.BeginActionGate();
                try
                {
                    await _delay.DelayAsync(80, linked.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (_abortRequested || linked.IsCancellationRequested)
                {
                    TransitionTo(RunnerState.Aborted);
                    ReportStepFinished(step, RunReportOutcomes.Aborted, attempt, lastLayer, lastConfidence, "Aborted");
                    return StepOutcome.Aborted;
                }

                if (_interference.IsUserInterfering())
                {
                    Report(new RunReportEvent
                    {
                        Event = RunReportEventNames.InterferencePaused,
                        StepId = step.Id,
                        StepType = step.Type,
                        Attempt = attempt,
                        FailureReason = "User mouse/keyboard activity detected before input",
                    });

                    RequestPause(PauseReasons.UserInterference);
                    await WaitWhilePausedAsync(linked).ConfigureAwait(false);
                    if (_abortRequested || linked.IsCancellationRequested)
                    {
                        TransitionTo(RunnerState.Aborted);
                        ReportStepFinished(step, RunReportOutcomes.Aborted, attempt, lastLayer, lastConfidence, "Aborted");
                        return StepOutcome.Aborted;
                    }

                    // Re-resolve after resume; do not consume a retry attempt for interference.
                    attempt--;
                    continue;
                }
            }

            TransitionTo(RunnerState.Executing);
            if (ShouldAbort(linked))
            {
                TransitionTo(RunnerState.Aborted);
                ReportStepFinished(step, RunReportOutcomes.Aborted, attempt, lastLayer, lastConfidence, "Aborted");
                return StepOutcome.Aborted;
            }

            try
            {
                await _executor.ExecuteAsync(step, candidate, linked.Token).ConfigureAwait(false);
                if (IsSyntheticInputStep(step))
                {
                    _interference.NoteSyntheticInput();
                }
            }
            catch (OperationCanceledException) when (_abortRequested || linked.IsCancellationRequested)
            {
                TransitionTo(RunnerState.Aborted);
                ReportStepFinished(step, RunReportOutcomes.Aborted, attempt, lastLayer, lastConfidence, "Aborted");
                return StepOutcome.Aborted;
            }

            if (ShouldAbort(linked))
            {
                TransitionTo(RunnerState.Aborted);
                ReportStepFinished(step, RunReportOutcomes.Aborted, attempt, lastLayer, lastConfidence, "Aborted");
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
                ReportStepFinished(step, RunReportOutcomes.Aborted, attempt, lastLayer, lastConfidence, "Aborted");
                return StepOutcome.Aborted;
            }

            if (!postOk)
            {
                TransitionTo(RunnerState.FailedStep);
                ReportStepFinished(
                    step,
                    RunReportOutcomes.Failed,
                    attempt,
                    lastLayer,
                    lastConfidence,
                    "Post-execute verification failed",
                    defaults);
                return StepOutcome.Failed;
            }

            TransitionTo(RunnerState.Idle);
            ReportStepFinished(step, RunReportOutcomes.Succeeded, attempt, lastLayer, lastConfidence, null);
            return StepOutcome.Succeeded;
        }
    }

    /// <summary>
    /// Polls the resolver until a candidate is found, <paramref name="timeoutMs"/> wall-clock elapses,
    /// or abort is requested. <c>timeoutMs == 0</c> means a single resolve attempt with no polling wait.
    /// </summary>
    private async Task<ResolveResult> ResolveWithTimeoutAsync(
        ScriptStep step,
        int timeoutMs,
        CancellationTokenSource linked)
    {
        const int pollMs = 50;
        ResolveResult last = ResolveResult.NotFound("No resolve attempt yet.");
        var sw = timeoutMs > 0 ? System.Diagnostics.Stopwatch.StartNew() : null;

        while (true)
        {
            if (ShouldAbort(linked) || linked.IsCancellationRequested)
            {
                linked.Cancel();
                throw new OperationCanceledException(linked.Token);
            }

            last = await _resolver.ResolveAsync(step, linked.Token).ConfigureAwait(false);
            if (last.Found)
            {
                return last;
            }

            if (sw is null || sw.ElapsedMilliseconds >= timeoutMs)
            {
                return last;
            }

            var remaining = timeoutMs - (int)sw.ElapsedMilliseconds;
            if (remaining <= 0)
            {
                return last;
            }

            var slice = Math.Min(pollMs, remaining);
            await _delay.DelayAsync(slice, linked.Token).ConfigureAwait(false);
        }
    }

    private static bool IsSyntheticInputStep(ScriptStep step) =>
        !string.Equals(step.Type, "Wait", StringComparison.OrdinalIgnoreCase);

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

    private void Report(RunReportEvent evt)
    {
        try
        {
            _reporter?.Write(evt);
        }
        catch (Exception ex)
        {
            // Diagnostics must not crash the run.
            System.Diagnostics.Debug.WriteLine($"[runner] report write failed: {ex.Message}");
        }
    }

    private void ReportStepFinished(
        ScriptStep step,
        string outcome,
        int attempts,
        string? matchedLayer,
        double? confidence,
        string? failureReason,
        ProjectDefaults? defaults = null)
    {
        string? screenshot = null;
        if (outcome == RunReportOutcomes.Failed
            && defaults is not null
            && ShouldCaptureFailureScreenshot(step, defaults))
        {
            screenshot = TryCaptureFailureScreenshot(step.Id);
        }

        Report(new RunReportEvent
        {
            Event = RunReportEventNames.StepFinished,
            StepId = step.Id,
            StepType = step.Type,
            Outcome = outcome,
            Attempts = attempts,
            MatchedLayer = matchedLayer,
            Confidence = confidence,
            FailureReason = failureReason,
            Screenshot = screenshot,
        });
    }

    private void ReportRunFinished(string outcome)
    {
        Report(new RunReportEvent
        {
            Event = RunReportEventNames.RunFinished,
            FinalState = State.ToString(),
            Outcome = outcome,
        });
    }

    private string? TryCaptureFailureScreenshot(string stepId)
    {
        if (_reporter is null || _screenshotCapture is null)
        {
            return null;
        }

        try
        {
            var png = _screenshotCapture.CapturePng();
            if (png is null || png.Length == 0)
            {
                return null;
            }

            return _reporter.SaveFailureScreenshot(stepId, png);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[runner] failure screenshot failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Effective opt-in: per-step override wins; otherwise project default (false).
    /// SecretRef Type steps never capture (would risk putting the secret on disk).
    /// </summary>
    public static bool ShouldCaptureFailureScreenshot(ScriptStep step, ProjectDefaults defaults)
    {
        if (!string.IsNullOrWhiteSpace(step.SecretRef))
        {
            return false;
        }

        if (step.CaptureFailureScreenshot.HasValue)
        {
            return step.CaptureFailureScreenshot.Value;
        }

        return defaults.CaptureFailureScreenshots;
    }

    private enum StepOutcome
    {
        Succeeded,
        Failed,
        Aborted,
    }
}
