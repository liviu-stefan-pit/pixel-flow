using System.IO.Pipes;
using System.Text.Json.Nodes;
using PixelFlow.Core.Ipc;
using PixelFlow.Core.Projects;
using PixelFlow.Core.Runner;
using PixelFlow.Runner.Automation;

namespace PixelFlow.Runner;

/// <summary>
/// Named-pipe server host: accepts Studio connection and drives <see cref="RunnerEngine"/> with live UIA services.
/// </summary>
internal sealed class RunnerIpcHost : IAsyncDisposable
{
    private readonly string _pipeName;
    private readonly ProjectStore _store = new();
    private readonly CancellationTokenSource _shutdown = new();
    private readonly EmergencyStopHotkey _emergencyStop;
    private RunnerEngine? _engine;
    private Task? _runTask;
    private IpcPipeConnection? _connection;
    private NamedPipeServerStream? _pipe;

    public RunnerIpcHost(string pipeName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        _pipeName = pipeName;
        _emergencyStop = new EmergencyStopHotkey(OnEmergencyStop);
    }

    private void OnEmergencyStop()
    {
        var engine = _engine;
        if (engine is null)
        {
            Console.WriteLine("[runner] Emergency stop ignored: no active engine.");
            return;
        }

        engine.RequestAbort();
        _ = SendLogAsync("warning", $"Emergency stop ({EmergencyStopHotkey.ChordDisplay}): aborting run.");
    }

    public async Task RunAsync()
    {
        Console.WriteLine($"[runner] Listening on named pipe '{_pipeName}' (schema v{IpcProtocol.SchemaVersion})");
        _pipe = new NamedPipeServerStream(
            _pipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);

        await _pipe.WaitForConnectionAsync(_shutdown.Token).ConfigureAwait(false);
        Console.WriteLine("[runner] Studio connected");

        _connection = new IpcPipeConnection(_pipe);

        // Handshake: Studio writes Hello first; Runner replies. Avoids duplex write/write races.
        var hello = await _connection.ReadAsync(_shutdown.Token).ConfigureAwait(false);
        if (hello is null)
        {
            Console.WriteLine("[runner] Disconnected before Hello");
            return;
        }

        if (hello.Name != IpcProtocol.MessageNames.Hello
            || hello.SchemaVersion != IpcProtocol.SchemaVersion)
        {
            await SendErrorAsync(
                    $"Expected Hello schema v{IpcProtocol.SchemaVersion}, got '{hello.Name}' v{hello.SchemaVersion}.")
                .ConfigureAwait(false);
            return;
        }

        await SendHelloAckAsync().ConfigureAwait(false);
        await SendStatusAsync(connected: true, RunnerState.Idle).ConfigureAwait(false);
        Console.WriteLine($"[runner] Handshake OK — waiting for Run/Pause/Resume/Stop (emergency stop: {EmergencyStopHotkey.ChordDisplay})");

        try
        {
            while (!_shutdown.IsCancellationRequested)
            {
                var message = await _connection.ReadAsync(_shutdown.Token).ConfigureAwait(false);
                if (message is null)
                {
                    Console.WriteLine("[runner] Pipe closed by Studio");
                    break;
                }

                await HandleAsync(message).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
            // normal shutdown
        }
        catch (IOException ex)
        {
            Console.WriteLine($"[runner] Pipe IO ended: {ex.Message}");
        }
    }

    private async Task HandleAsync(IpcEnvelope message)
    {
        if (message.SchemaVersion != IpcProtocol.SchemaVersion)
        {
            await SendErrorAsync($"Unsupported IPC schemaVersion {message.SchemaVersion}; expected {IpcProtocol.SchemaVersion}.")
                .ConfigureAwait(false);
            return;
        }

        switch (message.Name)
        {
            case IpcProtocol.MessageNames.Hello:
                await SendHelloAckAsync().ConfigureAwait(false);
                break;

            case IpcProtocol.MessageNames.Run:
                await HandleRunAsync(message).ConfigureAwait(false);
                break;

            case IpcProtocol.MessageNames.Pause:
                if (_engine is null || _runTask is null || _runTask.IsCompleted)
                {
                    await SendLogAsync("warning", "Pause ignored: no active run.").ConfigureAwait(false);
                    break;
                }

                _engine.RequestPause();
                Console.WriteLine("[runner] Pause requested — will pause AFTER the current step (not mid-Wait)");
                await SendLogAsync(
                        "info",
                        "Pause noted. Current step finishes first, then Runner holds on Paused until Resume or Stop.")
                    .ConfigureAwait(false);
                break;

            case IpcProtocol.MessageNames.Resume:
                if (_engine is null)
                {
                    await SendLogAsync("warning", "Resume ignored: no engine.").ConfigureAwait(false);
                    break;
                }

                _engine.RequestResume();
                Console.WriteLine("[runner] Resume requested");
                await SendLogAsync("info", "Resume requested").ConfigureAwait(false);
                break;

            case IpcProtocol.MessageNames.Stop:
                if (_engine is null)
                {
                    await SendLogAsync("warning", "Stop ignored: no active run.").ConfigureAwait(false);
                    break;
                }

                _engine.RequestAbort();
                Console.WriteLine("[runner] Stop/abort requested");
                await SendLogAsync("info", "Stop requested").ConfigureAwait(false);
                break;

            default:
                await SendErrorAsync($"Unknown message: {message.Name}").ConfigureAwait(false);
                break;
        }
    }

    private async Task HandleRunAsync(IpcEnvelope message)
    {
        if (_runTask is { IsCompleted: false })
        {
            var state = _engine?.State.ToString() ?? "Running";
            await SendErrorAsync(
                    $"A run is already in progress (state: {state}). Use Resume if paused, or Stop first.")
                .ConfigureAwait(false);
            return;
        }

        var projectFolder = IpcJson.GetString(message, IpcPayloadKeys.ProjectFolder);
        if (string.IsNullOrWhiteSpace(projectFolder))
        {
            await SendErrorAsync("Run requires payload.projectFolder.").ConfigureAwait(false);
            return;
        }

        projectFolder = Path.GetFullPath(projectFolder);

        ProjectDocument project;
        try
        {
            project = _store.Load(projectFolder);
        }
        catch (Exception ex)
        {
            await SendErrorAsync($"Failed to load project: {ex.Message}").ConfigureAwait(false);
            return;
        }

        Console.WriteLine($"[runner] Run starting: {project.Name} ({project.Steps.Count} steps) from {projectFolder}");
        await SendLogAsync("info", $"Run starting: {project.Name}").ConfigureAwait(false);

        var live = new LiveStepServices(projectFolder);
        _engine = new RunnerEngine(live, live, live);
        _engine.RequestResume();
        _engine.StateChanged += OnEngineStateChanged;

        var engine = _engine;
        _runTask = Task.Run(async () =>
        {
            try
            {
                await engine.RunAsync(project, _shutdown.Token).ConfigureAwait(false);
                if (engine.State == RunnerState.Idle)
                {
                    Console.WriteLine("[runner] Run finished successfully (all steps done)");
                    await SendLogAsync(
                            "info",
                            "Run finished successfully. Click Run to play again.")
                        .ConfigureAwait(false);
                }
                else
                {
                    Console.WriteLine($"[runner] Run ended in state {engine.State}");
                    await SendLogAsync("info", $"Run ended: {engine.State}").ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[runner] Run failed: {ex.Message}");
                await SendErrorAsync($"Run failed: {ex.Message}").ConfigureAwait(false);
            }
            finally
            {
                engine.StateChanged -= OnEngineStateChanged;
                await SendStatusAsync(connected: true, engine.State).ConfigureAwait(false);
            }
        });
    }

    private void OnEngineStateChanged(RunnerState state)
    {
        Console.WriteLine($"[runner] State -> {state}");
        if (state == RunnerState.Paused)
        {
            Console.WriteLine("[runner] PAUSED between steps — Resume to continue, Stop to abort (still an active run)");
            _ = SendLogAsync(
                "info",
                "PAUSED between steps. Run is still active — click Resume to continue or Stop to abort.");
        }
        else if (state == RunnerState.FailedStep)
        {
            Console.WriteLine("[runner] FailedStep — retry/timeout budget exhausted or post-check failed");
            _ = SendLogAsync(
                "error",
                "FailedStep: resolve/verify budget exhausted or post-check failed (no recovery configured → abort).");
        }
        else if (state == RunnerState.Aborted)
        {
            _ = SendLogAsync("warning", "Run Aborted.");
        }
        else if (state == RunnerState.Retrying)
        {
            _ = SendLogAsync("info", "Retrying after miss/backoff…");
        }

        _ = SendStatusAsync(connected: true, state);
    }

    private Task SendHelloAckAsync() =>
        WriteAsync(IpcEnvelope.Create(
            IpcProtocol.MessageNames.HelloAck,
            IpcJson.Payload(
                (IpcPayloadKeys.Accepted, JsonValue.Create(true)),
                (IpcPayloadKeys.Message, JsonValue.Create($"PixelFlow Runner schema v{IpcProtocol.SchemaVersion}")))));

    private Task SendStatusAsync(bool connected, RunnerState state) =>
        WriteAsync(IpcEnvelope.Create(
            IpcProtocol.MessageNames.Status,
            IpcJson.Payload(
                (IpcPayloadKeys.Connected, JsonValue.Create(connected)),
                (IpcPayloadKeys.State, JsonValue.Create(state.ToString())))));

    private Task SendLogAsync(string level, string message) =>
        WriteAsync(IpcEnvelope.Create(
            IpcProtocol.MessageNames.Log,
            IpcJson.Payload(
                (IpcPayloadKeys.Level, JsonValue.Create(level)),
                (IpcPayloadKeys.Message, JsonValue.Create(message)))));

    private Task SendErrorAsync(string reason) =>
        WriteAsync(IpcEnvelope.Create(
            IpcProtocol.MessageNames.Error,
            IpcJson.Payload(
                (IpcPayloadKeys.Reason, JsonValue.Create(reason)),
                (IpcPayloadKeys.Message, JsonValue.Create(reason)))));

    private async Task WriteAsync(IpcEnvelope envelope)
    {
        var connection = _connection;
        if (connection is null)
        {
            return;
        }

        try
        {
            await connection.WriteAsync(envelope, _shutdown.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException)
        {
            // Peer gone.
        }
    }

    public async ValueTask DisposeAsync()
    {
        _shutdown.Cancel();
        _engine?.RequestAbort();

        if (_runTask is not null)
        {
            try
            {
                await _runTask.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            }
            catch
            {
                // best-effort
            }
        }

        if (_connection is not null)
        {
            await _connection.DisposeAsync().ConfigureAwait(false);
        }

        if (_pipe is not null)
        {
            await _pipe.DisposeAsync().ConfigureAwait(false);
        }

        _emergencyStop.Dispose();
        _shutdown.Dispose();
    }
}
