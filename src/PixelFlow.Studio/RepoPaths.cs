using System.IO;

namespace PixelFlow.Studio;

/// <summary>
/// Locates the solution root, Runner binary, and fixture projects relative to Studio.
/// </summary>
internal static class RepoPaths
{
    public static string? FindSolutionRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "PixelFlow.slnx"))
                || File.Exists(Path.Combine(dir.FullName, "PixelFlow.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return null;
    }

    public static string ResolveDefaultProjectFolder()
    {
        var overrideFolder = Environment.GetEnvironmentVariable("PIXELFLOW_PROJECT_FOLDER");
        if (!string.IsNullOrWhiteSpace(overrideFolder))
        {
            return Path.GetFullPath(overrideFolder);
        }

        var root = FindSolutionRoot()
            ?? throw new InvalidOperationException("Could not locate PixelFlow solution root from Studio base directory.");
        return Path.Combine(root, "fixtures", "projects", "click-submit.pflow");
    }

    public static (string FileName, string Arguments) ResolveRunnerLaunch()
    {
        var overridePath = Environment.GetEnvironmentVariable("PIXELFLOW_RUNNER_PATH");
        if (!string.IsNullOrWhiteSpace(overridePath) && File.Exists(overridePath))
        {
            return (overridePath!, "");
        }

        var localExe = Path.Combine(AppContext.BaseDirectory, "PixelFlow.Runner.exe");
        if (File.Exists(localExe))
        {
            return (localExe, "");
        }

        var localDll = Path.Combine(AppContext.BaseDirectory, "PixelFlow.Runner.dll");
        if (File.Exists(localDll))
        {
            return (GetDotnetHost(), $"exec \"{localDll}\"");
        }

        var root = FindSolutionRoot()
            ?? throw new InvalidOperationException("Could not locate PixelFlow.Runner. Build the solution or set PIXELFLOW_RUNNER_PATH.");

        var tfms = new[] { "net10.0-windows10.0.19041.0", "net10.0-windows", "net10.0" };
        var configs = new[] { "Debug", "Release" };
        foreach (var config in configs)
        {
            foreach (var tfm in tfms)
            {
                var dll = Path.Combine(root, "src", "PixelFlow.Runner", "bin", config, tfm, "PixelFlow.Runner.dll");
                if (File.Exists(dll))
                {
                    return (GetDotnetHost(), $"exec \"{dll}\"");
                }

                var exe = Path.Combine(root, "src", "PixelFlow.Runner", "bin", config, tfm, "PixelFlow.Runner.exe");
                if (File.Exists(exe))
                {
                    return (exe, "");
                }
            }
        }

        var csproj = Path.Combine(root, "src", "PixelFlow.Runner", "PixelFlow.Runner.csproj");
        if (File.Exists(csproj))
        {
            return (GetDotnetHost(), $"run --project \"{csproj}\" --no-build --");
        }

        throw new FileNotFoundException(
            "PixelFlow.Runner was not found. Run `dotnet build PixelFlow.slnx` first, or set PIXELFLOW_RUNNER_PATH.");
    }

    private static string GetDotnetHost()
    {
        var dotnet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        if (!string.IsNullOrWhiteSpace(dotnet) && File.Exists(dotnet))
        {
            return dotnet!;
        }

        return "dotnet";
    }
}
