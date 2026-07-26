using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text.Json.Nodes;
using PixelFlow.Core.Ipc;

namespace PixelFlow.Studio;

/// <summary>
/// Starts the Runner as a separate OS process and talks over a versioned named pipe.
/// </summary>
internal sealed class RunnerSession : IAsyncDisposable
{
    private CancellationTokenSource _cts = new();
    private Process? _process;
    private NamedPipeClientStream? _pipe;
    private IpcPipeConnection? _connection;
    private Task? _readLoop;
    private bool _disposed;
    private bool _pipeBroken;

    public bool IsConnected =>
        !_pipeBroken && _connection is not null && _process is { HasExited: false };

    public int? RunnerProcessId => _process is { HasExited: false } ? _process.Id : null;

    public string? LastRunnerState { get; private set; }

    public bool IsRunInProgress { get; private set; }

    public event Action<string>? StatusTextChanged;
    public event Action<string>? LogReceived;
    public event Action? Disconnected;
    public event Action? ConnectionStateChanged;

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsConnected)
        {
            return;
        }

        await DisconnectCoreAsync(killProcess: true).ConfigureAwait(false);
        _pipeBroken = false;

        var pipeName = $"PixelFlow.{Guid.NewGuid():N}";
        var (fileName, argsPrefix) = RepoPaths.ResolveRunnerLaunch();
        var args = string.IsNullOrEmpty(argsPrefix)
            ? $"--pipe {pipeName}"
            : $"{argsPrefix} --pipe {pipeName}";

        var start = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = args,
            UseShellExecute = false,
            // Keep hidden: stdout/stderr are redirected into Studio's log.
            // A visible console is easy to close by mistake, which kills the pipe.
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        _process = new Process { StartInfo = start, EnableRaisingEvents = true };
        _process.OutputDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
            {
                LogReceived?.Invoke(e.Data);
            }
        };
        _process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
            {
                LogReceived?.Invoke("[stderr] " + e.Data);
            }
        };
        _process.Exited += (_, _) => OnPipeOrProcessLost("Runner process exited");

        if (!_process.Start())
        {
            throw new InvalidOperationException("Failed to start PixelFlow.Runner process.");
        }

        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();
        StatusTextChanged?.Invoke($"Starting Runner (PID {_process.Id})…");

        _pipe = new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cts.Token);
        linked.CancelAfter(TimeSpan.FromSeconds(15));

        try
        {
            await _pipe.ConnectAsync(linked.Token).ConfigureAwait(false);
        }
        catch (Exception)
        {
            await DisconnectCoreAsync(killProcess: true).ConfigureAwait(false);
            throw;
        }

        _connection = new IpcPipeConnection(_pipe);

        // Studio writes Hello first; wait for HelloAck before declaring connected.
        await _connection.WriteAsync(
            IpcEnvelope.Create(
                IpcProtocol.MessageNames.Hello,
                IpcJson.Payload((IpcPayloadKeys.Message, JsonValue.Create("PixelFlow Studio")))),
            _cts.Token).ConfigureAwait(false);

        var ack = await _connection.ReadAsync(_cts.Token).ConfigureAwait(false);
        if (ack is null)
        {
            await DisconnectCoreAsync(killProcess: true).ConfigureAwait(false);
            throw new IOException("Runner closed the pipe during handshake.");
        }

        if (ack.Name != IpcProtocol.MessageNames.HelloAck)
        {
            await DisconnectCoreAsync(killProcess: true).ConfigureAwait(false);
            throw new InvalidOperationException($"Expected HelloAck, got '{ack.Name}'.");
        }

        LogReceived?.Invoke(IpcJson.GetString(ack, IpcPayloadKeys.Message) ?? "HelloAck");

        _readLoop = Task.Run(() => ReadLoopAsync(_cts.Token), CancellationToken.None);
        StatusTextChanged?.Invoke($"Connected (PID {_process.Id}) — Runner state: Idle");
        ConnectionStateChanged?.Invoke();
    }

    public async Task RunProjectAsync(string projectFolder, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(projectFolder) || projectFolder.StartsWith("(unavailable", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "No fixture project folder resolved. Build from the repo root so Studio can find fixtures/projects/ipc-wait.pflow.");
        }

        if (!Directory.Exists(projectFolder))
        {
            throw new DirectoryNotFoundException($"Project folder not found: {projectFolder}");
        }

        await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
        IsRunInProgress = true;
        ConnectionStateChanged?.Invoke();

        await SendAsync(
            IpcEnvelope.Create(
                IpcProtocol.MessageNames.Run,
                IpcJson.Payload((IpcPayloadKeys.ProjectFolder, JsonValue.Create(projectFolder)))),
            cancellationToken).ConfigureAwait(false);
    }

    public Task PauseAsync(CancellationToken cancellationToken = default) =>
        SendAsync(IpcEnvelope.Create(IpcProtocol.MessageNames.Pause), cancellationToken);

    public Task ResumeAsync(CancellationToken cancellationToken = default) =>
        SendAsync(IpcEnvelope.Create(IpcProtocol.MessageNames.Resume), cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken = default) =>
        SendAsync(IpcEnvelope.Create(IpcProtocol.MessageNames.Stop), cancellationToken);

    private async Task EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        if (!IsConnected)
        {
            await ConnectAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task SendAsync(IpcEnvelope envelope, CancellationToken cancellationToken)
    {
        await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
        if (_connection is null || _pipeBroken)
        {
            throw new InvalidOperationException("Not connected to Runner. Click Run first.");
        }

        try
        {
            await _connection.WriteAsync(envelope, cancellationToken).ConfigureAwait(false);
        }
        catch (IOException ex)
        {
            OnPipeOrProcessLost(ex.Message);
            throw new InvalidOperationException(
                "Lost connection to Runner (pipe broken). Click Run to start a new Runner process.", ex);
        }
    }

    private async Task ReadLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && _connection is not null && !_pipeBroken)
            {
                var message = await _connection.ReadAsync(cancellationToken).ConfigureAwait(false);
                if (message is null)
                {
                    OnPipeOrProcessLost("pipe closed");
                    break;
                }

                HandleMessage(message);
            }
        }
        catch (OperationCanceledException)
        {
            // shutting down
        }
        catch (Exception ex)
        {
            OnPipeOrProcessLost(ex.GetType().Name);
        }
    }

    private void HandleMessage(IpcEnvelope message)
    {
        if (message.SchemaVersion != IpcProtocol.SchemaVersion)
        {
            LogReceived?.Invoke($"IPC schema mismatch: got {message.SchemaVersion}, expected {IpcProtocol.SchemaVersion}");
            return;
        }

        switch (message.Name)
        {
            case IpcProtocol.MessageNames.HelloAck:
                LogReceived?.Invoke(IpcJson.GetString(message, IpcPayloadKeys.Message) ?? "HelloAck");
                break;

            case IpcProtocol.MessageNames.Status:
            {
                var state = IpcJson.GetString(message, IpcPayloadKeys.State) ?? "?";
                var connected = IpcJson.GetBool(message, IpcPayloadKeys.Connected);
                LastRunnerState = state;

                // Idle fires between steps too — do NOT clear IsRunInProgress on Idle.
                // Cleared only when Runner reports the run ended (see Log handler) or Aborted/FailedStep.
                if (state is "Aborted" or "FailedStep")
                {
                    IsRunInProgress = false;
                }
                else if (state is "Paused" or "Resolving" or "Verifying" or "Executing" or "PostCheck" or "Retrying")
                {
                    IsRunInProgress = true;
                }

                var pid = RunnerProcessId is int id ? $"PID {id}" : "no PID";
                var conn = connected == false ? "Disconnected" : "Connected";
                var hint = state switch
                {
                    "Paused" => " (waiting for Resume/Stop)",
                    "Idle" when IsRunInProgress => " (between steps)",
                    "Idle" => " (ready)",
                    "Aborted" => " (stopped)",
                    _ => "",
                };
                StatusTextChanged?.Invoke($"{conn} ({pid}) — Runner state: {state}{hint}");
                ConnectionStateChanged?.Invoke();
                break;
            }

            case IpcProtocol.MessageNames.Log:
            {
                var logMessage = IpcJson.GetString(message, IpcPayloadKeys.Message) ?? "(log)";
                LogReceived?.Invoke(logMessage);
                if (logMessage.StartsWith("Run finished", StringComparison.OrdinalIgnoreCase)
                    || logMessage.StartsWith("Run ended", StringComparison.OrdinalIgnoreCase))
                {
                    IsRunInProgress = false;
                    ConnectionStateChanged?.Invoke();
                }

                break;
            }

            case IpcProtocol.MessageNames.Error:
                LogReceived?.Invoke("ERROR: " + (IpcJson.GetString(message, IpcPayloadKeys.Reason)
                    ?? IpcJson.GetString(message, IpcPayloadKeys.Message)
                    ?? "unknown"));
                break;
        }
    }

    private void OnPipeOrProcessLost(string reason)
    {
        if (_pipeBroken)
        {
            return;
        }

        _pipeBroken = true;
        IsRunInProgress = false;
        StatusTextChanged?.Invoke($"Disconnected ({reason})");
        Disconnected?.Invoke();
        ConnectionStateChanged?.Invoke();
    }

    private async Task DisconnectCoreAsync(bool killProcess)
    {
        var previousCts = _cts;
        _cts = new CancellationTokenSource();
        try
        {
            previousCts.Cancel();
        }
        catch
        {
            // ignore
        }

        if (_readLoop is not null)
        {
            try
            {
                await _readLoop.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
            }
            catch
            {
                // ignore
            }

            _readLoop = null;
        }

        if (_connection is not null)
        {
            await _connection.DisposeAsync().ConfigureAwait(false);
            _connection = null;
        }

        _pipe = null;

        if (killProcess && _process is not null)
        {
            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                    _process.WaitForExit(2000);
                }
            }
            catch
            {
                // ignore
            }

            _process.Dispose();
            _process = null;
        }

        previousCts.Dispose();
        IsRunInProgress = false;
        _pipeBroken = false;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await DisconnectCoreAsync(killProcess: true).ConfigureAwait(false);
        _cts.Dispose();
    }
}
