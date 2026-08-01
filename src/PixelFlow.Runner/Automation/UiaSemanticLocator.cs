using PixelFlow.Core.Projects;
using PixelFlow.Core.Runner;

namespace PixelFlow.Runner.Automation;

/// <summary>
/// P15: UIA semantic match — Name / ControlType without requiring AutomationId.
/// </summary>
internal static class UiaSemanticLocator
{
    public static ResolveResult Find(LocatorLayer layer, ProcessWindowScope? scope)
    {
        if (!layer.Enabled)
        {
            return ResolveResult.NotFound("UiaSemantic layer is disabled.");
        }

        if (string.IsNullOrWhiteSpace(layer.Name) && string.IsNullOrWhiteSpace(layer.ControlType))
        {
            return ResolveResult.NotFound("UiaSemantic layer requires Name and/or ControlType.");
        }

        // Reuse structural finder but without AutomationId so sibling-scoped IDs cannot dominate.
        var semantic = new LocatorLayer
        {
            Kind = LocatorKinds.UiaSemantic,
            Enabled = true,
            ConfidenceThreshold = layer.ConfidenceThreshold,
            AutomationId = null,
            ControlType = layer.ControlType,
            Name = layer.Name,
        };

        var result = UiaStructuralLocator.Find(semantic, scope);
        if (!result.Found)
        {
            return result;
        }

        return result with
        {
            MatchedLayer = LocatorKinds.UiaSemantic,
            Confidence = 0.95,
        };
    }
}
