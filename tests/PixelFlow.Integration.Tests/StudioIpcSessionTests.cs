using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using PixelFlow.Core.Diagnostics;
using PixelFlow.Integration.Tests.Infrastructure;
using PixelFlow.Studio;

namespace PixelFlow.Integration.Tests;

/// <summary>
/// Exercises the Studio↔Runner IPC contract through <see cref="RunnerSession"/> — the exact class
/// Studio's Run/Pause/Resume/Stop buttons call — without driving any WPF UI.
/// </summary>
[Collection(TestBenchCollection.Name)]
public sealed class StudioIpcSessionTests
{
    private readonly TestBenchFixture _bench;

    public StudioIpcSessionTests(TestBenchFixture bench)
    {
        _bench = bench;
    }

    [SkippableFact]
    [Trait("Category", "Live")]
    public async Task Run_ConnectsAndCompletesSuccessfully()
    {
        Skip.IfNot(_bench.IsAvailable, _bench.UnavailableReason ?? "PixelFlow.TestBench unavailable.");
        _bench.EnsureForeground();

        using var workspace = FixtureWorkspace.CreateCopy("click-submit");
        await using var session = new RunnerSession();

        await session.ConnectAsync();
        Assert.True(session.IsConnected);

        await session.RunProjectAsync(workspace.ProjectFolder);
        await WaitUntilAsync(() => !session.IsRunInProgress, TimeSpan.FromSeconds(15));

        var events = ReadLatestEvents(workspace.ProjectFolder);
        var runFinished = Assert.Single(events, e => e.Event == RunReportEventNames.RunFinished);
        Assert.Equal(RunReportOutcomes.Succeeded, runFinished.Outcome);
    }

    [SkippableFact]
    [Trait("Category", "Live")]
    public async Task PauseResume_HoldsBeforeClickThenCompletes()
    {
        Skip.IfNot(_bench.IsAvailable, _bench.UnavailableReason ?? "PixelFlow.TestBench unavailable.");
        _bench.EnsureForeground();

        // wait-before (4s) -> click-submit -> wait-after (4s). Pause is honored only between
        // steps, so pausing during wait-before holds the run right before click-submit runs.
        using var workspace = FixtureWorkspace.CreateCopy("pause-resume");
        await using var session = new RunnerSession();
        await session.ConnectAsync();

        await session.RunProjectAsync(workspace.ProjectFolder);

        await Task.Delay(TimeSpan.FromMilliseconds(500));
        await session.PauseAsync();
        await WaitUntilAsync(() => session.LastRunnerState == "Paused", TimeSpan.FromSeconds(15));

        var beforeResume = ReadLatestEvents(workspace.ProjectFolder);
        Assert.DoesNotContain(
            beforeResume,
            e => e.Event == RunReportEventNames.StepFinished && e.StepId == "click-submit");

        await session.ResumeAsync();
        await WaitUntilAsync(() => !session.IsRunInProgress, TimeSpan.FromSeconds(15));

        var afterResume = ReadLatestEvents(workspace.ProjectFolder);
        var clickFinished = Assert.Single(
            afterResume,
            e => e.Event == RunReportEventNames.StepFinished && e.StepId == "click-submit");
        Assert.Equal(RunReportOutcomes.Succeeded, clickFinished.Outcome);

        var runFinished = Assert.Single(afterResume, e => e.Event == RunReportEventNames.RunFinished);
        Assert.Equal(RunReportOutcomes.Succeeded, runFinished.Outcome);
    }

    [SkippableFact]
    [Trait("Category", "Live")]
    public async Task UserInterference_BeforeClick_PausesWithoutClick_ThenResumeCompletes()
    {
        Skip.IfNot(_bench.IsAvailable, _bench.UnavailableReason ?? "PixelFlow.TestBench unavailable.");
        _bench.EnsureForeground();

        // wait-before (2.5s) → click-submit. Nudge the mouse near the end of the wait so
        // GetLastInputInfo is recent when the Runner reaches the click action gate.
        using var workspace = FixtureWorkspace.CreateCopy("interference-pause");
        await using var session = new RunnerSession();
        await session.ConnectAsync();

        await session.RunProjectAsync(workspace.ProjectFolder);

        // Inject mouse moves so at least one lands in the ~80ms pre-click observe window.
        await Task.Delay(TimeSpan.FromMilliseconds(2300));
        var nudgeDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (session.LastRunnerState != "Paused" && DateTime.UtcNow < nudgeDeadline)
        {
            SyntheticUserInput.NudgeMouse();
            await Task.Delay(TimeSpan.FromMilliseconds(25));
        }

        await WaitUntilAsync(() => session.LastRunnerState == "Paused", TimeSpan.FromSeconds(5));

        var beforeResume = ReadLatestEvents(workspace.ProjectFolder);
        Assert.Contains(beforeResume, e => e.Event == RunReportEventNames.InterferencePaused);
        Assert.DoesNotContain(
            beforeResume,
            e => e.Event == RunReportEventNames.StepFinished && e.StepId == "click-submit");

        await session.ResumeAsync();
        await WaitUntilAsync(() => !session.IsRunInProgress, TimeSpan.FromSeconds(15));

        var afterResume = ReadLatestEvents(workspace.ProjectFolder);
        Assert.Contains(afterResume, e => e.Event == RunReportEventNames.InterferencePaused);
        var clickFinished = Assert.Single(
            afterResume,
            e => e.Event == RunReportEventNames.StepFinished && e.StepId == "click-submit");
        Assert.Equal(RunReportOutcomes.Succeeded, clickFinished.Outcome);

        var runFinished = Assert.Single(afterResume, e => e.Event == RunReportEventNames.RunFinished);
        Assert.Equal(RunReportOutcomes.Succeeded, runFinished.Outcome);

        var summary = RunReportStore.FormatSummary(
            RunReportStore.FindLatestReportDirectory(workspace.ProjectFolder)
            ?? throw new InvalidOperationException("missing report"));
        Assert.Contains("User interference pauses:", summary, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Live")]
    public async Task Stop_AbortsDuringWait_LaterStepsDoNotRun()
    {
        // ipc-wait: three 4s Wait steps, no Test Bench dependency. Stop mid-Wait cancels
        // immediately (unlike Pause, which only takes effect between steps).
        using var workspace = FixtureWorkspace.CreateCopy("ipc-wait");
        await using var session = new RunnerSession();
        await session.ConnectAsync();

        await session.RunProjectAsync(workspace.ProjectFolder);
        await Task.Delay(TimeSpan.FromMilliseconds(500));
        await session.StopAsync();

        await WaitUntilAsync(() => session.LastRunnerState == "Aborted", TimeSpan.FromSeconds(15));

        var events = ReadLatestEvents(workspace.ProjectFolder);
        Assert.DoesNotContain(events, e => e.Event == RunReportEventNames.StepStarted && e.StepId == "wait-2");
        Assert.DoesNotContain(events, e => e.Event == RunReportEventNames.StepStarted && e.StepId == "wait-3");

        var runFinished = Assert.Single(events, e => e.Event == RunReportEventNames.RunFinished);
        Assert.Equal(RunReportOutcomes.Aborted, runFinished.Outcome);
    }

    [Fact]
    [Trait("Category", "Live")]
    public async Task KillRunnerProcess_SessionReportsDisconnected_WithoutHanging()
    {
        using var workspace = FixtureWorkspace.CreateCopy("ipc-wait");
        await using var session = new RunnerSession();
        await session.ConnectAsync();

        var disconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        session.Disconnected += () => disconnected.TrySetResult();

        await session.RunProjectAsync(workspace.ProjectFolder);

        var pid = session.RunnerProcessId;
        Assert.NotNull(pid);
        using (var runnerProcess = Process.GetProcessById(pid!.Value))
        {
            runnerProcess.Kill(entireProcessTree: true);
        }

        var completed = await Task.WhenAny(disconnected.Task, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.Same(disconnected.Task, completed);
        Assert.False(session.IsConnected);
    }

    private static IReadOnlyList<RunReportEvent> ReadLatestEvents(string projectFolder)
    {
        var reportDir = RunReportStore.FindLatestReportDirectory(projectFolder)
            ?? throw new InvalidOperationException($"No report directory under {projectFolder}.");
        return RunReportStore.ReadEvents(RunReportStore.EventsPath(reportDir));
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var sw = Stopwatch.StartNew();
        while (!condition())
        {
            if (sw.Elapsed > timeout)
            {
                throw new TimeoutException($"Condition not met within {timeout}.");
            }

            await Task.Delay(100).ConfigureAwait(false);
        }
    }
}
