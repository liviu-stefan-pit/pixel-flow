namespace PixelFlow.Core.Secrets;

/// <summary>
/// Resolves a secret by reference name at runtime (P30). Implementations must never
/// persist resolved values into project files or diagnostics.
/// </summary>
public interface ISecretResolver
{
    /// <summary>
    /// Looks up <paramref name="secretRef"/> (Credential Manager target name).
    /// Returns false with a safe <paramref name="error"/> when missing or unreadable.
    /// </summary>
    bool TryResolve(string secretRef, out string secret, out string? error);
}
