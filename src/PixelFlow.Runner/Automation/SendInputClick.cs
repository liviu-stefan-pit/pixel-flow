using System.Runtime.InteropServices;
using PixelFlow.Core.Coordinates;
using PixelFlow.Core.Runner;

namespace PixelFlow.Runner.Automation;

/// <summary>
/// Physical mouse click at the center of a screen rect (for Win32/OCR/Image targets without Invoke).
/// Coordinates are physical pixels under Per-Monitor V2; SendInput absolute mapping uses the
/// virtual desktop (not primary-only metrics).
/// </summary>
internal static class SendInputClick
{
    private const int SmXVirtualScreen = 76;
    private const int SmYVirtualScreen = 77;
    private const int SmCxVirtualScreen = 78;
    private const int SmCyVirtualScreen = 79;

    public static void ClickCenter(ScreenRect bounds)
    {
        if (bounds.IsEmpty)
        {
            throw new InvalidOperationException("Cannot click empty bounds.");
        }

        var x = (int)Math.Round(bounds.X + bounds.Width / 2.0);
        var y = (int)Math.Round(bounds.Y + bounds.Height / 2.0);
        ClickAbsolute(x, y);
    }

    public static void ClickAbsolute(int x, int y)
    {
        var virtualLeft = NativeMethods.GetSystemMetrics(SmXVirtualScreen);
        var virtualTop = NativeMethods.GetSystemMetrics(SmYVirtualScreen);
        var virtualWidth = NativeMethods.GetSystemMetrics(SmCxVirtualScreen);
        var virtualHeight = NativeMethods.GetSystemMetrics(SmCyVirtualScreen);
        if (virtualWidth <= 1 || virtualHeight <= 1)
        {
            throw new InvalidOperationException("Unable to read virtual screen metrics for SendInput.");
        }

        // Best-effort: foreground the top-level window under the click so WPF/WinFormsHost
        // targets actually receive injected input (UIPI/foreground lock may still deny this).
        TryForegroundWindowAt(x, y);

        var (absX, absY) = DpiCoordinates.PhysicalToSendInputAbsolute(
            x, y, virtualLeft, virtualTop, virtualWidth, virtualHeight);

        var inputs = new NativeMethods.Input[3];
        inputs[0] = MouseMove(absX, absY);
        // Button events must NOT carry Absolute/VirtualDesk with dx=dy=0 — that repositions
        // the cursor to the virtual-desktop origin before the click.
        inputs[1] = MouseButton(NativeMethods.MouseeventfLeftDown);
        inputs[2] = MouseButton(NativeMethods.MouseeventfLeftUp);

        var sent = NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.Input>());
        if (sent != inputs.Length)
        {
            throw new InvalidOperationException(
                $"SendInput failed (sent {sent}/{inputs.Length}, error={Marshal.GetLastWin32Error()}).");
        }
    }

    private static void TryForegroundWindowAt(int x, int y)
    {
        var point = new NativeMethods.Point { X = x, Y = y };
        var hwnd = WindowFromPoint(point);
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var root = GetAncestorRoot(hwnd);
        if (root == 0)
        {
            root = hwnd;
        }

        NativeMethods.ShowWindow(root, NativeMethods.SwRestore);
        NativeMethods.SetForegroundWindow(root);
    }

    public static void ClickHwnd(nint hwnd)
    {
        if (hwnd == 0)
        {
            throw new InvalidOperationException("Cannot BM_CLICK a null HWND.");
        }

        // Prefer bringing parent top-level to foreground so the control can receive input.
        var root = GetAncestorRoot(hwnd);
        if (root != 0)
        {
            NativeMethods.ShowWindow(root, NativeMethods.SwRestore);
            NativeMethods.SetForegroundWindow(root);
        }

        NativeMethods.SendMessage(hwnd, NativeMethods.BmClick, IntPtr.Zero, IntPtr.Zero);
    }

    private static nint GetAncestorRoot(nint hwnd)
    {
        // Walk parents via GetAncestor if available; fallback: hwnd itself.
        const uint GaRoot = 2;
        var root = GetAncestor(hwnd, GaRoot);
        return root != 0 ? root : hwnd;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);

    private static NativeMethods.Input MouseMove(int absX, int absY) =>
        new()
        {
            Type = NativeMethods.InputMouse,
            U = new NativeMethods.InputUnion
            {
                Mi = new NativeMethods.MouseInput
                {
                    Dx = absX,
                    Dy = absY,
                    DwFlags = NativeMethods.MouseeventfMove
                        | NativeMethods.MouseeventfAbsolute
                        | NativeMethods.MouseeventfVirtualDesk,
                },
            },
        };

    private static NativeMethods.Input MouseButton(uint flag) =>
        new()
        {
            Type = NativeMethods.InputMouse,
            U = new NativeMethods.InputUnion
            {
                Mi = new NativeMethods.MouseInput
                {
                    DwFlags = flag,
                },
            },
        };

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(NativeMethods.Point pt);
}
