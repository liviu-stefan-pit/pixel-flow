#Requires -Version 5.1
<#
.SYNOPSIS
  Authenticode-sign published PixelFlow binaries (P32 groundwork).

.DESCRIPTION
  Dry-run (default): creates a short-lived self-signed code-signing cert in CurrentUser\My,
  signs Studio/Runner EXEs via Set-AuthenticodeSignature, verifies signatures, then removes
  the test cert (binaries remain test-signed - not trusted by SmartScreen).

  Production: pass -CertificatePath to a .pfx (password via -CertificatePassword or
  env PIXELFLOW_SIGN_PFX_PASSWORD). Never commit PFX files; see docs/signing-and-release.md.

  Does NOT enable uiAccess in the application manifests.

.PARAMETER PayloadDir
  Folder with PixelFlow.Studio.exe / PixelFlow.Runner.exe
  (default: <repo>/artifacts/package/PixelFlow).

.PARAMETER CertificatePath
  Optional path to a .pfx for real/test signing.

.PARAMETER CertificatePassword
  Password for -CertificatePath (or set PIXELFLOW_SIGN_PFX_PASSWORD).

.PARAMETER KeepTestCertificate
  Keep the generated self-signed cert in the store after dry-run.
#>
[CmdletBinding()]
param(
    [string] $PayloadDir = "",
    [string] $CertificatePath = "",
    [SecureString] $CertificatePassword = $null,
    [switch] $KeepTestCertificate
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
if (-not $PayloadDir) {
    $PayloadDir = Join-Path $repoRoot "artifacts\package\PixelFlow"
}
if (-not (Test-Path $PayloadDir)) {
    throw "Payload not found: $PayloadDir. Run scripts\pack.ps1 first."
}

$targets = @(
    (Join-Path $PayloadDir "PixelFlow.Studio.exe"),
    (Join-Path $PayloadDir "PixelFlow.Runner.exe")
)
foreach ($t in $targets) {
    if (-not (Test-Path $t)) { throw "Missing binary to sign: $t" }
}

$generatedThumbprint = $null
$cert = $null

try {
    if ($CertificatePath) {
        if (-not (Test-Path $CertificatePath)) {
            throw "Certificate file not found: $CertificatePath"
        }
        if (-not $CertificatePassword) {
            $envPw = $env:PIXELFLOW_SIGN_PFX_PASSWORD
            if ([string]::IsNullOrEmpty($envPw)) {
                throw "Provide -CertificatePassword or set PIXELFLOW_SIGN_PFX_PASSWORD."
            }
            $CertificatePassword = ConvertTo-SecureString $envPw -AsPlainText -Force
        }
        Write-Host "Loading certificate from $CertificatePath ..."
        $cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2(
            $CertificatePath,
            $CertificatePassword,
            [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::Exportable
        )
    }
    else {
        Write-Host "Dry-run: creating self-signed code-signing certificate..."
        $cert = New-SelfSignedCertificate `
            -Type CodeSigningCert `
            -Subject "CN=PixelFlow Test Signing (DO NOT SHIP)" `
            -CertStoreLocation "Cert:\CurrentUser\My" `
            -KeyExportPolicy Exportable `
            -NotAfter (Get-Date).AddDays(1)
        $generatedThumbprint = $cert.Thumbprint
        Write-Host "  Thumbprint: $generatedThumbprint"
    }

    foreach ($t in $targets) {
        Write-Host "Signing $t ..."
        $result = Set-AuthenticodeSignature -FilePath $t -Certificate $cert -TimestampServer "http://timestamp.digicert.com" -ErrorAction SilentlyContinue
        if (-not $result -or $result.Status -eq "UnknownError" -or $result.Status -eq "NotTrusted") {
            # Timestamp server may fail offline; retry without timestamp for local dry-run
            Write-Host "  Timestamp unavailable or status $($result.Status); retrying without timestamp..."
            $result = Set-AuthenticodeSignature -FilePath $t -Certificate $cert
        }
        Write-Host "  Status: $($result.Status) ($($result.StatusMessage))"
        if ($result.Status -ne "Valid" -and $result.Status -ne "UnknownError") {
            # Self-signed often reports UnknownError/NotTrusted for trust chain but Hash matches
            $verify = Get-AuthenticodeSignature -FilePath $t
            if ($null -eq $verify.SignerCertificate) {
                throw "Signing failed for $t : $($result.Status) $($result.StatusMessage)"
            }
            Write-Host "  Signer present (thumbprint $($verify.SignerCertificate.Thumbprint)); Status=$($verify.Status)"
        }
    }

    Write-Host "Verifying signatures..."
    foreach ($t in $targets) {
        $sig = Get-AuthenticodeSignature -FilePath $t
        if ($null -eq $sig.SignerCertificate) {
            throw "No signer certificate on $t after signing."
        }
        Write-Host ("  {0}: Status={1}; Subject={2}" -f (Split-Path $t -Leaf), $sig.Status, $sig.SignerCertificate.Subject)
    }

    Write-Host "Sign complete."
    Write-Host "Note: self-signed / untrusted publisher signatures are for pipeline dry-run only."
    Write-Host "uiAccess is intentionally NOT enabled - see docs/signing-and-release.md."
}
finally {
    if ($generatedThumbprint -and -not $KeepTestCertificate) {
        $store = New-Object System.Security.Cryptography.X509Certificates.X509Store(
            [System.Security.Cryptography.X509Certificates.StoreName]::My,
            [System.Security.Cryptography.X509Certificates.StoreLocation]::CurrentUser)
        $store.Open([System.Security.Cryptography.X509Certificates.OpenFlags]::ReadWrite)
        try {
            $matches = $store.Certificates.Find(
                [System.Security.Cryptography.X509Certificates.X509FindType]::FindByThumbprint,
                $generatedThumbprint,
                $false)
            foreach ($c in $matches) {
                $store.Remove($c)
            }
            if ($matches.Count -gt 0) {
                Write-Host "Removed temporary test certificate $generatedThumbprint."
            }
        }
        finally {
            $store.Close()
        }
    }
}
