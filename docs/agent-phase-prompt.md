# Agent prompt (copy into a new chat)

Copy everything inside the block below. Replace `PHASES` with a phase id from [phases.md](./phases.md), for example `P00` or `P01-P02`.

```text
You are implementing PixelFlow in this repo.

Before writing any code:
1. Read docs/architecture-plan.md (product scope, stack, non-goals, architecture).
2. Read docs/phases.md (executable phases and Done criteria).
3. Read docs/agent-phase-prompt.md (including Agent-owned verification).

Implement ONLY these phase(s): PHASES

Hard rules:
- Do not implement later phases, "nice to have" extras, or refactors outside the active phase Done criteria.
- Do not invent Python runtimes, cloud services, or features marked out of scope for v1 in the architecture doc.
- Prefer the smallest change that satisfies the phase In scope + Done when sections.
- Match existing project structure and naming; if P00 is not done yet, follow docs/phases.md expected layout.
- You own verification for this phase. Do not ask the human to manually test as the Done gate. The human is the director; you implement, test, fix, and only then mark Done.
- After the phase passes agent-owned verification, set that phase Status to Done in docs/phases.md (and the overview table). Do not mark other phases Done.
- Do not edit any Cursor plan files under .cursor/plans or the user's Downloads plan.
- End with: what changed, what you verified (commands + outcomes), any residual risk, and what is intentionally NOT done yet.

If something in the phase is ambiguous, ask me before coding. Do not guess product requirements.
```

## Agent-owned verification (required every phase)

The human does **not** act as QA. For every phase you implement, you must verify and fix until green **before** marking `Done`.

### Required loop

1. **Implement** the smallest change that meets In scope + Done when.
2. **Verify bottom-up** (skip layers that truly do not apply; say why):
   - **Build:** `dotnet build` (solution or affected projects) succeeds.
   - **Unit / component:** `dotnet test` and any phase-specific tests; add or extend tests when the phase introduces logic that can be asserted without a full desktop (parser, store, state machine, ranking, etc.).
   - **Integration / live:** exercise real processes against Test Bench or fixtures when the phase touches UIA, IPC, Runner, Studio, locators, input, or hotkeys. Start/stop apps as needed; drive CLI or UI automation yourself.
   - **End-to-end behavior:** run the phase's **Verification checklist** (labeled Manual test in older phase entries — treat it as *your* checklist). Confirm success and failure paths from Done when.
   - **Performance / timing (phase-scoped):** where the phase defines budgets (timeouts, retries, emergency-stop latency, backoff), measure wall-clock or log timings and fail the phase if behavior is unbounded, hang-prone, or clearly outside the documented budget. Do not invent heavy load/perf suites outside the phase.
3. **Fix** any failure in the same session (or continue until fixed). Do not mark Done with known broken verification.
4. **Mark Done** only after the checklist passes and evidence is recorded in your final reply.
5. **Report evidence:** commands run, pass/fail, key log lines or timings, and what you could not fully automate (e.g. physical multi-monitor unplug) with the closest substitute you used.

### Rules of thumb

- Prefer automated assertions checked into the repo when cheap; use live Test Bench / CLI when the phase is about real Windows UI.
- Never click random screen areas on resolve failure; failed cases must fail safely.
- If a check needs an interactive desktop and the environment cannot provide one, say **Blocked** with exact reason — do not mark Done.
- Do not expand scope to “improve testing infrastructure for all future phases” unless the active phase requires it; add only what this phase needs to prove itself.

## If the director reports a defect

Rare path — human spotted something after your verification. New chat:

```text
Read docs/architecture-plan.md and docs/phases.md first.
Fix phase PHASES only based on my feedback below.
Do not start other phases. Do not expand scope.
Re-run agent-owned verification for this phase after the fix.
When fixed and re-verified, keep or set the phase Status correctly in docs/phases.md.

Feedback:
- ...
```

## Marking Done

The **agent** marks the phase `Done` in `docs/phases.md` (overview table + phase section) only after agent-owned verification passes. The human may override status if they disagree.
