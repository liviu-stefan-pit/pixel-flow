using PixelFlow.Core.Projects;
using PixelFlow.Core.Runner;

namespace PixelFlow.Core.Tests.Runner;

public sealed class RunnerEngineTests
{
    [Fact]
    public async Task HappyPath_IdleResolvingVerifyingExecutingPostCheckIdle()
    {
        var resolver = new MockResolver(_ => new ResolveResult(true, "el-1"));
        var verifier = new MockVerifier(before: true, after: true);
        var executor = new MockExecutor();
        var engine = new RunnerEngine(resolver, verifier, executor, new ImmediateRunnerDelay());

        await engine.RunAsync(OneClickProject());

        Assert.Equal(RunnerState.Idle, engine.State);
        Assert.Equal(1, executor.ExecuteCount);
        AssertTransitionSequence(
            engine.TransitionLog,
            RunnerState.Idle,        // ctor
            RunnerState.Resolving,
            RunnerState.Verifying,
            RunnerState.Executing,
            RunnerState.PostCheck,
            RunnerState.Idle,        // step done
            RunnerState.Idle);       // run complete
    }

    [Fact]
    public async Task RetryExhaustion_GoesToFailedStepThenAborted()
    {
        var resolver = new MockResolver(_ => new ResolveResult(false));
        var verifier = new MockVerifier(before: true, after: true);
        var executor = new MockExecutor();
        var engine = new RunnerEngine(resolver, verifier, executor, new ImmediateRunnerDelay());

        var project = OneClickProject(maxAttempts: 3, backoffMs: 0);
        await engine.RunAsync(project);

        Assert.Equal(RunnerState.Aborted, engine.State);
        Assert.Equal(0, executor.ExecuteCount);
        Assert.Equal(3, resolver.CallCount);

        var log = engine.TransitionLog;
        Assert.Contains(RunnerState.Retrying, log);
        Assert.Contains(RunnerState.FailedStep, log);
        Assert.Equal(RunnerState.Aborted, log[^1]);

        // Resolving attempted 3 times with Retrying between the first two failures.
        Assert.Equal(3, log.Count(s => s == RunnerState.Resolving));
        Assert.Equal(2, log.Count(s => s == RunnerState.Retrying));
        Assert.Equal(1, log.Count(s => s == RunnerState.FailedStep));
    }

    [Fact]
    public async Task EmergencyAbort_FromResolving_GoesToAborted()
    {
        var resolveStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowResolve = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var resolver = new MockResolver(async (_, ct) =>
        {
            resolveStarted.TrySetResult();
            await allowResolve.Task.WaitAsync(ct).ConfigureAwait(false);
            return new ResolveResult(true, "late");
        });

        var engine = new RunnerEngine(
            resolver,
            new MockVerifier(true, true),
            new MockExecutor(),
            new ImmediateRunnerDelay());

        var runTask = engine.RunAsync(OneClickProject());
        await resolveStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        engine.RequestAbort();
        allowResolve.TrySetResult();

        await runTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(RunnerState.Aborted, engine.State);
        Assert.Contains(RunnerState.Resolving, engine.TransitionLog);
        Assert.Equal(RunnerState.Aborted, engine.TransitionLog[^1]);
        Assert.DoesNotContain(RunnerState.Executing, engine.TransitionLog);
    }

    [Fact]
    public async Task EmergencyAbort_FromExecuting_GoesToAborted()
    {
        var executeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowExecute = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var executor = new MockExecutor(async (_, _, ct) =>
        {
            executeStarted.TrySetResult();
            await allowExecute.Task.WaitAsync(ct).ConfigureAwait(false);
        });

        var engine = new RunnerEngine(
            new MockResolver(_ => new ResolveResult(true, "el")),
            new MockVerifier(true, true),
            executor,
            new ImmediateRunnerDelay());

        var runTask = engine.RunAsync(OneClickProject());
        await executeStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(RunnerState.Executing, engine.State);
        engine.RequestAbort();
        allowExecute.TrySetCanceled();

        await runTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(RunnerState.Aborted, engine.State);
        Assert.Contains(RunnerState.Executing, engine.TransitionLog);
        Assert.Equal(RunnerState.Aborted, engine.TransitionLog[^1]);
        Assert.DoesNotContain(RunnerState.PostCheck, engine.TransitionLog);
    }

    [Fact]
    public async Task VerifyBeforeFails_RetriesThenFailedStep()
    {
        var resolver = new MockResolver(_ => new ResolveResult(true, "el"));
        var verifier = new MockVerifier(before: false, after: true);
        var executor = new MockExecutor();
        var engine = new RunnerEngine(resolver, verifier, executor, new ImmediateRunnerDelay());

        await engine.RunAsync(OneClickProject(maxAttempts: 2, backoffMs: 0));

        Assert.Equal(0, executor.ExecuteCount);
        Assert.Contains(RunnerState.Retrying, engine.TransitionLog);
        Assert.Contains(RunnerState.FailedStep, engine.TransitionLog);
        Assert.Equal(RunnerState.Aborted, engine.State);
    }

    [Fact]
    public async Task PauseDuringWait_HoldsBeforeNextStepUntilResume()
    {
        var waitStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowWaitFinish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var resolver = new MockResolver(step => new ResolveResult(true, step.Id));
        var executor = new MockExecutor(async (step, _, ct) =>
        {
            if (step.Id == "wait-before")
            {
                waitStarted.TrySetResult();
                await allowWaitFinish.Task.WaitAsync(ct).ConfigureAwait(false);
            }
        });
        var delay = new ControllableDelay();
        var engine = new RunnerEngine(resolver, new MockVerifier(true, true), executor, delay);

        var project = new ProjectDocument
        {
            SchemaVersion = ProjectSchema.CurrentVersion,
            Name = "pause-mid",
            Defaults = new ProjectDefaults
            {
                TimeoutMs = 0,
                Retry = new RetryPolicy { MaxAttempts = 1, BackoffMs = 0 },
            },
            Steps =
            [
                new ScriptStep { Id = "wait-before", Type = "Wait", WaitMs = 4000 },
                new ScriptStep
                {
                    Id = "click-submit",
                    Type = "Click",
                    Locator = new LocatorChain
                    {
                        Layers = [new LocatorLayer { Kind = "UiaStructural", AutomationId = "TbSubmit" }],
                    },
                },
                new ScriptStep { Id = "wait-after", Type = "Wait", WaitMs = 100 },
            ],
        };

        var runTask = engine.RunAsync(project);
        await waitStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(RunnerState.Executing, engine.State);
        engine.RequestPause();
        Assert.Equal(1, executor.ExecuteCount);

        allowWaitFinish.TrySetResult();
        await WaitForStateAsync(engine, RunnerState.Paused, TimeSpan.FromSeconds(5), delay);
        Assert.Equal(1, executor.ExecuteCount);

        engine.RequestResume();
        delay.ReleaseAll();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(RunnerState.Idle, engine.State);
        Assert.Equal(3, executor.ExecuteCount);
        Assert.Contains(RunnerState.Paused, engine.TransitionLog);
    }

    [Fact]
    public async Task PerAttemptTimeout_PollsThenRetriesThenFailedStep()
    {
        var resolver = new MockResolver(_ => new ResolveResult(false, FailureReason: "missing"));
        var executor = new MockExecutor();
        var engine = new RunnerEngine(resolver, new MockVerifier(true, true), executor, new SystemRunnerDelay());

        var project = OneClickProject(maxAttempts: 3, backoffMs: 50, timeoutMs: 100);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        await engine.RunAsync(project);
        sw.Stop();

        Assert.Equal(RunnerState.Aborted, engine.State);
        Assert.Contains(RunnerState.FailedStep, engine.TransitionLog);
        Assert.Equal(0, executor.ExecuteCount);
        Assert.Equal(3, engine.TransitionLog.Count(s => s == RunnerState.Resolving));
        Assert.Equal(2, engine.TransitionLog.Count(s => s == RunnerState.Retrying));
        // ~3×100ms timeout + 2×50ms backoff; allow resolve overhead.
        Assert.InRange(sw.ElapsedMilliseconds, 250, 3000);
        Assert.True(resolver.CallCount >= 3);
    }

    [Fact]
    public async Task PauseBeforeFirstStep_ResumeContinues()
    {
        var resolver = new MockResolver(_ => new ResolveResult(true, "el"));
        var executor = new MockExecutor();
        var delay = new ControllableDelay();
        var engine = new RunnerEngine(resolver, new MockVerifier(true, true), executor, delay);

        engine.RequestPause();
        var runTask = engine.RunAsync(OneClickProject(maxAttempts: 1));

        await WaitForStateAsync(engine, RunnerState.Paused, TimeSpan.FromSeconds(5), delay);
        Assert.Equal(0, executor.ExecuteCount);

        engine.RequestResume();
        delay.ReleaseAll();

        await runTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(RunnerState.Idle, engine.State);
        Assert.Equal(1, executor.ExecuteCount);
        Assert.Contains(RunnerState.Paused, engine.TransitionLog);
    }

    [Fact]
    public async Task RecoverySkip_ContinuesWithNextStep()
    {
        var resolver = new MockResolver(step =>
            step.Id == "miss"
                ? new ResolveResult(false, FailureReason: "missing")
                : new ResolveResult(true, step.Id));
        var executor = new MockExecutor();
        var engine = new RunnerEngine(resolver, new MockVerifier(true, true), executor, new ImmediateRunnerDelay());

        var project = new ProjectDocument
        {
            SchemaVersion = ProjectSchema.CurrentVersion,
            Name = "recovery-skip",
            Defaults = new ProjectDefaults
            {
                TimeoutMs = 0,
                Retry = new RetryPolicy { MaxAttempts = 1, BackoffMs = 0 },
            },
            Steps =
            [
                new ScriptStep
                {
                    Id = "miss",
                    Type = "Click",
                    Recovery = new StepRecovery { Action = StepRecoveryActions.Skip },
                    Locator = new LocatorChain
                    {
                        Layers = [new LocatorLayer { Kind = "UiaStructural", AutomationId = "No" }],
                    },
                },
                new ScriptStep { Id = "after", Type = "Wait", WaitMs = 1 },
            ],
        };

        await engine.RunAsync(project);

        Assert.Equal(RunnerState.Idle, engine.State);
        Assert.Equal(["after"], executor.ExecutedStepIds);
        Assert.Contains(RunnerState.FailedStep, engine.TransitionLog);
        Assert.DoesNotContain(RunnerState.Aborted, engine.TransitionLog);
    }

    [Fact]
    public async Task RecoveryJump_ReachesLabeledStep_SkipsInBetween()
    {
        var resolver = new MockResolver(step =>
            step.Id == "miss"
                ? new ResolveResult(false, FailureReason: "missing")
                : new ResolveResult(true, step.Id));
        var executor = new MockExecutor();
        var engine = new RunnerEngine(resolver, new MockVerifier(true, true), executor, new ImmediateRunnerDelay());

        var project = new ProjectDocument
        {
            SchemaVersion = ProjectSchema.CurrentVersion,
            Name = "recovery-jump",
            Defaults = new ProjectDefaults
            {
                TimeoutMs = 0,
                Retry = new RetryPolicy { MaxAttempts = 1, BackoffMs = 0 },
            },
            Steps =
            [
                new ScriptStep
                {
                    Id = "miss",
                    Type = "Click",
                    Recovery = new StepRecovery
                    {
                        Action = StepRecoveryActions.Jump,
                        JumpTo = "landing",
                    },
                    Locator = new LocatorChain
                    {
                        Layers = [new LocatorLayer { Kind = "UiaStructural", AutomationId = "No" }],
                    },
                },
                new ScriptStep { Id = "skipped", Type = "Wait", WaitMs = 1 },
                new ScriptStep { Id = "landing", Type = "Wait", WaitMs = 1 },
            ],
        };

        await engine.RunAsync(project);

        Assert.Equal(RunnerState.Idle, engine.State);
        Assert.Equal(["landing"], executor.ExecutedStepIds);
        Assert.Contains(RunnerState.FailedStep, engine.TransitionLog);
        Assert.DoesNotContain(RunnerState.Aborted, engine.TransitionLog);
    }

    [Fact]
    public async Task RecoveryAbort_StopsWithoutLaterSteps()
    {
        var resolver = new MockResolver(step =>
            step.Id == "miss"
                ? new ResolveResult(false, FailureReason: "missing")
                : new ResolveResult(true, step.Id));
        var executor = new MockExecutor();
        var engine = new RunnerEngine(resolver, new MockVerifier(true, true), executor, new ImmediateRunnerDelay());

        var project = new ProjectDocument
        {
            SchemaVersion = ProjectSchema.CurrentVersion,
            Name = "recovery-abort",
            Defaults = new ProjectDefaults
            {
                TimeoutMs = 0,
                Retry = new RetryPolicy { MaxAttempts = 1, BackoffMs = 0 },
            },
            Steps =
            [
                new ScriptStep
                {
                    Id = "miss",
                    Type = "Click",
                    Recovery = new StepRecovery { Action = StepRecoveryActions.Abort },
                    Locator = new LocatorChain
                    {
                        Layers = [new LocatorLayer { Kind = "UiaStructural", AutomationId = "No" }],
                    },
                },
                new ScriptStep { Id = "should-not-run", Type = "Wait", WaitMs = 1 },
            ],
        };

        await engine.RunAsync(project);

        Assert.Equal(RunnerState.Aborted, engine.State);
        Assert.Empty(executor.ExecutedStepIds);
        Assert.Contains(RunnerState.FailedStep, engine.TransitionLog);
        Assert.Equal(RunnerState.Aborted, engine.TransitionLog[^1]);
    }

    [Fact]
    public async Task RecoveryJump_MissingTarget_Aborts()
    {
        var resolver = new MockResolver(_ => new ResolveResult(false, FailureReason: "missing"));
        var executor = new MockExecutor();
        var engine = new RunnerEngine(resolver, new MockVerifier(true, true), executor, new ImmediateRunnerDelay());

        var project = OneClickProject(maxAttempts: 1);
        project.Steps[0].Recovery = new StepRecovery
        {
            Action = StepRecoveryActions.Jump,
            JumpTo = "does-not-exist",
        };

        await engine.RunAsync(project);

        Assert.Equal(RunnerState.Aborted, engine.State);
        Assert.Empty(executor.ExecutedStepIds);
    }

    private static async Task WaitForStateAsync(
        RunnerEngine engine,
        RunnerState expected,
        TimeSpan timeout,
        ControllableDelay? delay = null)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (engine.State == expected)
            {
                return;
            }

            delay?.ReleaseOne();
            await Task.Delay(10);
        }

        Assert.Fail($"Timed out waiting for state {expected}; last was {engine.State}");
    }

    /// <summary>Delay that completes only when Release is called (for pause-loop tests).</summary>
    private sealed class ControllableDelay : IRunnerDelay
    {
        private readonly object _gate = new();
        private TaskCompletionSource _pulse = NewPulse();
        private volatile bool _passthrough;

        public void ReleaseOne()
        {
            TaskCompletionSource current;
            lock (_gate)
            {
                current = _pulse;
                _pulse = NewPulse();
            }

            current.TrySetResult();
        }

        public void ReleaseAll()
        {
            _passthrough = true;
            ReleaseOne();
        }

        public Task DelayAsync(int milliseconds, CancellationToken cancellationToken)
        {
            if (_passthrough)
            {
                return cancellationToken.IsCancellationRequested
                    ? Task.FromCanceled(cancellationToken)
                    : Task.CompletedTask;
            }

            TaskCompletionSource pulse;
            lock (_gate)
            {
                pulse = _pulse;
            }

            return pulse.Task.WaitAsync(cancellationToken);
        }

        private static TaskCompletionSource NewPulse() =>
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private static ProjectDocument OneClickProject(int maxAttempts = 3, int backoffMs = 0, int timeoutMs = 0) => new()
    {
        SchemaVersion = ProjectSchema.CurrentVersion,
        Name = "runner-test",
        Defaults = new ProjectDefaults
        {
            TimeoutMs = timeoutMs,
            Retry = new RetryPolicy { MaxAttempts = maxAttempts, BackoffMs = backoffMs },
        },
        Steps =
        [
            new ScriptStep
            {
                Id = "click-1",
                Type = "Click",
                TimeoutMs = timeoutMs,
                Locator = new LocatorChain
                {
                    Layers =
                    [
                        new LocatorLayer
                        {
                            Kind = "UiaStructural",
                            AutomationId = "TbSubmit",
                        },
                    ],
                },
            },
        ],
    };

    private static void AssertTransitionSequence(IReadOnlyList<RunnerState> actual, params RunnerState[] expected)
    {
        Assert.Equal(expected, actual.ToArray());
    }

    private sealed class MockResolver : ITargetResolver
    {
        private readonly Func<ScriptStep, CancellationToken, Task<ResolveResult>> _impl;

        public MockResolver(Func<ScriptStep, ResolveResult> sync)
            : this((step, _) => Task.FromResult(sync(step)))
        {
        }

        public MockResolver(Func<ScriptStep, CancellationToken, Task<ResolveResult>> impl)
        {
            _impl = impl;
        }

        public int CallCount { get; private set; }

        public async Task<ResolveResult> ResolveAsync(ScriptStep step, CancellationToken cancellationToken)
        {
            CallCount++;
            return await _impl(step, cancellationToken).ConfigureAwait(false);
        }
    }

    private sealed class MockVerifier : IStepVerifier
    {
        private readonly bool _before;
        private readonly bool _after;

        public MockVerifier(bool before, bool after)
        {
            _before = before;
            _after = after;
        }

        public Task<bool> VerifyBeforeExecuteAsync(ScriptStep step, ResolveResult candidate, CancellationToken cancellationToken) =>
            Task.FromResult(_before);

        public Task<bool> VerifyAfterExecuteAsync(ScriptStep step, ResolveResult candidate, CancellationToken cancellationToken) =>
            Task.FromResult(_after);
    }

    private sealed class MockExecutor : IStepExecutor
    {
        private readonly Func<ScriptStep, ResolveResult, CancellationToken, Task>? _impl;

        public MockExecutor(Func<ScriptStep, ResolveResult, CancellationToken, Task>? impl = null)
        {
            _impl = impl;
        }

        public int ExecuteCount { get; private set; }

        public List<string> ExecutedStepIds { get; } = [];

        public async Task ExecuteAsync(ScriptStep step, ResolveResult candidate, CancellationToken cancellationToken)
        {
            ExecuteCount++;
            ExecutedStepIds.Add(step.Id);
            if (_impl is not null)
            {
                await _impl(step, candidate, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}