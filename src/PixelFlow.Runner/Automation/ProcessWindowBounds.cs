using System.Diagnostics;
using System.Text;
using PixelFlow.Core.Runner;

namespace PixelFlow.Runner.Automation;

/// <summary>
/// Resolves the screen bounds of a process top-level window for scoped capture.
/// </summary>
internal static class ProcessWindowBounds
{
    public static bool TryGet(
        string processName,
        string? windowTitle,
        out int x,
        out int y,
        out int width,
        out int height,
        out int processId,
        out string failureReason)
    {
        x = y = width = height = processId = 0;
        failureReason = "";

        Process[] processes;
        try
        {
            processes = Process.GetProcessesByName(Normalize(processName));
        }
        catch (Exception ex)
        {
            failureReason = $"Failed to enumerate process '{processName}': {ex.Message}";
            return false;
        }

        if (processes.Length == 0)
        {
            failureReason = $"No running process named '{processName}'.";
            return false;
        }

        try
        {
            var pids = new HashSet<int>();
            foreach (var p in processes)
            {
                pids.Add(p.Id);
            }

            nint found = 0;
            var foundPid = 0;
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

                found = hwnd;
                foundPid = (int)pid;
                return false; // stop
            }, IntPtr.Zero);

            if (found == 0)
            {
                failureReason = string.IsNullOrWhiteSpace(windowTitle)
                    ? $"Process '{processName}' has no visible top-level window."
                    : $"No visible window titled '{windowTitle}' for process '{processName}'.";
                return false;
            }

            if (!NativeMethods.GetWindowRect(found, out var rect))
            {
                failureReason = "GetWindowRect failed for target window.";
                return false;
            }

            x = rect.Left;
            y = rect.Top;
            width = rect.Right - rect.Left;
            height = rect.Bottom - rect.Top;
            processId = foundPid;

            if (width <= 0 || height <= 0)
            {
                failureReason = "Target window has empty bounds.";
                return false;
            }

            return true;
        }
        finally
        {
            foreach (var p in processes)
            {
                p.Dispose();
            }
        }
    }

    private static string GetWindowText(nint hwnd)
    {
        var sb = new StringBuilder(512);
        _ = GetWindowTextNative(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindowText", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int GetWindowTextNative(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    private static string Normalize(string processName)
    {
        var name = processName.Trim();
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^4];
        }

        return name;
    }
}
