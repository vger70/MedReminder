<#
.SYNOPSIS
    Firma uno o più file (exe/dll/msi/msix/appx) con signtool.exe.

.DESCRIPTION
    Wrapper su signtool.exe con SHA256 + timestamp RFC 3161. Supporta
    due modalità:
      1) Certificato nello store Windows: passa -CertificateThumbprint
      2) File .pfx: passa -CertificatePath + -CertificatePassword

    Il timestamp è OBBLIGATORIO: senza, la firma scade con il certificato
    (e Windows la rigetta). Il server di timestamp default è il free
    endpoint DigiCert; sostituibile via -TimestampUrl.

.PARAMETER Files
    Uno o più path di file da firmare (accetta wildcard).

.PARAMETER CertificateThumbprint
    Thumbprint (SHA-1, senza spazi) del certificato in
    Cert:\CurrentUser\My o Cert:\LocalMachine\My.

.PARAMETER CertificatePath
    Path al file .pfx.

.PARAMETER CertificatePassword
    SecureString con la password del .pfx.

.PARAMETER TimestampUrl
    URL del timestamp server RFC 3161. Default:
    http://timestamp.digicert.com

.PARAMETER Description
    Descrizione mostrata nel dialog UAC/SmartScreen. Default: MedReminder.

.PARAMETER SignToolPath
    Path a signtool.exe. Se omesso, cerca in PATH e nei percorsi
    tipici del Windows SDK (10.0.22621.0, 10.0.26100.0).

.EXAMPLE
    # Firma con self-signed generato da make-selfsigned-cert.ps1
    .\sign-artifact.ps1 -Files dist\*.msi -CertificateThumbprint ABC123...

.EXAMPLE
    # Firma da CI con .pfx (variabile d'ambiente per la password)
    $pwd = ConvertTo-SecureString $env:SIGN_PFX_PASSWORD -AsPlainText -Force
    .\sign-artifact.ps1 -Files dist\MedReminder-1.0.1-x64.msix `
        -CertificatePath cert.pfx -CertificatePassword $pwd

.NOTES
    Verifica dopo la firma con:
      signtool.exe verify /pa /v <file>
    Deve mostrare "Successfully verified" e includere una linea
    "The signature is timestamped".
#>
[CmdletBinding(DefaultParameterSetName = 'Thumbprint')]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string[]]$Files,

    [Parameter(Mandatory = $true, ParameterSetName = 'Thumbprint')]
    [string]$CertificateThumbprint,

    [Parameter(Mandatory = $true, ParameterSetName = 'Pfx')]
    [string]$CertificatePath,

    [Parameter(Mandatory = $true, ParameterSetName = 'Pfx')]
    [SecureString]$CertificatePassword,

    [string]$TimestampUrl = "http://timestamp.digicert.com",
    [string]$Description = "MedReminder",
    [string]$SignToolPath
)

$ErrorActionPreference = 'Stop'

function Find-SignTool {
    param([string]$Explicit)
    if ($Explicit) {
        if (Test-Path $Explicit) { return $Explicit }
        throw "SignTool non trovato al path: $Explicit"
    }
    $cmd = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }

    $candidates = @(
        "${env:ProgramFiles(x86)}\Windows Kits\10\bin\10.0.26100.0\x64\signtool.exe",
        "${env:ProgramFiles(x86)}\Windows Kits\10\bin\10.0.22621.0\x64\signtool.exe",
        "${env:ProgramFiles(x86)}\Windows Kits\10\bin\10.0.22000.0\x64\signtool.exe",
        "${env:ProgramFiles(x86)}\Windows Kits\10\bin\10.0.19041.0\x64\signtool.exe"
    )
    foreach ($c in $candidates) {
        if (Test-Path $c) { return $c }
    }
    throw "signtool.exe non trovato. Installa Windows SDK o passa -SignToolPath."
}

$signtool = Find-SignTool -Explicit $SignToolPath
Write-Host "SignTool: $signtool" -ForegroundColor Cyan
Write-Host "Timestamp: $TimestampUrl" -ForegroundColor Cyan

$resolvedFiles = @()
foreach ($pattern in $Files) {
    $matches = Get-ChildItem -Path $pattern -File -ErrorAction SilentlyContinue
    if (-not $matches) {
        Write-Warning "Nessun file trovato per: $pattern"
        continue
    }
    $resolvedFiles += $matches.FullName
}

if ($resolvedFiles.Count -eq 0) {
    throw "Nessun file da firmare."
}

# Costruisce gli argomenti signtool
$args = @(
    'sign',
    '/fd', 'SHA256',
    '/td', 'SHA256',
    '/tr', $TimestampUrl,
    '/d', $Description,
    '/v'
)

if ($PSCmdlet.ParameterSetName -eq 'Thumbprint') {
    # signtool cerca in CurrentUser\My di default; se il cert sta in
    # LocalMachine\My aggiungi /sm come argomento extra a signtool.
    Write-Host "Firma con certificato in store, thumbprint: $CertificateThumbprint" -ForegroundColor Cyan
    $args += @('/sha1', $CertificateThumbprint)
}
else {
    Write-Host "Firma con .pfx: $CertificatePath" -ForegroundColor Cyan
    if (-not (Test-Path $CertificatePath)) {
        throw "PFX non trovato: $CertificatePath"
    }
    $plainPwd = [Runtime.InteropServices.Marshal]::PtrToStringAuto(
        [Runtime.InteropServices.Marshal]::SecureStringToBSTR($CertificatePassword))
    $args += @('/f', $CertificatePath, '/p', $plainPwd)
}

$args += $resolvedFiles

Write-Host ""
Write-Host "Firma in corso ($($resolvedFiles.Count) file)..." -ForegroundColor Yellow
& $signtool @args
if ($LASTEXITCODE -ne 0) {
    throw "signtool sign fallito con exit code $LASTEXITCODE"
}

Write-Host ""
Write-Host "Verifica firma..." -ForegroundColor Yellow
foreach ($f in $resolvedFiles) {
    Write-Host ""
    Write-Host "  $f" -ForegroundColor White
    & $signtool verify /pa /v $f
    if ($LASTEXITCODE -ne 0) {
        throw "Verifica firma fallita per: $f"
    }
}

Write-Host ""
Write-Host "OK — tutti i file firmati e verificati." -ForegroundColor Green
