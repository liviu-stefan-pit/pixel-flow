using PixelFlow.Core.Coordinates;
using PixelFlow.Core.Runner;

namespace PixelFlow.Core.Tests.Coordinates;

public sealed class DisplayChangeInvalidationTests
{
    [Fact]
    public void DisplayTopology_Equality_ComparesAllFields()
    {
        var a = new DisplayTopology(0, 0, 1920, 1080, 1);
        var b = new DisplayTopology(0, 0, 1920, 1080, 1);
        var c = new DisplayTopology(-1920, 0, 3840, 1080, 2);

        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
        Assert.False(a.IsEmpty);
        Assert.True(new DisplayTopology(0, 0, 0, 0, 0).IsEmpty);
    }

    [Fact]
    public void DisplayChangeTracker_Invalidate_BumpsGenerationAndIsStale()
    {
        var tracker = new DisplayChangeTracker(new DisplayTopology(0, 0, 1920, 1080, 1));
        Assert.Equal(1, tracker.Generation);
        Assert.False(tracker.IsStale(1));
        Assert.True(tracker.IsStale(0));

        DisplayChangeEvent? seen = null;
        tracker.Changed += e => seen = e;

        var next = new DisplayTopology(-1920, 0, 3840, 1080, 2);
        tracker.Invalidate(next, "test-unplug");

        Assert.Equal(2, tracker.Generation);
        Assert.Equal(next, tracker.Snapshot);
        Assert.True(tracker.IsStale(1));
        Assert.False(tracker.IsStale(2));
        Assert.NotNull(seen);
        Assert.Equal(2, seen!.Generation);
        Assert.Equal("test-unplug", seen.Reason);
    }

    [Fact]
    public void AbsoluteCoordinateCache_TryGet_FailsAfterDisplayInvalidation()
    {
        var tracker = new DisplayChangeTracker(new DisplayTopology(0, 0, 1920, 1080, 1));
        var cache = new AbsoluteCoordinateCache(tracker);
        var bounds = new ScreenRect(100, 200, 40, 20);

        cache.Store("click-submit", bounds, tracker.Generation);
        Assert.True(cache.TryGet("click-submit", out var hit));
        Assert.Equal(bounds, hit);
        Assert.Equal(0, cache.InvalidationCount);

        tracker.Invalidate(new DisplayTopology(0, 0, 2560, 1440, 1), "resolution-change");

        Assert.False(cache.TryGet("click-submit", out _));
        Assert.Equal(1, cache.InvalidationCount);
    }

    [Fact]
    public void AbsoluteCoordinateCache_TryGet_FailsForWrongStepOrEmpty()
    {
        var tracker = new DisplayChangeTracker(new DisplayTopology(0, 0, 1920, 1080, 1));
        var cache = new AbsoluteCoordinateCache(tracker);

        cache.Store("a", new ScreenRect(1, 2, 3, 4), tracker.Generation);
        Assert.False(cache.TryGet("b", out _));

        cache.Clear();
        Assert.False(cache.TryGet("a", out _));
        Assert.Equal(1, cache.InvalidationCount);
    }

    [Fact]
    public void AbsoluteCoordinateCache_DoesNotStoreEmptyOrUntaggedBounds()
    {
        var tracker = new DisplayChangeTracker(new DisplayTopology(0, 0, 1920, 1080, 1));
        var cache = new AbsoluteCoordinateCache(tracker);

        cache.Store("a", default, tracker.Generation);
        Assert.False(cache.TryGet("a", out _));

        cache.Store("a", new ScreenRect(1, 2, 3, 4), displayGeneration: 0);
        Assert.False(cache.TryGet("a", out _));
    }

    [Fact]
    public void ResolveResult_WithDisplayGeneration_PreservesStamp()
    {
        var result = new ResolveResult(
            Found: true,
            BoundingRect: new ScreenRect(10, 20, 30, 40),
            DisplayGeneration: 7);

        Assert.Equal(7, result.DisplayGeneration);
        var staleTagged = result with { DisplayGeneration = 1 };
        Assert.Equal(1, staleTagged.DisplayGeneration);
    }
}
