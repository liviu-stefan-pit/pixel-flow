#Requires -Version 5.1
<#
.SYNOPSIS
  Publishes Studio + Runner into a side-by-side unsigned package folder (and optional zip).

.DESCRIPTION
  P31: produces an installable layout under artifacts/package/PixelFlow with both
  PixelFlow.Studio.exe and PixelFlow.Runner.exe in the same directory (Studio resolves
  the Runner from its base directory). No Authenticode signing here — see scripts/sign.ps1 (P32).

.PARAMETER Configuration
  Build configuration (default Release).

.PARAMETER Version
  Package version written to package-version.txt (default 0.1.0).

.PARAMETER OutputRoot
  Root for package output (default <repo>/artifacts/package).

.PARAMETER SelfContained
  If set, publish self-contained win-x64 (larger; no shared runtime required).

.PARAMETER SkipZip
  If set, skip creating the .zip archive.
#>
[CmdletBinding()]
param(
    [string] $Configuration = "Release",
    [string] $Version = "0.1.0",
    [string] $OutputRoot = "",
    [switch] $SelfContained,
    [switch] $SkipZip
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
if (-not $OutputRoot) {
    $OutputRoot = Join-Path $repoRoot "artifacts\package"
}

$payloadDir = Join-Path $OutputRoot "PixelFlow"
$runnerStaging = Join-Path $OutputRoot "_runner-staging"
$zipPath = Join-Path $OutputRoot "PixelFlow-$Version-unsigned-win-x64.zip"

Write-Host "Packaging PixelFlow $Version ($Configuration) -> $payloadDir"

if (Test-Path $payloadDir) {
    Remove-Item -Recurse -Force $payloadDir
}
if (Test-Path $runnerStaging) {
    Remove-Item -Recurse -Force $runnerStaging
}
New-Item -ItemType Directory -Path $payloadDir -Force | Out-Null

$publishArgs = @(
    "-c", $Configuration,
    "-r", "win-x64",
    "--self-contained", $(if ($SelfContained) { "true" } else { "false" }),
    "-p:PublishSingleFile=false",
    "-p:IncludeNativeLibrariesForSelfExtract=true",
    "-p:DebugType=None",
    "-p:DebugSymbols=false"
)

Write-Host "Publishing Studio..."
dotnet publish (Join-Path $repoRoot "src\PixelFlow.Studio\PixelFlow.Studio.csproj") `
    -o $payloadDir @publishArgs
if ($LASTEXITCODE -ne 0) { throw "Studio publish failed (exit $LASTEXITCODE)." }

Write-Host "Publishing Runner..."
dotnet publish (Join-Path $repoRoot "src\PixelFlow.Runner\PixelFlow.Runner.csproj") `
    -o $runnerStaging @publishArgs
if ($LASTEXITCODE -ne 0) { throw "Runner publish failed (exit $LASTEXITCODE)." }

Write-Host "Merging Runner into package payload..."
Copy-Item -Path (Join-Path $runnerStaging "*") -Destination $payloadDir -Recurse -Force
Remove-Item -Recurse -Force $runnerStaging

$studioExe = Join-Path $payloadDir "PixelFlow.Studio.exe"
$runnerExe = Join-Path $payloadDir "PixelFlow.Runner.exe"
if (-not (Test-Path $studioExe)) { throw "Missing $studioExe after publish." }
if (-not (Test-Path $runnerExe)) { throw "Missing $runnerExe after publish." }

Set-Content -Path (Join-Path $payloadDir "package-version.txt") -Value $Version -NoNewline
Set-Content -Path (Join-Path $payloadDir "README-PACKAGE.txt") -Value @"
PixelFlow unsigned package ($Version)

Contents:
  PixelFlow.Studio.exe  — editor shell
  PixelFlow.Runner.exe  — automation worker (must stay beside Studio)

Install / uninstall (from a clone, or copy these scripts next to this folder):
  .\scripts\install.ps1 -PackageDir <this folder>
  .\scripts\uninstall.ps1

Requires: .NET 10 Desktop Runtime (win-x64) unless this package was built -SelfContained.
Signing: not applied. See docs/signing-and-release.md and scripts\sign.ps1.
"@

# Bundle install/uninstall helpers inside the package root's parent for zip convenience
$scriptsOut = Join-Path $OutputRoot "scripts"
New-Item -ItemType Directory -Path $scriptsOut -Force | Out-Null
Copy-Item (Join-Path $PSScriptRoot "install.ps1") $scriptsOut -Force
Copy-Item (Join-Path $PSScriptRoot "uninstall.ps1") $scriptsOut -Force

if (-not $SkipZip) {
    if (Test-Path $zipPath) { Remove-Item -Force $zipPath }
    Write-Host "Creating zip $zipPath ..."
    # Zip payload + scripts so a download is self-contained for install
    $zipStage = Join-Path $OutputRoot "_zip-stage"
    if (Test-Path $zipStage) { Remove-Item -Recurse -Force $zipStage }
    New-Item -ItemType Directory -Path (Join-Path $zipStage "PixelFlow") -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $zipStage "scripts") -Force | Out-Null
    Copy-Item -Path (Join-Path $payloadDir "*") -Destination (Join-Path $zipStage "PixelFlow") -Recurse -Force
    Copy-Item (Join-Path $scriptsOut "install.ps1") (Join-Path $zipStage "scripts") -Force
    Copy-Item (Join-Path $scriptsOut "uninstall.ps1") (Join-Path $zipStage "scripts") -Force
    Compress-Archive -Path (Join-Path $zipStage "*") -DestinationPath $zipPath -Force
    Remove-Item -Recurse -Force $zipStage
}

Write-Host "Pack complete."
Write-Host "  Payload: $payloadDir"
if (-not $SkipZip) { Write-Host "  Zip:     $zipPath" }
