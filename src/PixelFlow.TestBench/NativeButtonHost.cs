using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace PixelFlow.TestBench;

/// <summary>
/// Hosts a native Win32 BUTTON with a stable control ID for the P12 Win32 locator.
/// Uses an intermediate host HWND so BN_CLICKED (WM_COMMAND) is delivered to this class.
/// </summary>
internal sealed class NativeButtonHost : HwndHost
{
    public const int ControlId = 1001;
    public const string WindowClass = "BUTTON";
    public const string ButtonText = "Win32 Click";

    private readonly Action _onClick;
    private IntPtr _hwndHost;
    private IntPtr _hwndButton;

    private const int WsChild = 0x40000000;
    private const int WsVisible = 0x10000000;
    private const int WsTabstop = 0x00010000;
    private const int WsClipchildren = 0x02000000;
    private const int BsPushButton = 0x00000000;
    private const int BnClicked = 0;
    private const int WmCommand = 0x0111;
    private const int WmSize = 0x0005;
    private const string HostClass = "STATIC";

    public NativeButtonHost(Action onClick)
    {
        _onClick = onClick ?? throw new ArgumentNullException(nameof(onClick));
    }

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        var w = Math.Max(1, (int)Width);
        var h = Math.Max(1, (int)Height);

        // Intermediate host receives WM_COMMAND from the child BUTTON.
        _hwndHost = CreateWindowEx(
            0,
            HostClass,
            "",
            WsChild | WsVisible | WsClipchildren,
            0,
            0,
            w,
            h,
            hwndParent.Handle,
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero);

        if (_hwndHost == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                $"CreateWindowEx(STATIC host) failed (error={Marshal.GetLastWin32Error()}).");
        }

        _hwndButton = CreateWindowEx(
            0,
            WindowClass,
            ButtonText,
            WsChild | WsVisible | WsTabstop | BsPushButton,
            0,
            0,
            w,
            h,
            _hwndHost,
            new IntPtr(ControlId),
            IntPtr.Zero,
            IntPtr.Zero);

        if (_hwndButton == IntPtr.Zero)
        {
            DestroyWindow(_hwndHost);
            _hwndHost = IntPtr.Zero;
            throw new InvalidOperationException(
                $"CreateWindowEx(BUTTON) failed (error={Marshal.GetLastWin32Error()}).");
        }

        return new HandleRef(this, _hwndHost);
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        if (_hwndButton != IntPtr.Zero)
        {
            DestroyWindow(_hwndButton);
            _hwndButton = IntPtr.Zero;
        }

        DestroyWindow(hwnd.Handle);
        _hwndHost = IntPtr.Zero;
    }

    protected override IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmSize && _hwndButton != IntPtr.Zero)
        {
            var width = (int)(lParam.ToInt64() & 0xFFFF);
            var height = (int)((lParam.ToInt64() >> 16) & 0xFFFF);
            MoveWindow(_hwndButton, 0, 0, Math.Max(1, width), Math.Max(1, height), true);
        }

        if (msg == WmCommand)
        {
            var code = (int)((wParam.ToInt64() >> 16) & 0xFFFF);
            var id = (int)(wParam.ToInt64() & 0xFFFF);
            if (code == BnClicked && (id == ControlId || lParam == _hwndButton))
            {
                _onClick();
                handled = true;
                return IntPtr.Zero;
            }
        }

        return base.WndProc(hwnd, msg, wParam, lParam, ref handled);
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowEx(
        int dwExStyle,
        string lpClassName,
        string lpWindowName,
        int dwStyle,
        int x,
        int y,
        int nWidth,
        int nHeight,
        IntPtr hWndParent,
        IntPtr hMenu,
        IntPtr hInstance,
        IntPtr lpParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);
}
