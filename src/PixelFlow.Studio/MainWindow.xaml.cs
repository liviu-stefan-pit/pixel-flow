using System.Windows;
using PixelFlow.Core.Projects;

namespace PixelFlow.Studio;

public partial class MainWindow : Window
{
    private readonly RunnerSession _session = new();
    private readonly string _projectFolder;
    private readonly UiaInspectorService _inspector;
    private bool _runCommandBusy;

    public MainWindow()
    {
        InitializeComponent();
        _projectFolder = TryResolveProjectFolder();
        ProjectPathText.Text = "Project: " + _projectFolder;

        _inspector = new UiaInspectorService(snap =>
        {
            InspectorBox.Text = snap.FormatDisplay();
        });

        _session.StatusTextChanged += text => Dispatcher.BeginInvoke(() => StatusText.Text = "Runner: " + text);
        _session.LogReceived += line => Dispatcher.BeginInvoke(() => AppendLog(line));
        _session.Disconnected += () => Dispatcher.BeginInvoke(() =>
        {
            AppendLog("Disconnected from Runner. Click Run to start again.");
            UpdateCommandButtons();
        });
        _session.ConnectionStateChanged += () => Dispatcher.BeginInvoke(UpdateCommandButtons);

        Closed += async (_, _) =>
        {
            _inspector.Dispose();
            await _session.DisposeAsync();
        };
        UpdateCommandButtons();
    }

    private void OnTestLocatorClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var layer = new LocatorLayer
            {
                Kind = "UiaStructural",
                Enabled = true,
                AutomationId = NullIfBlank(LocatorAutomationIdBox.Text),
                ControlType = NullIfBlank(LocatorControlTypeBox.Text),
                Name = NullIfBlank(LocatorNameBox.Text),
            };

            var scope = new ProcessWindowScope
            {
                ProcessName = NullIfBlank(LocatorProcessBox.Text),
                WindowTitle = NullIfBlank(LocatorWindowBox.Text),
            };

            var result = UiaLocatorProbe.Find(layer, scope);
            if (!result.Found)
            {
                var reason = result.FailureReason ?? "No match.";
                LocatorTestResultBox.Text = "FAIL: " + reason;
                AppendLog("Test locator FAIL: " + reason);
                return;
            }

            var summary =
                $"OK: layer={result.MatchedLayer}, confidence={result.Confidence:0.###}, " +
                $"AutomationId={result.AutomationId}, Name={result.Name}, " +
                $"bounds={result.BoundingRect}";
            LocatorTestResultBox.Text = summary;
            AppendLog("Test locator " + summary);
            HighlightOverlayWindow.Flash(result.BoundingRect);
        }
        catch (Exception ex)
        {
            LocatorTestResultBox.Text = "ERROR: " + ex.Message;
            AppendLog("Test locator ERROR: " + ex.Message);
        }
    }

    private static string? NullIfBlank(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
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

    private void OnInspectorChecked(object sender, RoutedEventArgs e)
    {
        _inspector.Start();
        InspectorBox.Text = "Inspector on — hover a UI element…";
    }

    private void OnInspectorUnchecked(object sender, RoutedEventArgs e)
    {
        _inspector.Stop();
        InspectorBox.Text = "Inspector off — enable the checkbox above.";
    }

    private async void OnRunClick(object sender, RoutedEventArgs e)
    {
        try
        {
            _runCommandBusy = true;
            UpdateCommandButtons();
            AppendLog("Run requested…");
            await _session.RunProjectAsync(_projectFolder);
            AppendLog("Playing project (Idle = success). Emergency stop: Ctrl+Shift+F12.");
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
