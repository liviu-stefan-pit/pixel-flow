using System.IO;
using System.Windows.Media.Imaging;
using PixelFlow.Core.Projects;

namespace PixelFlow.Studio;

/// <summary>
/// Loads snipped PNG assets as WPF thumbnails bound to <see cref="LocatorLayer.ImageAssetHash"/>.
/// </summary>
public static class ImageTokenLoader
{
    /// <summary>
    /// Resolves the on-disk PNG path for a content-hash asset when the file exists.
    /// </summary>
    public static string? TryResolvePath(string? projectFolder, string? imageAssetHash)
    {
        if (string.IsNullOrWhiteSpace(projectFolder) || string.IsNullOrWhiteSpace(imageAssetHash))
        {
            return null;
        }

        var path = ProjectPaths.AssetPath(projectFolder, imageAssetHash.Trim());
        return File.Exists(path) ? path : null;
    }

    /// <summary>
    /// Loads a frozen thumbnail bitmap from the project asset file, or null if missing.
    /// </summary>
    public static BitmapImage? TryLoadThumbnail(string? projectFolder, string? imageAssetHash, int decodePixelWidth = 128)
    {
        var path = TryResolvePath(projectFolder, imageAssetHash);
        if (path is null)
        {
            return null;
        }

        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
        bmp.UriSource = new Uri(path, UriKind.Absolute);
        if (decodePixelWidth > 0)
        {
            bmp.DecodePixelWidth = decodePixelWidth;
        }

        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    /// <summary>
    /// Returns the first Image-layer asset hash on a click step, if any.
    /// </summary>
    public static string? GetImageAssetHash(ScriptStep step)
    {
        return step.Locator?.Layers
            .FirstOrDefault(l =>
                string.Equals(l.Kind, "Image", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(l.ImageAssetHash))
            ?.ImageAssetHash;
    }
}
