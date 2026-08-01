using System.Runtime.InteropServices;
using PixelFlow.Core.Runner;

namespace PixelFlow.Runner.Automation;

/// <summary>
/// Heuristic user-interference detection via <c>GetLastInputInfo</c> + cursor movement (P24).
/// Watches only after <see cref="BeginActionGate"/> so ambient pre-step activity does not false-pause.
/// </summary>
internal sealed class Win32UserInterferenceDetector : IUserInterferenceDetector
{
    /// <summary>Cursor delta (px) during the action gate that counts as mouse movement.</summary>
    public const int DefaultMoveThresholdPx = 6;

    private readonly int _moveThresholdPx;
    private readonly object _gate = new();

    private uint _lastSyntheticInputTime;
    private uint _gateLastInputTime;
    private NativeMethods.Point _gateCursor;
    private bool _gateOpen;

    public Win32UserInterferenceDetector(int moveThresholdPx = DefaultMoveThresholdPx)
    {
        _moveThresholdPx = moveThresholdPx > 0 ? moveThresholdPx : DefaultMoveThresholdPx;
    }

    public void BeginActionGate()
    {
        lock (_gate)
        {
            _gateLastInputTime = ReadLastInputTime();
            _ = NativeMethods.GetCursorPos(out _gateCursor);
            _gateOpen = true;
        }
    }

    public bool IsUserInterfering()
    {
        lock (_gate)
        {
            if (!_gateOpen)
            {
                return false;
            }

            var lastInput = ReadLastInputTime();

            // Physical input arrived after the gate opened (and was not our synthetic input).
            if (lastInput > _gateLastInputTime && lastInput > _lastSyntheticInputTime)
            {
                return true;
            }

            // Cursor moved while resolving / verifying / about to act.
            if (NativeMethods.GetCursorPos(out var nowCursor))
            {
                var dx = nowCursor.X - _gateCursor.X;
                var dy = nowCursor.Y - _gateCursor.Y;
                if ((dx * dx) + (dy * dy) >= _moveThresholdPx * _moveThresholdPx)
                {
                    return true;
                }
            }

            return false;
        }
    }

    public void NoteSyntheticInput()
    {
        lock (_gate)
        {
            _lastSyntheticInputTime = ReadLastInputTime();
            _gateOpen = false;
        }
    }

    private static uint ReadLastInputTime()
    {
        var info = new NativeMethods.LastInputInfo
        {
            CbSize = (uint)Marshal.SizeOf<NativeMethods.LastInputInfo>(),
        };

        if (!NativeMethods.GetLastInputInfo(ref info))
        {
            return 0;
        }

        return info.DwTime;
    }
}
