using OpenCvSharp;
using OpenCvSharp.Extensions;
using PixelFlow.Core.Projects;
using PixelFlow.Core.Runner;

namespace PixelFlow.Runner.Automation;

/// <summary>
/// P14: multi-scale OpenCV template match against a content-hashed project asset.
/// </summary>
internal static class ImageTemplateLocator
{
    private static readonly double[] Scales = [1.0, 0.9, 0.8, 1.1, 1.25, 0.75, 1.5];

    public static ResolveResult Find(LocatorLayer layer, ProcessWindowScope? scope, string? projectFolder)
    {
        if (!layer.Enabled)
        {
            return ResolveResult.NotFound("Image layer is disabled.");
        }

        if (string.IsNullOrWhiteSpace(layer.ImageAssetHash))
        {
            return ResolveResult.NotFound("Image layer requires imageAssetHash.");
        }

        if (string.IsNullOrWhiteSpace(projectFolder))
        {
            return ResolveResult.NotFound("Project folder is required to resolve image assets.");
        }

        var processName = scope?.ProcessName;
        if (string.IsNullOrWhiteSpace(processName))
        {
            return ResolveResult.NotFound("Process scope is required (locator.scope.processName).");
        }

        var assetPath = ProjectPaths.AssetPath(projectFolder, layer.ImageAssetHash);
        if (!File.Exists(assetPath))
        {
            // Also accept hash without extension already baked into AssetPath (.png).
            return ResolveResult.NotFound($"Image asset not found: {assetPath}");
        }

        if (!ProcessWindowBounds.TryGet(processName, scope?.WindowTitle, out var originX, out var originY, out var width, out var height, out var pid, out var boundFailure))
        {
            return ResolveResult.NotFound(boundFailure);
        }

        var threshold = layer.ConfidenceThreshold <= 0 ? 0.85 : layer.ConfidenceThreshold;

        using var screenBmp = ScreenCapture.CaptureRegion(originX, originY, width, height);
        using var haystack = BitmapConverter.ToMat(screenBmp);
        using var templateFull = Cv2.ImRead(assetPath, ImreadModes.Color);
        if (templateFull.Empty())
        {
            return ResolveResult.NotFound($"Failed to load template image: {assetPath}");
        }

        double bestScore = 0;
        OpenCvSharp.Point bestLoc = default;
        Size bestSize = default;

        foreach (var scale in Scales)
        {
            var tw = Math.Max(8, (int)Math.Round(templateFull.Width * scale));
            var th = Math.Max(8, (int)Math.Round(templateFull.Height * scale));
            if (tw >= haystack.Width || th >= haystack.Height)
            {
                continue;
            }

            using var scaled = new Mat();
            Cv2.Resize(templateFull, scaled, new Size(tw, th), 0, 0, InterpolationFlags.Area);
            using var result = new Mat();
            Cv2.MatchTemplate(haystack, scaled, result, TemplateMatchModes.CCoeffNormed);
            Cv2.MinMaxLoc(result, out _, out var maxVal, out _, out var maxLoc);

            if (maxVal > bestScore)
            {
                bestScore = maxVal;
                bestLoc = maxLoc;
                bestSize = new Size(tw, th);
            }
        }

        if (bestScore < threshold || bestSize.Width <= 0)
        {
            return ResolveResult.NotFound(
                $"Image template below threshold {threshold:0.##} (best={bestScore:0.##}, asset={layer.ImageAssetHash}).");
        }

        var bounds = new ScreenRect(
            originX + bestLoc.X,
            originY + bestLoc.Y,
            bestSize.Width,
            bestSize.Height);

        return new ResolveResult(
            Found: true,
            CandidateId: $"image:{pid}:{layer.ImageAssetHash}:{bestScore:0.###}",
            BoundingRect: bounds,
            Name: layer.ImageAssetHash,
            ControlType: "Image.Template",
            ProcessId: pid,
            MatchedLayer: LocatorKinds.Image,
            Confidence: bestScore);
    }
}
