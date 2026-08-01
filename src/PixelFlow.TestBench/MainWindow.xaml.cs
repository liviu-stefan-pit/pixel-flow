using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Automation;

namespace PixelFlow.TestBench;

public partial class MainWindow : Window
{
    private int _clicks;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Clear AutomationId so OCR fixtures don't accidentally resolve via UIA.
        AutomationProperties.SetAutomationId(OcrTargetButton, "");
        AutomationProperties.SetName(OcrTargetButton, "OCR Target Label");

        var host = new NativeButtonHost(IncrementClicks)
        {
            Width = 158,
            Height = 34,
        };
        Win32HostSlot.Child = host;

        ImageTarget.Source = CreateTemplateBitmap();
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
