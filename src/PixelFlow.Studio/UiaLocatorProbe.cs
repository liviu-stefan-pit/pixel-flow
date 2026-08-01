using System.Diagnostics;
using System.Windows.Automation;
using PixelFlow.Core.Projects;
using PixelFlow.Core.Runner;

namespace PixelFlow.Studio;

/// <summary>
/// P17: author-time UIA structural resolve against the live desktop (Studio process).
/// Mirrors Runner UiaStructuralLocator enough to validate hand-authored fields.
/// </summary>
internal static class UiaLocatorProbe
{
    public static ResolveResult Find(LocatorLayer layer, ProcessWindowScope? scope)
    {
        ArgumentNullException.ThrowIfNull(layer);

        if (!layer.Enabled)
        {
            return ResolveResult.NotFound("UiaStructural layer is disabled.");
        }

        if (string.IsNullOrWhiteSpace(layer.AutomationId)
            && string.IsNullOrWhiteSpace(layer.Name)
            && string.IsNullOrWhiteSpace(layer.ControlType))
        {
            return ResolveResult.NotFound(
                "Locator requires at least one of AutomationId, ControlType, or Name.");
        }

        var processName = scope?.ProcessName;
        if (string.IsNullOrWhiteSpace(processName))
        {
            return ResolveResult.NotFound("Process scope is required (process name).");
        }

        Process[] processes;
        try
        {
            processes = Process.GetProcessesByName(NormalizeProcessName(processName));
        }
        catch (Exception ex)
        {
            return ResolveResult.NotFound($"Failed to enumerate process '{processName}': {ex.Message}");
        }

        if (processes.Length == 0)
        {
            return ResolveResult.NotFound($"No running process named '{processName}'.");
        }

        try
        {
            var condition = BuildCondition(layer);
            if (condition is null)
            {
                return ResolveResult.NotFound($"Unknown ControlType '{layer.ControlType}'.");
            }

            var matches = new List<AutomationElement>();
            var windowTitle = scope?.WindowTitle;
            var examinedWindows = 0;

            foreach (var process in processes)
            {
                foreach (var window in EnumerateCandidateWindows(process, windowTitle))
                {
                    examinedWindows++;

                    AutomationElement? found;
                    try
                    {
                        found = window.FindFirst(TreeScope.Descendants, condition);
                    }
                    catch (ElementNotAvailableException)
                    {
                        return ResolveResult.NotFound("UIA element tree became unavailable while searching.");
                    }

                    if (found is null)
                    {
                        continue;
                    }

                    if (matches.Any(existing => AreSameElement(existing, found)))
                    {
                        continue;
                    }

                    matches.Add(found);
                }
            }

            var actionable = matches.Where(IsActionable).ToList();
            var chosen = actionable.Count > 0 ? actionable : matches;

            if (chosen.Count > 1)
            {
                return ResolveResult.NotFound(
                    "Multiple UIA matches; refine AutomationId/Name/scope.");
            }

            if (chosen.Count == 1)
            {
                try
                {
                    return ToResult(chosen[0]);
                }
                catch (ElementNotAvailableException)
                {
                    return ResolveResult.NotFound(
                        "Matched UIA element became unavailable while reading properties.");
                }
            }

            if (examinedWindows == 0)
            {
                return ResolveResult.NotFound(
                    $"Process '{processName}' is running but has no top-level windows yet.");
            }

            if (!string.IsNullOrWhiteSpace(windowTitle))
            {
                return ResolveResult.NotFound(
                    $"No window titled '{windowTitle}' under process '{processName}', or no matching control inside.");
            }

            return ResolveResult.NotFound(BuildNotFoundMessage(layer, processName));
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }
    }

    private static ResolveResult ToResult(AutomationElement element)
    {
        var current = element.Current;
        var rect = current.BoundingRectangle;
        var automationId = current.AutomationId ?? "";
        var name = current.Name ?? "";
        var controlType = current.ControlType?.ProgrammaticName ?? "";
        var processId = current.ProcessId;
        var runtimeId = element.GetRuntimeId();
        var candidateId = runtimeId is { Length: > 0 }
            ? "runtime:" + string.Join('.', runtimeId)
            : $"uia:{processId}:{automationId}:{name}";

        return new ResolveResult(
            Found: true,
            CandidateId: candidateId,
            BoundingRect: new ScreenRect(rect.X, rect.Y, rect.Width, rect.Height),
            AutomationId: automationId,
            Name: name,
            ControlType: controlType,
            ProcessId: processId,
            MatchedLayer: LocatorKinds.UiaStructural,
            Confidence: 1.0);
    }

    private static IEnumerable<AutomationElement> EnumerateCandidateWindows(
        Process process,
        string? windowTitle)
    {
        var results = new List<AutomationElement>();
        IntPtr mainHandle = IntPtr.Zero;

        try
        {
            process.Refresh();
            mainHandle = process.MainWindowHandle;
            if (mainHandle != IntPtr.Zero)
            {
                var main = AutomationElement.FromHandle(mainHandle);
                if (main is not null && MatchesWindowTitle(main, windowTitle))
                {
                    results.Add(main);
                }
            }
        }
        catch (ElementNotAvailableException)
        {
            // fall through
        }

        foreach (var window in EnumerateTopLevelWindows(process.Id))
        {
            if (!MatchesWindowTitle(window, windowTitle))
            {
                continue;
            }

            try
            {
                if (mainHandle != IntPtr.Zero
                    && window.Current.NativeWindowHandle == mainHandle.ToInt32())
                {
                    continue;
                }
            }
            catch (ElementNotAvailableException)
            {
                continue;
            }

            results.Add(window);
        }

        return results;
    }

    private static bool MatchesWindowTitle(AutomationElement window, string? windowTitle)
    {
        if (string.IsNullOrWhiteSpace(windowTitle))
        {
            return true;
        }

        try
        {
            return string.Equals(window.Current.Name, windowTitle, StringComparison.Ordinal);
        }
        catch (ElementNotAvailableException)
        {
            return false;
        }
    }

    private static bool IsActionable(AutomationElement element)
    {
        try
        {
            var current = element.Current;
            if (current.IsOffscreen)
            {
                return false;
            }

            var rect = current.BoundingRectangle;
            if (rect.IsEmpty || rect.Width <= 0 || rect.Height <= 0)
            {
                return false;
            }

            if (Math.Abs(rect.X) > 20_000 || Math.Abs(rect.Y) > 20_000)
            {
                return false;
            }

            return true;
        }
        catch (ElementNotAvailableException)
        {
            return false;
        }
    }

    private static bool AreSameElement(AutomationElement a, AutomationElement b)
    {
        try
        {
            var idA = a.GetRuntimeId();
            var idB = b.GetRuntimeId();
            if (idA is null || idB is null || idA.Length != idB.Length)
            {
                return false;
            }

            return idA.SequenceEqual(idB);
        }
        catch (ElementNotAvailableException)
        {
            return false;
        }
    }

    private static Condition? BuildCondition(LocatorLayer layer)
    {
        var conditions = new List<Condition>();

        if (!string.IsNullOrWhiteSpace(layer.AutomationId))
        {
            conditions.Add(new PropertyCondition(
                AutomationElement.AutomationIdProperty,
                layer.AutomationId,
                PropertyConditionFlags.IgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(layer.ControlType))
        {
            if (!TryMapControlType(layer.ControlType, out var controlType))
            {
                return null;
            }

            conditions.Add(new PropertyCondition(AutomationElement.ControlTypeProperty, controlType));
        }

        if (!string.IsNullOrWhiteSpace(layer.Name))
        {
            conditions.Add(new PropertyCondition(
                AutomationElement.NameProperty,
                layer.Name,
                PropertyConditionFlags.IgnoreCase));
        }

        if (conditions.Count == 0)
        {
            return null;
        }

        return conditions.Count == 1 ? conditions[0] : new AndCondition(conditions.ToArray());
    }

    private static IEnumerable<AutomationElement> EnumerateTopLevelWindows(int processId)
    {
        var desktop = AutomationElement.RootElement;
        var condition = new PropertyCondition(AutomationElement.ProcessIdProperty, processId);

        AutomationElementCollection windows;
        try
        {
            windows = desktop.FindAll(TreeScope.Children, condition);
        }
        catch (ElementNotAvailableException)
        {
            yield break;
        }

        foreach (AutomationElement window in windows)
        {
            yield return window;
        }
    }

    private static string NormalizeProcessName(string processName)
    {
        var name = processName.Trim();
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^4];
        }

        return name;
    }

    private static string BuildNotFoundMessage(LocatorLayer layer, string processName)
    {
        var parts = new List<string> { $"process={processName}" };
        if (!string.IsNullOrWhiteSpace(layer.AutomationId))
        {
            parts.Add($"AutomationId={layer.AutomationId}");
        }

        if (!string.IsNullOrWhiteSpace(layer.ControlType))
        {
            parts.Add($"ControlType={layer.ControlType}");
        }

        if (!string.IsNullOrWhiteSpace(layer.Name))
        {
            parts.Add($"Name={layer.Name}");
        }

        return "No UIA element matched (" + string.Join(", ", parts) + ").";
    }

    private static bool TryMapControlType(string value, out ControlType controlType)
    {
        var key = value.Trim();
        if (key.StartsWith("ControlType.", StringComparison.OrdinalIgnoreCase))
        {
            key = key["ControlType.".Length..];
        }

        switch (key.ToLowerInvariant())
        {
            case "button":
                controlType = ControlType.Button;
                return true;
            case "text":
                controlType = ControlType.Text;
                return true;
            case "edit":
                controlType = ControlType.Edit;
                return true;
            case "window":
                controlType = ControlType.Window;
                return true;
            case "pane":
                controlType = ControlType.Pane;
                return true;
            case "checkbox":
                controlType = ControlType.CheckBox;
                return true;
            case "combobox":
                controlType = ControlType.ComboBox;
                return true;
            case "list":
                controlType = ControlType.List;
                return true;
            case "listitem":
                controlType = ControlType.ListItem;
                return true;
            case "menuitem":
                controlType = ControlType.MenuItem;
                return true;
            case "hyperlink":
                controlType = ControlType.Hyperlink;
                return true;
            case "image":
                controlType = ControlType.Image;
                return true;
            case "tabitem":
                controlType = ControlType.TabItem;
                return true;
            case "treeitem":
                controlType = ControlType.TreeItem;
                return true;
            case "document":
                controlType = ControlType.Document;
                return true;
            case "custom":
                controlType = ControlType.Custom;
                return true;
            default:
                controlType = ControlType.Custom;
                return false;
        }
    }
}
