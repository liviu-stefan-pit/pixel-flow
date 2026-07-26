namespace PixelFlow.Core.Ipc;

/// <summary>
/// Versioned Studio ↔ Runner IPC contract (architecture Section 4).
/// </summary>
public static class IpcProtocol
{
    /// <summary>Schema version carried on every envelope.</summary>
    public const int SchemaVersion = 1;

    public static class MessageNames
    {
        public const string Hello = "Hello";
        public const string HelloAck = "HelloAck";
        public const string Run = "Run";
        public const string Pause = "Pause";
        public const string Resume = "Resume";
        public const string Stop = "Stop";
        public const string Status = "Status";
        public const string Log = "Log";
        public const string Error = "Error";
    }
}
