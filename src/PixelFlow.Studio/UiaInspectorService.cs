using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Threading;

namespace PixelFlow.Studio;

/// <summary>
/// P16: polls the UIA element under the mouse and exposes properties for hand-authoring locators.
/// </summary>
internal sealed class UiaInspectorService : IDisposable
{
    private readonly DispatcherTimer _timer;
    private readonly Action<UiaInspectSnapshot> _onUpdate;
    private bool _disposed;

    public UiaInspectorService(Action<UiaInspectSnapshot> onUpdate)
    {
        _onUpdate = onUpdate ?? throw new ArgumentNullException(nameof(onUpdate));
        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(120),
        };
        _timer.Tick += (_, _) => Poll();
    }

    public bool IsRunning => _timer.IsEnabled;

    public void Start()
    {
        if (!_timer.IsEnabled)
        {
            _timer.Start();
        }
    }

    public void Stop()
    {
        _timer.Stop();
        _onUpdate(UiaInspectSnapshot.Idle);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer.Stop();
    }

    private void Poll()
    {
        try
        {
            if (!GetCursorPos(out var pt))
            {
                _onUpdate(UiaInspectSnapshot.Idle with { Error = "GetCursorPos failed." });
                return;
            }

            var element = AutomationElement.FromPoint(new System.Windows.Point(pt.X, pt.Y));
            if (element is null)
            {
                _onUpdate(new UiaInspectSnapshot(
                    ScreenX: pt.X,
                    ScreenY: pt.Y,
                    Status: "No element under cursor"));
                return;
            }

            var current = element.Current;
            var rect = current.BoundingRectangle;
            var pid = current.ProcessId;
            string processName = "";
            try
            {
                if (pid > 0)
                {
                    using var proc = System.Diagnostics.Process.GetProcessById(pid);
                    processName = proc.ProcessName;
                }
            }
            catch
            {
                processName = $"(pid {pid})";
            }

            string windowTitle = "";
            try
            {
                var walker = TreeWalker.ControlViewWalker;
                var node = element;
                for (var i = 0; i < 32 && node is not null; i++)
                {
                    if (node.Current.ControlType == ControlType.Window)
                    {
                        windowTitle = node.Current.Name ?? "";
                        break;
                    }

                    node = walker.GetParent(node);
                }
            }
            catch (ElementNotAvailableException)
            {
                // ignore
            }

            _onUpdate(new UiaInspectSnapshot(
                ScreenX: pt.X,
                ScreenY: pt.Y,
                Status: "Hovering",
                AutomationId: current.AutomationId ?? "",
                Name: current.Name ?? "",
                ControlType: current.ControlType?.ProgrammaticName ?? "",
                Bounds: $"[{rect.X:0},{rect.Y:0} {rect.Width:0}x{rect.Height:0}]",
                ProcessName: processName,
                ProcessId: pid,
                WindowTitle: windowTitle,
                ClassName: current.ClassName ?? ""));
        }
        catch (ElementNotAvailableException)
        {
            _onUpdate(UiaInspectSnapshot.Idle with { Status = "Element unavailable" });
        }
        catch (Exception ex)
        {
            _onUpdate(UiaInspectSnapshot.Idle with { Error = ex.Message });
        }
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }
}

internal sealed record UiaInspectSnapshot(
    int ScreenX = 0,
    int ScreenY = 0,
    string Status = "Idle",
    string AutomationId = "",
    string Name = "",
    string ControlType = "",
    string Bounds = "",
    string ProcessName = "",
    int ProcessId = 0,
    string WindowTitle = "",
    string ClassName = "",
    string? Error = null)
{
    public static UiaInspectSnapshot Idle { get; } = new();

    public string FormatDisplay()
    {
        if (!string.IsNullOrEmpty(Error))
        {
            return "Error: " + Error;
        }

        var sb = new StringBuilder();
        sb.AppendLine($"Status      : {Status}");
        sb.AppendLine($"Cursor      : ({ScreenX}, {ScreenY})");
        sb.AppendLine($"AutomationId: {AutomationId}");
        sb.AppendLine($"Name        : {Name}");
        sb.AppendLine($"ControlType : {ControlType}");
        sb.AppendLine($"ClassName   : {ClassName}");
        sb.AppendLine($"Bounds      : {Bounds}");
        sb.AppendLine($"Process     : {ProcessName} (pid {ProcessId})");
        sb.AppendLine($"Window      : {WindowTitle}");
        return sb.ToString().TrimEnd();
    }
}
