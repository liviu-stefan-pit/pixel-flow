using PixelFlow.Core.Projects;

namespace PixelFlow.Core.Runner;

public interface IStepVerifier
{
    /// <summary>Re-check immediately before input (Verifying).</summary>
    Task<bool> VerifyBeforeExecuteAsync(ScriptStep step, ResolveResult candidate, CancellationToken cancellationToken);

    /// <summary>Post-action assertion (PostCheck).</summary>
    Task<bool> VerifyAfterExecuteAsync(ScriptStep step, ResolveResult candidate, CancellationToken cancellationToken);
}