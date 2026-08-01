using System.Diagnostics;
using System.IO;

namespace PixelFlow.Integration.Tests.Infrastructure;

/// <summary>
/// Ensures PixelFlow.TestBench is running for the duration of the Live collection. Reuses an
/// already-running instance if one is found; otherwise launches and owns its lifetime.
///
/// If Test Bench cannot be launched (no interactive desktop session, missing build, etc.),
/// <see cref="IsAvailable"/> is false and tests should skip via
/// <c>Skip.IfNot(fixture.IsAvailable, fixture.UnavailableReason)</c> rather than fail hard.
/// </summary>
public sealed class TestBenchFixture : IDisposable
{
    private readonly Process? _owned;

    public bool IsAvailable { get; }

    public string? UnavailableReason { get; }

    public TestBenchFixture()
    {
        try
        {
            if (TryFindRunning() is not null)
            {
                IsAvailable = true;
                return;
            }

            _owned = Launch();
            IsAvailable = WaitForWindow(_owned, TimeSpan.FromSeconds(10));
            if (!IsAvailable)
            {
                UnavailableReason = "PixelFlow.TestBench launched but no window appeared within 10s.";
            }
        }
        catch (Exception ex)
        {
            IsAvailable = false;
            UnavailableReason = $"Could not start PixelFlow.TestBench: {ex.Message}";
        }
    }

    private static Process? TryFindRunning()
    {
        foreach (var candidate in Process.GetProcessesByName("PixelFlow.TestBench"))
        {
            try
            {
                if (!candidate.HasExited && candidate.MainWindowHandle != IntPtr.Zero)
                {
                    return candidate;
                }
            }
            catch
            {
                // process may have exited between enumeration and inspection
            }
        }

        return null;
    }

    private static Process Launch()
    {
        var (fileName, arguments) = ResolveTestBenchLaunch();
        var start = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = false,
            WorkingDirectory = RepoLocator.Root,
        };

        return Process.Start(start)
            ?? throw new InvalidOperationException("Process.Start returned null for PixelFlow.TestBench.");
    }

    private static bool WaitForWindow(Process process, TimeSpan timeout)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            process.Refresh();
            if (process.HasExited)
            {
                return false;
            }

            if (process.MainWindowHandle != IntPtr.Zero)
            {
                return true;
            }

            Thread.Sleep(100);
        }

        return false;
    }

    private static (string FileName, string Arguments) ResolveTestBenchLaunch()
    {
        var overridePath = Environment.GetEnvironmentVariable("PIXELFLOW_TESTBENCH_PATH");
        if (!string.IsNullOrWhiteSpace(overridePath) && File.Exists(overridePath))
        {
            return (overridePath, "");
        }

        var root = RepoLocator.Root;
        var tfms = new[] { "net10.0-windows", "net10.0" };
        var configs = new[] { "Debug", "Release" };
        foreach (var config in configs)
        {
            foreach (var tfm in tfms)
            {
                var exe = Path.Combine(root, "src", "PixelFlow.TestBench", "bin", config, tfm, "PixelFlow.TestBench.exe");
                if (File.Exists(exe))
                {
                    return (exe, "");
                }
            }
        }

        var csproj = Path.Combine(root, "src", "PixelFlow.TestBench", "PixelFlow.TestBench.csproj");
        if (File.Exists(csproj))
        {
            return ("dotnet", $"run --project \"{csproj}\" --no-build");
        }

        throw new FileNotFoundException(
            "PixelFlow.TestBench was not found. Run `dotnet build PixelFlow.slnx` first, or set PIXELFLOW_TESTBENCH_PATH.");
    }

    public void Dispose()
    {
        if (_owned is null)
        {
            return;
        }

        try
        {
            if (!_owned.HasExited)
            {
                _owned.CloseMainWindow();
                if (!_owned.WaitForExit(2000))
                {
                    _owned.Kill(entireProcessTree: true);
                }
            }
        }
        catch
        {
            // best-effort
        }
        finally
        {
            _owned.Dispose();
        }
    }
}
