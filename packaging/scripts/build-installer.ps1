<#
.SYNOPSIS
    Orchestrates the full MedReminder installer build:
    self-contained publish → sign exe → build MSI + MSIX → sign
    installers → move output into dist/.

.DESCRIPTION
    Must run on Windows with the .NET 10 SDK + Windows SDK
    installed. Does not run on Linux / macOS (depends on
    MakeAppx.exe, signtool.exe, WiX toolset).

    Order of operations:
      1. dotnet publish win-x64-self-contained (SC single-file)
      2. Sign MedReminder.exe (if a cert is configured)
      3. Build MSI via wixproj
      4. Sign the .msi
      5. Build MSIX via MakeAppx pack
      6. Sign the .msix
      7. Copy the outputs into dist\<version>\

    Signing is optional: if you pass no cert parameter the build
    proceeds unsigned (useful for local testing of the chain).

.PARAMETER Version
    Version to use (default: reads Directory.Build.props).

.PARAMETER SkipPublish
    Skip dotnet publish (use the existing publish output).

.PARAMETER SkipMsi
    Do not build the .msi.

.PARAMETER SkipMsix
    Do not build the .msix.

.PARAMETER CertificateThumbprint
    Cert thumbprint. If omitted, signing is SKIPPED.

.PARAMETER CertificatePath / CertificatePassword
    Alternative to Thumbprint: .pfx + password.

.EXAMPLE
    # Unsigned build (local testing)
    .\build-installer.ps1

    # Signed build with a dev self-signed cert
    .\build-installer.ps1 -CertificateThumbprint ABC123...

    # Signed build with a .pfx from CI
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

# ---- Version inferred from Directory.Build.props if not passed -----
if (-not $Version) {
    $propsFile = Join-Path $RepoRoot 'Directory.Build.props'
    $propsXml  = [xml](Get-Content $propsFile)
    $Version   = ($propsXml.Project.PropertyGroup | Where-Object { $_.Label -eq 'Versioning' }).VersionPrefix
    if (-not $Version) { throw "Unable to read VersionPrefix from $propsFile" }
}
Write-Host "=== MedReminder Installer Build — version $Version ===" -ForegroundColor Green

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
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }
}
else {
    Write-Host "[1/6] Publish SKIPPED (using existing in $PublishDir)" -ForegroundColor Yellow
}

if (-not (Test-Path (Join-Path $PublishDir 'MedReminder.exe'))) {
    throw "Publish output not found: $PublishDir\MedReminder.exe"
}

# ---- Signing wrapper -----------------------------------------------
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
        Write-Host "  (signing skipped: no cert configured)" -ForegroundColor DarkYellow
        return
    }
    & $signScript -Files $Files @signParams
}

# ---- 2. Sign exe ---------------------------------------------------
Write-Host ""
Write-Host "[2/6] Sign MedReminder.exe..." -ForegroundColor Cyan
Invoke-Sign @((Join-Path $PublishDir 'MedReminder.exe'))

# ---- 3. Build MSI --------------------------------------------------
if (-not $SkipMsi) {
    Write-Host ""
    Write-Host "[3/6] Build MSI (WiX v5)..." -ForegroundColor Cyan
    dotnet build $WixProj -c Release `
        /p:PublishDir=$PublishDir `
        /p:ProductVersion=$Version
    if ($LASTEXITCODE -ne 0) { throw "wix build failed." }

    $msiOut = Join-Path $RepoRoot "packaging\wix\bin\Release\MedReminder-$Version-x64.msi"
    if (-not (Test-Path $msiOut)) { throw "MSI not generated: $msiOut" }

    Write-Host "[4/6] Sign MSI..." -ForegroundColor Cyan
    Invoke-Sign @($msiOut)

    Copy-Item $msiOut $DistVer -Force
}
else {
    Write-Host "[3-4/6] MSI SKIPPED." -ForegroundColor Yellow
}

# ---- 5. Build MSIX -------------------------------------------------
if (-not $SkipMsix) {
    Write-Host ""
    Write-Host "[5/6] Build MSIX..." -ForegroundColor Cyan

    # Verify that the assets are present
    $requiredAssets = @('Square44x44Logo.png','Square71x71Logo.png',
        'Square150x150Logo.png','Square310x310Logo.png','Wide310x150Logo.png',
        'StoreLogo.png','SplashScreen.png')
    $missing = $requiredAssets | Where-Object { -not (Test-Path (Join-Path $AssetsDir $_)) }
    if ($missing) {
        throw "MSIX assets missing in $AssetsDir : $($missing -join ', '). See packaging/msix/Assets/README.md to generate them."
    }

    # Prepare the mapping file with substituted paths
    $mapContent = Get-Content $MsixMap -Raw
    $mapContent = $mapContent.Replace('{PUBLISH_DIR}', $PublishDir)
    $mapContent = $mapContent.Replace('{PROJECT_DIR}', $MsixDir)
    $mapContent = $mapContent.Replace('{ASSETS_DIR}', $AssetsDir)
    $tempMap    = Join-Path $env:TEMP "MedReminder.mapping.$Version.txt"
    Set-Content -Path $tempMap -Value $mapContent -Encoding UTF8

    # Update the Version in the manifest
    $manifestSrc = Join-Path $MsixDir 'Package.appxmanifest'
    $manifestTmp = Join-Path $env:TEMP "AppxManifest.$Version.xml"
    $mxml = [xml](Get-Content $manifestSrc)
    $mxml.Package.Identity.Version = "$Version.0"
    $mxml.Save($manifestTmp)

    # Update the mapping to point at the temporary manifest
    (Get-Content $tempMap) -replace [regex]::Escape((Join-Path $MsixDir 'Package.appxmanifest')), $manifestTmp | Set-Content $tempMap

    # Look up MakeAppx.exe
    $makeAppx = Get-Command MakeAppx.exe -ErrorAction SilentlyContinue
    if (-not $makeAppx) {
        $candidates = @(
            "${env:ProgramFiles(x86)}\Windows Kits\10\bin\10.0.26100.0\x64\MakeAppx.exe",
            "${env:ProgramFiles(x86)}\Windows Kits\10\bin\10.0.22621.0\x64\MakeAppx.exe",
            "${env:ProgramFiles(x86)}\Windows Kits\10\bin\10.0.19041.0\x64\MakeAppx.exe"
        )
        $makeAppx = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
        if (-not $makeAppx) { throw "MakeAppx.exe not found — install the Windows SDK." }
    } else { $makeAppx = $makeAppx.Source }

    $msixOut = Join-Path $DistVer "MedReminder-$Version-x64.msix"
    & $makeAppx pack /f $tempMap /p $msixOut /overwrite /verbose
    if ($LASTEXITCODE -ne 0) { throw "MakeAppx pack failed." }

    Write-Host "[6/6] Sign MSIX..." -ForegroundColor Cyan
    Invoke-Sign @($msixOut)
}
else {
    Write-Host "[5-6/6] MSIX SKIPPED." -ForegroundColor Yellow
}

# ---- Summary -------------------------------------------------------
Write-Host ""
Write-Host "=== BUILD COMPLETED ===" -ForegroundColor Green
Write-Host "Output in: $DistVer"
Get-ChildItem $DistVer | Format-Table Name, Length, LastWriteTime
