using PixelFlow.Core.Runner;

namespace PixelFlow.Core.Tests.Runner;

public class LocatorKindsTests
{
    [Fact]
    public void ResolveOrder_IsArchitectureSection6()
    {
        Assert.Equal(
            new[]
            {
                LocatorKinds.UiaStructural,
                LocatorKinds.UiaSemantic,
                LocatorKinds.Win32,
                LocatorKinds.Ocr,
                LocatorKinds.Image,
            },
            LocatorKinds.ResolveOrder);
    }

    [Fact]
    public void OrderIndex_RanksKnownKinds()
    {
        Assert.True(LocatorKinds.OrderIndex("UiaStructural") < LocatorKinds.OrderIndex("Win32"));
        Assert.True(LocatorKinds.OrderIndex("Win32") < LocatorKinds.OrderIndex("Ocr"));
        Assert.True(LocatorKinds.OrderIndex("Ocr") < LocatorKinds.OrderIndex("Image"));
        Assert.Equal(int.MaxValue, LocatorKinds.OrderIndex("Unknown"));
    }
}
