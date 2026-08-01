# PixelFlow

Windows attended desktop automation studio (.NET 10 + WPF). Architecture and implementation are docs-driven.

## Docs

- [Architecture plan](docs/architecture-plan.md) — product scope, stack, reliability design
- [Executable phases](docs/phases.md) — small implementation slices; agent verifies each phase before Done
- [Agent prompt](docs/agent-phase-prompt.md) — paste into a new chat; includes agent-owned verification (unit → E2E)

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (`dotnet --version` ≥ 10.x)

## Build

```powershell
dotnet build PixelFlow.slnx
```

## Run Studio

```powershell
dotnet run --project src/PixelFlow.Studio/PixelFlow.Studio.csproj
```

Studio starts the Runner as a separate process and sends versioned run/pause/resume/stop over a named pipe.
Default fixture: `fixtures/projects/click-submit.pflow` (one verified UIA click on Test Bench).
Override: set `PIXELFLOW_PROJECT_FOLDER` to another `.pflow` folder (e.g. `emergency-stop`, `pause-resume`, `retry-miss`).

Optional: set `PIXELFLOW_RUNNER_PATH` to a Runner exe/dll if auto-discovery fails.

**Script editor (P18):** Open / Save / New, ordered step list (Wait / Click / Type), add/remove/reorder, step details bound to `project.json` via `ProjectStore`. Run auto-saves first.

**Snip (P19/P20):** **Snip** opens a region overlay; PNG is stored as `assets/sha256-<hex>.png` (identical bytes reuse the same hash). If a Click step is selected, an Image locator layer is attached and an inline thumbnail token appears in step details (and the step list).

**Emergency stop:** `Ctrl+Shift+F12` (global, registered by the Runner). Works even when another window has focus.

## Run Runner (manual / help)

```powershell
dotnet run --project src/PixelFlow.Runner/PixelFlow.Runner.csproj -- --help
dotnet run --project src/PixelFlow.Runner/PixelFlow.Runner.csproj -- --pipe PixelFlow.demo
```

### P07 — resolve Test Bench button (UIA structural)

1. Start Test Bench (see below).
2. Resolve:

```powershell
dotnet run --project src/PixelFlow.Runner/PixelFlow.Runner.csproj -- --resolve
```

Prints AutomationId, Name, ControlType, bounds, process id. Exit code `2` if not found.
Move the Test Bench window and run `--resolve` again; it should still find `TbSubmit`.
Close Test Bench and run again; it should print `NOT FOUND` (no guessed click).

### P08 — verified click end-to-end

1. Start Test Bench at **Clicks: 0**.
2. Run the fixture:

```powershell
dotnet run --project src/PixelFlow.Runner/PixelFlow.Runner.csproj -- --run-project fixtures/projects/click-submit.pflow
```

Counter should become **Clicks: 1**. Close Test Bench and re-run; the run fails safely without clicking elsewhere.

Or use Studio **Run** with Test Bench open (same fixture).

### P09 — emergency stop hotkey

1. Run a long-wait fixture:

```powershell
dotnet run --project src/PixelFlow.Runner/PixelFlow.Runner.csproj -- --run-project fixtures/projects/emergency-stop.pflow
```

2. Click another window so Runner does not have focus.
3. Press **Ctrl+Shift+F12**.
4. Runner logs abort, finishes in `Aborted`, and does not continue later Wait steps.

Or from Studio: `$env:PIXELFLOW_PROJECT_FOLDER="fixtures/projects/emergency-stop.pflow"`, Run, then Ctrl+Shift+F12 — Studio status should show Aborted.

### P10 — pause / resume between steps

1. Start Test Bench at **Clicks: 0**.
2. Run:

```powershell
dotnet run --project src/PixelFlow.Studio/PixelFlow.Studio.csproj
```

with `$env:PIXELFLOW_PROJECT_FOLDER` pointing at `fixtures/projects/pause-resume.pflow` (Wait → Click → Wait).

3. During the first Wait, click **Pause**. Status stays Executing until the Wait finishes, then **Paused**.
4. Confirm the Click has not run yet (counter still 0).
5. Click **Resume**; counter becomes 1 and the final Wait completes.

### P11 — retry budget and FailedStep

No Test Bench needed (targets a missing AutomationId):

```powershell
dotnet run --project src/PixelFlow.Runner/PixelFlow.Runner.csproj -- --run-project fixtures/projects/retry-miss.pflow
```

Expect three resolve attempts (`timeoutMs=200`, `backoffMs=100`), then `FailedStep` → `Aborted`. Wall time roughly ~3×200ms + 2×100ms (order of ~1s), not an infinite hang. Exit code `3`.

### P12 — Win32 locator fallback

1. Start Test Bench (native **Win32 Click** button, class `BUTTON`, control id `1001`).
2. Run:

```powershell
dotnet run --project src/PixelFlow.Runner/PixelFlow.Runner.csproj -- --run-project fixtures/projects/win32-click.pflow
```

Log should show `layer=Win32`; counter increments. Broken-UIA fallback: `fixtures/projects/chain-win32-fallback.pflow`.

### P13 — OCR locator fallback

```powershell
dotnet run --project src/PixelFlow.Runner/PixelFlow.Runner.csproj -- --run-project fixtures/projects/ocr-click.pflow
```

Hits **OCR Target Label** via Windows.Media.Ocr. Miss: `fixtures/projects/ocr-miss.pflow` → FailedStep, no click.

### P14 — Image template fallback

```powershell
dotnet run --project src/PixelFlow.Runner/PixelFlow.Runner.csproj -- --run-project fixtures/projects/image-click.pflow
```

Matches the magenta icon via OpenCvSharp multi-scale template. Miss (noise asset below threshold): `fixtures/projects/image-miss.pflow`.

### P15 — Locator chain ranking

- UIA wins: `fixtures/projects/chain-uia-wins.pflow` → log `layer=UiaStructural`
- Win32 fallback: `fixtures/projects/chain-win32-fallback.pflow` → log `layer=Win32`
- All miss: `fixtures/projects/chain-all-miss.pflow` → FailedStep, no click

Resolve logs always include `layer=` and `confidence=`.

### P16 — UIA inspector

1. `dotnet run --project src/PixelFlow.Studio/PixelFlow.Studio.csproj`
2. Check **UIA Inspector**, hover Test Bench **Submit**.
3. Panel shows AutomationId / Name / ControlType / bounds / process / window (hand-copy into locators).

### P17 — Test this locator

1. Start Test Bench.
2. `dotnet run --project src/PixelFlow.Studio/PixelFlow.Studio.csproj`
3. In **Test locator**, keep defaults (`TbSubmit` / `PixelFlow.TestBench`) and click **Test locator**.
4. Result shows OK; a green highlight flashes over the Submit button (no full script run).
5. Change AutomationId to a wrong value (e.g. `TbMissing`); Test shows FAIL with a clear reason.

### P18 — minimal script editor

1. `dotnet run --project src/PixelFlow.Studio/PixelFlow.Studio.csproj`
2. Use **+ Wait / + Click / + Type**, reorder with ↑↓, edit details, **Save**.
3. **Open** the same folder again; steps match.
4. With Test Bench running, **Run**; Wait+Click+Type behavior matches the list (Type focuses the locator target and pastes via clipboard, then restores the prior clipboard).

### P19 — screen snipping

1. Open/Save a project folder in Studio.
2. Click **Snip**, drag a region, release.
3. Confirm `assets/sha256-....png` under the project folder; Last snip shows the hash.
4. Snip the same pixels again (or identical bytes); hash is reused (single file).

### P20 — inline image tokens

1. Open `fixtures/projects/image-click.pflow` (or Snip onto a Click step).
2. Step details show the thumbnail token + hash; the step list shows a small preview.
3. Save, close, reopen — thumbnail and `imageAssetHash` remain in sync.
4. With Test Bench running, **Run** (image-only step); Runner matches via the Image layer.

## Run reports (P21) and failure screenshots (P22)

Each `--run-project` / Studio **Run** writes a self-contained folder:

`{project}/reports/run-<utc-timestamp>/events.jsonl`

JSONL events include `runStarted`, `stepStarted`, `resolveAttempt` (layer + confidence), `stepFinished` (outcome + attempts), and `runFinished`. Studio **Last report** prints a pass/fail summary of the newest run.

**Opt-in failure screenshots (default off):**

- Project default: `defaults.captureFailureScreenshots`
- Per-step override: `steps[].captureFailureScreenshot` (Studio checkbox on the step)
- On failure with capture on → `failure-<stepId>.png` beside `events.jsonl`
- Capture off → no PNG
- Older runs under `reports/` rotate (keep last 20)

Fixtures: `fixtures/projects/failure-screenshot-on.pflow`, `failure-screenshot-off.pflow` (missing AutomationId → FailedStep).

### P23 — recovery steps (skip / jump / abort)

No Test Bench needed (missing AutomationId). On `FailedStep`, the step's `recovery` field chooses the path (`jumpTo` is a step `id`):

```powershell
dotnet run --project src/PixelFlow.Runner/PixelFlow.Runner.csproj -- --run-project fixtures/projects/recovery-skip.pflow
# FailedStep → Wait after-skip → Idle (exit 0)

dotnet run --project src/PixelFlow.Runner/PixelFlow.Runner.csproj -- --run-project fixtures/projects/recovery-jump.pflow
# FailedStep → Wait landing only (skips "skipped") → Idle (exit 0)

dotnet run --project src/PixelFlow.Runner/PixelFlow.Runner.csproj -- --run-project fixtures/projects/recovery-abort.pflow
# FailedStep → Aborted; "should-not-run" never executes (exit 3)
```

Omitted `recovery` still aborts (same as P11 `retry-miss.pflow`).

### P24 — user-interference detection

If the user moves the mouse or types near the moment a Click (or other synthetic input) would fire, the Runner **pauses before sending input** instead of fighting for control. Resume continues with a fresh resolve.

Heuristic: recent `GetLastInputInfo` (excluding the Runner's own prior synthetic input) and/or cursor movement during the pre-execute gate. Not a perfect classifier.

```powershell
# From Studio: Run fixtures/projects/interference-pause.pflow (or set PIXELFLOW_PROJECT_FOLDER),
# wiggle the mouse near the end of the opening Wait, watch status become Paused with an
# interference warning, then Resume — click completes.
```

Live automated cover: `StudioIpcSessionTests.UserInterference_BeforeClick_PausesWithoutClick_ThenResumeCompletes` (nudges mouse via SendInput).

### P25 — clipboard restore on type/paste

Type steps resolve a target (e.g. Test Bench `TbInput`), focus it, paste via the clipboard (`Ctrl+A` / `Ctrl+V`), then **always restore** the previous Unicode clipboard contents (success or mid-paste failure).

```powershell
# Put any text on the clipboard first, then:
dotnet run --project src/PixelFlow.Runner/PixelFlow.Runner.csproj -- --run-project fixtures/projects/type-paste.pflow
# TbInput becomes "hello-paste-B"; clipboard is back to whatever it was before the step.
```

### P26 — DPI-aware coordinates

Studio, Runner, and Test Bench declare **Per-Monitor V2** DPI awareness (`app.manifest` + `ApplicationHighDpiMode`). Locator bounds stay in physical pixels; `DpiCoordinates` converts DIP ↔ physical and maps SendInput absolute coords over the **virtual desktop**. Resolve logs include `dpi=` for the hit monitor.

```powershell
# Verified click at the current display scaling (100% / 125% / 150% …):
dotnet run --project src/PixelFlow.Runner/PixelFlow.Runner.csproj -- --run-project fixtures/projects/click-submit.pflow
```

### P27 — display-change invalidation

Monitor add/remove/resolution change (`WM_DISPLAYCHANGE`) bumps a display generation, clears the absolute-coordinate cache, and forces re-resolve before input. Stale physical coords from before the change are never clicked.

```powershell
# Watcher arms on run; resolve logs include displayGen=:
dotnet run --project src/PixelFlow.Runner/PixelFlow.Runner.csproj -- --run-project fixtures/projects/click-submit.pflow
# Optionally change display settings mid-run / between resolve and click — next action re-resolves or fails safely.
```

Live cover: `DisplayChangeInvalidationTests` (simulated `Invalidate` + CLI `displayGen=` / watcher armed).

## Run Test Bench

```powershell
dotnet run --project src/PixelFlow.TestBench/PixelFlow.TestBench.csproj
```

Companion window with shared click counter plus WPF Submit (`TbSubmit`), Type target (`TbInput`), native Win32 button (`BUTTON` / id 1001), WinForms button (`TbWinForms`), OCR target text, magenta 64×64 image icon, custom canvas (no UIA peers), 16×16 icon grid, and moving target (`TbMovingTarget`).

### Test Bench surface coverage (P28)

| Surface | Fixture | Path |
|---|---|---|
| WPF UIA | `click-submit` | UiaStructural |
| Win32 | `win32-click` | Win32 |
| WinForms | `winforms-click` | UiaStructural |
| OCR | `ocr-click` | Ocr |
| Image 64×64 | `image-click` | Image |
| Custom canvas | `canvas-click` | Image |
| Icon grid 16×16 | `icon-grid-click` | Image |
| Moving target | `moving-target-click` | UiaStructural (re-resolve) |

Electron/WebView2 deferred beyond P28.

## Tests

```powershell
dotnet build PixelFlow.slnx                                     # build once; Live tests launch the built binaries
dotnet test PixelFlow.slnx --filter Category!=Live               # unit tests (any machine with .NET 10)
dotnet test PixelFlow.slnx --filter Category=Live                # Live: real Runner + Test Bench, needs an interactive Windows desktop
```

After every phase, agents must run **both** filters (full suite) before marking Done — see [agent-phase-prompt.md](docs/agent-phase-prompt.md).

- **`PixelFlow.Core.Tests`** — pure unit tests (project model, IPC schema, locator ranking, run reports, screenshot capture flag, DPI/display invalidation). No desktop/process dependency.
- **`PixelFlow.Studio.Tests`** — Studio-facing pure-helper tests (`ImageTokenLoader` path/hash round-trips, **Last report** summary formatting). WPF types only, no window is shown.
- **`PixelFlow.Integration.Tests`** (`Category=Live`) — launches real `PixelFlow.Runner`/`PixelFlow.TestBench` processes against a temp copy of each fixture (never dirties `fixtures/projects/*/reports/`):
  - Full locator fixture matrix (`click-submit`, `chain-uia-wins`, `chain-win32-fallback`, `win32-click`, `winforms-click`, `ocr-click`, `image-click`, `canvas-click`, `icon-grid-click`, `moving-target-click`, and miss counterparts) asserting exit code + `events.jsonl` layer/outcome/confidence.
  - P28 **Test Bench surface coverage** (`TestBenchSurfaceCoverageTests`): WPF, Win32, WinForms, OCR, image, custom canvas, 16×16 icon grid, moving target.
  - Cross-phase **regression smoke** (`RegressionSmokeTests`): multi-step click+type (`smoke-click-type`), retry budget wall-time, `--resolve` hit/miss, sequential UIA→Win32→OCR on one Test Bench, Studio IPC fail-then-succeed session reuse.
  - P22 screenshot on/off assertions (PNG present + `screenshot` field vs. no PNG).
  - P23 recovery skip/jump/abort fixtures.
  - P25 type/paste clipboard restore (`type-paste` + `ClipboardAndDpiTests`).
  - P26 DPI helpers + Per-Monitor V2 click smoke at current scaling.
  - P27 display-change invalidation (coordinate cache bust + forced re-resolve; `WM_DISPLAYCHANGE` watcher).
  - Studio↔Runner IPC contract via `RunnerSession` (the same class Studio's buttons call): Run, Pause/Resume, Stop/Abort, interference pause, and killing the Runner process mid-run.
  - Starts `PixelFlow.TestBench` automatically (reuses an already-running instance if found) and skips cleanly (not a hard failure) if no interactive desktop is available.

## Layout

| Path | Role |
|---|---|
| `src/PixelFlow.Core` | Shared project model, JSON, store, migrations, IPC schema, runner engine, run reports |
| `src/PixelFlow.Runner` | Automation worker (named-pipe host, locator chain, verified click, report writer) |
| `src/PixelFlow.Studio` | WPF editor shell (list script editor, inline image tokens, snip→assets, run/pause/stop IPC, UIA inspector, test-locator, last report) |
| `src/PixelFlow.TestBench` | Target app for locator/integration tests (WPF + Win32 + WinForms + OCR + image + canvas + icon grid + moving target) |
| `tests/PixelFlow.Core.Tests` | Unit tests (project model, IPC, locators, run reports) |
| `tests/PixelFlow.Studio.Tests` | Studio pure-helper unit tests (image tokens, last-report summary) |
| `tests/PixelFlow.Integration.Tests` | Live end-to-end tests (`Category=Live`): real Runner + Test Bench + IPC |
| `fixtures/projects` | Sample `.pflow` project bundles |

## Status

Phases **P00–P28** implemented (broader Test Bench surfaces). See [docs/phases.md](docs/phases.md).
