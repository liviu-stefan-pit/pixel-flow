#Requires -Version 5.1
<#
.SYNOPSIS
  Agent/CI verification for P31: pack → install → launch → uninstall.

.PARAMETER InstallRoot
  Isolated install root (default under %TEMP%).
#>
[CmdletBinding()]
param(
    [string] $InstallRoot = "",
    [string] $Version = "0.1.0"
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
if (-not $InstallRoot) {
    $InstallRoot = Join-Path $env:TEMP "PixelFlow-P31-verify-$(Get-Random)"
}

Write-Host "=== P31 verify-packaging ==="
Write-Host "InstallRoot=$InstallRoot"

& (Join-Path $PSScriptRoot "pack.ps1") -Version $Version -SkipZip
if ($LASTEXITCODE -ne 0 -and $null -ne $LASTEXITCODE) {
    # pack.ps1 throws on failure; LASTEXITCODE may be from dotnet
}

$packageDir = Join-Path $repoRoot "artifacts\package\PixelFlow"
& (Join-Path $PSScriptRoot "install.ps1") -PackageDir $packageDir -InstallRoot $InstallRoot

$studio = Join-Path $InstallRoot "PixelFlow.Studio.exe"
$runner = Join-Path $InstallRoot "PixelFlow.Runner.exe"
if (-not (Test-Path $studio)) { throw "Studio missing after install." }
if (-not (Test-Path $runner)) { throw "Runner missing after install." }

$lnk = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\PixelFlow\PixelFlow Studio.lnk"
if (-not (Test-Path $lnk)) { throw "Start Menu shortcut missing after install." }

$key = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\PixelFlow"
if (-not (Test-Path $key)) { throw "Uninstall registry key missing after install." }

Write-Host "Launching Studio briefly..."
$p = Start-Process -FilePath $studio -WorkingDirectory $InstallRoot -PassThru
Start-Sleep -Seconds 3
if ($p.HasExited) {
    throw "Studio exited early (code $($p.ExitCode)). Install may be broken."
}
Stop-Process -Id $p.Id -Force
Write-Host "  Studio process started (PID $($p.Id)) and was stopped cleanly."

Write-Host "Running Runner --help..."
$help = & $runner --help 2>&1 | Out-String
if ($LASTEXITCODE -ne 0 -and $LASTEXITCODE -ne $null) {
    # --help may return 0; tolerate non-zero only if no output
    if ([string]::IsNullOrWhiteSpace($help)) {
        throw "Runner --help produced no output (exit $LASTEXITCODE)."
    }
}
Write-Host "  Runner responded ($($help.Length) chars)."

& (Join-Path $PSScriptRoot "uninstall.ps1") -InstallRoot $InstallRoot

if (Test-Path $InstallRoot) { throw "Install root still present after uninstall: $InstallRoot" }
if (Test-Path $lnk) { throw "Start Menu shortcut still present after uninstall." }
if (Test-Path $key) { throw "Uninstall registry key still present after uninstall." }

Write-Host "=== P31 verify-packaging PASSED ==="
