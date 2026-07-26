using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace PixelFlow.Core.Ipc;

public static class IpcJson
{
    public static readonly JsonSerializerOptions Options = CreateOptions();

    public static string Serialize(IpcEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        return JsonSerializer.Serialize(envelope, Options);
    }

    public static IpcEnvelope Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        var envelope = JsonSerializer.Deserialize<IpcEnvelope>(json, Options)
            ?? throw new InvalidOperationException("IPC JSON deserialized to null.");
        return envelope;
    }

    public static string? GetString(IpcEnvelope envelope, string key)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (envelope.Payload is null || !envelope.Payload.TryGetPropertyValue(key, out var node) || node is null)
        {
            return null;
        }

        return node.GetValue<string>();
    }

    public static bool? GetBool(IpcEnvelope envelope, string key)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (envelope.Payload is null || !envelope.Payload.TryGetPropertyValue(key, out var node) || node is null)
        {
            return null;
        }

        return node.GetValue<bool>();
    }

    public static JsonObject Payload(params (string Key, JsonNode? Value)[] fields)
    {
        var obj = new JsonObject();
        foreach (var (key, value) in fields)
        {
            obj[key] = value;
        }

        return obj;
    }

    private static JsonSerializerOptions CreateOptions()
    {
        return new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false,
        };
    }
}
