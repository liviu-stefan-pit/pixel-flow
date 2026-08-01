using PixelFlow.Core.Coordinates;
using PixelFlow.Core.Diagnostics;
using PixelFlow.Core.Runner;
using PixelFlow.Integration.Tests.Infrastructure;
using PixelFlow.Runner.Automation;

namespace PixelFlow.Integration.Tests;

/// <summary>
/// P27: display-change invalidation busts absolute coordinate cache and forces re-resolve
/// (simulated via <see cref="IDisplayChangeTracker.Invalidate"/> — physical unplug not required).
/// </summary>
[Collection(TestBenchCollection.Name)]
public sealed class DisplayChangeInvalidationTests
{
    private readonly TestBenchFixture _bench;

    public DisplayChangeInvalidationTests(TestBenchFixture bench)
    {
        _bench = bench;
    }

    [Fact]
    [Trait("Category", "Live")]
    public void DisplayChangeWatcher_CapturesNonEmptyVirtualDesktopTopology()
    {
        var topology = DisplayChangeWatcher.CaptureTopology();
        Assert.False(topology.IsEmpty);
        Assert.True(topology.VirtualWidth > 1);
        Assert.True(topology.VirtualHeight > 1);
        Assert.True(topology.MonitorCount >= 1);
    }

    [Fact]
    [Trait("Category", "Live")]
    public void DisplayChangeWatcher_StartsAndReportsTopology()
    {
        var tracker = new DisplayChangeTracker(DisplayChangeWatcher.CaptureTopology());
        using var watcher = new DisplayChangeWatcher(tracker);
        Assert.False(tracker.Snapshot.IsEmpty);
        Assert.Equal(1, tracker.Generation);
    }

    [SkippableFact]
    [Trait("Category", "Live")]
    public async Task SimulatedDisplayChange_BustsCache_AndClickStillSucceedsViaReresolve()
    {
        Skip.IfNot(_bench.IsAvailable, _bench.UnavailableReason ?? "PixelFlow.TestBench unavailable.");
        _bench.EnsureForeground();

        var display = new DisplayChangeTracker(DisplayChangeWatcher.CaptureTopology());
        var cache = new AbsoluteCoordinateCache(display);
        var services = new LiveStepServices(
            projectFolder: null,
            display: display,
            coordinateCache: cache);

        using var workspace = FixtureWorkspace.CreateCopy("click-submit");
        var store = new PixelFlow.Core.Projects.ProjectStore();
        var project = store.Load(workspace.ProjectFolder);
        var step = Assert.Single(project.Steps);

        // Resolve once — cache should hold absolute bounds under gen 1.
        var first = await services.ResolveAsync(step, CancellationToken.None);
        Assert.True(first.Found, first.FailureReason);
        Assert.Equal(1, first.DisplayGeneration);
        Assert.True(cache.TryGet(step.Id, out var cachedBefore));
        Assert.False(cachedBefore.IsEmpty);

        // Simulate monitor/resolution change between resolve and execute.
        display.Invalidate(
            new DisplayTopology(
                display.Snapshot.VirtualLeft,
                display.Snapshot.VirtualTop,
                display.Snapshot.VirtualWidth + 1,
                display.Snapshot.VirtualHeight,
                display.Snapshot.MonitorCount),
            "test-simulate-display-change");

        Assert.Equal(2, display.Generation);
        Assert.True(display.IsStale(first.DisplayGeneration));
        Assert.False(cache.TryGet(step.Id, out _), "Cache must be empty after display invalidation.");
        Assert.True(cache.InvalidationCount >= 1);

        // Execute must refuse stale candidate coords, re-resolve, and still click correctly.
        var counterBefore = ReadClickCount();
        await services.ExecuteAsync(step, first, CancellationToken.None);
        var counterAfter = ReadClickCount();

        Assert.Equal(counterBefore + 1, counterAfter);

        // Fresh stamp after re-resolve should match current generation.
        Assert.True(cache.TryGet(step.Id, out var cachedAfter));
        Assert.False(cachedAfter.IsEmpty);
        Assert.False(display.IsStale(display.Generation));
    }

    [SkippableFact]
    [Trait("Category", "Live")]
    public async Task RunnerCli_ClickSubmit_LogsDisplayGeneration()
    {
        Skip.IfNot(_bench.IsAvailable, _bench.UnavailableReason ?? "PixelFlow.TestBench unavailable.");
        _bench.EnsureForeground();

        using var workspace = FixtureWorkspace.CreateCopy("click-submit");
        var result = await RunnerCli.RunProjectAsync(workspace.ProjectFolder);

        Assert.True(
            result.ExitCode == 0,
            $"Runner exit {result.ExitCode}. stdout:\n{result.StdOut}\nstderr:\n{result.StdErr}");

        Assert.Contains("displayGen=", result.StdOut, StringComparison.Ordinal);
        Assert.Contains("Display-change watcher armed", result.StdOut, StringComparison.Ordinal);

        var events = RunReportStore.ReadEvents(
            RunReportStore.EventsPath(
                RunReportStore.FindLatestReportDirectory(workspace.ProjectFolder)
                ?? throw new InvalidOperationException("No report directory.")));
        var finished = Assert.Single(events, e => e.Event == RunReportEventNames.StepFinished && e.StepId == "click-submit");
        Assert.Equal(RunReportOutcomes.Succeeded, finished.Outcome);
    }

    private static int ReadClickCount()
    {
        var root = System.Windows.Automation.AutomationElement.RootElement;
        var window = root.FindFirst(
            System.Windows.Automation.TreeScope.Children,
            new System.Windows.Automation.AndCondition(
                new System.Windows.Automation.PropertyCondition(
                    System.Windows.Automation.AutomationElement.ControlTypeProperty,
                    System.Windows.Automation.ControlType.Window),
                new System.Windows.Automation.PropertyCondition(
                    System.Windows.Automation.AutomationElement.NameProperty,
                    "Test Bench")));
        Assert.NotNull(window);

        var counter = window.FindFirst(
            System.Windows.Automation.TreeScope.Descendants,
            new System.Windows.Automation.PropertyCondition(
                System.Windows.Automation.AutomationElement.AutomationIdProperty,
                "TbCounter"));
        Assert.NotNull(counter);

        var name = counter.Current.Name ?? "";
        var match = System.Text.RegularExpressions.Regex.Match(name, @"Clicks:\s*(?<n>\d+)");
        Assert.True(match.Success, $"Unexpected counter name: {name}");
        return int.Parse(match.Groups["n"].Value, System.Globalization.CultureInfo.InvariantCulture);
    }
}
