using System.Text.Json;
using System.Text.Json.Serialization;

namespace PixelFlow.Core.Diagnostics;

/// <summary>
/// Compact JSON for JSONL run report lines (one object per line, no indentation).
/// </summary>
public static class RunReportJson
{
    public static readonly JsonSerializerOptions Options = CreateOptions();

    public static string Serialize(RunReportEvent evt)
    {
        ArgumentNullException.ThrowIfNull(evt);
        return JsonSerializer.Serialize(evt, Options);
    }

    public static RunReportEvent Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        return JsonSerializer.Deserialize<RunReportEvent>(json, Options)
            ?? throw new InvalidOperationException("Run report event JSON deserialized to null.");
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}
