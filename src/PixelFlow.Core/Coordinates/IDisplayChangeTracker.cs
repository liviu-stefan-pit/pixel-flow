namespace PixelFlow.Core.Coordinates;

/// <summary>
/// Tracks display-configuration generations so absolute coordinates captured under an
/// older layout are never reused after a monitor/DPI/resolution change.
/// </summary>
public interface IDisplayChangeTracker
{
    /// <summary>Monotonic generation; increments on each invalidation.</summary>
    long Generation { get; }

    /// <summary>Last known topology (updated on <see cref="Invalidate"/>).</summary>
    DisplayTopology Snapshot { get; }

    /// <summary>True when <paramref name="capturedGeneration"/> is older than the current generation.</summary>
    bool IsStale(long capturedGeneration);

    /// <summary>
    /// Bust any absolute-coordinate cache consumers: bump generation and store the new topology.
    /// </summary>
    void Invalidate(DisplayTopology newTopology, string? reason = null);

    /// <summary>Raised after generation increments (including the reason string when known).</summary>
    event Action<DisplayChangeEvent>? Changed;
}

/// <summary>Payload for <see cref="IDisplayChangeTracker.Changed"/>.</summary>
public sealed record DisplayChangeEvent(
    long Generation,
    DisplayTopology Topology,
    string? Reason);

/// <summary>
/// In-memory tracker (unit tests and Runner). Thread-safe.
/// </summary>
public sealed class DisplayChangeTracker : IDisplayChangeTracker
{
    private readonly object _gate = new();
    private long _generation = 1;
    private DisplayTopology _topology;

    public DisplayChangeTracker(DisplayTopology initialTopology = default)
    {
        _topology = initialTopology;
    }

    public long Generation
    {
        get { lock (_gate) { return _generation; } }
    }

    public DisplayTopology Snapshot
    {
        get { lock (_gate) { return _topology; } }
    }

    public bool IsStale(long capturedGeneration)
    {
        if (capturedGeneration <= 0)
        {
            // Untagged results are treated as stale so callers must stamp a generation.
            return true;
        }

        lock (_gate)
        {
            return capturedGeneration != _generation;
        }
    }

    public void Invalidate(DisplayTopology newTopology, string? reason = null)
    {
        DisplayChangeEvent evt;
        lock (_gate)
        {
            _topology = newTopology;
            _generation++;
            evt = new DisplayChangeEvent(_generation, _topology, reason);
        }

        Changed?.Invoke(evt);
    }

    public event Action<DisplayChangeEvent>? Changed;
}
