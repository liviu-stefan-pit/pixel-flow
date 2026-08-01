using PixelFlow.Core.Coordinates;

namespace PixelFlow.Core.Tests.Coordinates;

public sealed class DpiCoordinatesTests
{
    [Theory]
    [InlineData(96, 1.0, 100.0)]
    [InlineData(120, 1.25, 125.0)]
    [InlineData(144, 1.5, 150.0)]
    [InlineData(192, 2.0, 200.0)]
    public void ScaleAndPercent_MatchCommonWindowsDpiLevels(double dpi, double expectedScale, double expectedPercent)
    {
        Assert.Equal(expectedScale, DpiCoordinates.ScaleFromDpi(dpi), 5);
        Assert.Equal(expectedPercent, DpiCoordinates.PercentFromDpi(dpi), 5);
    }

    [Theory]
    [InlineData(96)]
    [InlineData(120)]
    [InlineData(144)]
    [InlineData(192)]
    public void DipPhysical_RoundTrip_PreservesValues(double dpi)
    {
        const double x = 100.0;
        const double y = 50.5;
        const double w = 80.0;
        const double h = 24.0;

        var (px, py, pw, ph) = DpiCoordinates.DipRectToPhysical(x, y, w, h, dpi);
        var (rx, ry, rw, rh) = DpiCoordinates.PhysicalRectToDip(px, py, pw, ph, dpi);

        Assert.Equal(x, rx, 5);
        Assert.Equal(y, ry, 5);
        Assert.Equal(w, rw, 5);
        Assert.Equal(h, rh, 5);
    }

    [Fact]
    public void DipToPhysical_At150Percent_ScalesBy1_5()
    {
        // 150% = 144 DPI → button at (10,20) DIP → (15,30) physical.
        Assert.Equal(15.0, DpiCoordinates.DipToPhysical(10, 144), 5);
        Assert.Equal(30.0, DpiCoordinates.DipToPhysical(20, 144), 5);
        Assert.Equal(10.0, DpiCoordinates.PhysicalToDip(15, 144), 5);
    }

    [Fact]
    public void PhysicalToSendInputAbsolute_PrimaryOrigin_MapsCorners()
    {
        var (absX0, absY0) = DpiCoordinates.PhysicalToSendInputAbsolute(0, 0, 0, 0, 1920, 1080);
        Assert.Equal(0, absX0);
        Assert.Equal(0, absY0);

        var (absX1, absY1) = DpiCoordinates.PhysicalToSendInputAbsolute(1919, 1079, 0, 0, 1920, 1080);
        Assert.Equal(65535, absX1);
        Assert.Equal(65535, absY1);
    }

    [Fact]
    public void PhysicalToSendInputAbsolute_NegativeVirtualOrigin_OffsetsCorrectly()
    {
        // Secondary monitor to the left: virtual left = -1920.
        var (absX, absY) = DpiCoordinates.PhysicalToSendInputAbsolute(
            physicalX: -960,
            physicalY: 540,
            virtualLeft: -1920,
            virtualTop: 0,
            virtualWidth: 3840,
            virtualHeight: 1080);

        // (-960 - (-1920)) / 3839 * 65535 ≈ mid-left half
        Assert.InRange(absX, 16300, 16500);
        Assert.InRange(absY, 32700, 32850);
    }

    [Fact]
    public void ScaleFromDpi_RejectsNonPositive()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DpiCoordinates.ScaleFromDpi(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => DpiCoordinates.ScaleFromDpi(-96));
    }
}
