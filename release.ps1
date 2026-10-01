param(
    [Parameter(Position = 0)]
    [ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+(-[a-zA-Z0-9.-]+)?$')]
    [string]$Version,

    # Build, sign and package locally instead of running the Git release flow.
    [switch]$LocalBuild,

    # SHA-1 thumbprint of the Certum code signing certificate exposed by
    # SimplySign Desktop in Cert:\CurrentUser\My. Falls back to the
    # CERTUM_CERT_THUMBPRINT environment variable.
    [string]$CertificateThumbprint = $env:CERTUM_CERT_THUMBPRINT,

    # RFC 3161 timestamp server. Certum's TSA by default.
    [string]$TimestampUrl = "http://time.certum.pl",

    # Produce an unsigned local build (testing the packaging chain only).
    [switch]$NoSign,

    # Skip both WiX MSI builds (framework-dependent and Microsoft Store).
    [switch]$SkipMsi,

    [Alias("h")]
    [switch]$Help
)

function Show-Usage {
    Write-Host ""
    Write-Host "Release Script" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "USAGE:"
    Write-Host "  .\release.ps1 <version>"
    Write-Host "  .\release.ps1 <version> -LocalBuild [-CertificateThumbprint <sha1>] [-TimestampUrl <url>] [-NoSign] [-SkipMsi]"
    Write-Host "  .\release.ps1 -Help"
    Write-Host ""
    Write-Host "EXAMPLES:"
    Write-Host "  .\release.ps1 1.1.0"
    Write-Host "  .\release.ps1 1.1.0 -LocalBuild -CertificateThumbprint 0123456789ABCDEF0123456789ABCDEF01234567"
    Write-Host "  .\release.ps1 1.1.0 -LocalBuild -NoSign"
    Write-Host ""
    Write-Host "DESCRIPTION:"
    Write-Host "  Default mode runs the Git release procedure (the tag triggers CI):"
    Write-Host "    - update VersionPrefix in Directory.Build.props"
    Write-Host "    - git status / add / commit / push"
    Write-Host "    - git tag v<version> / git push origin v<version>"
    Write-Host ""
    Write-Host "  -LocalBuild runs the same packaging as CI on this machine, without Git:"
    Write-Host "    - publish self-contained and framework-dependent (win-x64)"
    Write-Host "    - sign MedReminder*.exe / MedReminder*.dll with the Certum certificate"
    Write-Host "    - build the MSI from the signed framework-dependent output, sign the MSI"
    Write-Host "    - build the Microsoft Store MSI from the self-contained output, after"
    Write-Host "      signing every PE file still unsigned there, sign the MSI"
    Write-Host "    - create the ZIP packages and SHA-256 checksums in dist\<version>\"
    Write-Host ""
    Write-Host "  Signing requires SimplySign Desktop running and connected, so that the"
    Write-Host "  certificate is visible in Cert:\CurrentUser\My. Every signature is"
    Write-Host "  timestamped (RFC 3161, SHA-256) so it stays valid after the certificate expires."
    Write-Host ""
}

# Show help when requested or when the version is missing
if ($Help -or [string]::IsNullOrWhiteSpace($Version)) {
    Show-Usage
    exit 0
}

function Set-Version {
	$PropsFile = Join-Path $PSScriptRoot "Directory.Build.props"

	if (-not (Test-Path $PropsFile)) {
		Write-Host "Directory.Build.props not found: $PropsFile" -ForegroundColor Red
		exit 1
	}

	Write-Host "==> Updating version in Directory.Build.props" -ForegroundColor Cyan

	[xml]$xml = Get-Content $PropsFile

	$versionPrefixNode = $xml.SelectSingleNode("//VersionPrefix")

	if ($null -eq $versionPrefixNode) {
		Write-Host "<VersionPrefix> element not found" -ForegroundColor Red
		exit 1
	}

	$currentVersion = $versionPrefixNode.InnerText

	if ($currentVersion -ne $Version) {
		$versionPrefixNode.InnerText = $Version
		$xml.Save($PropsFile)

		Write-Host "VersionPrefix updated from $currentVersion to $Version" -ForegroundColor Green
	}
	else {
		Write-Host "VersionPrefix already set to $Version" -ForegroundColor Yellow
	}
}

function Invoke-GitCommand {
    param(
        [string]$Command,
        [string]$Description
    )

    Write-Host "==> $Description" -ForegroundColor Cyan

    Invoke-Expression $Command

    if ($LASTEXITCODE -ne 0) {
        Write-Host "ERROR during: $Description" -ForegroundColor Red
        exit $LASTEXITCODE
    }

    Write-Host "OK: $Description" -ForegroundColor Green
}

function Invoke-GitRelease {
    $CommitMessage = "Prepare release v$Version"
    $TagName = "v$Version"

	Set-Version

    Invoke-GitCommand "git status" "Check repository status"
    Invoke-GitCommand "git add ." "Stage files"

	Write-Host "==> Commit" -ForegroundColor Cyan
    git commit -m "$CommitMessage"

	if ($LASTEXITCODE -eq 0) {
		Write-Host "OK: Commit created" -ForegroundColor Green
	}
	else {
		Write-Host "Nothing to commit, continuing..." -ForegroundColor Yellow
	}

    Invoke-GitCommand "git push" "Push branch"
    Invoke-GitCommand "git tag $TagName" "Create tag"
    Invoke-GitCommand "git push origin $TagName" "Push tag"

    Write-Host ""
    Write-Host "Release $TagName completed successfully." -ForegroundColor Green
}

# ---------------------------------------------------------------------
# Local build: mirrors .github/workflows/dotnet-desktop.yml, plus signing.
# ---------------------------------------------------------------------

function Assert-SigningCertificate {
    if ([string]::IsNullOrWhiteSpace($script:CertificateThumbprint)) {
        throw "No certificate thumbprint. Pass -CertificateThumbprint, set CERTUM_CERT_THUMBPRINT, or use -NoSign."
    }

    $script:CertificateThumbprint = ($script:CertificateThumbprint -replace '\s', '').ToUpperInvariant()
    $cert = Get-Item "Cert:\CurrentUser\My\$($script:CertificateThumbprint)" -ErrorAction SilentlyContinue

    if ($null -eq $cert) {
        throw "Certificate $($script:CertificateThumbprint) not found in Cert:\CurrentUser\My. Start SimplySign Desktop and connect with the token from the SimplySign mobile app."
    }
    if ($cert.NotAfter -lt (Get-Date)) {
        throw "Certificate $($script:CertificateThumbprint) expired on $($cert.NotAfter.ToString('u'))."
    }

    Write-Host "Signing certificate: $($cert.Subject)" -ForegroundColor Green
    Write-Host "  Issuer   : $($cert.Issuer)"
    Write-Host "  Expires  : $($cert.NotAfter)"
    Write-Host "  Timestamp: $TimestampUrl"
}

function Invoke-Sign {
    param([string[]]$Files)

    if ($NoSign) {
        Write-Host "  (signing skipped: -NoSign)" -ForegroundColor DarkYellow
        return
    }

    $signScript = Join-Path $PSScriptRoot "packaging\scripts\sign-artifact.ps1"
    & $signScript -Files $Files `
        -CertificateThumbprint $script:CertificateThumbprint `
        -TimestampUrl $TimestampUrl `
        -Description "MedReminder"
}

function Get-OwnBinaries {
    param([string]$Directory)

    # Only MedReminder's own binaries are signed in the ZIPs and the
    # framework-dependent MSI. Third-party files keep their vendors'
    # signatures (or lack of them): signing them would make the Certum
    # identity vouch for code not built from this repo. The Microsoft
    # Store MSI is the one exception (Get-UnsignedBinaries).
    Get-ChildItem -Path $Directory -File |
        Where-Object { $_.Name -like 'MedReminder*' -and $_.Extension -in '.exe', '.dll' } |
        ForEach-Object { $_.FullName }
}

function Get-UnsignedBinaries {
    param([string]$Directory)

    # The Microsoft Store requires every PE file inside a submitted MSI
    # to be signed by a CA in the Microsoft Trusted Root Program
    # (docs/PACKAGING.md §26). Files already carrying a valid vendor
    # signature (the .NET runtime, WebView2) keep it; the rest are
    # signed with the Certum certificate in the Store copy only.
    Get-ChildItem -Path $Directory -File -Recurse |
        Where-Object { $_.Extension -in '.exe', '.dll' } |
        Where-Object { (Get-AuthenticodeSignature -FilePath $_.FullName).Status -ne 'Valid' } |
        ForEach-Object { $_.FullName }
}

function Invoke-MsiBuild {
    param(
        [string]$PublishDir,
        [string]$Destination
    )

    # --no-incremental: both MSIs share packaging\wix\obj, and an
    # incremental build could reuse the harvest of the other folder.
    dotnet build $script:WixProj -c Release --no-incremental /p:PublishDir="$PublishDir" /p:ProductVersion=$Version
    if ($LASTEXITCODE -ne 0) { throw "MSI build failed ($PublishDir)." }

    $msiOut = Join-Path $PSScriptRoot "packaging\wix\bin\Release\MedReminder-$Version-x64.msi"
    if (-not (Test-Path $msiOut)) { throw "MSI not generated: $msiOut" }

    Copy-Item $msiOut $Destination -Force
    Invoke-Sign -Files @($Destination)
}

function Invoke-Publish {
    param(
        [string]$OutputDir,
        [bool]$SelfContained
    )

    if (Test-Path $OutputDir) { Remove-Item $OutputDir -Recurse -Force }

    # Google Drive OAuth credentials are read from the
    # MEDREMINDER_GOOGLE_CLIENT_ID / MEDREMINDER_GOOGLE_CLIENT_SECRET
    # environment variables when present (docs/PACKAGING.md §24).
    dotnet publish $script:UIProj `
        -c Release `
        -r win-x64 `
        --self-contained $($SelfContained.ToString().ToLowerInvariant()) `
        -o $OutputDir `
        -p:Version=$Version `
        -p:VersionPrefix=$Version `
        -p:AssemblyVersion=$Version `
        -p:FileVersion=$Version `
        -p:InformationalVersion=$Version

    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($OutputDir)." }
}

function Invoke-LocalBuild {
    if ($Version -notmatch '^\d+\.\d+\.\d+$') {
        throw "Local release builds require a vX.Y.Z version (MSI ProductVersion), got: $Version"
    }

    $script:UIProj = Join-Path $PSScriptRoot "src\MedReminder.UI\MedReminder.UI.csproj"
    $script:WixProj = Join-Path $PSScriptRoot "packaging\wix\MedReminder.wixproj"
    $WorkDir   = Join-Path $PSScriptRoot "dist\$Version\work"
    $DistDir   = Join-Path $PSScriptRoot "dist\$Version"
    $ScDir     = Join-Path $WorkDir "publish-net10"
    $FdDir     = Join-Path $WorkDir "publish"
    $StoreDir  = Join-Path $WorkDir "publish-store"

    Write-Host "=== MedReminder local build - version $Version ===" -ForegroundColor Green

    if (-not $NoSign) { Assert-SigningCertificate }

    if (Test-Path $DistDir) { Remove-Item $DistDir -Recurse -Force }
    New-Item -ItemType Directory -Path $WorkDir -Force | Out-Null

    Write-Host ""
    Write-Host "[1/8] Restore" -ForegroundColor Cyan
    dotnet restore (Join-Path $PSScriptRoot "MedReminder.sln")
    if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed." }
    if (-not $SkipMsi) {
        dotnet restore $script:WixProj
        if ($LASTEXITCODE -ne 0) { throw "dotnet restore (WiX) failed." }
    }

    Write-Host ""
    Write-Host "[2/8] Publish self-contained" -ForegroundColor Cyan
    Invoke-Publish -OutputDir $ScDir -SelfContained $true

    Write-Host ""
    Write-Host "[3/8] Publish framework-dependent" -ForegroundColor Cyan
    Invoke-Publish -OutputDir $FdDir -SelfContained $false

    # Binaries must be signed before they are zipped or harvested into the MSI.
    Write-Host ""
    Write-Host "[4/8] Sign application binaries" -ForegroundColor Cyan
    $binaries = @(Get-OwnBinaries $ScDir) + @(Get-OwnBinaries $FdDir)
    if ($binaries.Count -eq 0) { throw "No MedReminder binaries found in the publish output." }
    Invoke-Sign -Files $binaries

    Write-Host ""
    Write-Host "[5/8] Create ZIP packages" -ForegroundColor Cyan
    Compress-Archive -Path (Join-Path $ScDir '*') -DestinationPath (Join-Path $DistDir "MedReminder-win-x64-net10.zip")
    Compress-Archive -Path (Join-Path $FdDir '*') -DestinationPath (Join-Path $DistDir "MedReminder-win-x64.zip")

    if (-not $SkipMsi) {
        Write-Host ""
        Write-Host "[6/8] Build and sign MSI" -ForegroundColor Cyan
        Invoke-MsiBuild -PublishDir $FdDir -Destination (Join-Path $DistDir "MedReminder-win-x64.msi")

        # Copy, so that the self-contained ZIP keeps vendor files untouched.
        Write-Host ""
        Write-Host "[7/8] Build and sign Microsoft Store MSI (self-contained)" -ForegroundColor Cyan
        Copy-Item $ScDir $StoreDir -Recurse
        $unsigned = @(Get-UnsignedBinaries $StoreDir)
        Write-Host "  $($unsigned.Count) unsigned PE files to sign"
        # Batches keep the signtool command line under the Windows limit.
        for ($i = 0; $i -lt $unsigned.Count; $i += 40) {
            Invoke-Sign -Files $unsigned[$i..([Math]::Min($i + 39, $unsigned.Count - 1))]
        }
        Invoke-MsiBuild -PublishDir $StoreDir -Destination (Join-Path $DistDir "MedReminder-win-x64-net10.msi")
    }
    else {
        Write-Host "[6-7/8] MSI skipped." -ForegroundColor Yellow
    }

    Write-Host ""
    Write-Host "[8/8] SHA-256 checksums" -ForegroundColor Cyan
    Remove-Item $WorkDir -Recurse -Force
    $packages = Get-ChildItem -Path $DistDir -File | Where-Object { $_.Extension -in '.zip', '.msi' }
    $packages |
        ForEach-Object { "{0}  {1}" -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name } |
        Set-Content -Path (Join-Path $DistDir "SHA256SUMS.txt") -Encoding ascii

    Write-Host ""
    Write-Host "=== Local build completed ===" -ForegroundColor Green
    Write-Host "Output: $DistDir"
    Get-ChildItem $DistDir | Format-Table Name, Length, LastWriteTime
    if ($NoSign) {
        Write-Host "WARNING: packages are NOT signed (-NoSign)." -ForegroundColor Yellow
    }
}

try {
    if ($LocalBuild) {
        Invoke-LocalBuild
    }
    else {
        Invoke-GitRelease
    }
}
catch {
    Write-Host "Unexpected error:" $_.Exception.Message -ForegroundColor Red
    exit 1
}
