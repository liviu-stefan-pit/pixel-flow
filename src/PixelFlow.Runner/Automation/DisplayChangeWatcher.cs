using System.ComponentModel;
using System.Runtime.InteropServices;
using PixelFlow.Core.Coordinates;

namespace PixelFlow.Runner.Automation;

/// <summary>
/// Listens for <c>WM_DISPLAYCHANGE</c> on a message-only HWND and invalidates
/// <see cref="IDisplayChangeTracker"/> so absolute coordinate caches are busted.
/// </summary>
internal sealed class DisplayChangeWatcher : IDisposable
{
    private const int WmDisplayChange = 0x007E;
    private const int WmDestroy = 0x0002;
    private const int WmAppShutdown = 0x8000; // WM_APP
    private const int SmXVirtualScreen = 76;
    private const int SmYVirtualScreen = 77;
    private const int SmCxVirtualScreen = 78;
    private const int SmCyVirtualScreen = 79;
    private const int SmCMonitors = 80;
    private static readonly IntPtr HwndMessage = new(-3);

    // Keep alive for native callback lifetime.
    // ReSharper disable once PrivateFieldCanBeConvertedToLocalVariable
    private static readonly WndProc SharedWndProc = WindowProc;

    private readonly IDisplayChangeTracker _tracker;
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new(false);
    private readonly ManualResetEventSlim _stopped = new(false);
    private volatile Exception? _startError;
    private IntPtr _hwnd;
    private string? _className;
    private bool _disposed;

    public DisplayChangeWatcher(IDisplayChangeTracker tracker)
    {
        _tracker = tracker ?? throw new ArgumentNullException(nameof(tracker));

        _thread = new Thread(MessageLoop)
        {
            IsBackground = true,
            Name = "PixelFlow.DisplayChangeWatcher",
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();

        if (!_ready.Wait(TimeSpan.FromSeconds(5)))
        {
            throw new TimeoutException("Display-change watcher message loop did not start.");
        }

        if (_startError is not null)
        {
            throw new InvalidOperationException("Failed to start display-change watcher.", _startError);
        }
    }

    /// <summary>Read current virtual-desktop topology via Win32 system metrics.</summary>
    public static DisplayTopology CaptureTopology()
    {
        return new DisplayTopology(
            NativeMethods.GetSystemMetrics(SmXVirtualScreen),
            NativeMethods.GetSystemMetrics(SmYVirtualScreen),
            NativeMethods.GetSystemMetrics(SmCxVirtualScreen),
            NativeMethods.GetSystemMetrics(SmCyVirtualScreen),
            NativeMethods.GetSystemMetrics(SmCMonitors));
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
            _className = "PixelFlow.DisplayChange." + Guid.NewGuid().ToString("N");
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
                "PixelFlow.DisplayChange",
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

            // Store hwnd so WindowProc can find the watcher via a static map.
            RegisterInstance(_hwnd, this);

            Console.WriteLine(
                $"[runner] Display-change watcher armed (topology {_tracker.Snapshot}, gen={_tracker.Generation}).");
            _ready.Set();

            while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
            {
                if (msg.message == WmAppShutdown)
                {
                    PostQuitMessage(0);
                    continue;
                }

                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }

            if (_hwnd != IntPtr.Zero)
            {
                UnregisterInstance(_hwnd);
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

    private void OnDisplayChange()
    {
        var topology = CaptureTopology();
        Console.WriteLine(
            $"[runner] Display change detected — invalidating absolute coordinate cache " +
            $"(gen will bump; topology={topology}).");
        _tracker.Invalidate(topology, "WM_DISPLAYCHANGE");
        Console.WriteLine(
            $"[runner] Absolute coordinate cache busted (gen={_tracker.Generation}, topology={_tracker.Snapshot}).");
    }

    private static readonly object InstanceGate = new();
    private static readonly Dictionary<IntPtr, DisplayChangeWatcher> Instances = new();

    private static void RegisterInstance(IntPtr hwnd, DisplayChangeWatcher watcher)
    {
        lock (InstanceGate)
        {
            Instances[hwnd] = watcher;
        }
    }

    private static void UnregisterInstance(IntPtr hwnd)
    {
        lock (InstanceGate)
        {
            Instances.Remove(hwnd);
        }
    }

    private static IntPtr WindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WmDisplayChange)
        {
            DisplayChangeWatcher? watcher;
            lock (InstanceGate)
            {
                Instances.TryGetValue(hWnd, out watcher);
            }

            try
            {
                watcher?.OnDisplayChange();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[runner] Display-change handler error: {ex.Message}");
            }

            return IntPtr.Zero;
        }

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
