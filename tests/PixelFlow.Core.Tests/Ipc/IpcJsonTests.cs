using System.Text.Json.Nodes;
using PixelFlow.Core.Ipc;

namespace PixelFlow.Core.Tests.Ipc;

public sealed class IpcJsonTests
{
    [Fact]
    public void Envelope_RoundTrips_WithSchemaVersion()
    {
        var original = IpcEnvelope.Create(
            IpcProtocol.MessageNames.Run,
            IpcJson.Payload((IpcPayloadKeys.ProjectFolder, JsonValue.Create(@"C:\proj\demo.pflow"))),
            correlationId: "c1");

        var json = IpcJson.Serialize(original);
        var again = IpcJson.Deserialize(json);

        Assert.Equal(IpcProtocol.SchemaVersion, again.SchemaVersion);
        Assert.Equal(IpcProtocol.MessageNames.Run, again.Name);
        Assert.Equal("c1", again.CorrelationId);
        Assert.Equal(@"C:\proj\demo.pflow", IpcJson.GetString(again, IpcPayloadKeys.ProjectFolder));
    }

    [Fact]
    public void StatusPayload_BoolAndState_RoundTrip()
    {
        var original = IpcEnvelope.Create(
            IpcProtocol.MessageNames.Status,
            IpcJson.Payload(
                (IpcPayloadKeys.Connected, JsonValue.Create(true)),
                (IpcPayloadKeys.State, JsonValue.Create("Paused"))));

        var again = IpcJson.Deserialize(IpcJson.Serialize(original));
        Assert.True(IpcJson.GetBool(again, IpcPayloadKeys.Connected));
        Assert.Equal("Paused", IpcJson.GetString(again, IpcPayloadKeys.State));
    }
}
