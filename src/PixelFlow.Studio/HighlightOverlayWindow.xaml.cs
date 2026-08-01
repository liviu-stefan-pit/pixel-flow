using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using PixelFlow.Core.Runner;

namespace PixelFlow.Studio;

/// <summary>
/// P17: brief topmost overlay that flashes the matched element's screen bounds.
/// </summary>
public partial class HighlightOverlayWindow : Window
{
    private static HighlightOverlayWindow? _current;
    private readonly ScreenRect _screenRect;
    private readonly DispatcherTimer _closeTimer;

    private HighlightOverlayWindow(ScreenRect screenRect)
    {
        InitializeComponent();
        _screenRect = screenRect;
        _closeTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(1600),
        };
        _closeTimer.Tick += (_, _) =>
        {
            _closeTimer.Stop();
            Close();
        };
    }

    /// <summary>Show (or replace) a highlight around the given physical-pixel rect.</summary>
    public static void Flash(ScreenRect screenRect)
    {
        if (screenRect.IsEmpty)
        {
            return;
        }

        if (_current is not null)
        {
            try
            {
                _current._closeTimer.Stop();
                _current.Close();
            }
            catch
            {
                // ignore
            }

            _current = null;
        }

        var pad = 3.0;
        var inflated = new ScreenRect(
            screenRect.X - pad,
            screenRect.Y - pad,
            screenRect.Width + pad * 2,
            screenRect.Height + pad * 2);

        var overlay = new HighlightOverlayWindow(inflated);
        _current = overlay;
        overlay.Closed += (_, _) =>
        {
            if (ReferenceEquals(_current, overlay))
            {
                _current = null;
            }
        };
        overlay.Show();
        overlay._closeTimer.Start();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var hwnd = new WindowInteropHelper(this).Handle;
        MakeClickThrough(hwnd);
        PositionFromScreenPixels(hwnd);
    }

    private void PositionFromScreenPixels(IntPtr hwnd)
    {
        var dpi = GetDpiForWindow(hwnd);
        if (dpi == 0)
        {
            dpi = 96;
        }

        var scale = dpi / 96.0;
        Left = _screenRect.X / scale;
        Top = _screenRect.Y / scale;
        Width = Math.Max(8, _screenRect.Width / scale);
        Height = Math.Max(8, _screenRect.Height / scale);
    }

    private static void MakeClickThrough(IntPtr hwnd)
    {
        const int gwlExStyle = -20;
        const int wsExTransparent = 0x00000020;
        const int wsExToolWindow = 0x00000080;
        const int wsExNoActivate = 0x08000000;

        var ex = GetWindowLong(hwnd, gwlExStyle);
        _ = SetWindowLong(hwnd, gwlExStyle, ex | wsExTransparent | wsExToolWindow | wsExNoActivate);
    }

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);
}
