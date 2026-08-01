using System.ComponentModel;
using System.Runtime.InteropServices;

namespace PixelFlow.Runner;

/// <summary>
/// Global emergency-stop hotkey (Ctrl+Shift+F12). Works regardless of which window has focus.
/// Uses a dedicated STA thread with a message-only HWND so the console Runner receives WM_HOTKEY.
/// </summary>
internal sealed class EmergencyStopHotkey : IDisposable
{
    public const string ChordDisplay = "Ctrl+Shift+F12";

    private const int HotkeyId = 0x5046; // 'PF'
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint VkF12 = 0x7B;
    private const int WmHotkey = 0x0312;
    private const int WmDestroy = 0x0002;
    private const int WmAppShutdown = 0x8000; // WM_APP
    private static readonly IntPtr HwndMessage = new(-3);

    // Keep alive for native callback lifetime.
    // ReSharper disable once PrivateFieldCanBeConvertedToLocalVariable
    private static readonly WndProc SharedWndProc = WindowProc;

    private readonly Action _onTriggered;
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new(false);
    private readonly ManualResetEventSlim _stopped = new(false);
    private volatile Exception? _startError;
    private IntPtr _hwnd;
    private string? _className;
    private bool _disposed;

    public EmergencyStopHotkey(Action onTriggered)
    {
        _onTriggered = onTriggered ?? throw new ArgumentNullException(nameof(onTriggered));
        _thread = new Thread(MessageLoop)
        {
            IsBackground = true,
            Name = "PixelFlow.EmergencyStopHotkey",
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();

        if (!_ready.Wait(TimeSpan.FromSeconds(5)))
        {
            throw new TimeoutException("Emergency-stop hotkey message loop did not start.");
        }

        if (_startError is not null)
        {
            throw new InvalidOperationException(
                $"Failed to register emergency-stop hotkey ({ChordDisplay}).",
                _startError);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        var hwnd = _hwnd;
        if (hwnd != IntPtr.Zero)
        {
            PostMessage(hwnd, WmAppShutdown, IntPtr.Zero, IntPtr.Zero);
        }

        _ = _stopped.Wait(TimeSpan.FromSeconds(2));
        _ready.Dispose();
        _stopped.Dispose();
    }

    private void MessageLoop()
    {
        try
        {
            _className = "PixelFlow.EmergencyStop." + Guid.NewGuid().ToString("N");
            var wndClass = new WndClassEx
            {
                cbSize = (uint)Marshal.SizeOf<WndClassEx>(),
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(SharedWndProc),
                hInstance = GetModuleHandle(null),
                lpszClassName = _className,
            };

            if (RegisterClassEx(ref wndClass) == 0)
            {
                _startError = new Win32Exception(Marshal.GetLastWin32Error(), "RegisterClassEx failed.");
                _ready.Set();
                return;
            }

            _hwnd = CreateWindowEx(
                0,
                _className,
                "PixelFlow.EmergencyStop",
                0,
                0,
                0,
                0,
                0,
                HwndMessage,
                IntPtr.Zero,
                wndClass.hInstance,
                IntPtr.Zero);

            if (_hwnd == IntPtr.Zero)
            {
                _startError = new Win32Exception(Marshal.GetLastWin32Error(), "CreateWindowEx failed.");
                _ready.Set();
                return;
            }

            if (!RegisterHotKey(_hwnd, HotkeyId, ModControl | ModShift, VkF12))
            {
                _startError = new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    $"RegisterHotKey({ChordDisplay}) failed. Another app may already own this chord.");
                DestroyWindow(_hwnd);
                _hwnd = IntPtr.Zero;
                _ready.Set();
                return;
            }

            Console.WriteLine($"[runner] Emergency stop armed: press {ChordDisplay} (global) to abort.");
            _ready.Set();

            while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
            {
                if (msg.message == WmAppShutdown)
                {
                    PostQuitMessage(0);
                    continue;
                }

                if (msg.message == WmHotkey && msg.wParam.ToInt32() == HotkeyId)
                {
                    Console.WriteLine($"[runner] Emergency stop hotkey ({ChordDisplay}) pressed — aborting.");
                    try
                    {
                        _onTriggered();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[runner] Emergency stop handler error: {ex.Message}");
                    }

                    continue;
                }

                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }

            if (_hwnd != IntPtr.Zero)
            {
                UnregisterHotKey(_hwnd, HotkeyId);
                DestroyWindow(_hwnd);
                _hwnd = IntPtr.Zero;
            }

            if (_className is not null)
            {
                UnregisterClass(_className, GetModuleHandle(null));
            }
        }
        catch (Exception ex)
        {
            _startError ??= ex;
            _ready.Set();
        }
        finally
        {
            _stopped.Set();
        }
    }

    private static IntPtr WindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WmDestroy)
        {
            PostQuitMessage(0);
            return IntPtr.Zero;
        }

        return DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WndClassEx
    {
        public uint cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int ptX;
        public int ptY;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

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
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool UnregisterClass(string lpClassName, IntPtr hInstance);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern void PostQuitMessage(int nExitCode);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out Msg lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref Msg lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref Msg lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassEx(ref WndClassEx lpwcx);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);
}
