#Requires -Version 5.1
<#
.SYNOPSIS
  Installs an unsigned PixelFlow package for the current user.

.DESCRIPTION
  Copies the published payload to %LocalAppData%\Programs\PixelFlow (or -InstallRoot),
  creates a Start Menu shortcut, and registers an HKCU Apps & Features uninstall entry.
  Re-running over an existing install is the supported update path (replace in place).

.PARAMETER PackageDir
  Folder containing PixelFlow.Studio.exe (default: <repo>/artifacts/package/PixelFlow).

.PARAMETER InstallRoot
  Destination directory (default: %LocalAppData%\Programs\PixelFlow).
#>
[CmdletBinding()]
param(
    [string] $PackageDir = "",
    [string] $InstallRoot = ""
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
if (-not $PackageDir) {
    $PackageDir = Join-Path $repoRoot "artifacts\package\PixelFlow"
}
$PackageDir = (Resolve-Path $PackageDir).Path

if (-not $InstallRoot) {
    $InstallRoot = Join-Path $env:LOCALAPPDATA "Programs\PixelFlow"
}

$studioSrc = Join-Path $PackageDir "PixelFlow.Studio.exe"
$runnerSrc = Join-Path $PackageDir "PixelFlow.Runner.exe"
if (-not (Test-Path $studioSrc)) { throw "Package missing PixelFlow.Studio.exe under $PackageDir. Run scripts\pack.ps1 first." }
if (-not (Test-Path $runnerSrc)) { throw "Package missing PixelFlow.Runner.exe under $PackageDir. Run scripts\pack.ps1 first." }

$version = "0.1.0"
$versionFile = Join-Path $PackageDir "package-version.txt"
if (Test-Path $versionFile) {
    $version = (Get-Content $versionFile -Raw).Trim()
}

Write-Host "Installing PixelFlow $version"
Write-Host "  From: $PackageDir"
Write-Host "  To:   $InstallRoot"

New-Item -ItemType Directory -Path $InstallRoot -Force | Out-Null
Copy-Item -Path (Join-Path $PackageDir "*") -Destination $InstallRoot -Recurse -Force

# Keep an uninstall helper next to the install for Apps & Features
Copy-Item (Join-Path $PSScriptRoot "uninstall.ps1") (Join-Path $InstallRoot "uninstall.ps1") -Force

$startMenuDir = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\PixelFlow"
New-Item -ItemType Directory -Path $startMenuDir -Force | Out-Null
$shortcutPath = Join-Path $startMenuDir "PixelFlow Studio.lnk"
$studioExe = Join-Path $InstallRoot "PixelFlow.Studio.exe"

$wsh = New-Object -ComObject WScript.Shell
$sc = $wsh.CreateShortcut($shortcutPath)
$sc.TargetPath = $studioExe
$sc.WorkingDirectory = $InstallRoot
$sc.Description = "PixelFlow Automation Studio"
$sc.Save()

$uninstallKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\PixelFlow"
New-Item -Path $uninstallKey -Force | Out-Null
$uninstallCmd = "powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$(Join-Path $InstallRoot 'uninstall.ps1')`""
Set-ItemProperty -Path $uninstallKey -Name "DisplayName" -Value "PixelFlow"
Set-ItemProperty -Path $uninstallKey -Name "DisplayVersion" -Value $version
Set-ItemProperty -Path $uninstallKey -Name "Publisher" -Value "PixelFlow"
Set-ItemProperty -Path $uninstallKey -Name "InstallLocation" -Value $InstallRoot
Set-ItemProperty -Path $uninstallKey -Name "UninstallString" -Value $uninstallCmd
Set-ItemProperty -Path $uninstallKey -Name "DisplayIcon" -Value $studioExe
Set-ItemProperty -Path $uninstallKey -Name "NoModify" -Value 1 -Type DWord
Set-ItemProperty -Path $uninstallKey -Name "NoRepair" -Value 1 -Type DWord

Set-Content -Path (Join-Path $InstallRoot "install-manifest.json") -Value (@{
    version       = $version
    installRoot   = $InstallRoot
    packageDir    = $PackageDir
    startMenuLnk  = $shortcutPath
    installedUtc  = [DateTime]::UtcNow.ToString("o")
} | ConvertTo-Json)

Write-Host "Install complete."
Write-Host "  Launch: $studioExe"
Write-Host "  Start Menu: $shortcutPath"
Write-Host "  Uninstall: .\scripts\uninstall.ps1   (or Apps & Features -> PixelFlow)"
