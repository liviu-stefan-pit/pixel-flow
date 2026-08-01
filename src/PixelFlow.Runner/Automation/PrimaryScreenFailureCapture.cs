using PixelFlow.Core.Diagnostics;

namespace PixelFlow.Runner.Automation;

/// <summary>
/// P22: full primary-screen PNG capture for opt-in failure screenshots.
/// </summary>
internal sealed class PrimaryScreenFailureCapture : IFailureScreenshotCapture
{
    public byte[]? CapturePng()
    {
        try
        {
            using var bitmap = ScreenCapture.CapturePrimaryScreen();
            return ScreenCapture.ToPngBytes(bitmap);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[runner] Screenshot capture failed: {ex.Message}");
            return null;
        }
    }
}
