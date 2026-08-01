using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace PixelFlow.Runner.Automation;

/// <summary>
/// GDI BitBlt full-desktop (or region) capture for OCR / image matching.
/// </summary>
internal static class ScreenCapture
{
    private const int SrcCopy = 0x00CC0020;

    public static Bitmap CapturePrimaryScreen()
    {
        var width = NativeMethods.GetSystemMetrics(0);
        var height = NativeMethods.GetSystemMetrics(1);
        if (width <= 0 || height <= 0)
        {
            throw new InvalidOperationException("Unable to read primary screen size.");
        }

        return CaptureRegion(0, 0, width, height);
    }

    public static Bitmap CaptureRegion(int x, int y, int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Capture region must be positive.");
        }

        var screenDc = NativeMethods.GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero)
        {
            throw new InvalidOperationException("GetDC failed.");
        }

        try
        {
            var memDc = NativeMethods.CreateCompatibleDC(screenDc);
            var bmp = NativeMethods.CreateCompatibleBitmap(screenDc, width, height);
            var old = NativeMethods.SelectObject(memDc, bmp);

            if (!NativeMethods.BitBlt(memDc, 0, 0, width, height, screenDc, x, y, SrcCopy))
            {
                NativeMethods.SelectObject(memDc, old);
                NativeMethods.DeleteObject(bmp);
                NativeMethods.DeleteDC(memDc);
                throw new InvalidOperationException($"BitBlt failed (error={Marshal.GetLastWin32Error()}).");
            }

            NativeMethods.SelectObject(memDc, old);
            NativeMethods.DeleteDC(memDc);

            var managed = Image.FromHbitmap(bmp);
            NativeMethods.DeleteObject(bmp);
            return managed;
        }
        finally
        {
            NativeMethods.ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    public static byte[] ToPngBytes(Bitmap bitmap)
    {
        using var ms = new MemoryStream();
        bitmap.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }
}
