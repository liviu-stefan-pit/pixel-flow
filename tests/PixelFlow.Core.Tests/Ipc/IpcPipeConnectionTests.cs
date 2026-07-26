using System.IO.Pipes;
using System.Text.Json.Nodes;
using PixelFlow.Core.Ipc;

namespace PixelFlow.Core.Tests.Ipc;

public sealed class IpcPipeConnectionTests
{
    [Fact]
    public async Task NamedPipe_HelloThenStatus_RoundTrips()
    {
        var pipeName = "PixelFlow.Test." + Guid.NewGuid().ToString("N");

        await using var serverPipe = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);

        var serverTask = Task.Run(async () =>
        {
            await serverPipe.WaitForConnectionAsync();
            await using var server = new IpcPipeConnection(serverPipe);
            var hello = await server.ReadAsync();
            Assert.NotNull(hello);
            Assert.Equal(IpcProtocol.MessageNames.Hello, hello!.Name);
            await server.WriteAsync(IpcEnvelope.Create(
                IpcProtocol.MessageNames.HelloAck,
                IpcJson.Payload((IpcPayloadKeys.Message, JsonValue.Create("ok")))));
            await server.WriteAsync(IpcEnvelope.Create(
                IpcProtocol.MessageNames.Status,
                IpcJson.Payload(
                    (IpcPayloadKeys.Connected, JsonValue.Create(true)),
                    (IpcPayloadKeys.State, JsonValue.Create("Idle")))));
        });

        await using var clientPipe = new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);
        await clientPipe.ConnectAsync(5000);

        await using var client = new IpcPipeConnection(clientPipe);
        await client.WriteAsync(IpcEnvelope.Create(
            IpcProtocol.MessageNames.Hello,
            IpcJson.Payload((IpcPayloadKeys.Message, JsonValue.Create("studio")))));

        var ack = await client.ReadAsync();
        Assert.NotNull(ack);
        Assert.Equal(IpcProtocol.MessageNames.HelloAck, ack!.Name);

        var status = await client.ReadAsync();
        Assert.NotNull(status);
        Assert.Equal(IpcProtocol.MessageNames.Status, status!.Name);
        Assert.Equal("Idle", IpcJson.GetString(status, IpcPayloadKeys.State));

        await serverTask.WaitAsync(TimeSpan.FromSeconds(5));
    }
}
