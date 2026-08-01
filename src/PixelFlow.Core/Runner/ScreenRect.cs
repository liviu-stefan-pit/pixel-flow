namespace PixelFlow.Core.Runner;

/// <summary>
/// Axis-aligned screen rectangle in <b>physical pixels</b> (device pixels), as returned by
/// UIA <c>BoundingRectangle</c> / Win32 <c>GetWindowRect</c> when the process is
/// Per-Monitor V2 DPI-aware. Convert to/from DIP via <c>PixelFlow.Core.Coordinates.DpiCoordinates</c>.
/// </summary>
public readonly record struct ScreenRect(double X, double Y, double Width, double Height)
{
    public bool IsEmpty => Width <= 0 || Height <= 0;

    public override string ToString() => $"[{X:0.##},{Y:0.##} {Width:0.##}x{Height:0.##}]";
}
