using System.Runtime.InteropServices;
using PixelFlow.Core.Runner;

namespace PixelFlow.Runner.Automation;

/// <summary>
/// Physical mouse click at the center of a screen rect (for Win32/OCR/Image targets without Invoke).
/// </summary>
internal static class SendInputClick
{
    private const int SmCxScreen = 0;
    private const int SmCyScreen = 1;

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
        var screenW = NativeMethods.GetSystemMetrics(SmCxScreen);
        var screenH = NativeMethods.GetSystemMetrics(SmCyScreen);
        if (screenW <= 1 || screenH <= 1)
        {
            throw new InvalidOperationException("Unable to read primary screen metrics for SendInput.");
        }

        // Absolute SendInput uses 0..65535 normalized coordinates.
        var absX = (int)Math.Round(x * 65535.0 / (screenW - 1));
        var absY = (int)Math.Round(y * 65535.0 / (screenH - 1));

        var inputs = new NativeMethods.Input[3];
        inputs[0] = MouseMove(absX, absY);
        inputs[1] = MouseButton(NativeMethods.MouseeventfLeftDown);
        inputs[2] = MouseButton(NativeMethods.MouseeventfLeftUp);

        var sent = NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.Input>());
        if (sent != inputs.Length)
        {
            throw new InvalidOperationException(
                $"SendInput failed (sent {sent}/{inputs.Length}, error={Marshal.GetLastWin32Error()}).");
        }
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
                    DwFlags = NativeMethods.MouseeventfMove | NativeMethods.MouseeventfAbsolute,
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
                    DwFlags = flag | NativeMethods.MouseeventfAbsolute,
                },
            },
        };
}
