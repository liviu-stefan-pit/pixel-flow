namespace PixelFlow.Core.Projects;

/// <summary>
/// Canonical in-memory project model (source of truth for Studio and Runner).
/// </summary>
public sealed class ProjectDocument
{
    public int SchemaVersion { get; set; } = ProjectSchema.CurrentVersion;

    public string Name { get; set; } = "";

    public Dictionary<string, string> Variables { get; set; } =
        new(StringComparer.Ordinal);

    public ProjectDefaults Defaults { get; set; } = new();

    public List<ScriptStep> Steps { get; set; } = [];
}

public sealed class ProjectDefaults
{
    public int TimeoutMs { get; set; } = 5000;

    public RetryPolicy Retry { get; set; } = new();

    /// <summary>
    /// P22: when true, capture a screenshot on step failure unless the step overrides.
    /// Default off (opt-in; keep off for sensitive flows).
    /// </summary>
    public bool CaptureFailureScreenshots { get; set; }
}

public sealed class RetryPolicy
{
    public int MaxAttempts { get; set; } = 3;

    public int BackoffMs { get; set; } = 250;
}

public sealed class ScriptStep
{
    public string Id { get; set; } = "";

    /// <summary>Constrained command type: Click, Type, Wait, etc.</summary>
    public string Type { get; set; } = "";

    public int? TimeoutMs { get; set; }

    public RetryPolicy? Retry { get; set; }

    public LocatorChain? Locator { get; set; }

    /// <summary>Text payload for Type steps.</summary>
    public string? Text { get; set; }

    /// <summary>Duration for Wait steps, in milliseconds.</summary>
    public int? WaitMs { get; set; }

    /// <summary>
    /// P22: per-step override for failure screenshots. Null inherits
    /// <see cref="ProjectDefaults.CaptureFailureScreenshots"/> (default off).
    /// </summary>
    public bool? CaptureFailureScreenshot { get; set; }
}

public sealed class LocatorChain
{
    public ProcessWindowScope? Scope { get; set; }

    public List<LocatorLayer> Layers { get; set; } = [];
}

public sealed class ProcessWindowScope
{
    public string? ProcessName { get; set; }

    public string? WindowTitle { get; set; }
}

/// <summary>
/// One layer in an ordered locator chain. Layer-specific fields are placeholders
/// until later phases wire real resolvers.
/// </summary>
public sealed class LocatorLayer
{
    /// <summary>
    /// UiaStructural | UiaSemantic | Win32 | Ocr | Image
    /// </summary>
    public string Kind { get; set; } = "";

    public bool Enabled { get; set; } = true;

    public double ConfidenceThreshold { get; set; } = 0.85;

    public string? AutomationId { get; set; }

    public string? ControlType { get; set; }

    public string? Name { get; set; }

    public string? WindowClass { get; set; }

    public int? ControlId { get; set; }

    public string? Text { get; set; }

    /// <summary>Content-hash asset id (e.g. sha256-...), not a mutable file path.</summary>
    public string? ImageAssetHash { get; set; }
}
