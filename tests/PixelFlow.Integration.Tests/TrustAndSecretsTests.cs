using System.IO;
using System.Windows.Automation;
using PixelFlow.Core.Diagnostics;
using PixelFlow.Core.Projects;
using PixelFlow.Core.Runner;
using PixelFlow.Integration.Tests.Infrastructure;
using PixelFlow.Runner.Automation;
using PixelFlow.Runner.Secrets;

namespace PixelFlow.Integration.Tests;

/// <summary>
/// P29 project trust (unit-covered in Core) + P30 secrets-by-reference live verification.
/// </summary>
[Collection(TestBenchCollection.Name)]
public sealed class TrustAndSecretsTests
{
    private const string SecretTarget = "PixelFlow/TestSecret";
    private const string SecretValue = "pf-secret-value-P30-never-in-json";

    private readonly TestBenchFixture _bench;

    public TrustAndSecretsTests(TestBenchFixture bench)
    {
        _bench = bench;
    }

    [SkippableFact]
    [Trait("Category", "Live")]
    public async Task TypeSecretRef_ResolvesFromCredentialManager_AndRedactsReports()
    {
        Skip.IfNot(_bench.IsAvailable, _bench.UnavailableReason ?? "PixelFlow.TestBench unavailable.");
        _bench.EnsureForeground();

        WindowsCredentialSecretResolver.Store(SecretTarget, SecretValue);
        try
        {
            ClearTbInput();

            using var workspace = FixtureWorkspace.CreateCopy("type-secret");

            // Project JSON must contain the name only — never the secret value.
            var projectJson = File.ReadAllText(ProjectPaths.ProjectFile(workspace.ProjectFolder));
            Assert.Contains("\"secretRef\"", projectJson, StringComparison.Ordinal);
            Assert.Contains(SecretTarget, projectJson, StringComparison.Ordinal);
            Assert.DoesNotContain(SecretValue, projectJson, StringComparison.Ordinal);
            Assert.DoesNotContain("\"text\"", projectJson, StringComparison.Ordinal);

            var result = await RunnerCli.RunProjectAsync(workspace.ProjectFolder);
            Assert.True(
                result.ExitCode == 0,
                $"Runner exit {result.ExitCode}. stdout:\n{result.StdOut}\nstderr:\n{result.StdErr}");

            Assert.Equal(SecretValue, ReadTbInputValue());

            // Console / report must not leak the secret plaintext.
            Assert.DoesNotContain(SecretValue, result.StdOut, StringComparison.Ordinal);
            Assert.DoesNotContain(SecretValue, result.StdErr, StringComparison.Ordinal);
            Assert.Contains("secretRef=" + SecretTarget, result.StdOut, StringComparison.Ordinal);
            Assert.Contains("redacted", result.StdOut, StringComparison.OrdinalIgnoreCase);

            var reportDir = RunReportStore.FindLatestReportDirectory(workspace.ProjectFolder)
                ?? throw new InvalidOperationException("No report directory.");
            var eventsJsonl = File.ReadAllText(RunReportStore.EventsPath(reportDir));
            Assert.DoesNotContain(SecretValue, eventsJsonl, StringComparison.Ordinal);

            var events = RunReportStore.ReadEvents(RunReportStore.EventsPath(reportDir));
            var finished = Assert.Single(
                events,
                e => e.Event == RunReportEventNames.StepFinished && e.StepId == "type-secret");
            Assert.Equal(RunReportOutcomes.Succeeded, finished.Outcome);
            Assert.Null(finished.FailureReason);
        }
        finally
        {
            WindowsCredentialSecretResolver.Delete(SecretTarget);
            try
            {
                ClearTbInput();
            }
            catch
            {
                // best-effort
            }
        }
    }

    [Fact]
    [Trait("Category", "Live")]
    public void WindowsCredentialStore_RoundTripsGenericSecret()
    {
        var name = "PixelFlow/UnitRoundTrip-" + Guid.NewGuid().ToString("N")[..8];
        const string value = "round-trip-secret";
        try
        {
            WindowsCredentialSecretResolver.Store(name, value);
            var resolver = new WindowsCredentialSecretResolver();
            Assert.True(resolver.TryResolve(name, out var resolved, out var error), error);
            Assert.Equal(value, resolved);
        }
        finally
        {
            WindowsCredentialSecretResolver.Delete(name);
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

        throw new InvalidOperationException("TbInput ValuePattern unavailable.");
    }

    private static AutomationElement FindTbInput()
    {
        var root = AutomationElement.RootElement;
        var window = root.FindFirst(
            TreeScope.Children,
            new AndCondition(
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window),
                new PropertyCondition(AutomationElement.NameProperty, "Test Bench")));
        if (window is null)
        {
            throw new InvalidOperationException("Test Bench window not found.");
        }

        var edit = window.FindFirst(
            TreeScope.Descendants,
            new PropertyCondition(AutomationElement.AutomationIdProperty, "TbInput"));
        return edit ?? throw new InvalidOperationException("TbInput not found.");
    }
}
