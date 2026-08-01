using System.Runtime.InteropServices;

namespace PixelFlow.Runner.Automation;

/// <summary>
/// Per-monitor DPI lookup for physical screen points (requires Per-Monitor V2 awareness).
/// </summary>
internal static class MonitorDpi
{
    private const int MdtdEffectiveDpi = 0;
    private const int MonitorDefaultToNearest = 2;

    /// <summary>Effective DPI for the monitor containing the physical point; falls back to 96.</summary>
    public static uint GetDpiForPhysicalPoint(int physicalX, int physicalY)
    {
        var pt = new NativeMethods.Point { X = physicalX, Y = physicalY };
        var monitor = MonitorFromPoint(pt, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero)
        {
            return 96;
        }

        if (GetDpiForMonitor(monitor, MdtdEffectiveDpi, out var dpiX, out _) == 0 && dpiX > 0)
        {
            return dpiX;
        }

        return 96;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(NativeMethods.Point pt, uint dwFlags);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);
}
