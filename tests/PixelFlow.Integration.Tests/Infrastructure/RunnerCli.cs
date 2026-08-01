using System.Diagnostics;
using System.Text;
using PixelFlow.Studio;

namespace PixelFlow.Integration.Tests.Infrastructure;

internal sealed record RunnerCliResult(int ExitCode, string StdOut, string StdErr, TimeSpan Elapsed);

/// <summary>
/// Launches PixelFlow.Runner headlessly, reusing the exact binary resolution Studio uses
/// (<see cref="RepoPaths.ResolveRunnerLaunch"/>).
/// </summary>
internal static class RunnerCli
{
    public static Task<RunnerCliResult> RunProjectAsync(
        string projectFolder,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var args = $"--run-project \"{projectFolder}\"";
        return RunAsync(args, timeout, cancellationToken);
    }

    public static Task<RunnerCliResult> ResolveAsync(
        string? automationId = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var id = string.IsNullOrWhiteSpace(automationId) ? "TbSubmit" : automationId;
        return RunAsync($"--resolve --automation-id {id}", timeout ?? TimeSpan.FromSeconds(30), cancellationToken);
    }

    public static async Task<RunnerCliResult> RunAsync(
        string runnerArgs,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var (fileName, argsPrefix) = RepoPaths.ResolveRunnerLaunch();
        var args = string.IsNullOrEmpty(argsPrefix)
            ? runnerArgs
            : $"{argsPrefix} {runnerArgs}";

        var start = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = args,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = RepoLocator.Root,
        };

        using var process = new Process { StartInfo = start };
        var stdOut = new StringBuilder();
        var stdErr = new StringBuilder();
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                stdOut.AppendLine(e.Data);
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                stdErr.AppendLine(e.Data);
            }
        };

        var sw = Stopwatch.StartNew();
        if (!process.Start())
        {
            throw new InvalidOperationException($"Failed to start Runner: {fileName} {args}");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(60);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(effectiveTimeout);

        try
        {
            await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // best-effort
            }

            throw new TimeoutException(
                $"Runner did not exit within {effectiveTimeout.TotalSeconds}s ({fileName} {args}). " +
                $"stdout so far:{Environment.NewLine}{stdOut}");
        }

        sw.Stop();
        return new RunnerCliResult(process.ExitCode, stdOut.ToString(), stdErr.ToString(), sw.Elapsed);
    }
}
