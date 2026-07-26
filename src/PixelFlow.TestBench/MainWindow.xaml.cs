using System.Windows;

namespace PixelFlow.TestBench;

public partial class MainWindow : Window
{
    private int _clicks;

    public MainWindow()
    {
        InitializeComponent();
    }

    private void OnSubmitClick(object sender, RoutedEventArgs e)
    {
        _clicks++;
        CounterLabel.Text = $"Clicks: {_clicks}";
    }
}
