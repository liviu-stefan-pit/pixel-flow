# Agent prompt (copy into a new chat)

Copy everything inside the block below. Replace `PHASES` with a phase id from [phases.md](./phases.md), for example `P00` or `P01-P02`.

```text
You are implementing PixelFlow in this repo.

Before writing any code:
1. Read docs/architecture-plan.md (product scope, stack, non-goals, architecture).
2. Read docs/phases.md (executable phases and Done criteria).
3. Read docs/agent-phase-prompt.md if you need these rules restated.

Implement ONLY these phase(s): PHASES

Hard rules:
- Do not implement later phases, "nice to have" extras, or refactors outside the active phase Done criteria.
- Do not invent Python runtimes, cloud services, or features marked out of scope for v1 in the architecture doc.
- Prefer the smallest change that satisfies the phase In scope + Done when sections.
- Match existing project structure and naming; if P00 is not done yet, follow docs/phases.md expected layout.
- After the phase passes its own criteria, set that phase Status to Done in docs/phases.md (and the overview table). Do not mark other phases Done.
- Do not edit any Cursor plan files under .cursor/plans or the user's Downloads plan.
- End with: what changed, how I should manually test (use the phase checklist), and what is intentionally NOT done yet.

If something in the phase is ambiguous, ask me before coding. Do not guess product requirements.
```

## After you test

If something is wrong, open a **new** chat and paste:

```text
Read docs/architecture-plan.md and docs/phases.md first.
Fix phase PHASES only based on my test feedback below.
Do not start other phases. Do not expand scope.
When fixed, keep or set the phase Status correctly in docs/phases.md.

Test feedback:
- ...
```

## Marking Done

You (the human) decide when a phase is Done after manual testing. You can either:

- Ask the agent: `Mark P0X as Done in docs/phases.md after my successful test.`
- Or edit `docs/phases.md` yourself: set **Status** to `Done` in the phase section and in the overview table.
