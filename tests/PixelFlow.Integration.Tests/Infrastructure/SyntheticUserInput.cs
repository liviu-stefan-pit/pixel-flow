using System.Runtime.InteropServices;

namespace PixelFlow.Integration.Tests.Infrastructure;

/// <summary>
/// Injects a tiny absolute mouse move via SendInput so GetLastInputInfo advances (P24 live tests).
/// </summary>
internal static class SyntheticUserInput
{
    private const uint InputMouse = 0;
    private const uint MouseeventfMove = 0x0001;
    private const uint MouseeventfAbsolute = 0x8000;

    public static void NudgeMouse()
    {
        var screenW = GetSystemMetrics(0);
        var screenH = GetSystemMetrics(1);
        if (screenW <= 0 || screenH <= 0)
        {
            throw new InvalidOperationException("Unable to read screen metrics for SendInput nudge.");
        }

        // Current cursor → normalized absolute coords, then +1px equivalent nudge.
        if (!GetCursorPos(out var pt))
        {
            throw new InvalidOperationException("GetCursorPos failed.");
        }

        var nx = (pt.X * 65535) / Math.Max(screenW - 1, 1);
        var ny = (pt.Y * 65535) / Math.Max(screenH - 1, 1);
        var nudgedX = Math.Min(nx + 200, 65535); // ~small move in normalized space
        var nudgedY = ny;

        var inputs = new[]
        {
            new Input
            {
                Type = InputMouse,
                U = new InputUnion
                {
                    Mi = new MouseInput
                    {
                        Dx = nudgedX,
                        Dy = nudgedY,
                        DwFlags = MouseeventfMove | MouseeventfAbsolute,
                    },
                },
            },
            new Input
            {
                Type = InputMouse,
                U = new InputUnion
                {
                    Mi = new MouseInput
                    {
                        Dx = nx,
                        Dy = ny,
                        DwFlags = MouseeventfMove | MouseeventfAbsolute,
                    },
                },
            },
        };

        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
        if (sent != inputs.Length)
        {
            throw new InvalidOperationException(
                $"SendInput nudge failed (sent {sent}/{inputs.Length}, error={Marshal.GetLastWin32Error()}).");
        }
    }

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, Input[] pInputs, int cbSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public MouseInput Mi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int Dx;
        public int Dy;
        public uint MouseData;
        public uint DwFlags;
        public uint Time;
        public IntPtr DwExtraInfo;
    }
}
