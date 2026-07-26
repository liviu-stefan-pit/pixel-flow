using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace PixelFlow.Core.Ipc;

/// <summary>
/// Versioned IPC message. Payload is a JSON object whose shape depends on <see cref="Name"/>.
/// </summary>
public sealed class IpcEnvelope
{
    public int SchemaVersion { get; set; } = IpcProtocol.SchemaVersion;

    /// <summary>Logical message name (see <see cref="IpcProtocol.MessageNames"/>).</summary>
    public string Name { get; set; } = "";

    /// <summary>Optional correlation id for request/response pairing.</summary>
    public string? CorrelationId { get; set; }

    /// <summary>Message-specific JSON object (always a JSON object when present).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonObject? Payload { get; set; }

    public static IpcEnvelope Create(string name, JsonObject? payload = null, string? correlationId = null) =>
        new()
        {
            SchemaVersion = IpcProtocol.SchemaVersion,
            Name = name,
            CorrelationId = correlationId,
            Payload = payload,
        };
}

public static class IpcPayloadKeys
{
    public const string ProjectFolder = "projectFolder";
    public const string State = "state";
    public const string Connected = "connected";
    public const string Message = "message";
    public const string Level = "level";
    public const string Accepted = "accepted";
    public const string Reason = "reason";
}
