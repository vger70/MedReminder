<#
.SYNOPSIS
    Orchestra la build completa dell'installer di MedReminder:
    publish self-contained → firma exe → build MSI + MSIX → firma
    installer → sposta output in dist/.

.DESCRIPTION
    Deve girare su Windows con .NET 10 SDK + Windows SDK installati.
    Non gira su Linux/macOS (dipende da MakeAppx.exe, signtool.exe,
    WiX toolset).

    Ordine delle operazioni:
      1. dotnet publish win-x64-self-contained (SC single-file)
      2. Firma MedReminder.exe (se cert configurato)
      3. Build MSI via wixproj
      4. Firma .msi
      5. Build MSIX via MakeAppx pack
      6. Firma .msix
      7. Copia gli output in dist\<versione>\

    La firma è opzionale: se non passi nessun parametro cert la build
    procede senza firma (utile per test locali della catena).

.PARAMETER Version
    Versione da usare (default: legge da Directory.Build.props).

.PARAMETER SkipPublish
    Salta il dotnet publish (usa la publish esistente).

.PARAMETER SkipMsi
    Non costruire l'.msi.

.PARAMETER SkipMsix
    Non costruire l'.msix.

.PARAMETER CertificateThumbprint
    Thumbprint del cert. Se omesso, la firma è SALTATA.

.PARAMETER CertificatePath / CertificatePassword
    Alternativa a Thumbprint: .pfx + password.

.EXAMPLE
    # Build senza firma (test locale)
    .\build-installer.ps1

    # Build firmata con cert self-signed di dev
    .\build-installer.ps1 -CertificateThumbprint ABC123...

    # Build firmata con .pfx da CI
    $pwd = ConvertTo-SecureString $env:SIGN_PFX_PASSWORD -AsPlainText -Force
    .\build-installer.ps1 -CertificatePath cert.pfx -CertificatePassword $pwd
#>
[CmdletBinding()]
param(
    [string]$Version,
    [switch]$SkipPublish,
    [switch]$SkipMsi,
    [switch]$SkipMsix,
    [string]$CertificateThumbprint,
    [string]$CertificatePath,
    [SecureString]$CertificatePassword
)

$ErrorActionPreference = 'Stop'

$RepoRoot   = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$UIProj     = Join-Path $RepoRoot 'src\MedReminder.UI\MedReminder.UI.csproj'
$WixProj    = Join-Path $RepoRoot 'packaging\wix\MedReminder.wixproj'
$MsixDir    = Join-Path $RepoRoot 'packaging\msix'
$MsixMap    = Join-Path $MsixDir 'MedReminder.mapping.txt'
$AssetsDir  = Join-Path $MsixDir 'Assets'
$DistBase   = Join-Path $RepoRoot 'dist'

# ---- Version dedotta da Directory.Build.props se non passata --------
if (-not $Version) {
    $propsFile = Join-Path $RepoRoot 'Directory.Build.props'
    $propsXml  = [xml](Get-Content $propsFile)
    $Version   = ($propsXml.Project.PropertyGroup | Where-Object { $_.Label -eq 'Versioning' }).VersionPrefix
    if (-not $Version) { throw "Impossibile leggere VersionPrefix da $propsFile" }
}
Write-Host "=== MedReminder Installer Build — versione $Version ===" -ForegroundColor Green

$PublishDir = Join-Path $RepoRoot 'src\MedReminder.UI\bin\Release\net10.0-windows10.0.19041.0\publish\win-x64-sc'
$DistVer    = Join-Path $DistBase $Version
New-Item -ItemType Directory -Path $DistVer -Force | Out-Null

# ---- 1. Publish -----------------------------------------------------
if (-not $SkipPublish) {
    Write-Host ""
    Write-Host "[1/6] dotnet publish (self-contained, single-file)..." -ForegroundColor Cyan
    if (Test-Path $PublishDir) { Remove-Item $PublishDir -Recurse -Force }
    dotnet publish $UIProj -c Release `
        /p:PublishProfile=win-x64-self-contained `
        /p:Version=$Version `
        /p:AssemblyVersion="$Version.0" `
        /p:FileVersion="$Version.0"
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish fallito." }
}
else {
    Write-Host "[1/6] Publish SALTATA (uso l'esistente in $PublishDir)" -ForegroundColor Yellow
}

if (-not (Test-Path (Join-Path $PublishDir 'MedReminder.exe'))) {
    throw "Publish output non trovato: $PublishDir\MedReminder.exe"
}

# ---- Wrapper firma -------------------------------------------------
$signScript = Join-Path $PSScriptRoot 'sign-artifact.ps1'
$signParams = @{}
$signEnabled = $false
if ($CertificateThumbprint) {
    $signParams['CertificateThumbprint'] = $CertificateThumbprint
    $signEnabled = $true
}
elseif ($CertificatePath) {
    $signParams['CertificatePath']     = $CertificatePath
    $signParams['CertificatePassword'] = $CertificatePassword
    $signEnabled = $true
}

function Invoke-Sign([string[]]$Files) {
    if (-not $signEnabled) {
        Write-Host "  (firma saltata: nessun cert configurato)" -ForegroundColor DarkYellow
        return
    }
    & $signScript -Files $Files @signParams
}

# ---- 2. Firma exe --------------------------------------------------
Write-Host ""
Write-Host "[2/6] Firma MedReminder.exe..." -ForegroundColor Cyan
Invoke-Sign @((Join-Path $PublishDir 'MedReminder.exe'))

# ---- 3. Build MSI --------------------------------------------------
if (-not $SkipMsi) {
    Write-Host ""
    Write-Host "[3/6] Build MSI (WiX v5)..." -ForegroundColor Cyan
    dotnet build $WixProj -c Release `
        /p:PublishDir=$PublishDir `
        /p:ProductVersion=$Version
    if ($LASTEXITCODE -ne 0) { throw "wix build fallita." }

    $msiOut = Join-Path $RepoRoot "packaging\wix\bin\Release\MedReminder-$Version-x64.msi"
    if (-not (Test-Path $msiOut)) { throw "MSI non generato: $msiOut" }

    Write-Host "[4/6] Firma MSI..." -ForegroundColor Cyan
    Invoke-Sign @($msiOut)

    Copy-Item $msiOut $DistVer -Force
}
else {
    Write-Host "[3-4/6] MSI SALTATO." -ForegroundColor Yellow
}

# ---- 5. Build MSIX -------------------------------------------------
if (-not $SkipMsix) {
    Write-Host ""
    Write-Host "[5/6] Build MSIX..." -ForegroundColor Cyan

    # Verifica asset presenti
    $requiredAssets = @('Square44x44Logo.png','Square71x71Logo.png',
        'Square150x150Logo.png','Square310x310Logo.png','Wide310x150Logo.png',
        'StoreLogo.png','SplashScreen.png')
    $missing = $requiredAssets | Where-Object { -not (Test-Path (Join-Path $AssetsDir $_)) }
    if ($missing) {
        throw "Asset MSIX mancanti in $AssetsDir : $($missing -join ', '). Vedi packaging/msix/Assets/README.md per generarli."
    }

    # Prepara mapping file con path sostituiti
    $mapContent = Get-Content $MsixMap -Raw
    $mapContent = $mapContent.Replace('{PUBLISH_DIR}', $PublishDir)
    $mapContent = $mapContent.Replace('{PROJECT_DIR}', $MsixDir)
    $mapContent = $mapContent.Replace('{ASSETS_DIR}', $AssetsDir)
    $tempMap    = Join-Path $env:TEMP "MedReminder.mapping.$Version.txt"
    Set-Content -Path $tempMap -Value $mapContent -Encoding UTF8

    # Aggiorna la Version nel manifest
    $manifestSrc = Join-Path $MsixDir 'Package.appxmanifest'
    $manifestTmp = Join-Path $env:TEMP "AppxManifest.$Version.xml"
    $mxml = [xml](Get-Content $manifestSrc)
    $mxml.Package.Identity.Version = "$Version.0"
    $mxml.Save($manifestTmp)

    # Aggiorna il mapping per puntare al manifest temporaneo
    (Get-Content $tempMap) -replace [regex]::Escape((Join-Path $MsixDir 'Package.appxmanifest')), $manifestTmp | Set-Content $tempMap

    # Cerca MakeAppx.exe
    $makeAppx = Get-Command MakeAppx.exe -ErrorAction SilentlyContinue
    if (-not $makeAppx) {
        $candidates = @(
            "${env:ProgramFiles(x86)}\Windows Kits\10\bin\10.0.26100.0\x64\MakeAppx.exe",
            "${env:ProgramFiles(x86)}\Windows Kits\10\bin\10.0.22621.0\x64\MakeAppx.exe",
            "${env:ProgramFiles(x86)}\Windows Kits\10\bin\10.0.19041.0\x64\MakeAppx.exe"
        )
        $makeAppx = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
        if (-not $makeAppx) { throw "MakeAppx.exe non trovato — installa Windows SDK." }
    } else { $makeAppx = $makeAppx.Source }

    $msixOut = Join-Path $DistVer "MedReminder-$Version-x64.msix"
    & $makeAppx pack /f $tempMap /p $msixOut /overwrite /verbose
    if ($LASTEXITCODE -ne 0) { throw "MakeAppx pack fallito." }

    Write-Host "[6/6] Firma MSIX..." -ForegroundColor Cyan
    Invoke-Sign @($msixOut)
}
else {
    Write-Host "[5-6/6] MSIX SALTATO." -ForegroundColor Yellow
}

# ---- Riepilogo -----------------------------------------------------
Write-Host ""
Write-Host "=== BUILD COMPLETATA ===" -ForegroundColor Green
Write-Host "Output in: $DistVer"
Get-ChildItem $DistVer | Format-Table Name, Length, LastWriteTime
