using System.Text.Json;
using System.Text.Json.Serialization;

namespace PixelFlow.Core.Projects;

/// <summary>
/// Deterministic JSON serialize/deserialize for <see cref="ProjectDocument"/>.
/// </summary>
public static class ProjectJson
{
    public static readonly JsonSerializerOptions Options = CreateOptions();

    public static string Serialize(ProjectDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        NormalizeForDeterminism(document);
        return JsonSerializer.Serialize(document, Options);
    }

    public static ProjectDocument Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        var document = JsonSerializer.Deserialize<ProjectDocument>(json, Options)
            ?? throw new InvalidOperationException("Project JSON deserialized to null.");
        NormalizeForDeterminism(document);
        return document;
    }

    public static ProjectDocument RoundTrip(ProjectDocument document) =>
        Deserialize(Serialize(document));

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = false,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }

    /// <summary>
    /// Sort dictionary keys so serialize output does not depend on insertion order.
    /// </summary>
    internal static void NormalizeForDeterminism(ProjectDocument document)
    {
        if (document.Variables.Count > 1)
        {
            var ordered = document.Variables
                .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
                .ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal);
            document.Variables = ordered;
        }
        else if (document.Variables.Comparer != StringComparer.Ordinal)
        {
            document.Variables = new Dictionary<string, string>(document.Variables, StringComparer.Ordinal);
        }
    }
}
