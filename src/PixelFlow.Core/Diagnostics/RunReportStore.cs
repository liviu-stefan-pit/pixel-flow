using System.Globalization;
using System.Text;
using PixelFlow.Core.Projects;

namespace PixelFlow.Core.Diagnostics;

/// <summary>
/// Creates run report folders under a project <c>reports/</c> directory and applies retention.
/// </summary>
public sealed class RunReportStore
{
    public const string EventsFileName = "events.jsonl";
    public const int DefaultRetentionCount = 20;

    private readonly int _retentionCount;

    public RunReportStore(int retentionCount = DefaultRetentionCount)
    {
        if (retentionCount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(retentionCount), "Retention must be at least 1.");
        }

        _retentionCount = retentionCount;
    }

    public int RetentionCount => _retentionCount;

    public static string ReportsFolder(string projectFolder) =>
        Path.Combine(projectFolder, ProjectPaths.ReportsFolderName);

    /// <summary>
    /// Creates a new run folder <c>reports/run-yyyyMMdd-HHmmss-ffffffff/</c> and returns a writer.
    /// Also rotates older runs down to <see cref="RetentionCount"/>.
    /// </summary>
    public RunReportWriter BeginRun(string projectFolder, string? runId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFolder);

        var reportsRoot = ReportsFolder(projectFolder);
        Directory.CreateDirectory(reportsRoot);

        runId ??= CreateRunId();
        var reportDir = Path.Combine(reportsRoot, "run-" + runId);
        Directory.CreateDirectory(reportDir);

        var writer = new RunReportWriter(runId, reportDir);
        Rotate(reportsRoot);
        return writer;
    }

    public static string? FindLatestReportDirectory(string projectFolder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFolder);
        var reportsRoot = ReportsFolder(projectFolder);
        if (!Directory.Exists(reportsRoot))
        {
            return null;
        }

        return Directory.GetDirectories(reportsRoot, "run-*")
            .OrderByDescending(static path => Path.GetFileName(path), StringComparer.Ordinal)
            .FirstOrDefault();
    }

    public static string EventsPath(string reportDirectory) =>
        Path.Combine(reportDirectory, EventsFileName);

    public void Rotate(string reportsRoot)
    {
        if (!Directory.Exists(reportsRoot))
        {
            return;
        }

        var dirs = Directory.GetDirectories(reportsRoot, "run-*")
            .OrderByDescending(static path => Path.GetFileName(path), StringComparer.Ordinal)
            .ToList();

        foreach (var stale in dirs.Skip(_retentionCount))
        {
            try
            {
                Directory.Delete(stale, recursive: true);
            }
            catch (IOException)
            {
                // best-effort retention
            }
            catch (UnauthorizedAccessException)
            {
                // best-effort retention
            }
        }
    }

    public static string CreateRunId() =>
        DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff", CultureInfo.InvariantCulture);

    /// <summary>
    /// Human-readable pass/fail summary from a report directory's events.jsonl.
    /// </summary>
    public static string FormatSummary(string reportDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reportDirectory);
        var eventsPath = EventsPath(reportDirectory);
        if (!File.Exists(eventsPath))
        {
            return $"No events.jsonl in {reportDirectory}";
        }

        var events = ReadEvents(eventsPath);
        var sb = new StringBuilder();
        sb.AppendLine("Report: " + reportDirectory);

        var runStarted = events.FirstOrDefault(e => e.Event == RunReportEventNames.RunStarted);
        if (runStarted is not null)
        {
            sb.AppendLine($"RunId: {runStarted.RunId}  Project: {runStarted.ProjectName}");
            sb.AppendLine($"Started: {runStarted.Timestamp:O}");
        }

        var stepResults = events
            .Where(e => e.Event == RunReportEventNames.StepFinished)
            .ToList();

        if (stepResults.Count == 0)
        {
            sb.AppendLine("Steps: (none finished)");
        }
        else
        {
            sb.AppendLine("Steps:");
            foreach (var step in stepResults)
            {
                var layer = string.IsNullOrWhiteSpace(step.MatchedLayer)
                    ? ""
                    : $" layer={step.MatchedLayer}";
                var conf = step.Confidence is null ? "" : $" conf={step.Confidence:0.###}";
                var attempts = step.Attempts is null ? "" : $" attempts={step.Attempts}";
                var reason = string.IsNullOrWhiteSpace(step.FailureReason)
                    ? ""
                    : $" reason={step.FailureReason}";
                var shot = string.IsNullOrWhiteSpace(step.Screenshot)
                    ? ""
                    : $" screenshot={step.Screenshot}";
                sb.AppendLine(
                    $"  [{step.Outcome}] {step.StepId} ({step.StepType}){attempts}{layer}{conf}{reason}{shot}");
            }
        }

        var finished = events.LastOrDefault(e => e.Event == RunReportEventNames.RunFinished);
        if (finished is not null)
        {
            sb.AppendLine($"Finished: {finished.FinalState} / {finished.Outcome} @ {finished.Timestamp:O}");
        }

        return sb.ToString().TrimEnd();
    }

    public static IReadOnlyList<RunReportEvent> ReadEvents(string eventsFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventsFilePath);
        if (!File.Exists(eventsFilePath))
        {
            return [];
        }

        var list = new List<RunReportEvent>();
        foreach (var line in File.ReadLines(eventsFilePath, Encoding.UTF8))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            list.Add(RunReportJson.Deserialize(line));
        }

        return list;
    }
}
