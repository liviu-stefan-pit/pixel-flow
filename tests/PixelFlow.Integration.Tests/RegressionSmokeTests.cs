using System.Diagnostics;
using System.Windows.Automation;
using PixelFlow.Core.Diagnostics;
using PixelFlow.Core.Runner;
using PixelFlow.Integration.Tests.Infrastructure;
using PixelFlow.Studio;

namespace PixelFlow.Integration.Tests;

/// <summary>
/// Cross-phase regression smoke: multi-step flows, retry budgets, resolve CLI, sequential
/// locator paths, and Studio session reuse. Complements the per-fixture matrix.
/// </summary>
[Collection(TestBenchCollection.Name)]
public sealed class RegressionSmokeTests
{
    private readonly TestBenchFixture _bench;

    public RegressionSmokeTests(TestBenchFixture bench)
    {
        _bench = bench;
    }

    [SkippableFact]
    [Trait("Category", "Live")]
    public async Task SmokeClickThenType_BothStepsSucceed()
    {
        Skip.IfNot(_bench.IsAvailable, _bench.UnavailableReason ?? "PixelFlow.TestBench unavailable.");
        _bench.EnsureForeground();
        ClearTbInput();

        using var workspace = FixtureWorkspace.CreateCopy("smoke-click-type");
        var result = await RunnerCli.RunProjectAsync(workspace.ProjectFolder);

        Assert.True(
            result.ExitCode == 0,
            $"exit {result.ExitCode}. stdout:\n{result.StdOut}\nstderr:\n{result.StdErr}");

        var events = ReadLatestEvents(workspace.ProjectFolder);
        var click = Assert.Single(events, e => e.Event == RunReportEventNames.StepFinished && e.StepId == "click-submit");
        Assert.Equal(RunReportOutcomes.Succeeded, click.Outcome);
        Assert.Equal(LocatorKinds.UiaStructural, click.MatchedLayer);

        var type = Assert.Single(events, e => e.Event == RunReportEventNames.StepFinished && e.StepId == "type-input");
        Assert.Equal(RunReportOutcomes.Succeeded, type.Outcome);
        Assert.Equal(LocatorKinds.UiaStructural, type.MatchedLayer);

        Assert.Equal("smoke-e2e", ReadTbInputValue());

        var runFinished = Assert.Single(events, e => e.Event == RunReportEventNames.RunFinished);
        Assert.Equal(RunReportOutcomes.Succeeded, runFinished.Outcome);
    }

    [SkippableFact]
    [Trait("Category", "Live")]
    public async Task RetryMiss_ExhaustsThreeAttempts_WithinBoundedWallTime()
    {
        Skip.IfNot(_bench.IsAvailable, _bench.UnavailableReason ?? "PixelFlow.TestBench unavailable.");
        _bench.EnsureForeground();

        using var workspace = FixtureWorkspace.CreateCopy("retry-miss");
        var result = await RunnerCli.RunProjectAsync(workspace.ProjectFolder, timeout: TimeSpan.FromSeconds(20));

        Assert.Equal(3, result.ExitCode);
        // 3 attempts × 200ms timeout + 2 × 100ms backoff ≈ ~0.8s; allow generous headroom, not unbounded.
        Assert.True(
            result.Elapsed < TimeSpan.FromSeconds(8),
            $"Retry budget looked unbounded: elapsed {result.Elapsed.TotalSeconds:0.00}s");

        var events = ReadLatestEvents(workspace.ProjectFolder);
        var finished = Assert.Single(events, e => e.Event == RunReportEventNames.StepFinished && e.StepId == "click-missing");
        Assert.Equal(RunReportOutcomes.Failed, finished.Outcome);
        Assert.Equal(3, finished.Attempts);

        var attempts = events.Count(e => e.Event == RunReportEventNames.ResolveAttempt && e.StepId == "click-missing");
        Assert.Equal(3, attempts);
    }

    [SkippableFact]
    [Trait("Category", "Live")]
    public async Task ResolveCli_FindsTbSubmit()
    {
        Skip.IfNot(_bench.IsAvailable, _bench.UnavailableReason ?? "PixelFlow.TestBench unavailable.");
        _bench.EnsureForeground();

        var result = await RunnerCli.ResolveAsync("TbSubmit");
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("[resolve] FOUND", result.StdOut, StringComparison.Ordinal);
        Assert.Contains("TbSubmit", result.StdOut, StringComparison.Ordinal);
    }

    [SkippableFact]
    [Trait("Category", "Live")]
    public async Task ResolveCli_MissingId_Exits2_WithoutGuess()
    {
        Skip.IfNot(_bench.IsAvailable, _bench.UnavailableReason ?? "PixelFlow.TestBench unavailable.");
        _bench.EnsureForeground();

        var result = await RunnerCli.ResolveAsync("TbDoesNotExist");
        Assert.Equal(2, result.ExitCode);
        Assert.Contains("NOT FOUND", result.StdOut, StringComparison.Ordinal);
    }

    [SkippableFact]
    [Trait("Category", "Live")]
    public async Task SequentialLocatorFixtures_UiaThenWin32ThenOcr_AllSucceed()
    {
        Skip.IfNot(_bench.IsAvailable, _bench.UnavailableReason ?? "PixelFlow.TestBench unavailable.");
        _bench.EnsureForeground();

        // Same Test Bench process across three locator paths — catches shared-state / DPI / display-gen regressions.
        foreach (var (fixture, stepId, layer) in new[]
                 {
                     ("click-submit", "click-submit", LocatorKinds.UiaStructural),
                     ("win32-click", "click-win32", LocatorKinds.Win32),
                     ("ocr-click", "click-ocr", LocatorKinds.Ocr),
                 })
        {
            _bench.EnsureForeground();
            using var workspace = FixtureWorkspace.CreateCopy(fixture);
            var result = await RunnerCli.RunProjectAsync(workspace.ProjectFolder);
            Assert.True(
                result.ExitCode == 0,
                $"Fixture '{fixture}' exit {result.ExitCode}. stdout:\n{result.StdOut}\nstderr:\n{result.StdErr}");

            var events = ReadLatestEvents(workspace.ProjectFolder);
            var finished = Assert.Single(events, e => e.Event == RunReportEventNames.StepFinished && e.StepId == stepId);
            Assert.Equal(RunReportOutcomes.Succeeded, finished.Outcome);
            Assert.Equal(layer, finished.MatchedLayer);
            Assert.Contains("displayGen=", result.StdOut, StringComparison.Ordinal);
        }
    }

    [SkippableFact]
    [Trait("Category", "Live")]
    public async Task SuccessfulClick_EmitsResolveAttemptAndDisplayGen()
    {
        Skip.IfNot(_bench.IsAvailable, _bench.UnavailableReason ?? "PixelFlow.TestBench unavailable.");
        _bench.EnsureForeground();

        using var workspace = FixtureWorkspace.CreateCopy("click-submit");
        var result = await RunnerCli.RunProjectAsync(workspace.ProjectFolder);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("displayGen=", result.StdOut, StringComparison.Ordinal);
        Assert.Contains("Display-change watcher armed", result.StdOut, StringComparison.Ordinal);

        var events = ReadLatestEvents(workspace.ProjectFolder);
        Assert.Contains(events, e => e.Event == RunReportEventNames.ResolveAttempt && e.Found == true);
        var finished = Assert.Single(events, e => e.Event == RunReportEventNames.StepFinished);
        Assert.Equal(RunReportOutcomes.Succeeded, finished.Outcome);
        Assert.Equal(LocatorKinds.UiaStructural, finished.MatchedLayer);
    }

    [SkippableFact]
    [Trait("Category", "Live")]
    public async Task StudioIpc_FailedRunThenSuccessfulRun_SameSession()
    {
        Skip.IfNot(_bench.IsAvailable, _bench.UnavailableReason ?? "PixelFlow.TestBench unavailable.");
        _bench.EnsureForeground();

        using var miss = FixtureWorkspace.CreateCopy("retry-miss");
        using var hit = FixtureWorkspace.CreateCopy("click-submit");
        await using var session = new RunnerSession();
        await session.ConnectAsync();
        Assert.True(session.IsConnected);

        await session.RunProjectAsync(miss.ProjectFolder);
        await WaitUntilAsync(() => !session.IsRunInProgress, TimeSpan.FromSeconds(20));
        var missEvents = ReadLatestEvents(miss.ProjectFolder);
        var missFinished = Assert.Single(missEvents, e => e.Event == RunReportEventNames.RunFinished);
        Assert.Equal(RunReportOutcomes.Failed, missFinished.Outcome);
        Assert.True(session.IsConnected);

        await session.RunProjectAsync(hit.ProjectFolder);
        await WaitUntilAsync(() => !session.IsRunInProgress, TimeSpan.FromSeconds(15));
        var hitEvents = ReadLatestEvents(hit.ProjectFolder);
        var hitFinished = Assert.Single(hitEvents, e => e.Event == RunReportEventNames.RunFinished);
        Assert.Equal(RunReportOutcomes.Succeeded, hitFinished.Outcome);
        Assert.True(session.IsConnected);
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

    private static void ClearTbInput()
    {
        var edit = FindTbInput();
        if (edit.TryGetCurrentPattern(ValuePattern.Pattern, out var patternObj)
            && patternObj is ValuePattern value)
        {
            value.SetValue("");
        }
    }

    private static string ReadTbInputValue()
    {
        var edit = FindTbInput();
        if (edit.TryGetCurrentPattern(ValuePattern.Pattern, out var patternObj)
            && patternObj is ValuePattern value)
        {
            return value.Current.Value ?? "";
        }

        return edit.Current.Name ?? "";
    }

    private static AutomationElement FindTbInput()
    {
        var root = AutomationElement.RootElement;
        var window = root.FindFirst(
            TreeScope.Children,
            new AndCondition(
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window),
                new PropertyCondition(AutomationElement.NameProperty, "Test Bench")));
        Assert.NotNull(window);

        var edit = window.FindFirst(
            TreeScope.Descendants,
            new PropertyCondition(AutomationElement.AutomationIdProperty, "TbInput"));
        Assert.NotNull(edit);
        return edit;
    }
}
