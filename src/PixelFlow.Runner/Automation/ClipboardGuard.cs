using System.Runtime.InteropServices;
using System.Text;

namespace PixelFlow.Runner.Automation;

/// <summary>
/// Snapshots the Unicode text clipboard, optionally replaces it for paste, then restores
/// previous contents (including empty / non-text) on <see cref="Dispose"/> or <see cref="Restore"/>.
/// </summary>
internal sealed class ClipboardGuard : IDisposable
{
    private const uint CfUnicodeText = 13;
    private const uint GmemMoveable = 0x0002;

    private readonly string? _previousText;
    private readonly bool _hadUnicodeText;
    private bool _restored;

    private ClipboardGuard(string? previousText, bool hadUnicodeText)
    {
        _previousText = previousText;
        _hadUnicodeText = hadUnicodeText;
    }

    /// <summary>
    /// Snapshot current clipboard, then set <paramref name="text"/> as CF_UNICODETEXT.
    /// Always dispose (or call <see cref="Restore"/>) to put the prior contents back.
    /// </summary>
    public static ClipboardGuard ReplaceWith(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (!OpenClipboardWithRetry())
        {
            throw new InvalidOperationException(
                $"OpenClipboard failed (error={Marshal.GetLastWin32Error()}).");
        }

        try
        {
            var hadText = TryReadUnicodeText(out var previous);
            var guard = new ClipboardGuard(previous, hadText);

            if (!EmptyClipboard())
            {
                throw new InvalidOperationException(
                    $"EmptyClipboard failed (error={Marshal.GetLastWin32Error()}).");
            }

            SetUnicodeText(text);
            return guard;
        }
        finally
        {
            CloseClipboard();
        }
    }

    /// <summary>Read current Unicode text without mutating the clipboard.</summary>
    public static bool TryGetText(out string text)
    {
        text = "";
        if (!OpenClipboardWithRetry())
        {
            return false;
        }

        try
        {
            return TryReadUnicodeText(out text!);
        }
        finally
        {
            CloseClipboard();
        }
    }

    /// <summary>Set Unicode text without snapshotting (tests / callers that manage restore).</summary>
    public static void SetText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (!OpenClipboardWithRetry())
        {
            throw new InvalidOperationException(
                $"OpenClipboard failed (error={Marshal.GetLastWin32Error()}).");
        }

        try
        {
            if (!EmptyClipboard())
            {
                throw new InvalidOperationException(
                    $"EmptyClipboard failed (error={Marshal.GetLastWin32Error()}).");
            }

            SetUnicodeText(text);
        }
        finally
        {
            CloseClipboard();
        }
    }

    public void Restore()
    {
        if (_restored)
        {
            return;
        }

        _restored = true;

        if (!OpenClipboardWithRetry())
        {
            Console.WriteLine(
                $"[runner] Clipboard restore: OpenClipboard failed (error={Marshal.GetLastWin32Error()}).");
            return;
        }

        try
        {
            if (!EmptyClipboard())
            {
                Console.WriteLine(
                    $"[runner] Clipboard restore: EmptyClipboard failed (error={Marshal.GetLastWin32Error()}).");
                return;
            }

            if (_hadUnicodeText && _previousText is not null)
            {
                SetUnicodeText(_previousText);
            }
            // else: leave empty (prior clipboard had no Unicode text)
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[runner] Clipboard restore failed: {ex.Message}");
        }
        finally
        {
            CloseClipboard();
        }
    }

    public void Dispose() => Restore();

    private static bool OpenClipboardWithRetry()
    {
        // Another app may hold the clipboard briefly; retry a few times.
        for (var i = 0; i < 10; i++)
        {
            if (OpenClipboard(IntPtr.Zero))
            {
                return true;
            }

            Thread.Sleep(20);
        }

        return false;
    }

    private static bool TryReadUnicodeText(out string? text)
    {
        text = null;
        if (!IsClipboardFormatAvailable(CfUnicodeText))
        {
            return false;
        }

        var handle = GetClipboardData(CfUnicodeText);
        if (handle == IntPtr.Zero)
        {
            return false;
        }

        var pointer = GlobalLock(handle);
        if (pointer == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            text = Marshal.PtrToStringUni(pointer) ?? "";
            return true;
        }
        finally
        {
            GlobalUnlock(handle);
        }
    }

    private static void SetUnicodeText(string text)
    {
        var bytes = Encoding.Unicode.GetBytes(text + "\0");
        var handle = GlobalAlloc(GmemMoveable, (UIntPtr)bytes.Length);
        if (handle == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                $"GlobalAlloc failed (error={Marshal.GetLastWin32Error()}).");
        }

        var pointer = GlobalLock(handle);
        if (pointer == IntPtr.Zero)
        {
            GlobalFree(handle);
            throw new InvalidOperationException(
                $"GlobalLock failed (error={Marshal.GetLastWin32Error()}).");
        }

        try
        {
            Marshal.Copy(bytes, 0, pointer, bytes.Length);
        }
        finally
        {
            GlobalUnlock(handle);
        }

        if (SetClipboardData(CfUnicodeText, handle) == IntPtr.Zero)
        {
            GlobalFree(handle);
            throw new InvalidOperationException(
                $"SetClipboardData failed (error={Marshal.GetLastWin32Error()}).");
        }
        // Ownership of handle transferred to the system on success.
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool IsClipboardFormatAvailable(uint format);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetClipboardData(uint uFormat);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalUnlock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalFree(IntPtr hMem);
}
