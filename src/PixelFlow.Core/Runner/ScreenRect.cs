namespace PixelFlow.Core.Runner;

/// <summary>
/// Axis-aligned screen rectangle in physical pixels (UIA bounding rect).
/// </summary>
public readonly record struct ScreenRect(double X, double Y, double Width, double Height)
{
    public bool IsEmpty => Width <= 0 || Height <= 0;

    public override string ToString() => $"[{X:0.##},{Y:0.##} {Width:0.##}x{Height:0.##}]";
}
