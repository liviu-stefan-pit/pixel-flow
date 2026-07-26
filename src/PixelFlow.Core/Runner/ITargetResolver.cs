using PixelFlow.Core.Projects;

namespace PixelFlow.Core.Runner;

public interface ITargetResolver
{
    Task<ResolveResult> ResolveAsync(ScriptStep step, CancellationToken cancellationToken);
}