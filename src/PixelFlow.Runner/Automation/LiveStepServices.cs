using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows.Automation;
using PixelFlow.Core.Projects;
using PixelFlow.Core.Runner;

namespace PixelFlow.Runner.Automation;

/// <summary>
/// Live resolve / re-check / click / post-check for Click steps; Wait remains timing-only.
/// Resolves via ordered locator chain (UIA → Win32 → OCR → Image). Never clicks without a fresh resolve.
/// </summary>
internal sealed class LiveStepServices : ITargetResolver, IStepVerifier, IStepExecutor
{
    private static readonly Regex ClickCountRegex = new(
        @"Clicks:\s*(?<n>\d+)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private readonly IRunnerDelay _delay;
    private readonly string? _projectFolder;
    private readonly Dictionary<string, int> _counterBeforeByStep = new(StringComparer.Ordinal);

    public LiveStepServices(string? projectFolder = null, IRunnerDelay? delay = null)
    {
        _projectFolder = projectFolder;
        _delay = delay ?? new SystemRunnerDelay();
    }

    public async Task<ResolveResult> ResolveAsync(ScriptStep step, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (IsWait(step))
        {
            return new ResolveResult(Found: true, CandidateId: $"wait:{step.Id}");
        }

        var result = await LocatorChainResolver.ResolveAsync(step, _projectFolder, cancellationToken)
            .ConfigureAwait(false);

        if (!result.Found)
        {
            Console.WriteLine($"[runner] Resolve miss (step {step.Id}): {result.FailureReason}");
        }
        else
        {
            Console.WriteLine(
                $"[runner] Resolve hit (step {step.Id}): layer={result.MatchedLayer}, " +
                $"confidence={result.Confidence:0.###}, AutomationId={result.AutomationId}, " +
                $"Name={result.Name}, ControlType={result.ControlType}, Bounds={result.BoundingRect}");
        }

        return result;
    }

    public async Task<bool> VerifyBeforeExecuteAsync(
        ScriptStep step,
        ResolveResult candidate,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (IsWait(step))
        {
            return true;
        }

        if (!candidate.Found)
        {
            return false;
        }

        // Re-resolve immediately before input (stale handle / closed window / moved UI).
        var live = await LocatorChainResolver.ResolveAsync(step, _projectFolder, cancellationToken)
            .ConfigureAwait(false);
        if (!live.Found || live.BoundingRect.IsEmpty)
        {
            Console.WriteLine(
                $"[runner] Pre-check failed (step {step.Id}): {live.FailureReason ?? "empty bounds"}");
            return false;
        }

        if (TryReadClickCount(step.Locator?.Scope, out var count))
        {
            _counterBeforeByStep[step.Id] = count;
            Console.WriteLine($"[runner] Pre-check counter snapshot (step {step.Id}): {count}");
        }
        else
        {
            _counterBeforeByStep.Remove(step.Id);
        }

        return true;
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

        // Final re-resolve: never click by absolute screen guess if the target is gone.
        var live = await LocatorChainResolver.ResolveAsync(step, _projectFolder, cancellationToken)
            .ConfigureAwait(false);
        if (!live.Found || live.BoundingRect.IsEmpty)
        {
            throw new InvalidOperationException(
                $"Refusing to click: target not found at execute time ({live.FailureReason}).");
        }

        var layer = live.MatchedLayer ?? "";
        if (string.Equals(layer, LocatorKinds.UiaStructural, StringComparison.OrdinalIgnoreCase)
            || string.Equals(layer, LocatorKinds.UiaSemantic, StringComparison.OrdinalIgnoreCase))
        {
            TryInvokeUia(step, live);
            return;
        }

        if (string.Equals(layer, LocatorKinds.Win32, StringComparison.OrdinalIgnoreCase)
            && live.NativeHandle != 0)
        {
            Console.WriteLine(
                $"[runner] Win32 BM_CLICK hwnd=0x{live.NativeHandle:X} (step {step.Id}, layer={layer})");
            SendInputClick.ClickHwnd(live.NativeHandle);
            return;
        }

        Console.WriteLine(
            $"[runner] SendInput click at {live.BoundingRect} (step {step.Id}, layer={layer}, confidence={live.Confidence:0.###})");
        SendInputClick.ClickCenter(live.BoundingRect);
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

        // Brief settle for SendInput / BM_CLICK to update the counter label.
        Thread.Sleep(50);

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

    private static void TryInvokeUia(ScriptStep step, ResolveResult live)
    {
        // Prefer InvokePattern when the winning layer was UIA; fall back to SendInput on bounds.
        var layer = step.Locator?.Layers.FirstOrDefault(l =>
            l.Enabled
            && (string.Equals(l.Kind, LocatorKinds.UiaStructural, StringComparison.OrdinalIgnoreCase)
                || string.Equals(l.Kind, LocatorKinds.UiaSemantic, StringComparison.OrdinalIgnoreCase)));

        if (layer is not null
            && UiaStructuralLocator.TryFindElement(
                string.Equals(live.MatchedLayer, LocatorKinds.UiaSemantic, StringComparison.OrdinalIgnoreCase)
                    ? new LocatorLayer
                    {
                        Kind = LocatorKinds.UiaSemantic,
                        Enabled = true,
                        Name = layer.Name,
                        ControlType = layer.ControlType,
                    }
                    : layer,
                step.Locator!.Scope,
                out var element,
                out var failure)
            && element is not null)
        {
            try
            {
                if (element.TryGetCurrentPattern(InvokePattern.Pattern, out var patternObj)
                    && patternObj is InvokePattern invoke)
                {
                    Console.WriteLine(
                        $"[runner] Invoke Click (step {step.Id}, layer={live.MatchedLayer}, AutomationId={live.AutomationId})");
                    invoke.Invoke();
                    return;
                }
            }
            catch (ElementNotAvailableException ex)
            {
                throw new InvalidOperationException(
                    "Refusing to click: element became unavailable before Invoke.", ex);
            }

            Console.WriteLine(
                $"[runner] No InvokePattern ({failure}); SendInput fallback (step {step.Id})");
        }

        SendInputClick.ClickCenter(live.BoundingRect);
    }

    private static bool IsWait(ScriptStep step) =>
        string.Equals(step.Type, "Wait", StringComparison.OrdinalIgnoreCase);

    private static bool TryReadClickCount(ProcessWindowScope? scope, out int count)
    {
        count = 0;
        var layer = new LocatorLayer
        {
            Kind = LocatorKinds.UiaStructural,
            Enabled = true,
            AutomationId = "TbCounter",
            ControlType = "Text",
        };

        if (!UiaStructuralLocator.TryFindElement(layer, scope, out var element, out _))
        {
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
