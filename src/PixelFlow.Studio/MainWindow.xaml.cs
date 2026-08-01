using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using PixelFlow.Core.Diagnostics;
using PixelFlow.Core.Projects;

namespace PixelFlow.Studio;

public partial class MainWindow : Window
{
    private readonly RunnerSession _session = new();
    private readonly ProjectStore _store = new();
    private readonly UiaInspectorService _inspector;
    private readonly ObservableCollection<StepListItem> _stepItems = [];

    private ProjectDocument _document = CreateBlankDocument();
    private string? _projectFolder;
    private bool _runCommandBusy;
    private bool _suppressDetailEvents;
    private string? _lastSnipHash;

    public MainWindow()
    {
        InitializeComponent();
        StepsList.ItemsSource = _stepItems;

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

        TryLoadStartupProject();
        UpdateCommandButtons();
        UpdateDetailEnabledState();
    }

    private void TryLoadStartupProject()
    {
        try
        {
            var folder = RepoPaths.ResolveDefaultProjectFolder();
            if (Directory.Exists(folder) && File.Exists(ProjectPaths.ProjectFile(folder)))
            {
                LoadProjectFromFolder(folder);
                return;
            }

            _projectFolder = folder;
            _document = CreateBlankDocument();
            RefreshStepList();
            UpdateProjectPathText();
            AppendLog("No project.json at default path — started blank. Use Open or Save.");
        }
        catch (Exception ex)
        {
            _projectFolder = null;
            _document = CreateBlankDocument();
            RefreshStepList();
            UpdateProjectPathText();
            AppendLog("Startup load skipped: " + ex.Message);
        }
    }

    private void LoadProjectFromFolder(string folder)
    {
        _document = _store.Load(folder);
        _projectFolder = Path.GetFullPath(folder);
        RefreshStepList();
        UpdateProjectPathText();
        AppendLog($"Loaded {_document.Steps.Count} step(s) from {_projectFolder}");
    }

    private void OnOpenClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Open PixelFlow project folder (.pflow)",
        };
        if (_projectFolder is not null && Directory.Exists(_projectFolder))
        {
            dialog.InitialDirectory = _projectFolder;
        }

        if (dialog.ShowDialog(this) != true || string.IsNullOrWhiteSpace(dialog.FolderName))
        {
            return;
        }

        try
        {
            CommitSelectedStepDetails();
            LoadProjectFromFolder(dialog.FolderName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Open failed", MessageBoxButton.OK, MessageBoxImage.Error);
            AppendLog("Open ERROR: " + ex.Message);
        }
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!EnsureProjectFolderForSave())
            {
                return;
            }

            CommitSelectedStepDetails();
            _store.Save(_projectFolder!, _document);
            AppendLog("Saved " + _projectFolder);
            UpdateProjectPathText();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Save failed", MessageBoxButton.OK, MessageBoxImage.Error);
            AppendLog("Save ERROR: " + ex.Message);
        }
    }

    private void OnNewClick(object sender, RoutedEventArgs e)
    {
        CommitSelectedStepDetails();
        _document = CreateBlankDocument();
        _projectFolder = null;
        _lastSnipHash = null;
        LastSnipBox.Text = "No snip yet.";
        RefreshStepList();
        RefreshImageTokenUi(null);
        UpdateProjectPathText();
        AppendLog("New blank project (Save to choose a folder).");
    }

    private void OnSnipClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!EnsureProjectFolderForSave())
            {
                AppendLog("Snip cancelled — project folder required to store assets.");
                return;
            }

            // Persist project.json so assets/ exists beside a real project.
            CommitSelectedStepDetails();
            _store.Save(_projectFolder!, _document);

            WindowState = WindowState.Minimized;
            try
            {
                var png = SnipOverlayWindow.CaptureRegionInteractive(owner: null);
                if (png is null || png.Length == 0)
                {
                    AppendLog("Snip cancelled.");
                    return;
                }

                var hash = _store.SavePngAsset(_projectFolder!, png);
                _lastSnipHash = hash;
                var path = ProjectPaths.AssetPath(_projectFolder!, hash);
                LastSnipBox.Text = hash + Environment.NewLine + path;
                AppendLog($"Snip saved: {hash} ({png.Length} bytes) → {path}");

                ApplySnipHashToSelectedClick(hash);
            }
            finally
            {
                WindowState = WindowState.Normal;
                Activate();
            }
        }
        catch (Exception ex)
        {
            WindowState = WindowState.Normal;
            AppendLog("Snip ERROR: " + ex.Message);
            MessageBox.Show(this, ex.Message, "Snip failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ApplySnipHashToSelectedClick(string hash)
    {
        if (StepsList.SelectedItem is not StepListItem item)
        {
            return;
        }

        if (!string.Equals(item.Step.Type, "Click", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        item.Step.Locator ??= new LocatorChain();
        var imageLayer = item.Step.Locator.Layers
            .FirstOrDefault(l => string.Equals(l.Kind, "Image", StringComparison.OrdinalIgnoreCase));
        if (imageLayer is null)
        {
            imageLayer = new LocatorLayer
            {
                Kind = "Image",
                Enabled = true,
                ConfidenceThreshold = 0.85,
            };
            item.Step.Locator.Layers.Add(imageLayer);
        }

        imageLayer.ImageAssetHash = hash;
        StepImageHashBox.Text = hash;
        RefreshImageTokenUi(hash);
        item.RefreshThumbnail(_projectFolder);
        item.NotifyDisplayChanged();
    }

    private void OnClearImageTokenClick(object sender, RoutedEventArgs e)
    {
        if (StepsList.SelectedItem is not StepListItem item)
        {
            return;
        }

        if (!string.Equals(item.Step.Type, "Click", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var locator = item.Step.Locator;
        if (locator is null)
        {
            return;
        }

        locator.Layers.RemoveAll(l =>
            string.Equals(l.Kind, "Image", StringComparison.OrdinalIgnoreCase));

        StepImageHashBox.Text = "";
        RefreshImageTokenUi(null);
        item.RefreshThumbnail(_projectFolder);
        item.NotifyDisplayChanged();
        AppendLog($"Cleared image token on step {item.Step.Id}");
    }

    private void RefreshImageTokenUi(string? imageAssetHash)
    {
        StepImageHashBox.Text = imageAssetHash ?? "";
        var thumbnail = ImageTokenLoader.TryLoadThumbnail(_projectFolder, imageAssetHash, decodePixelWidth: 112);
        StepImageTokenImage.Source = thumbnail;
        StepImageTokenPlaceholder.Visibility = thumbnail is null ? Visibility.Visible : Visibility.Collapsed;
        ClearImageTokenButton.IsEnabled =
            StepsList.SelectedItem is StepListItem &&
            !string.IsNullOrWhiteSpace(imageAssetHash);
    }

    private bool EnsureProjectFolderForSave()
    {
        if (!string.IsNullOrWhiteSpace(_projectFolder))
        {
            return true;
        }

        var dialog = new OpenFolderDialog
        {
            Title = "Choose folder to save this .pflow project",
        };
        if (dialog.ShowDialog(this) != true || string.IsNullOrWhiteSpace(dialog.FolderName))
        {
            return false;
        }

        _projectFolder = Path.GetFullPath(dialog.FolderName);
        if (string.IsNullOrWhiteSpace(_document.Name))
        {
            _document.Name = Path.GetFileName(_projectFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        }

        UpdateProjectPathText();
        return true;
    }

    private void OnAddWaitClick(object sender, RoutedEventArgs e) =>
        AddStep(new ScriptStep { Id = NextStepId("wait"), Type = "Wait", WaitMs = 500 });

    private void OnAddClickClick(object sender, RoutedEventArgs e) =>
        AddStep(new ScriptStep
        {
            Id = NextStepId("click"),
            Type = "Click",
            Locator = new LocatorChain
            {
                Scope = new ProcessWindowScope
                {
                    ProcessName = "PixelFlow.TestBench",
                    WindowTitle = "Test Bench",
                },
                Layers =
                [
                    new LocatorLayer
                    {
                        Kind = "UiaStructural",
                        Enabled = true,
                        ConfidenceThreshold = 0.9,
                        AutomationId = "TbSubmit",
                        ControlType = "Button",
                        Name = "Submit",
                    },
                ],
            },
        });

    private void OnAddTypeClick(object sender, RoutedEventArgs e) =>
        AddStep(new ScriptStep
        {
            Id = NextStepId("type"),
            Type = "Type",
            Text = "",
            Locator = new LocatorChain
            {
                Scope = new ProcessWindowScope
                {
                    ProcessName = "PixelFlow.TestBench",
                    WindowTitle = "Test Bench",
                },
                Layers =
                [
                    new LocatorLayer
                    {
                        Kind = "UiaStructural",
                        Enabled = true,
                        ConfidenceThreshold = 0.9,
                        AutomationId = "TbInput",
                        ControlType = "Edit",
                        Name = "Input",
                    },
                ],
            },
        });

    private void AddStep(ScriptStep step)
    {
        CommitSelectedStepDetails();
        _document.Steps.Add(step);
        var item = new StepListItem(step);
        item.RefreshThumbnail(_projectFolder);
        _stepItems.Add(item);
        StepsList.SelectedItem = item;
        StepsList.ScrollIntoView(item);
    }

    private void OnRemoveStepClick(object sender, RoutedEventArgs e)
    {
        if (StepsList.SelectedItem is not StepListItem item)
        {
            return;
        }

        var index = _stepItems.IndexOf(item);
        _document.Steps.Remove(item.Step);
        _stepItems.RemoveAt(index);
        if (_stepItems.Count > 0)
        {
            StepsList.SelectedIndex = Math.Min(index, _stepItems.Count - 1);
        }
        else
        {
            ClearDetailFields();
        }
    }

    private void OnMoveUpClick(object sender, RoutedEventArgs e) => MoveSelected(-1);

    private void OnMoveDownClick(object sender, RoutedEventArgs e) => MoveSelected(1);

    private void MoveSelected(int delta)
    {
        if (StepsList.SelectedItem is not StepListItem item)
        {
            return;
        }

        CommitSelectedStepDetails();
        var index = _stepItems.IndexOf(item);
        var target = index + delta;
        if (target < 0 || target >= _stepItems.Count)
        {
            return;
        }

        _document.Steps.RemoveAt(index);
        _document.Steps.Insert(target, item.Step);
        _stepItems.Move(index, target);
        StepsList.SelectedItem = item;
    }

    private void OnStepsSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressDetailEvents)
        {
            return;
        }

        // Commit the previously selected step (still in RemovedItems) before loading the new one.
        if (e.RemovedItems.Count > 0 && e.RemovedItems[0] is StepListItem previous)
        {
            ApplyDetailsToStep(previous.Step);
            previous.NotifyDisplayChanged();
        }

        LoadSelectedStepDetails();
        UpdateDetailEnabledState();
    }

    private void OnStepDetailChanged(object sender, RoutedEventArgs e)
    {
        if (_suppressDetailEvents)
        {
            return;
        }

        CommitSelectedStepDetails();
    }

    private void OnStepTypeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressDetailEvents)
        {
            return;
        }

        CommitSelectedStepDetails();
        UpdateDetailEnabledState();
    }

    private void CommitSelectedStepDetails()
    {
        if (StepsList.SelectedItem is not StepListItem item)
        {
            return;
        }

        ApplyDetailsToStep(item.Step);
        item.NotifyDisplayChanged();
    }

    private void ApplyDetailsToStep(ScriptStep step)
    {
        step.Id = StepIdBox.Text?.Trim() ?? "";
        step.Type = (StepTypeBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? step.Type;

        if (int.TryParse(StepWaitMsBox.Text?.Trim(), out var waitMs))
        {
            step.WaitMs = waitMs;
        }
        else if (string.Equals(step.Type, "Wait", StringComparison.OrdinalIgnoreCase))
        {
            step.WaitMs ??= 500;
        }

        step.Text = string.IsNullOrWhiteSpace(StepTextBox.Text) ? null : StepTextBox.Text;

        // Opt-in failure screenshot: checked => true; unchecked => clear override (inherit project default = off).
        step.CaptureFailureScreenshot = StepCaptureFailureScreenshotBox.IsChecked == true ? true : null;

        if (string.Equals(step.Type, "Click", StringComparison.OrdinalIgnoreCase)
            || string.Equals(step.Type, "Type", StringComparison.OrdinalIgnoreCase))
        {
            step.Locator ??= new LocatorChain();
            step.Locator.Scope ??= new ProcessWindowScope();
            step.Locator.Scope.ProcessName = NullIfBlank(StepProcessBox.Text);
            step.Locator.Scope.WindowTitle = NullIfBlank(StepWindowBox.Text);

            var uia = step.Locator.Layers
                .FirstOrDefault(l => string.Equals(l.Kind, "UiaStructural", StringComparison.OrdinalIgnoreCase));
            if (uia is null)
            {
                uia = new LocatorLayer { Kind = "UiaStructural", Enabled = true, ConfidenceThreshold = 0.9 };
                step.Locator.Layers.Insert(0, uia);
            }

            uia.AutomationId = NullIfBlank(StepAutomationIdBox.Text);
            uia.ControlType = NullIfBlank(StepControlTypeBox.Text);
            uia.Name = NullIfBlank(StepNameBox.Text);

            // Image tokens are Click-only (template match target).
            if (string.Equals(step.Type, "Click", StringComparison.OrdinalIgnoreCase))
            {
                var tokenHash = NullIfBlank(StepImageHashBox.Text);
                var image = step.Locator.Layers
                    .FirstOrDefault(l => string.Equals(l.Kind, "Image", StringComparison.OrdinalIgnoreCase));
                if (tokenHash is not null)
                {
                    if (image is null)
                    {
                        image = new LocatorLayer
                        {
                            Kind = "Image",
                            Enabled = true,
                            ConfidenceThreshold = 0.85,
                        };
                        step.Locator.Layers.Add(image);
                    }

                    image.ImageAssetHash = tokenHash;
                    image.Enabled = true;
                }
                else if (image is not null)
                {
                    step.Locator.Layers.Remove(image);
                }
            }
        }
    }

    private void LoadSelectedStepDetails()
    {
        _suppressDetailEvents = true;
        try
        {
            if (StepsList.SelectedItem is not StepListItem item)
            {
                ClearDetailFields();
                return;
            }

            var step = item.Step;
            StepIdBox.Text = step.Id;
            SelectStepType(step.Type);
            StepWaitMsBox.Text = step.WaitMs?.ToString() ?? "";
            StepTextBox.Text = step.Text ?? "";
            StepCaptureFailureScreenshotBox.IsChecked = step.CaptureFailureScreenshot == true;

            var scope = step.Locator?.Scope;
            StepProcessBox.Text = scope?.ProcessName ?? "";
            StepWindowBox.Text = scope?.WindowTitle ?? "";

            var uia = step.Locator?.Layers
                .FirstOrDefault(l => string.Equals(l.Kind, "UiaStructural", StringComparison.OrdinalIgnoreCase));
            StepAutomationIdBox.Text = uia?.AutomationId ?? "";
            StepControlTypeBox.Text = uia?.ControlType ?? "";
            StepNameBox.Text = uia?.Name ?? "";

            var image = step.Locator?.Layers
                .FirstOrDefault(l => string.Equals(l.Kind, "Image", StringComparison.OrdinalIgnoreCase));
            RefreshImageTokenUi(image?.ImageAssetHash);
        }
        finally
        {
            _suppressDetailEvents = false;
        }
    }

    private void ClearDetailFields()
    {
        _suppressDetailEvents = true;
        try
        {
            StepIdBox.Text = "";
            StepTypeBox.SelectedIndex = -1;
            StepWaitMsBox.Text = "";
            StepTextBox.Text = "";
            StepProcessBox.Text = "";
            StepWindowBox.Text = "";
            StepAutomationIdBox.Text = "";
            StepControlTypeBox.Text = "";
            StepNameBox.Text = "";
            StepCaptureFailureScreenshotBox.IsChecked = false;
            RefreshImageTokenUi(null);
        }
        finally
        {
            _suppressDetailEvents = false;
        }
    }

    private void SelectStepType(string type)
    {
        for (var i = 0; i < StepTypeBox.Items.Count; i++)
        {
            if (StepTypeBox.Items[i] is ComboBoxItem item &&
                string.Equals(item.Content?.ToString(), type, StringComparison.OrdinalIgnoreCase))
            {
                StepTypeBox.SelectedIndex = i;
                return;
            }
        }

        StepTypeBox.SelectedIndex = 0;
    }

    private void UpdateDetailEnabledState()
    {
        var hasSelection = StepsList.SelectedItem is StepListItem;
        var type = (StepTypeBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";
        var isWait = string.Equals(type, "Wait", StringComparison.OrdinalIgnoreCase);
        var isType = string.Equals(type, "Type", StringComparison.OrdinalIgnoreCase);
        var isClick = string.Equals(type, "Click", StringComparison.OrdinalIgnoreCase);

        StepIdBox.IsEnabled = hasSelection;
        StepTypeBox.IsEnabled = hasSelection;
        StepWaitMsBox.IsEnabled = hasSelection && isWait;
        StepTextBox.IsEnabled = hasSelection && isType;
        var needsLocator = isClick || isType;
        StepProcessBox.IsEnabled = hasSelection && needsLocator;
        StepWindowBox.IsEnabled = hasSelection && needsLocator;
        StepAutomationIdBox.IsEnabled = hasSelection && needsLocator;
        StepControlTypeBox.IsEnabled = hasSelection && needsLocator;
        StepNameBox.IsEnabled = hasSelection && needsLocator;
        StepImageTokenBorder.IsEnabled = hasSelection && isClick;
        ClearImageTokenButton.IsEnabled = hasSelection && isClick && !string.IsNullOrWhiteSpace(StepImageHashBox.Text);
        StepCaptureFailureScreenshotBox.IsEnabled = hasSelection;
    }

    private void RefreshStepList()
    {
        _suppressDetailEvents = true;
        try
        {
            _stepItems.Clear();
            foreach (var step in _document.Steps)
            {
                var item = new StepListItem(step);
                item.RefreshThumbnail(_projectFolder);
                _stepItems.Add(item);
            }

            if (_stepItems.Count > 0)
            {
                StepsList.SelectedIndex = 0;
            }
            else
            {
                ClearDetailFields();
            }
        }
        finally
        {
            _suppressDetailEvents = false;
        }

        LoadSelectedStepDetails();
        UpdateDetailEnabledState();
    }

    private string NextStepId(string prefix)
    {
        var n = 1;
        while (_document.Steps.Any(s => string.Equals(s.Id, $"{prefix}-{n}", StringComparison.OrdinalIgnoreCase)))
        {
            n++;
        }

        return $"{prefix}-{n}";
    }

    private void UpdateProjectPathText()
    {
        ProjectPathText.Text = _projectFolder is null
            ? "Project: (unsaved — Save to choose a folder)"
            : "Project: " + _projectFolder;
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
            CommitSelectedStepDetails();
            if (!EnsureProjectFolderForSave())
            {
                AppendLog("Run cancelled — save a project folder first.");
                return;
            }

            _store.Save(_projectFolder!, _document);
            AppendLog("Saved before run: " + _projectFolder);

            _runCommandBusy = true;
            UpdateCommandButtons();
            AppendLog("Run requested…");
            await _session.RunProjectAsync(_projectFolder!);
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

    private void OnLastReportClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_projectFolder))
            {
                AppendLog("No project folder — open or save a project first.");
                return;
            }

            var latest = RunReportStore.FindLatestReportDirectory(_projectFolder);
            if (latest is null)
            {
                AppendLog("No run reports yet under " + ProjectPaths.ReportsFolder(_projectFolder));
                return;
            }

            var summary = RunReportStore.FormatSummary(latest);
            AppendLog("--- Last report ---");
            foreach (var line in summary.Split('\n'))
            {
                AppendLog(line);
            }

            AppendLog("--- end report ---");
        }
        catch (Exception ex)
        {
            AppendLog("Last report ERROR: " + ex.Message);
            MessageBox.Show(this, ex.Message, "Last report failed", MessageBoxButton.OK, MessageBoxImage.Error);
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
        OpenButton.IsEnabled = !running;
        SaveButton.IsEnabled = !running;
        NewButton.IsEnabled = !running;
        SnipButton.IsEnabled = !running;
    }

    private static ProjectDocument CreateBlankDocument() => new()
    {
        SchemaVersion = ProjectSchema.CurrentVersion,
        Name = "untitled",
        Steps = [],
    };

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed class StepListItem : INotifyPropertyChanged
    {
        public StepListItem(ScriptStep step) => Step = step;

        public ScriptStep Step { get; }

        public string Display => Format(Step);

        public BitmapImage? Thumbnail { get; private set; }

        public Visibility ThumbnailVisibility =>
            Thumbnail is null ? Visibility.Collapsed : Visibility.Visible;

        public event PropertyChangedEventHandler? PropertyChanged;

        public void RefreshThumbnail(string? projectFolder)
        {
            var hash = ImageTokenLoader.GetImageAssetHash(Step);
            Thumbnail = ImageTokenLoader.TryLoadThumbnail(projectFolder, hash, decodePixelWidth: 44);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Thumbnail)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ThumbnailVisibility)));
        }

        public void NotifyDisplayChanged() =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Display)));

        private static string Format(ScriptStep step)
        {
            return step.Type switch
            {
                "Wait" => $"{step.Id} | Wait | {step.WaitMs ?? 0}ms",
                "Type" => $"{step.Id} | Type | {Truncate(step.Text)}",
                "Click" => FormatClick(step),
                _ => $"{step.Id} | {step.Type}",
            };
        }

        private static string FormatClick(ScriptStep step)
        {
            var imageHash = ImageTokenLoader.GetImageAssetHash(step);
            if (imageHash is not null)
            {
                var shortHash = imageHash.Length > 18 ? imageHash[..18] + "…" : imageHash;
                return $"{step.Id} | Click | [img] {shortHash}";
            }

            var automationId = step.Locator?.Layers
                .FirstOrDefault(l => !string.IsNullOrWhiteSpace(l.AutomationId))
                ?.AutomationId;
            return $"{step.Id} | Click | {automationId ?? "(no target)"}";
        }

        private static string Truncate(string? text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "(empty)";
            }

            return text.Length <= 24 ? text : text[..21] + "…";
        }
    }
}
