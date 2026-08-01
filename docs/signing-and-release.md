# Signing and release groundwork (P32)

PixelFlow v1 remains **attended, standard-integrity** automation. This document wires Authenticode + dependency scanning so a later signed release (and optional elevated/`uiAccess` milestone) has a clear path — without enabling `uiAccess` in shipping manifests today.

## Dependency vulnerability scan

Every release/CI build should fail closed on known vulnerable NuGet packages:

```powershell
.\scripts\scan-vulnerabilities.ps1
# optional: -FailOn Severe   # Critical/High only
```

Underlying command (per project):

```powershell
dotnet list <project> package --vulnerable --include-transitive
```

CI runs the same script (see `.github/workflows/ci.yml`). Treat a clean scan as required for release tagging.

## Authenticode signing

### Dry-run (local / CI without secrets)

After `.\scripts\pack.ps1`:

```powershell
.\scripts\sign.ps1
```

Creates a **temporary self-signed** code-signing certificate, signs `PixelFlow.Studio.exe` and `PixelFlow.Runner.exe` via `Set-AuthenticodeSignature`, verifies a signer is present, then removes the test cert from the store. SmartScreen will **not** trust this; it only proves the pipeline can attach signatures.

### Production / test PFX (secrets — do not commit)

1. Obtain an Authenticode code-signing certificate from a trusted CA (or an org test CA).
2. Store the `.pfx` outside the repo (CI secret store / secure share). `.gitignore` already excludes `*.pfx` / `*.p12` / `*.key`.
3. Sign:

```powershell
$env:PIXELFLOW_SIGN_PFX_PASSWORD = "<password>"   # prefer secret store injection
.\scripts\sign.ps1 -CertificatePath "C:\secure\PixelFlow-codesign.pfx"
# or: -CertificatePassword (SecureString)
```

4. Prefer a timestamp server (script tries DigiCert; falls back to no timestamp if offline).
5. Verify on a clean machine: `Get-AuthenticodeSignature` Status should be `Valid` with a trusted publisher chain.

### Suggested CI secrets (placeholders)

| Secret / variable | Purpose |
|---|---|
| `PIXELFLOW_SIGN_PFX` | Base64 of the .pfx (or path from a secured artifact store) |
| `PIXELFLOW_SIGN_PFX_PASSWORD` | PFX password |
| _(optional)_ `PIXELFLOW_SIGN_TIMESTAMPER` | Override timestamp URL |

Do **not** put production cert material in the repository or in unsigned PR artifacts.

## uiAccess prerequisites (documented only — not enabled in v1)

Architecture Section 2 defers elevated/`uiAccess` automation. Enabling `uiAccess="true"` in an application manifest is **not** a v1 product feature. Prerequisites if pursued later:

1. **Authenticode signature** by a certificate whose root is trusted on the machine.
2. **Install under a secure location** (typically `Program Files`), not `%LocalAppData%`.
3. Group Policy **“Only elevate UIAccess applications that are installed in secure locations”** remains satisfied (enabled by default).
4. Explicit product decision to ship elevated automation; manifests today keep `uiAccess` unset/false.

Until those are met, a standard-integrity PixelFlow process cannot drive elevated UAC/secure-desktop targets (by Windows design). Fail-fast privilege detection remains the correct v1 behavior.

## Release checklist (groundwork)

1. `dotnet build PixelFlow.slnx -c Release`
2. `.\scripts\scan-vulnerabilities.ps1` — must exit 0
3. `.\scripts\pack.ps1 -Configuration Release`
4. `.\scripts\sign.ps1` (dry-run) or PFX sign when secrets available
5. `.\scripts\verify-packaging.ps1` on a clean profile/folder (unsigned path is enough for P31; re-pack after sign if shipping signed bits)
6. Full test suite locally: `Category!=Live` then `Category=Live`
7. Attach `artifacts/package/*.zip` + scan log to the release notes

## Intentionally not done in P32

- Shipping `uiAccess`-enabled binaries
- Store / EV certificate purchase
- Automated notarization beyond Authenticode timestamping
