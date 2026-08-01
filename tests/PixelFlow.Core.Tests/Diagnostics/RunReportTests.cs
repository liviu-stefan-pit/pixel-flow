using PixelFlow.Core.Diagnostics;
using PixelFlow.Core.Projects;
using PixelFlow.Core.Runner;

namespace PixelFlow.Core.Tests.Diagnostics;

public sealed class RunReportTests
{
    [Fact]
    public async Task HappyPath_WritesJsonlWithLayerScoreAndOutcomes()
    {
        using var temp = new TempDir();
        var store = new RunReportStore(retentionCount: 5);
        using var writer = store.BeginRun(temp.Path, runId: "happy-001");

        var resolver = new MockResolver(_ => new ResolveResult(
            Found: true,
            CandidateId: "el",
            MatchedLayer: LocatorKinds.UiaStructural,
            Confidence: 0.97));
        var engine = new RunnerEngine(
            resolver,
            new MockVerifier(true, true),
            new MockExecutor(),
            new ImmediateDelay(),
            reporter: writer);

        await engine.RunAsync(OneClickProject());

        writer.Dispose();

        var events = RunReportStore.ReadEvents(RunReportStore.EventsPath(writer.ReportDirectory));
        Assert.Contains(events, e => e.Event == RunReportEventNames.RunStarted && e.ProjectName == "report-test");
        Assert.Contains(events, e => e.Event == RunReportEventNames.StepStarted && e.StepId == "click-1");
        Assert.Contains(
            events,
            e => e.Event == RunReportEventNames.ResolveAttempt
                 && e.Found == true
                 && e.MatchedLayer == LocatorKinds.UiaStructural
                 && e.Confidence == 0.97);
        Assert.Contains(
            events,
            e => e.Event == RunReportEventNames.StepFinished
                 && e.Outcome == RunReportOutcomes.Succeeded
                 && e.MatchedLayer == LocatorKinds.UiaStructural);
        Assert.Contains(
            events,
            e => e.Event == RunReportEventNames.RunFinished
                 && e.Outcome == RunReportOutcomes.Succeeded
                 && e.FinalState == nameof(RunnerState.Idle));

        var summary = RunReportStore.FormatSummary(writer.ReportDirectory);
        Assert.Contains("[Succeeded] click-1", summary);
        Assert.Contains("layer=UiaStructural", summary);
    }

    [Fact]
    public async Task Failure_WritesFailedOutcomeAndRetryAttempts()
    {
        using var temp = new TempDir();
        using var writer = new RunReportStore().BeginRun(temp.Path, runId: "fail-001");

        var resolver = new MockResolver(_ => ResolveResult.NotFound("missing target"));
        var engine = new RunnerEngine(
            resolver,
            new MockVerifier(true, true),
            new MockExecutor(),
            new ImmediateDelay(),
            reporter: writer);

        await engine.RunAsync(OneClickProject(maxAttempts: 3));
        writer.Dispose();

        var events = RunReportStore.ReadEvents(RunReportStore.EventsPath(writer.ReportDirectory));
        Assert.Equal(3, events.Count(e => e.Event == RunReportEventNames.ResolveAttempt && e.Found == false));
        var finished = Assert.Single(events, e => e.Event == RunReportEventNames.StepFinished);
        Assert.Equal(RunReportOutcomes.Failed, finished.Outcome);
        Assert.Equal(3, finished.Attempts);
        Assert.Contains("missing", finished.FailureReason, StringComparison.OrdinalIgnoreCase);

        var runDone = Assert.Single(events, e => e.Event == RunReportEventNames.RunFinished);
        Assert.Equal(RunReportOutcomes.Failed, runDone.Outcome);
        Assert.Equal(nameof(RunnerState.Aborted), runDone.FinalState);

        var summary = RunReportStore.FormatSummary(writer.ReportDirectory);
        Assert.Contains("[Failed] click-1", summary);
        Assert.Contains("attempts=3", summary);
    }

    [Fact]
    public async Task FailureScreenshot_OptInStoresPng_OptOutDoesNot()
    {
        using var temp = new TempDir();
        var png = CreateMinimalPng();

        // Capture ON
        using (var writer = new RunReportStore().BeginRun(temp.Path, runId: "shot-on"))
        {
            var capture = new FixedPngCapture(png);
            var engine = new RunnerEngine(
                new MockResolver(_ => ResolveResult.NotFound("gone")),
                new MockVerifier(true, true),
                new MockExecutor(),
                new ImmediateDelay(),
                reporter: writer,
                screenshotCapture: capture);

            var project = OneClickProject(maxAttempts: 1);
            project.Steps[0].CaptureFailureScreenshot = true;
            await engine.RunAsync(project);
            writer.Dispose();

            var finished = Assert.Single(
                RunReportStore.ReadEvents(RunReportStore.EventsPath(writer.ReportDirectory)),
                e => e.Event == RunReportEventNames.StepFinished);
            Assert.Equal("failure-click-1.png", finished.Screenshot);
            Assert.True(File.Exists(Path.Combine(writer.ReportDirectory, finished.Screenshot!)));
            Assert.Equal(1, capture.CallCount);
        }

        // Capture OFF (default)
        using (var writer = new RunReportStore().BeginRun(temp.Path, runId: "shot-off"))
        {
            var capture = new FixedPngCapture(png);
            var engine = new RunnerEngine(
                new MockResolver(_ => ResolveResult.NotFound("gone")),
                new MockVerifier(true, true),
                new MockExecutor(),
                new ImmediateDelay(),
                reporter: writer,
                screenshotCapture: capture);

            await engine.RunAsync(OneClickProject(maxAttempts: 1));
            writer.Dispose();

            var finished = Assert.Single(
                RunReportStore.ReadEvents(RunReportStore.EventsPath(writer.ReportDirectory)),
                e => e.Event == RunReportEventNames.StepFinished);
            Assert.Null(finished.Screenshot);
            Assert.Equal(0, capture.CallCount);
            Assert.Empty(Directory.GetFiles(writer.ReportDirectory, "*.png"));
        }
    }

    [Fact]
    public void ProjectDefault_CaptureFlag_InheritedWhenStepOverrideNull()
    {
        var defaults = new ProjectDefaults { CaptureFailureScreenshots = true };
        var step = new ScriptStep { Id = "s", CaptureFailureScreenshot = null };
        Assert.True(RunnerEngine.ShouldCaptureFailureScreenshot(step, defaults));

        step.CaptureFailureScreenshot = false;
        Assert.False(RunnerEngine.ShouldCaptureFailureScreenshot(step, defaults));

        defaults.CaptureFailureScreenshots = false;
        step.CaptureFailureScreenshot = null;
        Assert.False(RunnerEngine.ShouldCaptureFailureScreenshot(step, defaults));
    }

    [Fact]
    public void Retention_DeletesOldestRunsBeyondLimit()
    {
        using var temp = new TempDir();
        var store = new RunReportStore(retentionCount: 2);

        using (store.BeginRun(temp.Path, "a")) { }
        using (store.BeginRun(temp.Path, "b")) { }
        using (store.BeginRun(temp.Path, "c")) { }

        var dirs = Directory.GetDirectories(RunReportStore.ReportsFolder(temp.Path), "run-*")
            .Select(Path.GetFileName)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(2, dirs.Length);
        Assert.DoesNotContain("run-a", dirs);
        Assert.Contains("run-b", dirs);
        Assert.Contains("run-c", dirs);
    }

    [Fact]
    public void ReadEvents_WorksWhileWriterStillOpen()
    {
        using var temp = new TempDir();
        using var writer = new RunReportStore().BeginRun(temp.Path, runId: "concurrent-read");
        writer.Write(new RunReportEvent
        {
            Event = RunReportEventNames.RunStarted,
            ProjectName = "concurrent",
        });

        // Must not throw on Windows even while the writer holds events.jsonl open.
        var events = RunReportStore.ReadEvents(RunReportStore.EventsPath(writer.ReportDirectory));
        Assert.Single(events);
        Assert.Equal(RunReportEventNames.RunStarted, events[0].Event);

        var summary = RunReportStore.FormatSummary(writer.ReportDirectory);
        Assert.Contains("concurrent", summary);
    }

    [Fact]
    public void FindLatestReportDirectory_ReturnsNewest()
    {
        using var temp = new TempDir();
        var store = new RunReportStore();
        using (store.BeginRun(temp.Path, "20260101-000000-0000000")) { }
        using (store.BeginRun(temp.Path, "20260102-000000-0000000")) { }

        var latest = RunReportStore.FindLatestReportDirectory(temp.Path);
        Assert.NotNull(latest);
        Assert.EndsWith("run-20260102-000000-0000000", latest);
    }

    [Fact]
    public void CaptureFailureScreenshot_RoundTripsInProjectJson()
    {
        var doc = new ProjectDocument
        {
            SchemaVersion = ProjectSchema.CurrentVersion,
            Name = "caps",
            Defaults = new ProjectDefaults { CaptureFailureScreenshots = true },
            Steps =
            [
                new ScriptStep
                {
                    Id = "s1",
                    Type = "Click",
                    CaptureFailureScreenshot = true,
                },
            ],
        };

        var round = ProjectJson.RoundTrip(doc);
        Assert.True(round.Defaults.CaptureFailureScreenshots);
        Assert.True(round.Steps[0].CaptureFailureScreenshot);
    }

    private static ProjectDocument OneClickProject(int maxAttempts = 1) => new()
    {
        SchemaVersion = ProjectSchema.CurrentVersion,
        Name = "report-test",
        Defaults = new ProjectDefaults
        {
            TimeoutMs = 0,
            Retry = new RetryPolicy { MaxAttempts = maxAttempts, BackoffMs = 0 },
        },
        Steps =
        [
            new ScriptStep
            {
                Id = "click-1",
                Type = "Click",
                TimeoutMs = 0,
                Locator = new LocatorChain
                {
                    Layers = [new LocatorLayer { Kind = LocatorKinds.UiaStructural, AutomationId = "TbSubmit" }],
                },
            },
        ],
    };

    /// <summary>Minimal valid 1x1 PNG.</summary>
    private static byte[] CreateMinimalPng()
    {
        return Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");
    }

    private sealed class TempDir : IDisposable
    {
        public TempDir()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pf-report-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, recursive: true);
                }
            }
            catch
            {
                // ignore cleanup races
            }
        }
    }

    private sealed class ImmediateDelay : IRunnerDelay
    {
        public Task DelayAsync(int milliseconds, CancellationToken cancellationToken) =>
            cancellationToken.IsCancellationRequested
                ? Task.FromCanceled(cancellationToken)
                : Task.CompletedTask;
    }

    private sealed class FixedPngCapture : IFailureScreenshotCapture
    {
        private readonly byte[] _png;

        public FixedPngCapture(byte[] png) => _png = png;

        public int CallCount { get; private set; }

        public byte[]? CapturePng()
        {
            CallCount++;
            return _png;
        }
    }

    private sealed class MockResolver : ITargetResolver
    {
        private readonly Func<ScriptStep, ResolveResult> _impl;

        public MockResolver(Func<ScriptStep, ResolveResult> impl) => _impl = impl;

        public Task<ResolveResult> ResolveAsync(ScriptStep step, CancellationToken cancellationToken) =>
            Task.FromResult(_impl(step));
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
        public Task ExecuteAsync(ScriptStep step, ResolveResult candidate, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
