using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MediaColor = System.Windows.Media.Color;

namespace PixelFlow.TestBench;

/// <summary>
/// FrameworkElement that opts out of the UIA tree so Image/OCR fixtures are forced
/// (custom-drawn canvas and icon-grid cells).
/// </summary>
internal abstract class SilentAutomationElement : FrameworkElement
{
    protected override AutomationPeer? OnCreateAutomationPeer() => null;
}

/// <summary>
/// Custom-drawn hit target with no UIA peers — distinctive yellow-on-navy bullseye.
/// On-screen pixels match <see cref="CreateTemplatePixels"/> (fixture PNG source).
/// </summary>
internal sealed class CustomCanvasTarget : SilentAutomationElement
{
    public const int Size = 80;

    private readonly Action _onClick;
    private readonly ImageSource _bitmap;

    public CustomCanvasTarget(Action onClick)
    {
        _onClick = onClick ?? throw new ArgumentNullException(nameof(onClick));
        _bitmap = CreateBitmapSource(CreateTemplatePixels(), Size);
        Width = Size;
        Height = Size;
        Cursor = System.Windows.Input.Cursors.Hand;
        MouseLeftButtonUp += (_, _) => _onClick();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        drawingContext.DrawImage(_bitmap, new Rect(0, 0, Size, Size));
    }

    /// <summary>Pixel buffer matching the committed canvas-click fixture PNG.</summary>
    internal static byte[] CreateTemplatePixels()
    {
        var pixels = new byte[Size * Size * 4];
        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                var i = (y * Size + x) * 4;
                var dx = x - Size / 2.0;
                var dy = y - Size / 2.0;
                var r = Math.Sqrt(dx * dx + dy * dy);
                byte b, g, rr;
                if (r <= 6 || (r > 14 && r <= 28))
                {
                    rr = 0xFF;
                    g = 0xD4;
                    b = 0x00;
                }
                else
                {
                    rr = 0x0A;
                    g = 0x1A;
                    b = 0x3A;
                }

                pixels[i] = b;
                pixels[i + 1] = g;
                pixels[i + 2] = rr;
                pixels[i + 3] = 0xFF;
            }
        }

        return pixels;
    }

    private static ImageSource CreateBitmapSource(byte[] pixels, int size)
    {
        var bmp = BitmapSource.Create(
            size, size, 96, 96, PixelFormats.Bgra32, null, pixels, size * 4);
        bmp.Freeze();
        return bmp;
    }
}

/// <summary>
/// 16×16 icon cell with no UIA peer. Only the lime/red "hit" pattern is the fixture target;
/// decoys use a dull gray pattern. Hit-target pixels match the icon-grid-click fixture PNG.
/// </summary>
internal sealed class IconGridCell : SilentAutomationElement
{
    public const int Size = 16;

    private readonly bool _isHitTarget;
    private readonly Action? _onClick;
    private readonly ImageSource _bitmap;

    public IconGridCell(bool isHitTarget, Action? onClick, MediaColor bg, MediaColor fg)
    {
        _isHitTarget = isHitTarget;
        _onClick = onClick;
        _bitmap = CreateBitmapSource(CreatePixels(bg, fg), Size);
        Width = Size;
        Height = Size;
        Margin = new Thickness(2);
        if (isHitTarget)
        {
            Cursor = System.Windows.Input.Cursors.Hand;
            MouseLeftButtonUp += (_, _) => _onClick?.Invoke();
        }
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        drawingContext.DrawImage(_bitmap, new Rect(0, 0, Size, Size));
    }

    internal static byte[] CreateHitTemplatePixels()
    {
        var bg = MediaColor.FromRgb(0x20, 0xE0, 0x40);
        var fg = MediaColor.FromRgb(0xE0, 0x20, 0x20);
        return CreatePixels(bg, fg);
    }

    internal static byte[] CreatePixels(MediaColor bg, MediaColor fg)
    {
        var pixels = new byte[Size * Size * 4];
        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                var i = (y * Size + x) * 4;
                var hit =
                    (x >= 2 && x < 7 && y >= 2 && y < 14)
                    || (x >= 2 && x < 14 && y >= 2 && y < 7)
                    || (x >= 11 && x < 14 && y >= 11 && y < 14);
                var c = hit ? fg : bg;
                pixels[i] = c.B;
                pixels[i + 1] = c.G;
                pixels[i + 2] = c.R;
                pixels[i + 3] = 0xFF;
            }
        }

        return pixels;
    }

    private static ImageSource CreateBitmapSource(byte[] pixels, int size)
    {
        var bmp = BitmapSource.Create(
            size, size, 96, 96, PixelFormats.Bgra32, null, pixels, size * 4);
        bmp.Freeze();
        return bmp;
    }

    public bool IsHitTarget => _isHitTarget;
}
