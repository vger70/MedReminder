<#
.SYNOPSIS
    Genera un certificato self-signed per firmare build di sviluppo di
    MedReminder (MSI + MSIX + exe).

.DESCRIPTION
    Crea un certificato code-signing nello store CurrentUser\My. Il
    Subject deve corrispondere ESATTAMENTE al Publisher dichiarato in
    Package.appxmanifest (altrimenti il pacchetto MSIX non installa).

    Il certificato viene ANCHE esportato come .pfx (per usarlo da CI)
    e .cer (per import nel Trusted Root della macchina di test).

.PARAMETER Subject
    Il Subject X.500 del cert. Default: quello del manifest MSIX di dev.

.PARAMETER Password
    Password del .pfx esportato. NON committare mai.
    Default: prompt interattivo.

.PARAMETER OutDir
    Cartella dove salvare .pfx e .cer. Default: cartella dello script.

.EXAMPLE
    .\make-selfsigned-cert.ps1
    .\make-selfsigned-cert.ps1 -OutDir C:\temp\certs

.NOTES
    ATTENZIONE — self-signed:
      - SmartScreen segnalerà l'app come "sconosciuta".
      - Per installare un MSIX firmato self-signed, l'utente finale
        DEVE prima importare il .cer nel Trusted Root Certification
        Authorities del computer di destinazione (comando manuale o
        via GPO). L'MSI invece si installa comunque, ma il warning
        SmartScreen resta.
      - Uso in produzione: SCONSIGLIATO. Acquistare un cert OV/EV.
#>
[CmdletBinding()]
param(
    [string]$Subject = "CN=vger70 Development, O=vger70, C=IT",
    [SecureString]$Password,
    [string]$OutDir = $PSScriptRoot
)

$ErrorActionPreference = 'Stop'

if (-not $Password) {
    $Password = Read-Host "Password per il .pfx esportato" -AsSecureString
}

Write-Host "Generazione certificato self-signed..." -ForegroundColor Cyan
Write-Host "  Subject : $Subject"
Write-Host "  Validità: 3 anni"

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

Write-Host "Certificato creato:" -ForegroundColor Green
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
Write-Host "Esportato:" -ForegroundColor Green
Write-Host "  PFX (con chiave privata, per firmare): $pfxPath"
Write-Host "  CER (chiave pubblica, per Trusted Root utenti): $cerPath"
Write-Host ""
Write-Host "PROSSIMI PASSI:" -ForegroundColor Yellow
Write-Host "  1. Aggiungi *.pfx a .gitignore (già presente)."
Write-Host "  2. Per firmare le build passa a sign-artifact.ps1:"
Write-Host "       -CertificateThumbprint $($cert.Thumbprint)"
Write-Host "     oppure:"
Write-Host "       -CertificatePath ""$pfxPath"" -CertificatePassword <password>"
Write-Host "  3. Sulla macchina di test importa il .cer nel Trusted Root:"
Write-Host "       certutil -addstore -f Root ""$cerPath"""
Write-Host "     (richiede admin)"
