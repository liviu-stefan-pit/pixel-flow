using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PixelFlow.Studio;

/// <summary>
/// P19: fullscreen overlay for region snipping. Returns PNG bytes of the selected region.
/// </summary>
public partial class SnipOverlayWindow : Window
{
    private bool _dragging;
    private Point _startDip;
    private Rect _selectionDip;

    public byte[]? PngBytes { get; private set; }

    public SnipOverlayWindow()
    {
        InitializeComponent();
        WindowState = WindowState.Maximized;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;
    }

    /// <summary>Show the overlay and return PNG bytes, or null if cancelled.</summary>
    public static byte[]? CaptureRegionInteractive(Window? owner)
    {
        var overlay = new SnipOverlayWindow();
        if (owner is not null)
        {
            overlay.Owner = owner;
        }

        return overlay.ShowDialog() == true ? overlay.PngBytes : null;
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            Close();
        }
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragging = true;
        _startDip = e.GetPosition(RootCanvas);
        _selectionDip = new Rect(_startDip, new Size(0, 0));
        SelectionBorder.Visibility = Visibility.Visible;
        UpdateSelectionVisual();
        CaptureMouse();
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        var current = e.GetPosition(RootCanvas);
        _selectionDip = new Rect(
            Math.Min(_startDip.X, current.X),
            Math.Min(_startDip.Y, current.Y),
            Math.Abs(current.X - _startDip.X),
            Math.Abs(current.Y - _startDip.Y));
        UpdateSelectionVisual();
    }

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        ReleaseMouseCapture();

        if (_selectionDip.Width < 2 || _selectionDip.Height < 2)
        {
            DialogResult = false;
            Close();
            return;
        }

        // Hide overlay before capture so we don't snip our own dimmed overlay.
        Hide();
        Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Render);

        try
        {
            var topLeft = RootCanvas.PointToScreen(new Point(_selectionDip.X, _selectionDip.Y));
            var bottomRight = RootCanvas.PointToScreen(new Point(
                _selectionDip.X + _selectionDip.Width,
                _selectionDip.Y + _selectionDip.Height));

            var x = (int)Math.Round(Math.Min(topLeft.X, bottomRight.X));
            var y = (int)Math.Round(Math.Min(topLeft.Y, bottomRight.Y));
            var w = Math.Max(1, (int)Math.Round(Math.Abs(bottomRight.X - topLeft.X)));
            var h = Math.Max(1, (int)Math.Round(Math.Abs(bottomRight.Y - topLeft.Y)));

            PngBytes = ScreenRegionCapture.CapturePng(x, y, w, h);
            DialogResult = true;
        }
        catch
        {
            PngBytes = null;
            DialogResult = false;
            throw;
        }
        finally
        {
            Close();
        }
    }

    private void UpdateSelectionVisual()
    {
        Canvas.SetLeft(SelectionBorder, _selectionDip.X);
        Canvas.SetTop(SelectionBorder, _selectionDip.Y);
        SelectionBorder.Width = Math.Max(0, _selectionDip.Width);
        SelectionBorder.Height = Math.Max(0, _selectionDip.Height);
    }
}
