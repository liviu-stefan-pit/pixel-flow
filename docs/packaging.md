# PixelFlow packaging (P31) and signed-release groundwork (P32)

## Unsigned installer package (P31)

PixelFlow ships an **unsigned**, current-user folder install for local testing. This is not Store/MSIX submission and does not use Authenticode (see [signing-and-release.md](./signing-and-release.md)).

### Build the package

From the repo root (requires .NET 10 SDK):

```powershell
.\scripts\pack.ps1
```

Outputs:

| Path | Purpose |
|---|---|
| `artifacts/package/PixelFlow/` | Side-by-side `PixelFlow.Studio.exe` + `PixelFlow.Runner.exe` |
| `artifacts/package/PixelFlow-<ver>-unsigned-win-x64.zip` | Zip of payload + install/uninstall scripts |

Optional:

```powershell
.\scripts\pack.ps1 -SelfContained   # larger; no shared Desktop Runtime needed
.\scripts\pack.ps1 -SkipZip
.\scripts\pack.ps1 -Version 0.1.0
```

Default publish is **framework-dependent** `win-x64` and needs the [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0).

### Install / update / uninstall

```powershell
.\scripts\install.ps1
# optional: -PackageDir <path> -InstallRoot <path>
```

- **Install root (default):** `%LocalAppData%\Programs\PixelFlow`
- **Start Menu:** `PixelFlow\PixelFlow Studio`
- **Apps & Features:** HKCU uninstall entry named **PixelFlow**
- **Update:** run `install.ps1` again (replaces files in place)

```powershell
.\scripts\uninstall.ps1
# optional: -InstallRoot <path>
```

Uninstall stops Studio/Runner if running, removes the install directory, Start Menu folder, and the HKCU uninstall entry.

### Agent verification

```powershell
.\scripts\verify-packaging.ps1
```

Packs, installs to a temp folder, launches Studio briefly, runs `Runner --help`, uninstalls, and asserts entry points are gone.

---

## Out of scope here

- Microsoft Store submission
- `uiAccess="true"` elevated automation (documented only in signing-and-release.md; not enabled in v1 manifests)
