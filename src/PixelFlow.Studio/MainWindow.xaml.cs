using System.Windows;

namespace PixelFlow.Studio;

public partial class MainWindow : Window
{
    private readonly RunnerSession _session = new();
    private readonly string _projectFolder;
    private bool _runCommandBusy;

    public MainWindow()
    {
        InitializeComponent();
        _projectFolder = TryResolveProjectFolder();
        ProjectPathText.Text = "Project: " + _projectFolder;

        _session.StatusTextChanged += text => Dispatcher.BeginInvoke(() => StatusText.Text = "Runner: " + text);
        _session.LogReceived += line => Dispatcher.BeginInvoke(() => AppendLog(line));
        _session.Disconnected += () => Dispatcher.BeginInvoke(() =>
        {
            AppendLog("Disconnected from Runner. Click Run to start again.");
            UpdateCommandButtons();
        });
        _session.ConnectionStateChanged += () => Dispatcher.BeginInvoke(UpdateCommandButtons);

        Closed += async (_, _) => await _session.DisposeAsync();
        UpdateCommandButtons();
    }

    private static string TryResolveProjectFolder()
    {
        try
        {
            return RepoPaths.ResolveDefaultProjectFolder();
        }
        catch (Exception ex)
        {
            return $"(unavailable: {ex.Message})";
        }
    }

    private async void OnRunClick(object sender, RoutedEventArgs e)
    {
        try
        {
            _runCommandBusy = true;
            UpdateCommandButtons();
            AppendLog("Run requested…");
            await _session.RunProjectAsync(_projectFolder);
            AppendLog("Playing click-submit fixture (requires Test Bench open at Clicks: 0). Idle = success.");
        }
        catch (Exception ex)
        {
            AppendLog("ERROR: " + ex.Message);
            StatusText.Text = "Runner: error — " + ex.Message;
        }
        finally
        {
            _runCommandBusy = false;
            UpdateCommandButtons();
        }
    }

    private async void OnPauseClick(object sender, RoutedEventArgs e)
    {
        try
        {
            await _session.PauseAsync();
            AppendLog("Pause sent. Current Wait finishes, then status becomes Paused (held until Resume/Stop).");
        }
        catch (Exception ex)
        {
            AppendLog("ERROR: " + ex.Message);
            UpdateCommandButtons();
        }
    }

    private async void OnResumeClick(object sender, RoutedEventArgs e)
    {
        try
        {
            await _session.ResumeAsync();
            AppendLog("Resume sent — remaining steps should continue.");
        }
        catch (Exception ex)
        {
            AppendLog("ERROR: " + ex.Message);
            UpdateCommandButtons();
        }
    }

    private async void OnStopClick(object sender, RoutedEventArgs e)
    {
        try
        {
            await _session.StopAsync();
            AppendLog("Stop sent — run should abort.");
        }
        catch (Exception ex)
        {
            AppendLog("ERROR: " + ex.Message);
            UpdateCommandButtons();
        }
    }

    private void AppendLog(string line)
    {
        var stamp = DateTime.Now.ToString("HH:mm:ss");
        LogBox.AppendText($"[{stamp}] {line}{Environment.NewLine}");
        LogBox.ScrollToEnd();
    }

    private void UpdateCommandButtons()
    {
        var connected = _session.IsConnected;
        var running = connected && _session.IsRunInProgress;
        var paused = string.Equals(_session.LastRunnerState, "Paused", StringComparison.Ordinal);

        RunButton.IsEnabled = !_runCommandBusy && !running;
        PauseButton.IsEnabled = running && !paused;
        ResumeButton.IsEnabled = running && paused;
        StopButton.IsEnabled = running;
    }
}
