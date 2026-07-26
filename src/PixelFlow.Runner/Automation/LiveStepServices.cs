using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows.Automation;
using PixelFlow.Core.Projects;
using PixelFlow.Core.Runner;

namespace PixelFlow.Runner.Automation;

/// <summary>
/// Live UIA resolve / re-check / Invoke / post-check for Click steps; Wait remains timing-only.
/// Never sends input unless a scoped element was re-resolved successfully.
/// </summary>
internal sealed class LiveStepServices : ITargetResolver, IStepVerifier, IStepExecutor
{
    private static readonly Regex ClickCountRegex = new(
        @"Clicks:\s*(?<n>\d+)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private readonly IRunnerDelay _delay;
    private readonly Dictionary<string, int> _counterBeforeByStep = new(StringComparer.Ordinal);

    public LiveStepServices(IRunnerDelay? delay = null)
    {
        _delay = delay ?? new SystemRunnerDelay();
    }

    public Task<ResolveResult> ResolveAsync(ScriptStep step, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (IsWait(step))
        {
            return Task.FromResult(new ResolveResult(Found: true, CandidateId: $"wait:{step.Id}"));
        }

        if (!TryGetStructuralLayer(step, out var layer, out var reason))
        {
            return Task.FromResult(ResolveResult.NotFound(reason));
        }

        var result = UiaStructuralLocator.Find(layer!, step.Locator!.Scope);
        if (!result.Found)
        {
            Console.WriteLine($"[runner] Resolve miss (step {step.Id}): {result.FailureReason}");
        }
        else
        {
            Console.WriteLine(
                $"[runner] Resolve hit (step {step.Id}): AutomationId={result.AutomationId}, " +
                $"Name={result.Name}, ControlType={result.ControlType}, Bounds={result.BoundingRect}");
        }

        return Task.FromResult(result);
    }

    public Task<bool> VerifyBeforeExecuteAsync(
        ScriptStep step,
        ResolveResult candidate,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (IsWait(step))
        {
            return Task.FromResult(true);
        }

        if (!candidate.Found)
        {
            return Task.FromResult(false);
        }

        if (!TryGetStructuralLayer(step, out var layer, out _))
        {
            return Task.FromResult(false);
        }

        // Re-resolve immediately before input (stale handle / closed window).
        if (!UiaStructuralLocator.TryFindElement(layer!, step.Locator!.Scope, out var element, out var failure))
        {
            Console.WriteLine($"[runner] Pre-check failed (step {step.Id}): {failure}");
            return Task.FromResult(false);
        }

        try
        {
            var live = UiaStructuralLocator.ToResult(element!, element!.Current.ProcessId);
            if (live.BoundingRect.IsEmpty)
            {
                Console.WriteLine($"[runner] Pre-check failed (step {step.Id}): empty bounding rect.");
                return Task.FromResult(false);
            }

            // Snapshot Test Bench counter for post-check when present in the same scope.
            if (TryReadClickCount(step.Locator.Scope, out var count))
            {
                _counterBeforeByStep[step.Id] = count;
                Console.WriteLine($"[runner] Pre-check counter snapshot (step {step.Id}): {count}");
            }
            else
            {
                _counterBeforeByStep.Remove(step.Id);
            }

            return Task.FromResult(true);
        }
        catch (ElementNotAvailableException)
        {
            Console.WriteLine($"[runner] Pre-check failed (step {step.Id}): element unavailable.");
            return Task.FromResult(false);
        }
    }

    public async Task ExecuteAsync(
        ScriptStep step,
        ResolveResult candidate,
        CancellationToken cancellationToken)
    {
        if (IsWait(step))
        {
            var ms = step.WaitMs ?? 0;
            if (ms > 0)
            {
                Console.WriteLine($"[runner] Wait {ms}ms (step {step.Id})");
                await _delay.DelayAsync(ms, cancellationToken).ConfigureAwait(false);
            }

            return;
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (!string.Equals(step.Type, "Click", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Live executor does not support step type '{step.Type}' yet (step {step.Id}).");
        }

        if (!TryGetStructuralLayer(step, out var layer, out var reason))
        {
            throw new InvalidOperationException(reason);
        }

        // Final re-resolve: never click by absolute screen guess if the element is gone.
        if (!UiaStructuralLocator.TryFindElement(layer!, step.Locator!.Scope, out var element, out var failure))
        {
            throw new InvalidOperationException(
                $"Refusing to click: target not found at execute time ({failure}).");
        }

        object? patternObj;
        try
        {
            if (!element!.TryGetCurrentPattern(InvokePattern.Pattern, out patternObj) || patternObj is null)
            {
                throw new InvalidOperationException(
                    $"Refusing to click: element '{layer!.AutomationId}' does not support InvokePattern.");
            }
        }
        catch (ElementNotAvailableException ex)
        {
            throw new InvalidOperationException(
                "Refusing to click: element became unavailable before Invoke.", ex);
        }

        Console.WriteLine(
            $"[runner] Invoke Click on AutomationId={layer!.AutomationId} (step {step.Id})");
        ((InvokePattern)patternObj).Invoke();
    }

    public Task<bool> VerifyAfterExecuteAsync(
        ScriptStep step,
        ResolveResult candidate,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (IsWait(step))
        {
            return Task.FromResult(true);
        }

        if (!string.Equals(step.Type, "Click", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(true);
        }

        if (!_counterBeforeByStep.TryGetValue(step.Id, out var before))
        {
            Console.WriteLine(
                $"[runner] Post-check failed (step {step.Id}): no TbCounter snapshot before click.");
            return Task.FromResult(false);
        }

        if (!TryReadClickCount(step.Locator?.Scope, out var after))
        {
            Console.WriteLine($"[runner] Post-check failed (step {step.Id}): TbCounter unreadable after click.");
            return Task.FromResult(false);
        }

        var ok = after == before + 1;
        Console.WriteLine(
            ok
                ? $"[runner] Post-check OK (step {step.Id}): counter {before} -> {after}"
                : $"[runner] Post-check FAILED (step {step.Id}): expected {before + 1}, got {after}");
        return Task.FromResult(ok);
    }

    private static bool IsWait(ScriptStep step) =>
        string.Equals(step.Type, "Wait", StringComparison.OrdinalIgnoreCase);

    private static bool TryGetStructuralLayer(ScriptStep step, out LocatorLayer? layer, out string reason)
    {
        layer = null;
        var locator = step.Locator;
        if (locator is null)
        {
            reason = $"Step '{step.Id}' has no locator.";
            return false;
        }

        layer = locator.Layers.FirstOrDefault(static l =>
            l.Enabled
            && string.Equals(l.Kind, "UiaStructural", StringComparison.OrdinalIgnoreCase));

        if (layer is null)
        {
            reason = $"Step '{step.Id}' has no enabled UiaStructural locator layer.";
            return false;
        }

        reason = "";
        return true;
    }

    private static bool TryReadClickCount(ProcessWindowScope? scope, out int count)
    {
        count = 0;
        var layer = new LocatorLayer
        {
            Kind = "UiaStructural",
            Enabled = true,
            AutomationId = "TbCounter",
            ControlType = "Text",
        };

        if (!UiaStructuralLocator.TryFindElement(layer, scope, out var element, out _))
        {
            // ControlType may not always be Text for WPF TextBlock; try AutomationId only.
            layer.ControlType = null;
            if (!UiaStructuralLocator.TryFindElement(layer, scope, out element, out _))
            {
                return false;
            }
        }

        try
        {
            var name = element!.Current.Name ?? "";
            var match = ClickCountRegex.Match(name);
            if (!match.Success)
            {
                return false;
            }

            return int.TryParse(
                match.Groups["n"].Value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out count);
        }
        catch (ElementNotAvailableException)
        {
            return false;
        }
    }
}
