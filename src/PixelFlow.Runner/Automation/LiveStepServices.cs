using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows.Automation;
using PixelFlow.Core.Projects;
using PixelFlow.Core.Runner;

namespace PixelFlow.Runner.Automation;

/// <summary>
/// Live resolve / re-check / click / paste-type / post-check.
/// Resolves via ordered locator chain (UIA → Win32 → OCR → Image). Never clicks without a fresh resolve.
/// Type steps paste via the clipboard and always restore prior clipboard contents afterward.
/// </summary>
internal sealed class LiveStepServices : ITargetResolver, IStepVerifier, IStepExecutor
{
    private static readonly Regex ClickCountRegex = new(
        @"Clicks:\s*(?<n>\d+)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private readonly IRunnerDelay _delay;
    private readonly string? _projectFolder;
    private readonly Dictionary<string, int> _counterBeforeByStep = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _expectedTypeTextByStep = new(StringComparer.Ordinal);

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

        if (IsType(step) && string.IsNullOrEmpty(step.Text))
        {
            return ResolveResult.NotFound($"Type step '{step.Id}' has empty Text.");
        }

        var result = await LocatorChainResolver.ResolveAsync(step, _projectFolder, cancellationToken)
            .ConfigureAwait(false);

        if (!result.Found)
        {
            Console.WriteLine($"[runner] Resolve miss (step {step.Id}): {result.FailureReason}");
        }
        else
        {
            var dpi = result.BoundingRect.IsEmpty
                ? 0u
                : MonitorDpi.GetDpiForPhysicalPoint(
                    (int)Math.Round(result.BoundingRect.X + result.BoundingRect.Width / 2.0),
                    (int)Math.Round(result.BoundingRect.Y + result.BoundingRect.Height / 2.0));

            Console.WriteLine(
                $"[runner] Resolve hit (step {step.Id}): layer={result.MatchedLayer}, " +
                $"confidence={result.Confidence:0.###}, AutomationId={result.AutomationId}, " +
                $"Name={result.Name}, ControlType={result.ControlType}, Bounds={result.BoundingRect}, " +
                $"dpi={dpi}");
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

        if (IsClick(step) && TryReadClickCount(step.Locator?.Scope, out var count))
        {
            _counterBeforeByStep[step.Id] = count;
            Console.WriteLine($"[runner] Pre-check counter snapshot (step {step.Id}): {count}");
        }
        else
        {
            _counterBeforeByStep.Remove(step.Id);
        }

        if (IsType(step))
        {
            _expectedTypeTextByStep[step.Id] = step.Text ?? "";
        }
        else
        {
            _expectedTypeTextByStep.Remove(step.Id);
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

        if (IsType(step))
        {
            await ExecuteTypeAsync(step, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!IsClick(step))
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

        if (IsType(step))
        {
            return Task.FromResult(VerifyTypeAfter(step));
        }

        if (!IsClick(step))
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

    private async Task ExecuteTypeAsync(ScriptStep step, CancellationToken cancellationToken)
    {
        var text = step.Text ?? "";
        var live = await LocatorChainResolver.ResolveAsync(step, _projectFolder, cancellationToken)
            .ConfigureAwait(false);
        if (!live.Found || live.BoundingRect.IsEmpty)
        {
            throw new InvalidOperationException(
                $"Refusing to type: target not found at execute time ({live.FailureReason}).");
        }

        // Focus the field (click center), then paste via clipboard with guaranteed restore.
        Console.WriteLine(
            $"[runner] Type focus click at {live.BoundingRect} (step {step.Id}, layer={live.MatchedLayer})");
        FocusResolvedTarget(step, live);

        // Small settle so the Edit control accepts keyboard focus before Ctrl+V.
        await _delay.DelayAsync(50, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        using var clipboard = ClipboardGuard.ReplaceWith(text);
        try
        {
            Console.WriteLine($"[runner] Type paste via clipboard (step {step.Id}, length={text.Length})");
            SendInputKeyboard.SelectAll();
            await _delay.DelayAsync(20, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            SendInputKeyboard.Paste();
            await _delay.DelayAsync(50, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            // Restore even if paste / cancel fails mid-step.
            clipboard.Restore();
            Console.WriteLine($"[runner] Clipboard restored after Type (step {step.Id})");
        }
    }

    private static void FocusResolvedTarget(ScriptStep step, ResolveResult live)
    {
        // Prefer UIA SetFocus when we can re-find the element; otherwise SendInput click.
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
                out _)
            && element is not null)
        {
            try
            {
                element.SetFocus();
                return;
            }
            catch (ElementNotAvailableException)
            {
                // Fall through to SendInput click.
            }
            catch (InvalidOperationException)
            {
                // Some controls reject SetFocus; click instead.
            }
        }

        SendInputClick.ClickCenter(live.BoundingRect);
    }

    private bool VerifyTypeAfter(ScriptStep step)
    {
        if (!_expectedTypeTextByStep.TryGetValue(step.Id, out var expected))
        {
            expected = step.Text ?? "";
        }

        Thread.Sleep(50);

        if (!TryReadEditValue(step.Locator, out var actual))
        {
            Console.WriteLine($"[runner] Post-check failed (step {step.Id}): edit value unreadable after Type.");
            return false;
        }

        var ok = string.Equals(actual, expected, StringComparison.Ordinal);
        Console.WriteLine(
            ok
                ? $"[runner] Post-check OK (step {step.Id}): typed value matches"
                : $"[runner] Post-check FAILED (step {step.Id}): expected '{expected}', got '{actual}'");
        return ok;
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

    private static bool IsClick(ScriptStep step) =>
        string.Equals(step.Type, "Click", StringComparison.OrdinalIgnoreCase);

    private static bool IsType(ScriptStep step) =>
        string.Equals(step.Type, "Type", StringComparison.OrdinalIgnoreCase);

    private static bool TryReadEditValue(LocatorChain? chain, out string value)
    {
        value = "";
        if (chain is null)
        {
            return false;
        }

        var layer = chain.Layers.FirstOrDefault(l =>
            l.Enabled
            && (string.Equals(l.Kind, LocatorKinds.UiaStructural, StringComparison.OrdinalIgnoreCase)
                || string.Equals(l.Kind, LocatorKinds.UiaSemantic, StringComparison.OrdinalIgnoreCase)));
        if (layer is null)
        {
            return false;
        }

        if (!UiaStructuralLocator.TryFindElement(layer, chain.Scope, out var element, out _)
            || element is null)
        {
            return false;
        }

        try
        {
            if (element.TryGetCurrentPattern(ValuePattern.Pattern, out var patternObj)
                && patternObj is ValuePattern valuePattern)
            {
                value = valuePattern.Current.Value ?? "";
                return true;
            }

            value = element.Current.Name ?? "";
            return true;
        }
        catch (ElementNotAvailableException)
        {
            return false;
        }
    }

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
