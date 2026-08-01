# PixelFlow Executable Phases

This is the day-to-day implementation checklist. Architecture and rationale live in [architecture-plan.md](./architecture-plan.md). Do not invent features beyond the phase you are implementing.

**How to use (director mode)**

1. Paste the prompt from [agent-phase-prompt.md](./agent-phase-prompt.md) into a new chat.
2. Replace `PHASES` with one phase id (preferred) or a short consecutive range (e.g. `P01` or `P01-P02`).
3. The **agent** implements, verifies (unit → integration → E2E / timing as applicable), runs the **full** `dotnet test` suite (`Category!=Live` then `Category=Live`), fixes until green, then marks the phase `Done`.
4. You review the agent's evidence summary; you are not the primary QA. Only open a fix chat if you spot a defect the agent missed.
5. Each phase's **Manual test** section is the **agent verification checklist** (historical name kept). The agent must execute it — not hand it to you.

**Status legend:** `Todo` | `In Progress` | `Done` | `Blocked`

**Rules for agents (also in the prompt)**

- Read `docs/architecture-plan.md`, this file, and `docs/agent-phase-prompt.md` (Agent-owned verification) first.
- Implement only the listed phase(s). Do not start later phases "while you're at it."
- Prefer the smallest change that satisfies the phase Done criteria.
- Do not add Python, packaging/signing, OCR, OpenCV, rich editor, or installer work unless the active phase explicitly requires it.
- **Own testing:** build, unit/component tests, live/integration against Test Bench or fixtures when relevant, full verification checklist, phase-scoped timing/performance checks, then the **mandatory full suite** (`dotnet test PixelFlow.slnx --filter Category!=Live` and `--filter Category=Live`). Fix failures before marking Done. Do not treat the human as the Done gate.
- After verification passes, update this file: set the completed phase status to `Done`. Leave other phases unchanged unless asked.
- If the environment cannot run a required live check, set status `Blocked` and explain — do not mark `Done`.

---

## Progress overview

| Phase | Title | Status |
|---|---|---|
| P00 | Solution skeleton | Done |
| P01 | Project model types + JSON round-trip | Done |
| P02 | Atomic save/load + history backups | Done |
| P03 | Schema migration harness | Done |
| P04 | Runner state machine (mocked) | Done |
| P05 | Studio-Runner IPC (run/pause/stop) | Done |
| P06 | Minimal WPF Test Bench | Done |
| P07 | UIA structural locator | Done |
| P08 | One verified click end-to-end | Done |
| P09 | Emergency stop hotkey | Done |
| P10 | Pause/resume between steps | Done |
| P11 | Retry budget and FailedStep | Done |
| P12 | Win32 locator fallback | Done |
| P13 | OCR locator fallback | Done |
| P14 | Image template locator fallback | Done |
| P15 | Locator chain ranking + confidence | Done |
| P16 | UIA inspector panel | Done |
| P17 | Test-this-locator action | Done |
| P18 | Minimal script editor (list-based) | Done |
| P19 | Screen snipping tool | Done |
| P20 | Inline image tokens in editor | Done |
| P21 | Run reports (JSONL) | Done |
| P22 | Opt-in failure screenshots | Done |
| P23 | Recovery steps (skip/jump/abort) | Done |
| P24 | User-interference detection | Done |
| P25 | Clipboard restore on type/paste | Done |
| P26 | DPI-aware coordinates | Done |
| P27 | Display-change invalidation | Done |
| P28 | Broader Test Bench surfaces | Done |
| P29 | Project trust prompt | Done |
| P30 | Secrets by reference | Done |
| P31 | Unsigned installer package | Todo |
| P32 | Signed release groundwork | Todo |

---

## P00 - Solution skeleton

- **Status:** Done
- **Goal:** Create a buildable .NET 10 solution with empty Studio (WPF), Runner (console/worker), and shared Core library.
- **In scope:** Solution file, three projects, README build/run instructions, `dotnet build` succeeds.
- **Out of scope:** UI features, UIA, IPC payload, project files, automation logic.
- **Expected layout (adjust names only if needed):**
  - `src/PixelFlow.Core/`
  - `src/PixelFlow.Runner/`
  - `src/PixelFlow.Studio/`
  - `PixelFlow.slnx` or `PixelFlow.sln`
- **Manual test:**
  1. Install/verify .NET 10 SDK.
  2. `dotnet build` from repo root succeeds.
  3. Studio launches an empty WPF window.
  4. Runner starts and exits cleanly with a usage/help or idle message.
- **Done when:** Build + both apps launch; README has exact commands.

## P01 - Project model types + JSON round-trip

- **Status:** Done
- **Goal:** Define `schemaVersion`, steps, locator-chain placeholders, timeouts, retry policy; serialize/deserialize deterministically.
- **In scope:** C# models in Core, sample `fixtures/projects/minimal.pflow/project.json`, unit tests for round-trip equality.
- **Out of scope:** Atomic file IO, migrations, editor UI, locator resolution.
- **Manual test:**
  1. Run unit tests for serialize -> deserialize -> serialize (stable JSON keys/order as designed).
  2. Open the fixture JSON and confirm fields match architecture Section 5 (schema version, steps, timeouts, retries).
- **Done when:** Round-trip tests pass; fixture committed; no UI changes required.

## P02 - Atomic save/load + history backups

- **Status:** Done
- **Goal:** Load/save `.pflow` project folders safely with temp-write + rename and rolling `history/` backups.
- **In scope:** ProjectStore API, content-hash asset folder convention stub, backup retention N.
- **Out of scope:** Full asset pipeline, editor, migrations beyond identity load of current schema.
- **Manual test:**
  1. Save a project; `project.json` appears.
  2. Save again; previous copy appears under `history/`.
  3. Kill/interrupt simulation: write to temp then fail before rename; original `project.json` remains valid.
  4. Load returns the last good project.
- **Done when:** Manual crash/interrupt check passes; unit tests cover temp+rename and backup rotation.

## P03 - Schema migration harness

- **Status:** Done
- **Goal:** Opening an older `schemaVersion` runs an explicit migrator before use.
- **In scope:** Migrator interface, v1 identity migrator, one fake older fixture + golden expected output, tests.
- **Out of scope:** Real future schemas beyond one demo migration path.
- **Manual test:**
  1. Load `fixtures/projects/legacy-v0.pflow` (or similar).
  2. Confirm it migrates to current schema and saves/loads without data loss of mapped fields.
  3. Unknown future schema fails with a clear error (no silent ignore).
- **Done when:** Migration tests green; unknown schema fails loudly.

## P04 - Runner state machine (mocked)

- **Status:** Done
- **Goal:** Implement the Section 7 state machine against a mocked resolver/executor (no real UI).
- **In scope:** States, transitions, timeout/retry hooks as stubs, unit tests for happy path + failed resolve + abort.
- **Out of scope:** Real UIA, SendInput, IPC, Studio UI.
- **Manual test:**
  1. Run unit tests covering Idle -> Resolving -> Verifying -> Executing -> PostCheck -> Idle.
  2. Run unit tests for retry exhaustion -> FailedStep and emergency abort from Resolving/Executing.
- **Done when:** Transition tests cover the diagram paths listed above; no desktop automation yet.

## P05 - Studio-Runner IPC (run/pause/stop)

- **Status:** Done
- **Goal:** Studio starts Runner as a separate process and sends versioned run/pause/stop over a named pipe (or equivalent local IPC).
- **In scope:** Versioned message schema, process lifetime, Studio can show Runner connected/disconnected.
- **Out of scope:** Real locator work; Runner may still use mocked steps.
- **Manual test:**
  1. Start Studio; click Run on a fixture project.
  2. Confirm a separate Runner process appears in Task Manager.
  3. Pause/Stop from Studio changes Runner state (visible in Studio status and/or Runner console log).
  4. Kill Runner externally; Studio shows disconnected/error, does not hang.
- **Done when:** Manual process + pause/stop checks pass; hang-on-kill does not occur.

## P06 - Minimal WPF Test Bench

- **Status:** Done
- **Goal:** Ship a tiny companion WPF app with a button that has a stable `AutomationId` and a click counter label.
- **In scope:** One window, one button (`AutomationId` e.g. `TbSubmit`), counter text, easy to launch.
- **Out of scope:** WinForms/Win32/Electron surfaces (later phase).
- **Manual test:**
  1. Launch Test Bench.
  2. Inspect with Accessibility Insights / Inspect.exe: button exposes expected `AutomationId`.
  3. Manual click increments counter.
- **Done when:** App runs; AutomationId visible to Inspect tools.

## P07 - UIA structural locator

- **Status:** Done
- **Goal:** Resolve a target by `AutomationId` + `ControlType` + `Name` scoped to a process/window.
- **In scope:** Locator API in Core/Runner, find Test Bench button, return bounding rect + element identity.
- **Out of scope:** Clicking, OCR, images, ancestor-path sophistication beyond parent window scope if not needed yet.
- **Manual test:**
  1. Start Test Bench.
  2. Run a small console/command that prints found element info for `TbSubmit`.
  3. Move the Test Bench window; resolve again; still finds the button.
  4. Close Test Bench; resolve fails clearly (no throw-and-guess).
- **Done when:** Find success/fail cases work; results printed or unit/integration test against live Test Bench documented.

## P08 - One verified click end-to-end

- **Status:** Done
- **Goal:** Runner resolves Test Bench button, re-checks before input, clicks, verifies counter changed.
- **In scope:** Input executor (UIA Invoke and/or SendInput), post-action verification, one fixture script step.
- **Out of scope:** Editor UX polish, fallbacks, screenshots.
- **Manual test:**
  1. Launch Test Bench at counter 0.
  2. Run fixture script from Studio or CLI.
  3. Counter becomes 1.
  4. Close Test Bench mid-run; run fails safely without clicking elsewhere.
- **Done when:** Verified click works; failure on missing target does not click random screen areas.

## P09 - Emergency stop hotkey

- **Status:** Done
- **Goal:** Global hotkey aborts the Runner quickly during execution.
- **In scope:** Register hotkey, transition to Aborted, cancel in-flight work best-effort.
- **Out of scope:** Configurable keybinding UI (hardcode documented default first).
- **Manual test:**
  1. Run a script with a long Wait step.
  2. Press the documented emergency hotkey.
  3. Runner aborts; Studio shows Aborted; no further steps execute.
- **Done when:** Abort works with focus on another window; documented in README or phase notes.
- **Default hotkey:** `Ctrl+Shift+F12` (global, Runner-registered).

## P10 - Pause/resume between steps

- **Status:** Done
- **Goal:** Pause only between steps; resume continues next step without replaying half-finished input.
- **In scope:** Pause/resume IPC + state machine enforcement.
- **Out of scope:** Mid-keystroke pause support.
- **Manual test:**
  1. Multi-step fixture (wait, click, wait).
  2. Pause during wait; confirm next click does not run until resume.
  3. Resume; remaining steps complete.
- **Done when:** Manual pause/resume checklist passes.

## P11 - Retry budget and FailedStep

- **Status:** Done
- **Goal:** Bounded retries with backoff; exhausting budget yields FailedStep (not hang/crash).
- **In scope:** Per-step retry/timeout from project model wired into state machine.
- **Out of scope:** Recovery jump/skip (P23).
- **Manual test:**
  1. Script targets a missing AutomationId with retries=3, short timeout.
  2. Observe three attempts then FailedStep.
  3. Total wall time roughly matches configured budget (not infinite).
- **Done when:** FailedStep is observable in Studio/logs; no runaway retries.
- **Timeout semantics:** `TimeoutMs` is a **per-attempt** resolve poll budget (poll until found or timeout, then retry/backoff).
## P12 - Win32 locator fallback

- **Status:** Done
- **Goal:** If UIA structural match fails, try Win32 class/control id path for a simple Win32/WinForms control.
- **In scope:** Win32 locator layer + a small WinForms or Win32 Test Bench surface.
- **Out of scope:** OCR/image.
- **Manual test:**
  1. Target a control that is weak in UIA but addressable via Win32.
  2. Confirm logs show fallback to Win32.
  3. Click still verified.
- **Done when:** Fallback path proven on at least one non-WPF control.

## P13 - OCR locator fallback

- **Status:** Done
- **Goal:** Find on-screen text via Windows.Media.Ocr (preferred) or chosen .NET OCR wrapper; fuzzy match optional but bounded.
- **In scope:** OCR locator layer, fixture text label without useful AutomationId.
- **Out of scope:** Image templates.
- **Manual test:**
  1. Test Bench label with known text.
  2. Script uses OCR locator only.
  3. Click/move hits the label region; miss text fails clearly.
- **Done when:** OCR hit/miss cases work on the fixture.

## P14 - Image template locator fallback

- **Status:** Done
- **Goal:** Multi-scale OpenCvSharp template match as last-resort locator.
- **In scope:** Image asset by content hash, matchTemplate multi-scale, confidence threshold.
- **Out of scope:** Snipping UI (can use a pre-saved PNG fixture).
- **Manual test:**
  1. Fixture icon/button image in assets.
  2. Resolve finds it on Test Bench custom/icon surface.
  3. Wrong image fails below threshold (no low-confidence click).
- **Done when:** Hit/miss + threshold behavior confirmed.

## P15 - Locator chain ranking + confidence

- **Status:** Done
- **Goal:** Wire ordered chain UIA structural -> UIA semantic -> Win32 -> OCR -> image; record winning layer.
- **In scope:** Resolver orchestration, diagnostics field for matched layer/score.
- **Out of scope:** New locator types.
- **Manual test:**
  1. Step with full chain where UIA works: log shows UIA structural win.
  2. Disable/break UIA properties on a fixture so Win32/OCR/image wins as expected.
  3. Confirm no click when all layers fail.
- **Done when:** Winning layer is visible in logs/report for each case.

## P16 - UIA inspector panel

- **Status:** Done
- **Goal:** Studio panel shows live UIA properties for hovered/selected element.
- **In scope:** Tree or property list: AutomationId, Name, ControlType, bounds, process/window.
- **Out of scope:** Auto-writing steps (can be copy fields manually).
- **Manual test:**
  1. Open Studio inspector.
  2. Hover Test Bench button; properties match Inspect.exe.
- **Done when:** Properties are accurate enough to author a locator by hand.

## P17 - Test-this-locator action

- **Status:** Done
- **Goal:** From Studio, test a locator against the live desktop and highlight match or show failure reason.
- **In scope:** Highlight overlay or flash bounds; success/fail message.
- **Out of scope:** Full recorder that auto-builds chains.
- **Manual test:**
  1. Enter locator for `TbSubmit`; Test succeeds and highlights button.
  2. Change to wrong id; Test fails with clear message.
- **Done when:** Author-time validation works without running a full script.

## P18 - Minimal script editor (list-based)

- **Status:** Done
- **Goal:** Edit steps in Studio as an ordered list bound to the project model (add/remove/reorder simple commands).
- **In scope:** Click/Type/Wait steps, save/load through ProjectStore.
- **Out of scope:** Rich text with inline images (P20), snipping (P19).
- **Manual test:**
  1. Create steps in UI; save; reopen; steps identical.
  2. Run script; behavior matches list.
- **Done when:** Round-trip edit/run works without hand-editing JSON.

## P19 - Screen snipping tool

- **Status:** Done
- **Goal:** Hotkey or button opens region snip; saves hashed PNG into project `assets/`.
- **In scope:** Overlay selector, hash naming, asset reference returned.
- **Out of scope:** Inserting inline token into rich text (P20 can consume the asset API).
- **Manual test:**
  1. Snip a region.
  2. File appears under `assets/sha256-....png`.
  3. Re-snip duplicate content reuses same hash (no duplicate bytes) if designed that way.
- **Done when:** Asset lands in project folder and is referenceable.

## P20 - Inline image tokens in editor

- **Status:** Done
- **Goal:** Show snipped thumbnails inline in the step editor and map them to image locator assets in the model.
- **In scope:** Token UI <-> model binding.
- **Out of scope:** Full FlowDocument word-processor polish beyond needed token UX.
- **Manual test:**
  1. Insert snipped image into a Click step.
  2. Save/reload shows thumbnail.
  3. Run uses image locator asset correctly.
- **Done when:** Visual token and model stay in sync across reload.

## P21 - Run reports (JSONL)

- **Status:** Done
- **Goal:** Each run writes structured events: step id, timestamps, locator layer, score, outcome.
- **In scope:** Report writer, Studio opens last report summary.
- **Out of scope:** Screenshots.
- **Manual test:**
  1. Run success and failure scripts.
  2. Open report file; events match what happened.
- **Done when:** Report alone is enough to explain pass/fail per step.

## P22 - Opt-in failure screenshots

- **Status:** Done
- **Goal:** On failure, optionally capture screenshot; default off for sensitive steps.
- **In scope:** Flag per step/project, retention with reports.
- **Out of scope:** Redaction ML; simple full-screen or target-window capture is enough.
- **Manual test:**
  1. Failure with capture on -> image stored with report.
  2. Failure with capture off -> no image.
- **Done when:** Opt-in behavior verified.

## P23 - Recovery steps (skip/jump/abort)

- **Status:** Done
- **Goal:** On FailedStep, follow configured recovery: skip, jump to label, or abort.
- **In scope:** Project model fields + state machine behavior.
- **Out of scope:** Complex try/catch scripting language.
- **Manual test:**
  1. Missing target with recovery=skip continues.
  2. recovery=jump reaches labeled step.
  3. recovery=abort stops.
- **Done when:** All three recovery modes manually verified.

## P24 - User-interference detection

- **Status:** Done
- **Goal:** If user moves mouse/types near action time, Runner pauses instead of fighting for input.
- **In scope:** Detection heuristic + pause.
- **Out of scope:** Perfect classification of all input.
- **Manual test:**
  1. Start run; move mouse actively before a click step.
  2. Runner pauses and reports interference.
- **Done when:** Interference causes pause, not a misplaced click.

## P25 - Clipboard restore on type/paste

- **Status:** Done
- **Goal:** Steps that use clipboard snapshot/restore previous contents.
- **In scope:** Paste-based typing path or explicit Paste step.
- **Out of scope:** Cloud clipboard sync edge cases.
- **Manual test:**
  1. Copy known text A to clipboard.
  2. Run step that pastes text B.
  3. After step, clipboard is A again.
- **Done when:** Clipboard restored after success and after failure mid-paste if feasible.

## P26 - DPI-aware coordinates

- **Status:** Done
- **Goal:** Capture/replay coordinates correctly at 100%/125%/150% scaling.
- **In scope:** Per-monitor DPI awareness declared; coordinate helpers; tests or manual matrix.
- **Out of scope:** Full multi-monitor topology (P27).
- **Manual test:**
  1. At 100% scaling, verified click works.
  2. Change to 150%, re-run (re-resolve); click still hits button.
- **Done when:** No systematic offset at tested DPI levels.

## P27 - Display-change invalidation

- **Status:** Done
- **Goal:** Monitor add/remove/resolution change invalidates cached absolute coords and forces re-resolve.
- **In scope:** Display change listener + cache bust.
- **Out of scope:** Fancy UI prompts beyond a log/status message.
- **Manual test:**
  1. Start run or resolve with cache.
  2. Change display settings or unplug monitor if available.
  3. Next resolve does not use stale coords; either succeeds via re-resolve or fails safely.
- **Done when:** Stale-coordinate click does not occur after display change.

## P28 - Broader Test Bench surfaces

- **Status:** Done
- **Goal:** Expand Test Bench with WinForms, custom canvas, small icons, moving target (architecture Section 10 subset).
- **In scope:** Those surfaces + scripts proving each locator path.
- **Out of scope:** Electron/WebView2 if timeboxed; can be a follow-up note if deferred.
- **Manual test:**
  1. Each surface has at least one passing fixture script.
  2. Moving target still clicked via re-resolve.
- **Done when:** Checklist of surfaces marked covered in this file or a short test matrix note.
- **Surface coverage (P28):**

  | Surface | Fixture | Locator path | Live test |
  |---|---|---|---|
  | WPF UIA (`TbSubmit`) | `click-submit` | UiaStructural | matrix + `TestBenchSurfaceCoverageTests` |
  | Win32 native (BUTTON/1001) | `win32-click` | Win32 | matrix + surface coverage |
  | WinForms (`TbWinForms`) | `winforms-click` | UiaStructural (MSAA bridge) | matrix + surface coverage |
  | OCR label | `ocr-click` | Ocr | matrix + surface coverage |
  | Image 64×64 magenta | `image-click` | Image | matrix + surface coverage |
  | Custom canvas (no UIA peers) | `canvas-click` | Image | matrix + surface coverage |
  | Small icon grid 16×16 | `icon-grid-click` | Image | matrix + surface coverage |
  | Moving target (`TbMovingTarget`) | `moving-target-click` | UiaStructural + re-resolve | matrix + surface coverage |

  Deferred (explicit P28 out of scope): Electron / WebView2.

## P29 - Project trust prompt

- **Status:** Done
- **Goal:** Opening a project from an untrusted path prompts before run.
- **In scope:** Trust store (per user), block run until trusted.
- **Out of scope:** Full enterprise policy engine.
- **Manual test:**
  1. Open project from Downloads-like path; prompt appears.
  2. Decline -> cannot run.
  3. Accept -> run allowed; subsequent opens remember trust.
- **Done when:** Trust gate blocks untrusted runs.

## P30 - Secrets by reference

- **Status:** Done
- **Goal:** Type/secret steps reference Windows Credential Manager by name; secret values never written to `project.json` or reports.
- **In scope:** Secret reference field, resolve at runtime, redaction in logs.
- **Out of scope:** Custom vault providers.
- **Manual test:**
  1. Store secret in Credential Manager.
  2. Project JSON contains name only.
  3. Run types secret; report does not contain secret value.
- **Done when:** JSON/report inspection shows no secret plaintext.

## P31 - Unsigned installer package

- **Status:** Todo
- **Goal:** Produce an installable package (e.g. MSIX or setup.exe) for local testing without Authenticode yet.
- **In scope:** Install/update/uninstall on a clean folder/VM.
- **Out of scope:** Store submission, uiAccess.
- **Manual test:**
  1. Install on a clean machine/profile.
  2. App runs.
  3. Uninstall removes entry points.
- **Done when:** Install/run/uninstall documented and verified.

## P32 - Signed release groundwork

- **Status:** Todo
- **Goal:** Wire signing + dependency vulnerability scan in release build; document uiAccess prerequisites (do not enable uiAccess in v1 unless explicitly decided).
- **In scope:** CI/release notes, `dotnet list package --vulnerable`, signing pipeline placeholders/secrets docs.
- **Out of scope:** Shipping elevated uiAccess automation as a product feature.
- **Manual test:**
  1. Release build produces signed artifacts (or dry-run with test cert).
  2. Vulnerability scan runs and fails the build on known severe issues (or reports cleanly).
- **Done when:** Signing + scan path documented and exercised once.

---

## Suggested chat order

Start with `P00`, then one phase per chat unless two phases are trivial and consecutive (e.g. `P01-P02`). Prefer single-phase chats for anything touching UIA, input, or IPC.
