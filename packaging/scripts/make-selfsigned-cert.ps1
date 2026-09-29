<#
.SYNOPSIS
    Generates a self-signed certificate to sign MedReminder
    development builds (MSI + MSIX + exe).

.DESCRIPTION
    Creates a code-signing certificate in the CurrentUser\My store.
    The Subject must EXACTLY match the Publisher declared in
    Package.appxmanifest (otherwise the MSIX package will not
    install).

    The certificate is ALSO exported as .pfx (for CI use) and as
    .cer (to import into the Trusted Root of the test machine).

.PARAMETER Subject
    The cert's X.500 Subject. Default: the one in the dev MSIX
    manifest.

.PARAMETER Password
    Password of the exported .pfx. Never commit it.
    Default: interactive prompt.

.PARAMETER OutDir
    Folder where .pfx and .cer are saved. Default: the script's
    folder.

.EXAMPLE
    .\make-selfsigned-cert.ps1
    .\make-selfsigned-cert.ps1 -OutDir C:\temp\certs

.NOTES
    WARNING — self-signed:
      - SmartScreen flags the app as "unknown".
      - To install a self-signed MSIX, the end user MUST first
        import the .cer into the Trusted Root Certification
        Authorities of the target machine (manual command or via
        GPO). The MSI installs anyway, but the SmartScreen warning
        remains.
      - Production use: NOT RECOMMENDED. Buy an OV/EV cert.
#>
[CmdletBinding()]
param(
    [string]$Subject = "CN=vger70 Development, O=vger70, C=IT",
    [SecureString]$Password,
    [string]$OutDir = $PSScriptRoot
)

$ErrorActionPreference = 'Stop'

if (-not $Password) {
    $Password = Read-Host "Password for the exported .pfx" -AsSecureString
}

Write-Host "Generating self-signed certificate..." -ForegroundColor Cyan
Write-Host "  Subject : $Subject"
Write-Host "  Validity: 3 years"

$cert = New-SelfSignedCertificate `
    -Type CodeSigningCert `
    -Subject $Subject `
    -KeyUsage DigitalSignature `
    -FriendlyName "MedReminder Dev Signing" `
    -CertStoreLocation "Cert:\CurrentUser\My" `
    -NotAfter (Get-Date).AddYears(3) `
    -TextExtension @(
        "2.5.29.37={text}1.3.6.1.5.5.7.3.3",
        "2.5.29.19={text}"
    )

Write-Host "Certificate created:" -ForegroundColor Green
Write-Host "  Thumbprint: $($cert.Thumbprint)"
Write-Host "  Subject   : $($cert.Subject)"

if (-not (Test-Path $OutDir)) {
    New-Item -ItemType Directory -Path $OutDir -Force | Out-Null
}

$pfxPath = Join-Path $OutDir "MedReminder-Dev.pfx"
$cerPath = Join-Path $OutDir "MedReminder-Dev.cer"

Export-PfxCertificate -Cert $cert -FilePath $pfxPath -Password $Password | Out-Null
Export-Certificate  -Cert $cert -FilePath $cerPath -Type CERT | Out-Null

Write-Host ""
Write-Host "Exported:" -ForegroundColor Green
Write-Host "  PFX (with private key, for signing): $pfxPath"
Write-Host "  CER (public key, for users' Trusted Root): $cerPath"
Write-Host ""
Write-Host "NEXT STEPS:" -ForegroundColor Yellow
Write-Host "  1. Add *.pfx to .gitignore (already present)."
Write-Host "  2. To sign builds, invoke sign-artifact.ps1 with:"
Write-Host "       -CertificateThumbprint $($cert.Thumbprint)"
Write-Host "     or:"
Write-Host "       -CertificatePath ""$pfxPath"" -CertificatePassword <password>"
Write-Host "  3. On the test machine, import the .cer into the Trusted Root:"
Write-Host "       certutil -addstore -f Root ""$cerPath"""
Write-Host "     (requires admin)"
