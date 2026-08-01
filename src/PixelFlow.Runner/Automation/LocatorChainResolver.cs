using PixelFlow.Core.Projects;
using PixelFlow.Core.Runner;

namespace PixelFlow.Runner.Automation;

/// <summary>
/// P15: ordered locator chain — first enabled layer above its confidence threshold wins.
/// Order: UiaStructural → UiaSemantic → Win32 → Ocr → Image.
/// </summary>
internal static class LocatorChainResolver
{
    public static async Task<ResolveResult> ResolveAsync(
        ScriptStep step,
        string? projectFolder,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var locator = step.Locator;
        if (locator is null)
        {
            return ResolveResult.NotFound($"Step '{step.Id}' has no locator.");
        }

        if (locator.Layers.Count == 0)
        {
            return ResolveResult.NotFound($"Step '{step.Id}' has an empty locator chain.");
        }

        // Walk layers in architecture order, but only those present+enabled in the step.
        var ordered = locator.Layers
            .Where(static l => l.Enabled)
            .OrderBy(static l => LocatorKinds.OrderIndex(l.Kind))
            .ThenBy(l => locator.Layers.IndexOf(l))
            .ToList();

        if (ordered.Count == 0)
        {
            return ResolveResult.NotFound($"Step '{step.Id}' has no enabled locator layers.");
        }

        var failures = new List<string>();

        foreach (var layer in ordered)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var kind = layer.Kind?.Trim() ?? "";
            ResolveResult result;

            if (string.Equals(kind, LocatorKinds.UiaStructural, StringComparison.OrdinalIgnoreCase))
            {
                result = UiaStructuralLocator.Find(layer, locator.Scope);
                if (result.Found)
                {
                    result = result with
                    {
                        MatchedLayer = LocatorKinds.UiaStructural,
                        Confidence = result.Confidence > 0 ? result.Confidence : 1.0,
                    };
                }
            }
            else if (string.Equals(kind, LocatorKinds.UiaSemantic, StringComparison.OrdinalIgnoreCase))
            {
                result = UiaSemanticLocator.Find(layer, locator.Scope);
            }
            else if (string.Equals(kind, LocatorKinds.Win32, StringComparison.OrdinalIgnoreCase))
            {
                result = Win32Locator.Find(layer, locator.Scope);
            }
            else if (string.Equals(kind, LocatorKinds.Ocr, StringComparison.OrdinalIgnoreCase))
            {
                result = await OcrLocator.FindAsync(layer, locator.Scope).ConfigureAwait(false);
            }
            else if (string.Equals(kind, LocatorKinds.Image, StringComparison.OrdinalIgnoreCase))
            {
                result = ImageTemplateLocator.Find(layer, locator.Scope, projectFolder);
            }
            else
            {
                failures.Add($"{kind}: unknown layer kind");
                continue;
            }

            if (!result.Found)
            {
                failures.Add($"{kind}: {result.FailureReason}");
                continue;
            }

            var threshold = layer.ConfidenceThreshold <= 0 ? 0.85 : layer.ConfidenceThreshold;
            if (result.Confidence < threshold)
            {
                failures.Add(
                    $"{kind}: confidence {result.Confidence:0.###} below threshold {threshold:0.###}");
                continue;
            }

            return result;
        }

        return ResolveResult.NotFound(
            "All locator layers failed: " + string.Join(" | ", failures));
    }
}
