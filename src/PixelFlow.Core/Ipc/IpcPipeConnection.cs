using System.Buffers;
using System.Text;
using System.Text.Json;

namespace PixelFlow.Core.Ipc;

/// <summary>
/// Newline-delimited JSON framing over a duplex stream (named pipe).
/// Uses raw byte IO (no StreamReader/Writer) to avoid duplex pipe deadlocks.
/// </summary>
public sealed class IpcPipeConnection : IAsyncDisposable
{
    private readonly Stream _stream;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly byte[] _readBuffer = new byte[4096];
    private readonly ArrayBufferWriter<byte> _lineBuffer = new(1024);
    private bool _disposed;

    public IpcPipeConnection(Stream stream)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
    }

    public async Task WriteAsync(IpcEnvelope envelope, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(envelope);

        var line = IpcJson.Serialize(envelope) + "\n";
        var bytes = Encoding.UTF8.GetBytes(line);

        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
            await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>
    /// Reads the next envelope, or <c>null</c> on EOF / broken pipe.
    /// </summary>
    public async Task<IpcEnvelope?> ReadAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        while (true)
        {
            if (TryTakeLine(out var line))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                return IpcJson.Deserialize(line);
            }

            int read;
            try
            {
                read = await _stream.ReadAsync(_readBuffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            }
            catch (IOException)
            {
                return null;
            }
            catch (ObjectDisposedException)
            {
                return null;
            }

            if (read == 0)
            {
                return null;
            }

            _lineBuffer.Write(_readBuffer.AsSpan(0, read));
        }
    }

    private bool TryTakeLine(out string line)
    {
        var span = _lineBuffer.WrittenSpan;
        var newline = span.IndexOf((byte)'\n');
        if (newline < 0)
        {
            line = "";
            return false;
        }

        var lineSpan = span[..newline];
        if (lineSpan.Length > 0 && lineSpan[^1] == (byte)'\r')
        {
            lineSpan = lineSpan[..^1];
        }

        line = Encoding.UTF8.GetString(lineSpan);

        var remaining = span[(newline + 1)..];
        _lineBuffer.Clear();
        if (remaining.Length > 0)
        {
            _lineBuffer.Write(remaining);
        }

        return true;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            await _stream.DisposeAsync().ConfigureAwait(false);
        }
        catch
        {
            // ignore
        }

        _writeLock.Dispose();
    }
}
