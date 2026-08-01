namespace PixelFlow.Core.Runner;

/// <summary>
/// Detects physical mouse/keyboard activity so the Runner can pause instead of fighting the user (P24).
/// </summary>
public interface IUserInterferenceDetector
{
    /// <summary>
    /// Start watching ahead of a synthetic action (e.g. at pre-execute verify).
    /// </summary>
    void BeginActionGate();

    /// <summary>
    /// True when recent physical input would conflict with sending synthetic input now.
    /// </summary>
    bool IsUserInterfering();

    /// <summary>
    /// Record that the Runner just generated synthetic input so it is not treated as user activity.
    /// </summary>
    void NoteSyntheticInput();
}

/// <summary>No-op detector (never pauses for interference).</summary>
public sealed class NullUserInterferenceDetector : IUserInterferenceDetector
{
    public static NullUserInterferenceDetector Instance { get; } = new();

    public void BeginActionGate()
    {
    }

    public bool IsUserInterfering() => false;

    public void NoteSyntheticInput()
    {
    }
}

/// <summary>Known reasons for entering <see cref="RunnerState.Paused"/>.</summary>
public static class PauseReasons
{
    /// <summary>Studio/IPC Pause request — honored between steps.</summary>
    public const string UserRequested = "userRequested";

    /// <summary>Physical mouse/keyboard activity near synthetic input time.</summary>
    public const string UserInterference = "userInterference";
}
