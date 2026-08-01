using System.IO;
using PixelFlow.Core.Diagnostics;

namespace PixelFlow.Studio.Tests;

/// <summary>
/// Studio-shaped smoke test for the <b>Last report</b> button
/// (<c>MainWindow.OnLastReportClick</c>): <see cref="RunReportStore.FindLatestReportDirectory"/> +
/// <see cref="RunReportStore.FormatSummary"/>, split into log lines the same way Studio appends them.
/// Exercised here without any WPF window.
/// </summary>
public sealed class LastReportSummaryTests : IDisposable
{
    private readonly string _projectFolder;

    public LastReportSummaryTests()
    {
        _projectFolder = Path.Combine(Path.GetTempPath(), "PixelFlow.StudioTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_projectFolder);
    }

    [Fact]
    public void FindLatestReportDirectory_ReturnsNull_BeforeAnyRun()
    {
        Assert.Null(RunReportStore.FindLatestReportDirectory(_projectFolder));
    }

    [Fact]
    public void LastReport_Succeeded_ProducesStudioShapedSummaryLines()
    {
        var store = new RunReportStore();
        using (var reporter = store.BeginRun(_projectFolder))
        {
            reporter.Write(new RunReportEvent
            {
                Event = RunReportEventNames.RunStarted,
                ProjectName = "click-submit",
            });
            reporter.Write(new RunReportEvent
            {
                Event = RunReportEventNames.StepFinished,
                StepId = "click-submit",
                StepType = "Click",
                Outcome = RunReportOutcomes.Succeeded,
                Attempts = 1,
                MatchedLayer = "UiaStructural",
                Confidence = 0.97,
            });
            reporter.Write(new RunReportEvent
            {
                Event = RunReportEventNames.RunFinished,
                FinalState = "Idle",
                Outcome = RunReportOutcomes.Succeeded,
            });
        }

        var latest = RunReportStore.FindLatestReportDirectory(_projectFolder);
        Assert.NotNull(latest);

        // Mirrors MainWindow.OnLastReportClick exactly: FormatSummary(latest).Split('\n').
        var lines = RunReportStore.FormatSummary(latest!).Split('\n');

        Assert.Contains(lines, l => l.Contains("Project: click-submit"));
        Assert.Contains(lines, l => l.Contains("[Succeeded] click-submit") && l.Contains("layer=UiaStructural"));
        Assert.Contains(lines, l => l.StartsWith("Finished: Idle / Succeeded", StringComparison.Ordinal));
    }

    [Fact]
    public void LastReport_Failed_IncludesFailureReasonAndScreenshotField()
    {
        var store = new RunReportStore();
        using (var reporter = store.BeginRun(_projectFolder))
        {
            reporter.Write(new RunReportEvent
            {
                Event = RunReportEventNames.RunStarted,
                ProjectName = "failure-screenshot-on",
            });
            reporter.Write(new RunReportEvent
            {
                Event = RunReportEventNames.StepFinished,
                StepId = "click-missing",
                StepType = "Click",
                Outcome = RunReportOutcomes.Failed,
                Attempts = 1,
                FailureReason = "Resolve budget exhausted",
                Screenshot = "failure-click-missing.png",
            });
            reporter.Write(new RunReportEvent
            {
                Event = RunReportEventNames.RunFinished,
                FinalState = "Aborted",
                Outcome = RunReportOutcomes.Failed,
            });
        }

        var latest = RunReportStore.FindLatestReportDirectory(_projectFolder);
        var summary = RunReportStore.FormatSummary(latest!);

        Assert.Contains("reason=Resolve budget exhausted", summary);
        Assert.Contains("screenshot=failure-click-missing.png", summary);
        Assert.Contains("Finished: Aborted / Failed", summary);
    }

    [Fact]
    public void FindLatestReportDirectory_PicksNewestRun_AfterMultipleRuns()
    {
        var store = new RunReportStore();
        using (store.BeginRun(_projectFolder, runId: "20260101-000000-0000001"))
        {
        }

        string secondRunDir;
        using (var reporter = store.BeginRun(_projectFolder, runId: "20260101-000000-0000002"))
        {
            secondRunDir = reporter.ReportDirectory;
        }

        var latest = RunReportStore.FindLatestReportDirectory(_projectFolder);
        Assert.Equal(secondRunDir, latest);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_projectFolder, recursive: true);
        }
        catch
        {
            // best-effort cleanup
        }
    }
}
