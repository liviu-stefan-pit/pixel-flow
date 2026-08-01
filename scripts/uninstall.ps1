#Requires -Version 5.1
<#
.SYNOPSIS
  Removes a current-user PixelFlow install created by install.ps1.

.PARAMETER InstallRoot
  Install directory (default: %LocalAppData%\Programs\PixelFlow, or from install-manifest.json).
#>
[CmdletBinding()]
param(
    [string] $InstallRoot = ""
)

$ErrorActionPreference = "Stop"

if (-not $InstallRoot) {
    $defaultRoot = Join-Path $env:LOCALAPPDATA "Programs\PixelFlow"
    $manifest = Join-Path $defaultRoot "install-manifest.json"
    if (Test-Path $manifest) {
        $InstallRoot = (Get-Content $manifest -Raw | ConvertFrom-Json).installRoot
    }
    else {
        $InstallRoot = $defaultRoot
    }
}

Write-Host "Uninstalling PixelFlow from $InstallRoot"

# Stop running instances best-effort (do not fail uninstall if none)
foreach ($name in @("PixelFlow.Studio", "PixelFlow.Runner", "PixelFlow.TestBench")) {
    Get-Process -Name $name -ErrorAction SilentlyContinue | ForEach-Object {
        Write-Host "  Stopping $($_.ProcessName) (PID $($_.Id))..."
        Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue
    }
}

Start-Sleep -Milliseconds 300

$startMenuDir = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\PixelFlow"
if (Test-Path $startMenuDir) {
    Remove-Item -Recurse -Force $startMenuDir
    Write-Host "  Removed Start Menu folder."
}

$uninstallKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\PixelFlow"
if (Test-Path $uninstallKey) {
    Remove-Item -Path $uninstallKey -Recurse -Force
    Write-Host "  Removed Apps & Features entry."
}

if (Test-Path $InstallRoot) {
    Remove-Item -Recurse -Force $InstallRoot
    Write-Host "  Removed install directory."
}
else {
    Write-Host "  Install directory already absent."
}

Write-Host "Uninstall complete."
