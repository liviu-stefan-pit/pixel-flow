#Requires -Version 5.1
<#
.SYNOPSIS
  Scans solution NuGet dependencies for known vulnerabilities (P32).

.DESCRIPTION
  Runs `dotnet list package --vulnerable --include-transitive` for each project and
  fails with exit code 2 if any project reports vulnerable packages.
#>
[CmdletBinding()]
param(
    [ValidateSet("Any", "Severe")]
    [string] $FailOn = "Any"
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
Push-Location $repoRoot
try {
    $projects = @(
        "src\PixelFlow.Core\PixelFlow.Core.csproj",
        "src\PixelFlow.Runner\PixelFlow.Runner.csproj",
        "src\PixelFlow.Studio\PixelFlow.Studio.csproj",
        "src\PixelFlow.TestBench\PixelFlow.TestBench.csproj",
        "tests\PixelFlow.Core.Tests\PixelFlow.Core.Tests.csproj",
        "tests\PixelFlow.Studio.Tests\PixelFlow.Studio.Tests.csproj",
        "tests\PixelFlow.Integration.Tests\PixelFlow.Integration.Tests.csproj"
    )

    $hitProjects = @()
    $combined = New-Object System.Text.StringBuilder

    foreach ($proj in $projects) {
        Write-Host "=== Vulnerable packages: $proj ==="
        $output = & dotnet list $proj package --vulnerable --include-transitive 2>&1 | Out-String
        Write-Host $output
        [void]$combined.AppendLine($output)

        if ($output -match "has the following vulnerable packages") {
            $hitProjects += $proj
        }
    }

    $text = $combined.ToString()
    $severe = $text -match "(?i)\bCritical\b" -or $text -match "(?i)Severity\s*:\s*High"

    if ($hitProjects.Count -eq 0) {
        Write-Host "Vulnerability scan OK (no vulnerable packages reported)."
        exit 0
    }

    if ($FailOn -eq "Severe" -and -not $severe) {
        Write-Host "Vulnerable packages reported but none Critical/High; -FailOn Severe allows pass."
        Write-Host ($hitProjects -join ", ")
        exit 0
    }

    Write-Host "Vulnerable package(s) in: $($hitProjects -join ', ')"
    Write-Error "Vulnerability scan failed: one or more vulnerable packages reported."
    exit 2
}
finally {
    Pop-Location
}
