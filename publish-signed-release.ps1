param(
    [Parameter(Position = 0)]
    [ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+$')]
    [string]$Version,

    # SHA-1 thumbprint of the Certum certificate exposed by SimplySign
    # Desktop. Falls back to the CERTUM_CERT_THUMBPRINT environment variable.
    [string]$CertificateThumbprint = $env:CERTUM_CERT_THUMBPRINT,

    # RFC 3161 timestamp server passed to release.ps1 -LocalBuild.
    [string]$TimestampUrl = "http://time.certum.pl",

    # Resume on an existing tag and release: skip the Git release and the
    # wait for the CI workflow.
    [switch]$SkipGitRelease,

    # Maximum wait for the CI workflow run to appear after the tag push.
    [int]$WorkflowStartTimeoutSeconds = 300,

    [Alias("h")]
    [switch]$Help
)

function Show-Usage {
    Write-Host ""
    Write-Host "Signed Release Script" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "USAGE:"
    Write-Host "  .\publish-signed-release.ps1 <version> [-CertificateThumbprint <sha1>] [-TimestampUrl <url>] [-SkipGitRelease]"
    Write-Host "  .\publish-signed-release.ps1 -Help"
    Write-Host ""
    Write-Host "EXAMPLES:"
    Write-Host "  .\publish-signed-release.ps1 2.12.1"
    Write-Host "  .\publish-signed-release.ps1 2.12.1 -CertificateThumbprint 0123456789ABCDEF0123456789ABCDEF01234567"
    Write-Host "  .\publish-signed-release.ps1 2.12.1 -SkipGitRelease"
    Write-Host ""
    Write-Host "DESCRIPTION:"
    Write-Host "  Publishes a release and replaces its CI-built assets with Certum-signed ones:"
    Write-Host "    0. check prerequisites (gh login, signing certificate, clean tree, tag not used)"
    Write-Host "    1. git checkout main, git pull, .\release.ps1 <version>"
    Write-Host "       (commit, push, tag v<version>; the tag triggers the CI release)"
    Write-Host "    2. wait for the CI workflow, then git checkout v<version>"
    Write-Host "    3. .\release.ps1 <version> -LocalBuild (signed and timestamped),"
    Write-Host "       then signtool verify on the MSI"
    Write-Host "    4. gh release upload --clobber: signed ZIPs, MSI and SHA256SUMS.txt"
    Write-Host "    5. git checkout main (always, also on failure)"
    Write-Host ""
    Write-Host "  -SkipGitRelease resumes from step 2 on an existing tag and release,"
    Write-Host "  e.g. after a signing failure."
    Write-Host ""
    Write-Host "REQUIREMENTS:"
    Write-Host "  - GitHub CLI (gh) logged in: gh auth login"
    Write-Host "  - SimplySign Desktop running and connected"
    Write-Host "  - Windows SDK (signtool.exe), .NET 10 SDK"
    Write-Host ""
}

if ($Help -or [string]::IsNullOrWhiteSpace($Version)) {
    Show-Usage
    exit 0
}

# No global $ErrorActionPreference = 'Stop': under Windows PowerShell 5.1
# it turns any stderr output of git/gh into a terminating error. Exit codes
# are checked explicitly instead.

$TagName      = "v$Version"
$WorkflowFile = "dotnet-desktop.yml"
$ReleaseScript = Join-Path $PSScriptRoot "release.ps1"
$DistDir      = Join-Path $PSScriptRoot "dist\$Version"
$Assets       = @(
    "MedReminder-win-x64-net10.zip",
    "MedReminder-win-x64.zip",
    "MedReminder-win-x64.msi",
    "SHA256SUMS.txt"
)

function Write-Step {
    param([string]$Text)
    Write-Host ""
    Write-Host "==> $Text" -ForegroundColor Cyan
}

function Invoke-Native {
    param(
        [scriptblock]$Command,
        [string]$Description
    )

    & $Command
    if ($LASTEXITCODE -ne 0) {
        throw "$Description failed (exit code $LASTEXITCODE)."
    }
}

function Find-SignTool {
    $cmd = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }

    $found = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\signtool.exe" -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending |
        Select-Object -First 1
    if ($found) { return $found.FullName }

    throw "signtool.exe not found. Install the Windows SDK."
}

function Assert-Prerequisites {
    Write-Step "Checking prerequisites"

    if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
        throw "GitHub CLI (gh) not found. Install it and run: gh auth login"
    }
    gh auth status *> $null
    if ($LASTEXITCODE -ne 0) { throw "GitHub CLI is not logged in. Run: gh auth login" }

    # Fail before tagging, not after: signing is the step most likely to fail.
    if ([string]::IsNullOrWhiteSpace($script:CertificateThumbprint)) {
        throw "No certificate thumbprint. Pass -CertificateThumbprint or set CERTUM_CERT_THUMBPRINT."
    }
    $script:CertificateThumbprint = ($script:CertificateThumbprint -replace '\s', '').ToUpperInvariant()
    if (-not (Test-Path "Cert:\CurrentUser\My\$($script:CertificateThumbprint)")) {
        throw "Certificate $($script:CertificateThumbprint) not found in Cert:\CurrentUser\My. Start SimplySign Desktop and connect."
    }

    $script:SignTool = Find-SignTool

    $dirty = git status --porcelain
    if ($LASTEXITCODE -ne 0) { throw "git status failed." }
    if ($dirty -and -not $SkipGitRelease) {
        # release.ps1 commits everything with 'git add .'; refuse to sweep
        # unrelated local changes into the release commit.
        throw "Working tree is not clean. Commit or stash your changes first."
    }
    if ($dirty -and $SkipGitRelease) {
        throw "Working tree is not clean; checking out $TagName would fail or carry local changes."
    }

    Invoke-Native { git fetch origin --tags --quiet } "git fetch"
    git rev-parse --verify --quiet "refs/tags/$TagName" *> $null
    $tagExists = ($LASTEXITCODE -eq 0)

    if (-not $SkipGitRelease -and $tagExists) {
        throw "Tag $TagName already exists. Use a new version, or -SkipGitRelease to sign the existing release."
    }
    if ($SkipGitRelease -and -not $tagExists) {
        throw "Tag $TagName does not exist; -SkipGitRelease needs an existing tag."
    }

    Write-Host "OK: gh logged in, certificate found, signtool at $($script:SignTool)" -ForegroundColor Green
}

function Wait-ReleaseWorkflow {
    Write-Step "Waiting for the CI workflow on $TagName"

    $deadline = (Get-Date).AddSeconds($WorkflowStartTimeoutSeconds)
    $runId = $null

    $tagSha = git rev-list -n 1 $TagName
    if ($LASTEXITCODE -ne 0) { throw "Cannot resolve $TagName." }

    while (-not $runId) {
        $json = gh run list --workflow $WorkflowFile --commit $tagSha --event push --limit 1 --json databaseId
        if ($LASTEXITCODE -ne 0) { throw "gh run list failed." }

        $runs = $json | ConvertFrom-Json
        if ($runs.Count -gt 0) {
            $runId = $runs[0].databaseId
            break
        }
        if ((Get-Date) -gt $deadline) {
            throw "No $WorkflowFile run found for $TagName after $WorkflowStartTimeoutSeconds s."
        }
        Start-Sleep -Seconds 10
    }

    Write-Host "Workflow run: $runId"
    Invoke-Native { gh run watch $runId --exit-status --interval 15 } "CI workflow run $runId"

    Invoke-Native { gh release view $TagName --json tagName *> $null } "gh release view $TagName"
    Write-Host "OK: release $TagName created by CI" -ForegroundColor Green
}

$tagPushed  = $false
$leftBranch = $false
$exitCode   = 0

try {
    Assert-Prerequisites

    if (-not $SkipGitRelease) {
        Write-Step "[1/4] Git release $TagName"
        $leftBranch = $true
        Invoke-Native { git checkout main } "git checkout main"
        Invoke-Native { git pull } "git pull"
        & $ReleaseScript $Version
        if ($LASTEXITCODE -ne 0) { throw "release.ps1 $Version failed (exit code $LASTEXITCODE)." }
        $tagPushed = $true

        Write-Step "[2/4] Waiting for CI"
        Wait-ReleaseWorkflow
    }
    else {
        Write-Step "[1-2/4] Git release and CI wait skipped (-SkipGitRelease)"
        Invoke-Native { gh release view $TagName --json tagName *> $null } "gh release view $TagName"
    }

    Write-Step "Checking out $TagName"
    Invoke-Native { git fetch origin --tags --quiet } "git fetch --tags"
    $leftBranch = $true
    Invoke-Native { git checkout --quiet $TagName } "git checkout $TagName"

    Write-Step "[3/4] Signed local build"
    & $ReleaseScript $Version -LocalBuild `
        -CertificateThumbprint $script:CertificateThumbprint `
        -TimestampUrl $TimestampUrl
    if ($LASTEXITCODE -ne 0) { throw "release.ps1 $Version -LocalBuild failed (exit code $LASTEXITCODE)." }

    $msi = Join-Path $DistDir "MedReminder-win-x64.msi"
    Invoke-Native { & $script:SignTool verify /pa /v $msi } "signtool verify $msi"

    Write-Step "[4/4] Uploading signed assets to $TagName"
    $files = $Assets | ForEach-Object {
        $path = Join-Path $DistDir $_
        if (-not (Test-Path $path)) { throw "Missing asset: $path" }
        $path
    }
    Invoke-Native { gh release upload $TagName @files --clobber } "gh release upload"

    Write-Host ""
    Write-Host "Signed release $TagName published." -ForegroundColor Green
    gh release view $TagName --json url --jq .url
}
catch {
    Write-Host ""
    Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red
    if ($tagPushed -or $SkipGitRelease) {
        Write-Host "Fix the cause, then resume with: .\publish-signed-release.ps1 $Version -SkipGitRelease" -ForegroundColor Yellow
    }
    $exitCode = 1
}
finally {
    if ($leftBranch) {
        Write-Step "Returning to main"
        git checkout --quiet main
        if ($LASTEXITCODE -ne 0) {
            Write-Host "WARNING: git checkout main failed; check the working tree." -ForegroundColor Yellow
        }
    }
}

exit $exitCode
