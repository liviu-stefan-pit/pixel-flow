using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Forms.Integration;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WinForms = System.Windows.Forms;
using WpfButton = System.Windows.Controls.Button;
using WpfPoint = System.Windows.Point;
using MediaColor = System.Windows.Media.Color;

namespace PixelFlow.TestBench;

public partial class MainWindow : Window
{
    private int _clicks;
    private WpfButton? _movingTarget;
    private int _movingIndex;
    private DispatcherTimer? _movingTimer;

    private static readonly WpfPoint[] MovingPositions =
    [
        new(8, 18),
        new(160, 8),
        new(80, 36),
        new(200, 24),
    ];

    public MainWindow()
    {
        InitializeComponent();
        // Pin to the primary work area so Live SendInput paths are not flaky on a
        // secondary monitor with negative virtual-desktop coordinates.
        Left = SystemParameters.WorkArea.Left + 48;
        Top = SystemParameters.WorkArea.Top + 48;
        Loaded += OnLoaded;
        Closed += (_, _) => _movingTimer?.Stop();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Claim foreground so Live SendInput paths (image/OCR/type) receive clicks even when
        // the test host launched us without activation rights succeeding later.
        Activate();
        Topmost = true;
        Topmost = false;
        // Clear AutomationId so OCR fixtures don't accidentally resolve via UIA.
        AutomationProperties.SetAutomationId(OcrTargetButton, "");
        AutomationProperties.SetName(OcrTargetButton, "OCR Target Label");

        var win32Host = new NativeButtonHost(IncrementClicks)
        {
            Width = 158,
            Height = 34,
        };
        Win32HostSlot.Child = win32Host;

        HostWinFormsButton();
        HostCustomCanvas();
        HostIconGrid();
        HostMovingTarget();

        ImageTarget.Source = CreateTemplateBitmap();
    }

    private void HostWinFormsButton()
    {
        var host = new WindowsFormsHost();
        var button = new WinForms.Button
        {
            Name = "TbWinForms",
            Text = "WinForms Click",
            Dock = WinForms.DockStyle.Fill,
            TabStop = true,
        };
        // AccessibleName backs UIA Name; Name property is typically AutomationId for WinForms.
        button.AccessibleName = "WinForms Click";
        button.Click += (_, _) => IncrementClicks();
        host.Child = button;
        WinFormsHostSlot.Child = host;
    }

    private void HostCustomCanvas()
    {
        CustomCanvasSlot.Child = new CustomCanvasTarget(IncrementClicks);
    }

    private void HostIconGrid()
    {
        // Decoy icons (dull) + one lime/red hit target matching the fixture PNG.
        var decoys = new (byte R, byte G, byte B, byte Fr, byte Fg, byte Fb)[]
        {
            (0x60, 0x60, 0x70, 0xA0, 0xA0, 0xB0),
            (0x50, 0x40, 0x30, 0x90, 0x80, 0x70),
            (0x30, 0x50, 0x60, 0x70, 0x90, 0xA0),
            (0x55, 0x55, 0x55, 0x99, 0x99, 0x99),
            (0x40, 0x30, 0x50, 0x80, 0x70, 0x90),
            (0x35, 0x45, 0x35, 0x75, 0x85, 0x75),
            (0x48, 0x48, 0x58, 0x88, 0x88, 0x98),
        };

        foreach (var d in decoys)
        {
            IconGridPanel.Children.Add(new IconGridCell(
                isHitTarget: false,
                onClick: null,
                bg: MediaColor.FromRgb(d.R, d.G, d.B),
                fg: MediaColor.FromRgb(d.Fr, d.Fg, d.Fb)));
        }

        IconGridPanel.Children.Add(new IconGridCell(
            isHitTarget: true,
            onClick: IncrementClicks,
            bg: MediaColor.FromRgb(0x20, 0xE0, 0x40),
            fg: MediaColor.FromRgb(0xE0, 0x20, 0x20)));
    }

    private void HostMovingTarget()
    {
        _movingTarget = new WpfButton
        {
            Content = "Move",
            Width = 64,
            Height = 28,
            FontSize = 12,
        };
        AutomationProperties.SetAutomationId(_movingTarget, "TbMovingTarget");
        AutomationProperties.SetName(_movingTarget, "Move");
        _movingTarget.Click += (_, _) => IncrementClicks();

        MovingTargetCanvas.Children.Add(_movingTarget);
        PlaceMovingTarget(0);

        _movingTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(700),
        };
        _movingTimer.Tick += (_, _) =>
        {
            _movingIndex = (_movingIndex + 1) % MovingPositions.Length;
            PlaceMovingTarget(_movingIndex);
        };
        _movingTimer.Start();
    }

    private void PlaceMovingTarget(int index)
    {
        if (_movingTarget is null)
        {
            return;
        }

        var p = MovingPositions[index];
        Canvas.SetLeft(_movingTarget, p.X);
        Canvas.SetTop(_movingTarget, p.Y);
    }

    private void OnSubmitClick(object sender, RoutedEventArgs e) => IncrementClicks();

    private void OnOcrTargetClick(object sender, RoutedEventArgs e) => IncrementClicks();

    private void OnImageTargetClick(object sender, System.Windows.Input.MouseButtonEventArgs e) =>
        IncrementClicks();

    private void IncrementClicks()
    {
        _clicks++;
        CounterLabel.Text = $"Clicks: {_clicks}";
    }

    /// <summary>
    /// Distinctive 64x64 magenta/cyan pattern — must match fixtures/projects/image-click.pflow asset.
    /// </summary>
    internal static BitmapSource CreateTemplateBitmap()
    {
        const int size = 64;
        var pixels = new byte[size * size * 4];
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var i = (y * size + x) * 4;
                // Magenta background
                pixels[i + 0] = 0xAA; // B
                pixels[i + 1] = 0x00; // G
                pixels[i + 2] = 0xFF; // R
                pixels[i + 3] = 0xFF; // A

                // Cyan diamond / cross in the center
                var dx = Math.Abs(x - 32);
                var dy = Math.Abs(y - 32);
                if (dx + dy < 18 || (dx < 3 && dy < 22) || (dy < 3 && dx < 22))
                {
                    pixels[i + 0] = 0xFF; // B
                    pixels[i + 1] = 0xFF; // G
                    pixels[i + 2] = 0x00; // R
                }
            }
        }

        var bmp = BitmapSource.Create(
            size,
            size,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            pixels,
            size * 4);
        bmp.Freeze();
        return bmp;
    }
}
