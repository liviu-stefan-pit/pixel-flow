using PixelFlow.Core.Runner;

namespace PixelFlow.Core.Coordinates;

/// <summary>
/// Holds the last resolved absolute (physical) bounds for a step, stamped with the
/// display generation at capture time. A display-change invalidation clears the cache
/// so stale coords cannot be reused for clicks.
/// </summary>
public sealed class AbsoluteCoordinateCache
{
    private readonly IDisplayChangeTracker _display;
    private readonly object _gate = new();
    private string? _stepId;
    private ScreenRect _bounds;
    private long _generation;

    public AbsoluteCoordinateCache(IDisplayChangeTracker display)
    {
        _display = display ?? throw new ArgumentNullException(nameof(display));
        _display.Changed += OnDisplayChanged;
    }

    /// <summary>Number of times the cache was cleared due to display change (tests/diagnostics).</summary>
    public int InvalidationCount { get; private set; }

    public void Store(string stepId, ScreenRect bounds, long displayGeneration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stepId);
        if (bounds.IsEmpty || displayGeneration <= 0)
        {
            return;
        }

        lock (_gate)
        {
            _stepId = stepId;
            _bounds = bounds;
            _generation = displayGeneration;
        }
    }

    /// <summary>
    /// Returns cached bounds only when the step matches and the display generation is still current.
    /// </summary>
    public bool TryGet(string stepId, out ScreenRect bounds)
    {
        bounds = default;
        if (string.IsNullOrWhiteSpace(stepId))
        {
            return false;
        }

        lock (_gate)
        {
            if (_stepId is null
                || !string.Equals(_stepId, stepId, StringComparison.Ordinal)
                || _bounds.IsEmpty
                || _display.IsStale(_generation))
            {
                return false;
            }

            bounds = _bounds;
            return true;
        }
    }

    public void Clear(string? reason = null)
    {
        lock (_gate)
        {
            if (_stepId is null && _bounds.IsEmpty)
            {
                return;
            }

            _stepId = null;
            _bounds = default;
            _generation = 0;
            InvalidationCount++;
        }

        _ = reason; // reserved for callers that log
    }

    private void OnDisplayChanged(DisplayChangeEvent evt)
    {
        Clear(evt.Reason);
    }
}
