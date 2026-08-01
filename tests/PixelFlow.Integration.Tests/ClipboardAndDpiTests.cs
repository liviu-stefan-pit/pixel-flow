using System.Windows.Automation;
using PixelFlow.Core.Diagnostics;
using PixelFlow.Core.Runner;
using PixelFlow.Integration.Tests.Infrastructure;
using PixelFlow.Runner.Automation;

namespace PixelFlow.Integration.Tests;

/// <summary>
/// P25 clipboard restore + paste Type path; P26 DPI-aware click smoke at the current scaling.
/// </summary>
[Collection(TestBenchCollection.Name)]
public sealed class ClipboardAndDpiTests
{
    private readonly TestBenchFixture _bench;

    public ClipboardAndDpiTests(TestBenchFixture bench)
    {
        _bench = bench;
    }

    [Fact]
    [Trait("Category", "Live")]
    public void ClipboardGuard_RestoresAfterSuccessAndAfterSimulatedFailure()
    {
        const string original = "CLIPBOARD_A_RESTORE";
        ClipboardGuard.SetText(original);

        using (var guard = ClipboardGuard.ReplaceWith("CLIPBOARD_B_TEMP"))
        {
            Assert.True(ClipboardGuard.TryGetText(out var mid));
            Assert.Equal("CLIPBOARD_B_TEMP", mid);
            guard.Restore();
        }

        Assert.True(ClipboardGuard.TryGetText(out var afterSuccess));
        Assert.Equal(original, afterSuccess);

        try
        {
            using var guard = ClipboardGuard.ReplaceWith("CLIPBOARD_B_FAIL");
            Assert.True(ClipboardGuard.TryGetText(out var midFail));
            Assert.Equal("CLIPBOARD_B_FAIL", midFail);
            throw new InvalidOperationException("simulated mid-paste failure");
        }
        catch (InvalidOperationException)
        {
            // expected — Dispose/Restore must still run
        }

        Assert.True(ClipboardGuard.TryGetText(out var afterFail));
        Assert.Equal(original, afterFail);
    }

    [SkippableFact]
    [Trait("Category", "Live")]
    public async Task TypePaste_RestoresClipboard_AndWritesTextToTbInput()
    {
        Skip.IfNot(_bench.IsAvailable, _bench.UnavailableReason ?? "PixelFlow.TestBench unavailable.");
        _bench.EnsureForeground();

        const string originalClipboard = "CLIPBOARD_A_BEFORE_TYPE";
        ClipboardGuard.SetText(originalClipboard);

        // Clear any leftover text in TbInput from prior runs.
        ClearTbInput();

        using var workspace = FixtureWorkspace.CreateCopy("type-paste");
        var result = await RunnerCli.RunProjectAsync(workspace.ProjectFolder);

        Assert.True(
            result.ExitCode == 0,
            $"Runner exit {result.ExitCode}. stdout:\n{result.StdOut}\nstderr:\n{result.StdErr}");

        Assert.True(ClipboardGuard.TryGetText(out var after));
        Assert.Equal(originalClipboard, after);

        Assert.Equal("hello-paste-B", ReadTbInputValue());

        var events = RunReportStore.ReadEvents(
            RunReportStore.EventsPath(
                RunReportStore.FindLatestReportDirectory(workspace.ProjectFolder)
                ?? throw new InvalidOperationException("No report directory.")));
        var finished = Assert.Single(events, e => e.Event == RunReportEventNames.StepFinished && e.StepId == "type-input");
        Assert.Equal(RunReportOutcomes.Succeeded, finished.Outcome);
        Assert.Equal(LocatorKinds.UiaStructural, finished.MatchedLayer);
        Assert.Contains("Clipboard restored after Type", result.StdOut, StringComparison.Ordinal);
    }

    [SkippableFact]
    [Trait("Category", "Live")]
    public async Task DpiAware_ClickSubmit_SucceedsAtCurrentScaling()
    {
        Skip.IfNot(_bench.IsAvailable, _bench.UnavailableReason ?? "PixelFlow.TestBench unavailable.");
        _bench.EnsureForeground();

        using var workspace = FixtureWorkspace.CreateCopy("click-submit");
        var result = await RunnerCli.RunProjectAsync(workspace.ProjectFolder);

        Assert.True(
            result.ExitCode == 0,
            $"Runner exit {result.ExitCode}. stdout:\n{result.StdOut}\nstderr:\n{result.StdErr}");

        // Resolve log includes per-monitor DPI for the hit bounds (Per-Monitor V2).
        Assert.Contains("dpi=", result.StdOut, StringComparison.Ordinal);

        var events = RunReportStore.ReadEvents(
            RunReportStore.EventsPath(
                RunReportStore.FindLatestReportDirectory(workspace.ProjectFolder)
                ?? throw new InvalidOperationException("No report directory.")));
        var finished = Assert.Single(events, e => e.Event == RunReportEventNames.StepFinished && e.StepId == "click-submit");
        Assert.Equal(RunReportOutcomes.Succeeded, finished.Outcome);
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
