using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PixelFlow.Core.Projects;

namespace PixelFlow.Studio.Tests;

/// <summary>
/// Pure-helper regression coverage for <see cref="ImageTokenLoader"/> — the path/hash resolution
/// Studio's inline image tokens (P20) and step details depend on.
/// </summary>
public sealed class ImageTokenLoaderTests : IDisposable
{
    private readonly string _projectFolder;

    public ImageTokenLoaderTests()
    {
        _projectFolder = Path.Combine(Path.GetTempPath(), "PixelFlow.StudioTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_projectFolder);
    }

    [Fact]
    public void TryResolvePath_RoundTrips_AfterSavingAsset()
    {
        var store = new ProjectStore();
        var pngBytes = CreateTinyPngBytes();
        var hash = store.SavePngAsset(_projectFolder, pngBytes);

        var resolved = ImageTokenLoader.TryResolvePath(_projectFolder, hash);

        Assert.NotNull(resolved);
        Assert.True(File.Exists(resolved));
        Assert.Equal(pngBytes, File.ReadAllBytes(resolved!));
    }

    [Fact]
    public void TryResolvePath_ReturnsNull_WhenAssetFileMissing()
    {
        var resolved = ImageTokenLoader.TryResolvePath(_projectFolder, "sha256-doesnotexist");

        Assert.Null(resolved);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryResolvePath_ReturnsNull_ForBlankHash(string? hash)
    {
        Assert.Null(ImageTokenLoader.TryResolvePath(_projectFolder, hash));
    }

    [Fact]
    public void TryResolvePath_ReturnsNull_ForBlankProjectFolder()
    {
        Assert.Null(ImageTokenLoader.TryResolvePath("", "sha256-abc"));
    }

    [Fact]
    public void TryLoadThumbnail_DecodesSavedAsset()
    {
        var store = new ProjectStore();
        var pngBytes = CreateTinyPngBytes();
        var hash = store.SavePngAsset(_projectFolder, pngBytes);

        var thumbnail = ImageTokenLoader.TryLoadThumbnail(_projectFolder, hash, decodePixelWidth: 16);

        Assert.NotNull(thumbnail);
        Assert.True(thumbnail!.PixelWidth > 0);
    }

    [Fact]
    public void TryLoadThumbnail_ReturnsNull_WhenAssetMissing()
    {
        var thumbnail = ImageTokenLoader.TryLoadThumbnail(_projectFolder, "sha256-doesnotexist");

        Assert.Null(thumbnail);
    }

    [Fact]
    public void GetImageAssetHash_ReturnsFirstImageLayerHash()
    {
        var step = new ScriptStep
        {
            Id = "click-image",
            Type = "Click",
            Locator = new LocatorChain
            {
                Layers =
                [
                    new LocatorLayer { Kind = "UiaStructural", Enabled = true },
                    new LocatorLayer { Kind = "Image", Enabled = true, ImageAssetHash = "sha256-abc" },
                    new LocatorLayer { Kind = "Image", Enabled = true, ImageAssetHash = "sha256-should-not-win" },
                ],
            },
        };

        Assert.Equal("sha256-abc", ImageTokenLoader.GetImageAssetHash(step));
    }

    [Fact]
    public void GetImageAssetHash_ReturnsNull_WhenNoImageLayer()
    {
        var step = new ScriptStep
        {
            Id = "click",
            Type = "Click",
            Locator = new LocatorChain
            {
                Layers = [new LocatorLayer { Kind = "UiaStructural", Enabled = true }],
            },
        };

        Assert.Null(ImageTokenLoader.GetImageAssetHash(step));
    }

    [Fact]
    public void GetImageAssetHash_ReturnsNull_WhenNoLocator()
    {
        var step = new ScriptStep { Id = "wait-1", Type = "Wait" };

        Assert.Null(ImageTokenLoader.GetImageAssetHash(step));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_projectFolder, recursive: true);
        }
        catch
        {
            // best-effort cleanup
        }
    }

    /// <summary>2x2 magenta/cyan PNG — mirrors the shape of a real Snip (P19) asset.</summary>
    private static byte[] CreateTinyPngBytes()
    {
        var pixels = new byte[]
        {
            0xFF, 0x00, 0xFF, 0xFF,
            0x00, 0xFF, 0xFF, 0xFF,
            0xFF, 0xFF, 0x00, 0xFF,
            0x00, 0x00, 0xFF, 0xFF,
        };
        var bitmap = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null, pixels, 8);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }
}
