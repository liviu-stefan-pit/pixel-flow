using PixelFlow.Core.Diagnostics;
using PixelFlow.Core.Runner;
using PixelFlow.Integration.Tests.Infrastructure;

namespace PixelFlow.Integration.Tests;

/// <summary>
/// P28 surface coverage: each broader Test Bench surface has a passing fixture script.
/// Complements the locator-layer matrix with an explicit architecture §10 subset checklist.
/// </summary>
[Collection(TestBenchCollection.Name)]
public sealed class TestBenchSurfaceCoverageTests
{
    private readonly TestBenchFixture _bench;

    public TestBenchSurfaceCoverageTests(TestBenchFixture bench)
    {
        _bench = bench;
    }

    /// <summary>
    /// Surface checklist (architecture Section 10 subset for P28):
    /// WPF UIA, native Win32, WinForms, OCR, image 64×64, custom canvas, 16×16 icon grid, moving target.
    /// Electron/WebView2 deferred (out of scope for P28).
    /// </summary>
    [SkippableTheory]
    [Trait("Category", "Live")]
    [InlineData("WPF UIA", "click-submit", "click-submit", LocatorKinds.UiaStructural)]
    [InlineData("Win32 native", "win32-click", "click-win32", LocatorKinds.Win32)]
    [InlineData("WinForms", "winforms-click", "click-winforms", LocatorKinds.UiaStructural)]
    [InlineData("OCR label", "ocr-click", "click-ocr", LocatorKinds.Ocr)]
    [InlineData("Image 64x64", "image-click", "click-image", LocatorKinds.Image)]
    [InlineData("Custom canvas", "canvas-click", "click-canvas", LocatorKinds.Image)]
    [InlineData("Icon grid 16x16", "icon-grid-click", "click-icon-grid", LocatorKinds.Image)]
    [InlineData("Moving target", "moving-target-click", "click-moving", LocatorKinds.UiaStructural)]
    public async Task Surface_HasPassingFixture(
        string surface,
        string fixtureName,
        string stepId,
        string expectedLayer)
    {
        Skip.IfNot(_bench.IsAvailable, _bench.UnavailableReason ?? "PixelFlow.TestBench unavailable.");
        _bench.EnsureForeground();

        using var workspace = FixtureWorkspace.CreateCopy(fixtureName);
        var result = await RunnerCli.RunProjectAsync(workspace.ProjectFolder);

        Assert.True(
            result.ExitCode == 0,
            $"Surface '{surface}' fixture '{fixtureName}' exit {result.ExitCode}. stdout:\n{result.StdOut}\nstderr:\n{result.StdErr}");

        var reportDir = RunReportStore.FindLatestReportDirectory(workspace.ProjectFolder)
            ?? throw new InvalidOperationException($"No report under {workspace.ProjectFolder}.");
        var events = RunReportStore.ReadEvents(RunReportStore.EventsPath(reportDir));
        var finished = Assert.Single(events, e => e.Event == RunReportEventNames.StepFinished && e.StepId == stepId);
        Assert.Equal(RunReportOutcomes.Succeeded, finished.Outcome);
        Assert.Equal(expectedLayer, finished.MatchedLayer);
    }
}
