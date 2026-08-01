using System.IO;
using System.Linq;
using PixelFlow.Core.Diagnostics;
using PixelFlow.Core.Runner;
using PixelFlow.Integration.Tests.Infrastructure;

namespace PixelFlow.Integration.Tests;

/// <summary>
/// Live end-to-end matrix: runs every CLI-runnable fixture against a real Test Bench and asserts
/// exit code + <c>events.jsonl</c> outcome/layer/confidence. Requires an interactive Windows
/// desktop; run with <c>dotnet test --filter Category=Live</c>.
/// </summary>
[Collection(TestBenchCollection.Name)]
public sealed class RunnerFixtureMatrixTests
{
    private readonly TestBenchFixture _bench;

    public RunnerFixtureMatrixTests(TestBenchFixture bench)
    {
        _bench = bench;
    }

    [SkippableTheory]
    [Trait("Category", "Live")]
    [InlineData("click-submit", "click-submit", LocatorKinds.UiaStructural)]
    [InlineData("chain-uia-wins", "click-chain-uia", LocatorKinds.UiaStructural)]
    [InlineData("chain-win32-fallback", "click-chain-win32", LocatorKinds.Win32)]
    [InlineData("win32-click", "click-win32", LocatorKinds.Win32)]
    [InlineData("winforms-click", "click-winforms", LocatorKinds.UiaStructural)]
    [InlineData("ocr-click", "click-ocr", LocatorKinds.Ocr)]
    [InlineData("image-click", "click-image", LocatorKinds.Image)]
    [InlineData("canvas-click", "click-canvas", LocatorKinds.Image)]
    [InlineData("icon-grid-click", "click-icon-grid", LocatorKinds.Image)]
    [InlineData("moving-target-click", "click-moving", LocatorKinds.UiaStructural)]
    public async Task Fixture_Succeeds_ViaExpectedLocatorLayer(string fixtureName, string stepId, string expectedLayer)
    {
        Skip.IfNot(_bench.IsAvailable, _bench.UnavailableReason ?? "PixelFlow.TestBench unavailable.");
        _bench.EnsureForeground();

        using var workspace = FixtureWorkspace.CreateCopy(fixtureName);
        var result = await RunnerCli.RunProjectAsync(workspace.ProjectFolder);

        Assert.True(
            result.ExitCode == 0,
            $"Fixture '{fixtureName}' exit {result.ExitCode}. stdout:\n{result.StdOut}\nstderr:\n{result.StdErr}");

        var events = ReadLatestEvents(workspace.ProjectFolder);
        var finished = Assert.Single(events, e => e.Event == RunReportEventNames.StepFinished && e.StepId == stepId);
        Assert.Equal(RunReportOutcomes.Succeeded, finished.Outcome);
        Assert.Equal(expectedLayer, finished.MatchedLayer);

        if (expectedLayer == LocatorKinds.Image)
        {
            Assert.NotNull(finished.Confidence);
            Assert.True(finished.Confidence >= 0.8, $"Expected image confidence >= 0.8, got {finished.Confidence}.");
        }

        var runFinished = Assert.Single(events, e => e.Event == RunReportEventNames.RunFinished);
        Assert.Equal(RunReportOutcomes.Succeeded, runFinished.Outcome);
    }

    [SkippableTheory]
    [Trait("Category", "Live")]
    [InlineData("retry-miss", "click-missing")]
    [InlineData("ocr-miss", "click-ocr-miss")]
    [InlineData("image-miss", "click-image-miss")]
    [InlineData("chain-all-miss", "click-all-miss")]
    public async Task Fixture_Fails_WithExitCode3_AndNoClick(string fixtureName, string stepId)
    {
        Skip.IfNot(_bench.IsAvailable, _bench.UnavailableReason ?? "PixelFlow.TestBench unavailable.");
        _bench.EnsureForeground();

        using var workspace = FixtureWorkspace.CreateCopy(fixtureName);
        var result = await RunnerCli.RunProjectAsync(workspace.ProjectFolder);

        Assert.Equal(3, result.ExitCode);

        var events = ReadLatestEvents(workspace.ProjectFolder);
        var finished = Assert.Single(events, e => e.Event == RunReportEventNames.StepFinished && e.StepId == stepId);

        // Outcome=Failed is only reachable from the "not found" / pre-check-failed branches in
        // RunnerEngine.RunStepAsync, both of which return before IStepExecutor.ExecuteAsync runs —
        // so Failed here is itself the proof that no click was ever dispatched.
        Assert.Equal(RunReportOutcomes.Failed, finished.Outcome);

        var runFinished = Assert.Single(events, e => e.Event == RunReportEventNames.RunFinished);
        Assert.Equal(RunReportOutcomes.Failed, runFinished.Outcome);
    }

    [SkippableFact]
    [Trait("Category", "Live")]
    public async Task FailureScreenshotOn_WritesPngAndRecordsPathInReport()
    {
        Skip.IfNot(_bench.IsAvailable, _bench.UnavailableReason ?? "PixelFlow.TestBench unavailable.");
        _bench.EnsureForeground();

        using var workspace = FixtureWorkspace.CreateCopy("failure-screenshot-on");
        var result = await RunnerCli.RunProjectAsync(workspace.ProjectFolder);

        Assert.Equal(3, result.ExitCode);

        var reportDir = RequireLatestReportDirectory(workspace.ProjectFolder);
        var events = RunReportStore.ReadEvents(RunReportStore.EventsPath(reportDir));
        var finished = Assert.Single(events, e => e.Event == RunReportEventNames.StepFinished);
        Assert.Equal(RunReportOutcomes.Failed, finished.Outcome);
        Assert.False(string.IsNullOrWhiteSpace(finished.Screenshot));

        var pngPath = Path.Combine(reportDir, finished.Screenshot!);
        Assert.True(File.Exists(pngPath), $"Expected failure screenshot at {pngPath}.");
        Assert.True(new FileInfo(pngPath).Length > 0);
    }

    [SkippableFact]
    [Trait("Category", "Live")]
    public async Task FailureScreenshotOff_WritesNoPng()
    {
        Skip.IfNot(_bench.IsAvailable, _bench.UnavailableReason ?? "PixelFlow.TestBench unavailable.");
        _bench.EnsureForeground();

        using var workspace = FixtureWorkspace.CreateCopy("failure-screenshot-off");
        var result = await RunnerCli.RunProjectAsync(workspace.ProjectFolder);

        Assert.Equal(3, result.ExitCode);

        var reportDir = RequireLatestReportDirectory(workspace.ProjectFolder);
        var events = RunReportStore.ReadEvents(RunReportStore.EventsPath(reportDir));
        var finished = Assert.Single(events, e => e.Event == RunReportEventNames.StepFinished);
        Assert.Equal(RunReportOutcomes.Failed, finished.Outcome);
        Assert.True(string.IsNullOrWhiteSpace(finished.Screenshot));
        Assert.Empty(Directory.GetFiles(reportDir, "*.png"));
    }

    [Fact]
    [Trait("Category", "Live")]
    public async Task IpcWaitOnly_RunsAllSteps_ExitsZero_NoTestBenchRequired()
    {
        // Wait-only fixture: no locator/Test Bench dependency, so this always runs (not [SkippableFact]).
        using var workspace = FixtureWorkspace.CreateCopy("ipc-wait");
        var result = await RunnerCli.RunProjectAsync(workspace.ProjectFolder, timeout: TimeSpan.FromSeconds(30));

        Assert.Equal(0, result.ExitCode);

        var events = ReadLatestEvents(workspace.ProjectFolder);
        var finishedSteps = events.Where(e => e.Event == RunReportEventNames.StepFinished).ToList();
        Assert.Equal(3, finishedSteps.Count);
        Assert.All(finishedSteps, e => Assert.Equal(RunReportOutcomes.Succeeded, e.Outcome));

        var runFinished = Assert.Single(events, e => e.Event == RunReportEventNames.RunFinished);
        Assert.Equal(RunReportOutcomes.Succeeded, runFinished.Outcome);
    }

    [Fact]
    [Trait("Category", "Live")]
    public async Task RecoverySkip_ContinuesAfterFailedStep()
    {
        using var workspace = FixtureWorkspace.CreateCopy("recovery-skip");
        var result = await RunnerCli.RunProjectAsync(workspace.ProjectFolder, timeout: TimeSpan.FromSeconds(30));

        Assert.Equal(0, result.ExitCode);

        var events = ReadLatestEvents(workspace.ProjectFolder);
        var miss = Assert.Single(events, e => e.Event == RunReportEventNames.StepFinished && e.StepId == "click-missing");
        Assert.Equal(RunReportOutcomes.Failed, miss.Outcome);
        var after = Assert.Single(events, e => e.Event == RunReportEventNames.StepFinished && e.StepId == "after-skip");
        Assert.Equal(RunReportOutcomes.Succeeded, after.Outcome);
        var runFinished = Assert.Single(events, e => e.Event == RunReportEventNames.RunFinished);
        Assert.Equal(RunReportOutcomes.Succeeded, runFinished.Outcome);
    }

    [Fact]
    [Trait("Category", "Live")]
    public async Task RecoveryJump_ReachesLabeledStep()
    {
        using var workspace = FixtureWorkspace.CreateCopy("recovery-jump");
        var result = await RunnerCli.RunProjectAsync(workspace.ProjectFolder, timeout: TimeSpan.FromSeconds(30));

        Assert.Equal(0, result.ExitCode);

        var events = ReadLatestEvents(workspace.ProjectFolder);
        Assert.DoesNotContain(events, e => e.Event == RunReportEventNames.StepFinished && e.StepId == "skipped");
        var landing = Assert.Single(events, e => e.Event == RunReportEventNames.StepFinished && e.StepId == "landing");
        Assert.Equal(RunReportOutcomes.Succeeded, landing.Outcome);
        var runFinished = Assert.Single(events, e => e.Event == RunReportEventNames.RunFinished);
        Assert.Equal(RunReportOutcomes.Succeeded, runFinished.Outcome);
    }

    [Fact]
    [Trait("Category", "Live")]
    public async Task RecoveryAbort_StopsWithoutLaterSteps()
    {
        using var workspace = FixtureWorkspace.CreateCopy("recovery-abort");
        var result = await RunnerCli.RunProjectAsync(workspace.ProjectFolder, timeout: TimeSpan.FromSeconds(30));

        Assert.Equal(3, result.ExitCode);

        var events = ReadLatestEvents(workspace.ProjectFolder);
        Assert.DoesNotContain(events, e => e.Event == RunReportEventNames.StepFinished && e.StepId == "should-not-run");
        var miss = Assert.Single(events, e => e.Event == RunReportEventNames.StepFinished && e.StepId == "click-missing");
        Assert.Equal(RunReportOutcomes.Failed, miss.Outcome);
        var runFinished = Assert.Single(events, e => e.Event == RunReportEventNames.RunFinished);
        Assert.Equal(RunReportOutcomes.Failed, runFinished.Outcome);
    }

    private static string RequireLatestReportDirectory(string projectFolder) =>
        RunReportStore.FindLatestReportDirectory(projectFolder)
            ?? throw new InvalidOperationException($"No report directory under {projectFolder}.");

    private static IReadOnlyList<RunReportEvent> ReadLatestEvents(string projectFolder) =>
        RunReportStore.ReadEvents(RunReportStore.EventsPath(RequireLatestReportDirectory(projectFolder)));
}
