namespace PixelFlow.Core.Runner;

/// <summary>
/// Canonical locator layer kind strings and architecture Section 6 resolve order.
/// </summary>
public static class LocatorKinds
{
    public const string UiaStructural = "UiaStructural";
    public const string UiaSemantic = "UiaSemantic";
    public const string Win32 = "Win32";
    public const string Ocr = "Ocr";
    public const string Image = "Image";

    /// <summary>Architecture resolve order (first match above threshold wins).</summary>
    public static readonly string[] ResolveOrder =
    [
        UiaStructural,
        UiaSemantic,
        Win32,
        Ocr,
        Image,
    ];

    public static int OrderIndex(string kind)
    {
        for (var i = 0; i < ResolveOrder.Length; i++)
        {
            if (string.Equals(ResolveOrder[i], kind, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return int.MaxValue;
    }
}
