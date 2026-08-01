using System.Diagnostics;
using System.Text;
using PixelFlow.Core.Projects;
using PixelFlow.Core.Runner;

namespace PixelFlow.Runner.Automation;

/// <summary>
/// P12: Win32 class name + control ID locator, scoped to process/window.
/// </summary>
internal static class Win32Locator
{
    public static ResolveResult Find(LocatorLayer layer, ProcessWindowScope? scope)
    {
        if (!layer.Enabled)
        {
            return ResolveResult.NotFound("Win32 layer is disabled.");
        }

        if (string.IsNullOrWhiteSpace(layer.WindowClass) || layer.ControlId is null)
        {
            return ResolveResult.NotFound("Win32 layer requires windowClass and controlId.");
        }

        var processName = scope?.ProcessName;
        if (string.IsNullOrWhiteSpace(processName))
        {
            return ResolveResult.NotFound("Process scope is required (locator.scope.processName).");
        }

        Process[] processes;
        try
        {
            processes = Process.GetProcessesByName(NormalizeProcessName(processName));
        }
        catch (Exception ex)
        {
            return ResolveResult.NotFound($"Failed to enumerate process '{processName}': {ex.Message}");
        }

        if (processes.Length == 0)
        {
            foreach (var p in processes)
            {
                p.Dispose();
            }

            return ResolveResult.NotFound($"No running process named '{processName}'.");
        }

        try
        {
            var pids = new HashSet<int>();
            foreach (var process in processes)
            {
                pids.Add(process.Id);
            }

            var matches = new List<(nint Hwnd, NativeMethods.Rect Rect, int Pid)>();
            var wantedClass = layer.WindowClass.Trim();
            var wantedId = layer.ControlId.Value;
            var windowTitle = scope?.WindowTitle;

            EnumTopLevelForPids(pids, windowTitle, topLevel =>
            {
                EnumChildren(topLevel, child =>
                {
                    if (!NativeMethods.IsWindowVisible(child))
                    {
                        return;
                    }

                    if (!ClassMatches(child, wantedClass))
                    {
                        return;
                    }

                    if (NativeMethods.GetDlgCtrlID(child) != wantedId)
                    {
                        return;
                    }

                    if (!NativeMethods.GetWindowRect(child, out var rect))
                    {
                        return;
                    }

                    if (rect.Right <= rect.Left || rect.Bottom <= rect.Top)
                    {
                        return;
                    }

                    NativeMethods.GetWindowThreadProcessId(child, out var pid);
                    matches.Add((child, rect, (int)pid));
                });
            });

            if (matches.Count > 1)
            {
                return ResolveResult.NotFound(
                    "Multiple Win32 matches for class/controlId; refine scope.");
            }

            if (matches.Count == 0)
            {
                return ResolveResult.NotFound(
                    $"No Win32 control matched (class={wantedClass}, controlId={wantedId}, process={processName}).");
            }

            var (hwnd, r, pid) = matches[0];
            var bounds = new ScreenRect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
            return new ResolveResult(
                Found: true,
                CandidateId: $"win32:{pid}:{wantedClass}:{wantedId}",
                BoundingRect: bounds,
                Name: layer.Name,
                ControlType: "Win32." + wantedClass,
                ProcessId: pid,
                MatchedLayer: LocatorKinds.Win32,
                Confidence: 1.0,
                NativeHandle: hwnd);
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }
    }

    private static bool ClassMatches(nint hwnd, string wantedClass)
    {
        var sb = new StringBuilder(256);
        if (NativeMethods.GetClassName(hwnd, sb, sb.Capacity) <= 0)
        {
            return false;
        }

        var actual = sb.ToString();
        // Exact match, or WinForms-style prefix (WindowsForms10.BUTTON.app....).
        return string.Equals(actual, wantedClass, StringComparison.OrdinalIgnoreCase)
               || actual.StartsWith(wantedClass, StringComparison.OrdinalIgnoreCase);
    }

    private static void EnumTopLevelForPids(
        HashSet<int> pids,
        string? windowTitle,
        Action<nint> onWindow)
    {
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (!NativeMethods.IsWindowVisible(hwnd))
            {
                return true;
            }

            NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
            if (!pids.Contains((int)pid))
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(windowTitle))
            {
                var title = GetWindowText(hwnd);
                if (!string.Equals(title, windowTitle, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            onWindow(hwnd);
            return true;
        }, IntPtr.Zero);
    }

    private static void EnumChildren(nint parent, Action<nint> onChild)
    {
        NativeMethods.EnumChildWindows(parent, (hwnd, _) =>
        {
            onChild(hwnd);
            return true;
        }, IntPtr.Zero);
    }

    private static string GetWindowText(nint hwnd)
    {
        var sb = new StringBuilder(512);
        _ = GetWindowText(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    private static string NormalizeProcessName(string processName)
    {
        var name = processName.Trim();
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^4];
        }

        return name;
    }
}
