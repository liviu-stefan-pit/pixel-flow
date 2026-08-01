namespace PixelFlow.Core.Diagnostics;

/// <summary>
/// Sink for structured run events. Implementations write JSONL (and optional failure screenshots).
/// </summary>
public interface IRunReporter : IDisposable
{
    string RunId { get; }

    /// <summary>Directory containing events.jsonl (and optional screenshots).</summary>
    string ReportDirectory { get; }

    void Write(RunReportEvent evt);

    /// <summary>
    /// Saves a PNG under the report directory. Returns the relative file name, or null on failure.
    /// </summary>
    string? SaveFailureScreenshot(string stepId, byte[] pngBytes);
}

/// <summary>
/// Optional capture of a failure screenshot (full screen or target window). Null = no capture capability.
/// </summary>
public interface IFailureScreenshotCapture
{
    /// <summary>Returns PNG bytes, or null if capture is unavailable.</summary>
    byte[]? CapturePng();
}
