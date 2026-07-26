using PixelFlow.Core.Projects;
using PixelFlow.Core.Runner;
using PixelFlow.Runner.Automation;

namespace PixelFlow.Runner;

internal static class Program
{
    [STAThread]
    private static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || args.Any(IsHelp))
        {
            PrintUsage();
            return 0;
        }

        try
        {
            if (HasFlag(args, "--resolve"))
            {
                return RunResolve(args);
            }

            if (HasFlag(args, "--run-project"))
            {
                return await RunProjectAsync(args).ConfigureAwait(false);
            }

            string? pipeName = null;
            for (var i = 0; i < args.Length; i++)
            {
                if (args[i] is "--pipe" && i + 1 < args.Length)
                {
                    pipeName = args[++i];
                    continue;
                }

                Console.Error.WriteLine($"Unknown arguments: {string.Join(' ', args)}");
                PrintUsage();
                return 1;
            }

            if (string.IsNullOrWhiteSpace(pipeName))
            {
                Console.Error.WriteLine("Missing --pipe <name>.");
                PrintUsage();
                return 1;
            }

            await using var host = new RunnerIpcHost(pipeName);
            await host.RunAsync().ConfigureAwait(false);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 1;
        }
    }

    private static int RunResolve(string[] args)
    {
        var automationId = GetOption(args, "--automation-id") ?? "TbSubmit";
        var processName = GetOption(args, "--process") ?? "PixelFlow.TestBench";
        var controlType = GetOption(args, "--control-type") ?? "Button";
        var name = GetOption(args, "--name") ?? "Submit";
        var windowTitle = GetOption(args, "--window-title") ?? "Test Bench";

        Console.WriteLine(
            $"[resolve] Looking for AutomationId={automationId}, ControlType={controlType}, " +
            $"Name={name}, process={processName}, windowTitle={windowTitle}");

        var result = UiaStructuralLocator.Find(
            automationId,
            controlType,
            name,
            processName,
            windowTitle);

        if (!result.Found)
        {
            Console.WriteLine($"[resolve] NOT FOUND: {result.FailureReason}");
            return 2;
        }

        Console.WriteLine("[resolve] FOUND");
        Console.WriteLine($"  CandidateId : {result.CandidateId}");
        Console.WriteLine($"  AutomationId: {result.AutomationId}");
        Console.WriteLine($"  Name        : {result.Name}");
        Console.WriteLine($"  ControlType : {result.ControlType}");
        Console.WriteLine($"  ProcessId   : {result.ProcessId}");
        Console.WriteLine($"  Bounds      : {result.BoundingRect}");
        return 0;
    }

    private static async Task<int> RunProjectAsync(string[] args)
    {
        var projectFolder = GetOption(args, "--run-project");
        if (string.IsNullOrWhiteSpace(projectFolder))
        {
            // Allow: --run-project <path> as next arg (GetOption already handles that).
            Console.Error.WriteLine("Missing path for --run-project <folder>.");
            return 1;
        }

        var store = new ProjectStore();
        var project = store.Load(projectFolder);
        Console.WriteLine($"[runner] Loaded project '{project.Name}' ({project.Steps.Count} steps) from {projectFolder}");

        var services = new LiveStepServices();
        var engine = new RunnerEngine(services, services, services);
        await engine.RunAsync(project).ConfigureAwait(false);

        Console.WriteLine($"[runner] Finished in state {engine.State}");
        return engine.State == RunnerState.Idle ? 0 : 3;
    }

    private static bool HasFlag(string[] args, string flag) =>
        args.Any(a => string.Equals(a, flag, StringComparison.OrdinalIgnoreCase));

    private static string? GetOption(string[] args, string name)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (!string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (i + 1 < args.Length && !args[i + 1].StartsWith('-'))
            {
                return args[i + 1];
            }

            return null;
        }

        return null;
    }

    private static bool IsHelp(string arg) =>
        arg is "-h" or "--help" or "/?" or "-?";

    private static void PrintUsage()
    {
        var usage = """
            PixelFlow Runner
            Attended automation worker process (separate from Studio).

            Usage:
              PixelFlow.Runner --help
              PixelFlow.Runner --pipe <name>
              PixelFlow.Runner --resolve [--automation-id TbSubmit] [--process PixelFlow.TestBench]
                                         [--control-type Button] [--name Submit] [--window-title "Test Bench"]
              PixelFlow.Runner --run-project <path-to-.pflow-folder>

            Studio starts this process with --pipe for run/pause/resume/stop.
            Use --resolve (P07) to print UIA structural match info for the Test Bench button.
            Use --run-project (P08) to execute a fixture with live UIA click + post-check.
            """;
        Console.WriteLine(usage.ReplaceLineEndings(Environment.NewLine));
    }
}
