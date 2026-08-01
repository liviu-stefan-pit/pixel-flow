# AGENTS.md

## Cursor Cloud specific instructions

PixelFlow is a **Windows-only** attended desktop RPA product (.NET 10). See `README.md` and
`docs/` for full architecture, phase plan, and the standard `dotnet build` / `dotnet run` /
`dotnet test` commands. Notes below cover only what is non-obvious when working from this
**Linux** cloud VM.

### What can and cannot run on this Linux VM

- `PixelFlow.Core` (`net10.0`) and `tests/PixelFlow.Core.Tests` (`net10.0`) are cross-platform:
  they build, test, and run here. This is the practical dev scope on Linux.
- `PixelFlow.Studio` and `PixelFlow.TestBench` are WPF (`net10.0-windows`) and
  `PixelFlow.Runner` (`net10.0-windows10.0.19041.0`) depends on Windows-only APIs
  (UIA, Win32 `SendInput`, `Windows.Media.Ocr`, `OpenCvSharp4.runtime.win`). These
  **cannot build or run on Linux** — `dotnet build PixelFlow.slnx` / `dotnet restore PixelFlow.slnx`
  fail with `NETSDK1100` for those three projects. Full Studio+Runner+TestBench E2E requires
  Windows 10/11 x64 with an interactive desktop; it is out of scope on this VM.

### Build / test / run on Linux (cross-platform scope)

- Build the engine: `dotnet build src/PixelFlow.Core/PixelFlow.Core.csproj`
- Run unit tests: `dotnet test tests/PixelFlow.Core.Tests/PixelFlow.Core.Tests.csproj`
- Do NOT target the whole solution on Linux (the Windows projects break restore/build).

### Known Linux-only test failures (not product bugs, not caused by setup)

Out of 29 Core unit tests, 27–28 pass. Two are environment-sensitive on Linux:

- `Ipc.IpcPipeConnectionTests.NamedPipe_HelloThenStatus_RoundTrips` — **always** fails here.
  .NET named pipes are backed by Unix domain sockets on Linux; the test's server disposes the
  connection right after two writes, which truncates the still-buffered second message before the
  client reads it. Passes on Windows.
- `Projects.ProjectStoreTests.HistoryRotation_KeepsOnlyRetentionCount` — **intermittently** fails.
  History backups are named with a millisecond timestamp (`project-yyyyMMdd-HHmmss-fff.json`);
  four rapid saves on this fast VM can collide within the same millisecond. Timing-dependent.

Treat these two as expected on Linux. Investigate other failures normally.

### .NET SDK

.NET 10 SDK is provisioned into the VM image at `/usr/local/dotnet` and symlinked to
`/usr/local/bin/dotnet` (on PATH). The startup update script only refreshes NuGet packages; it does
not reinstall the SDK.
