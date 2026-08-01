using System.Text;

namespace PixelFlow.Core.Diagnostics;

/// <summary>
/// Appends JSONL events to <c>events.jsonl</c> under a run report directory.
/// </summary>
public sealed class RunReportWriter : IRunReporter
{
    private readonly object _gate = new();
    private readonly StreamWriter _writer;
    private bool _disposed;

    public RunReportWriter(string runId, string reportDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentException.ThrowIfNullOrWhiteSpace(reportDirectory);

        RunId = runId;
        ReportDirectory = Path.GetFullPath(reportDirectory);
        Directory.CreateDirectory(ReportDirectory);

        var eventsPath = Path.Combine(ReportDirectory, RunReportStore.EventsFileName);
        var stream = new FileStream(
            eventsPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.Read,
            bufferSize: 4096,
            FileOptions.None);
        _writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
        {
            AutoFlush = true,
            NewLine = "\n",
        };
    }

    public string RunId { get; }

    public string ReportDirectory { get; }

    public void Write(RunReportEvent evt)
    {
        ArgumentNullException.ThrowIfNull(evt);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            evt.RunId ??= RunId;
            if (evt.Timestamp == default)
            {
                evt.Timestamp = DateTimeOffset.UtcNow;
            }

            _writer.WriteLine(RunReportJson.Serialize(evt));
            _writer.Flush();
        }
    }

    public string? SaveFailureScreenshot(string stepId, byte[] pngBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stepId);
        ArgumentNullException.ThrowIfNull(pngBytes);
        if (pngBytes.Length == 0)
        {
            return null;
        }

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            var safeId = SanitizeFileToken(stepId);
            var fileName = $"failure-{safeId}.png";
            var path = Path.Combine(ReportDirectory, fileName);
            var temp = path + ".tmp";
            File.WriteAllBytes(temp, pngBytes);
            File.Move(temp, path, overwrite: true);
            return fileName;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _writer.Dispose();
        }
    }

    private static string SanitizeFileToken(string value)
    {
        var chars = value.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '-').ToArray();
        var token = new string(chars).Trim('-');
        return string.IsNullOrEmpty(token) ? "step" : token;
    }
}
