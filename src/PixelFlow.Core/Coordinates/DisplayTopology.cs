namespace PixelFlow.Core.Coordinates;

/// <summary>
/// Snapshot of the virtual desktop used for absolute screen coordinates.
/// A mismatch after a monitor add/remove/resolution change means cached physical
/// coords must be discarded and the target re-resolved.
/// </summary>
public readonly record struct DisplayTopology(
    int VirtualLeft,
    int VirtualTop,
    int VirtualWidth,
    int VirtualHeight,
    int MonitorCount)
{
    public bool IsEmpty => VirtualWidth <= 0 || VirtualHeight <= 0;

    public override string ToString() =>
        $"virt=({VirtualLeft},{VirtualTop} {VirtualWidth}x{VirtualHeight}) monitors={MonitorCount}";
}
