using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;

namespace PixelFlow.Studio;

/// <summary>
/// GDI BitBlt region capture to PNG bytes (Studio snipping / asset pipeline).
/// </summary>
internal static class ScreenRegionCapture
{
    private const int SrcCopy = 0x00CC0020;

    /// <summary>Capture a physical-pixel region of the desktop as PNG bytes.</summary>
    public static byte[] CapturePng(int x, int y, int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Capture region must be positive.");
        }

        var screenDc = GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero)
        {
            throw new InvalidOperationException("GetDC failed.");
        }

        IntPtr memDc = IntPtr.Zero;
        IntPtr hBitmap = IntPtr.Zero;
        try
        {
            memDc = CreateCompatibleDC(screenDc);
            hBitmap = CreateCompatibleBitmap(screenDc, width, height);
            var old = SelectObject(memDc, hBitmap);

            if (!BitBlt(memDc, 0, 0, width, height, screenDc, x, y, SrcCopy))
            {
                SelectObject(memDc, old);
                throw new InvalidOperationException($"BitBlt failed (error={Marshal.GetLastWin32Error()}).");
            }

            SelectObject(memDc, old);

            var source = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                hBitmap,
                IntPtr.Zero,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));
            using var ms = new MemoryStream();
            encoder.Save(ms);
            return ms.ToArray();
        }
        finally
        {
            if (hBitmap != IntPtr.Zero)
            {
                DeleteObject(hBitmap);
            }

            if (memDc != IntPtr.Zero)
            {
                DeleteDC(memDc);
            }

            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int width, int height);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool BitBlt(
        IntPtr hdcDest,
        int xDest,
        int yDest,
        int width,
        int height,
        IntPtr hdcSrc,
        int xSrc,
        int ySrc,
        int rop);
}
