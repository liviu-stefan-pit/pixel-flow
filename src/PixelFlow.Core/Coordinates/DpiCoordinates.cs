namespace PixelFlow.Core.Coordinates;

/// <summary>
/// Pure DIP ↔ physical pixel helpers using Windows DPI (96 DIP = 1 inch).
/// Physical pixels are what UIA/Win32/SendInput use under Per-Monitor V2 awareness.
/// </summary>
public static class DpiCoordinates
{
    public const double StandardDpi = 96.0;

    /// <summary>Scale factor for a monitor DPI (100% → 1.0, 125% → 1.25, 150% → 1.5).</summary>
    public static double ScaleFromDpi(double dpi)
    {
        if (dpi <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dpi), dpi, "DPI must be positive.");
        }

        return dpi / StandardDpi;
    }

    /// <summary>Percent scale for a monitor DPI (100, 125, 150, …).</summary>
    public static double PercentFromDpi(double dpi) => ScaleFromDpi(dpi) * 100.0;

    public static double DipToPhysical(double dip, double dpi) => dip * ScaleFromDpi(dpi);

    public static double PhysicalToDip(double physical, double dpi) => physical / ScaleFromDpi(dpi);

    public static (double X, double Y) DipPointToPhysical(double xDip, double yDip, double dpi) =>
        (DipToPhysical(xDip, dpi), DipToPhysical(yDip, dpi));

    public static (double X, double Y) PhysicalPointToDip(double xPhysical, double yPhysical, double dpi) =>
        (PhysicalToDip(xPhysical, dpi), PhysicalToDip(yPhysical, dpi));

    /// <summary>
    /// Convert a DIP-space rect (origin relative to monitor) to physical pixels at <paramref name="dpi"/>.
    /// </summary>
    public static (double X, double Y, double Width, double Height) DipRectToPhysical(
        double xDip,
        double yDip,
        double widthDip,
        double heightDip,
        double dpi)
    {
        var scale = ScaleFromDpi(dpi);
        return (xDip * scale, yDip * scale, widthDip * scale, heightDip * scale);
    }

    /// <summary>
    /// Convert a physical-pixel rect to DIP space at <paramref name="dpi"/>.
    /// </summary>
    public static (double X, double Y, double Width, double Height) PhysicalRectToDip(
        double xPhysical,
        double yPhysical,
        double widthPhysical,
        double heightPhysical,
        double dpi)
    {
        var scale = ScaleFromDpi(dpi);
        return (xPhysical / scale, yPhysical / scale, widthPhysical / scale, heightPhysical / scale);
    }

    /// <summary>
    /// Normalize a physical screen point into SendInput absolute units (0..65535) over the
    /// virtual desktop. Origins may be negative on multi-monitor layouts.
    /// </summary>
    public static (int AbsX, int AbsY) PhysicalToSendInputAbsolute(
        int physicalX,
        int physicalY,
        int virtualLeft,
        int virtualTop,
        int virtualWidth,
        int virtualHeight)
    {
        if (virtualWidth <= 1 || virtualHeight <= 1)
        {
            throw new ArgumentException("Virtual screen size must be greater than 1.");
        }

        var absX = (int)Math.Round((physicalX - virtualLeft) * 65535.0 / (virtualWidth - 1));
        var absY = (int)Math.Round((physicalY - virtualTop) * 65535.0 / (virtualHeight - 1));
        return (absX, absY);
    }
}
