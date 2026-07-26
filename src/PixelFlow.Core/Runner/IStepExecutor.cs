using PixelFlow.Core.Projects;

namespace PixelFlow.Core.Runner;

public interface IStepExecutor
{
    Task ExecuteAsync(ScriptStep step, ResolveResult candidate, CancellationToken cancellationToken);
}