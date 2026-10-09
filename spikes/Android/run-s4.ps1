<#
.SYNOPSIS
  Spike S4: StripReleaseDebugArtifacts (Directory.Build.props) against a
  Release Android build (docs/analysis/ANALYSIS-B1-MOBILE-SYNC.md §13
  Phase 0, decision D11).

.DESCRIPTION
  Publishes the spike app in Release for net10.0-android with the
  repository Directory.Build.props unchanged, then reports:
    - whether the publish succeeds;
    - whether StripReleaseDebugArtifacts ran, and what it deleted;
    - whether a signed APK was produced;
    - which *.pdb / *.xml are left in the output and publish folders.
  With -Install it also installs the APK on the connected device with
  adb and starts it; the S1-S3 buttons then run in this Release build.
  Writes results/s4-<timestamp>.md and the build log next to it.
  The build log may contain local paths; review it before committing.

.PARAMETER TrimMode
  Empty (default): the .NET for Android Release default. "full": the S3
  variant with full trimming.

.PARAMETER Install
  Install the APK with adb and launch it.
#>
param(
    [ValidateSet('', 'partial', 'full')]
    [string]$TrimMode = '',
    [switch]$Install
)

$ErrorActionPreference = 'Stop'
$projectDir = Join-Path $PSScriptRoot 'MedReminder.MobileSpikes'
$project = Join-Path $projectDir 'MedReminder.MobileSpikes.csproj'
$resultsDir = Join-Path $PSScriptRoot 'results'
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$variant = if ($TrimMode) { "trim-$TrimMode" } else { 'default' }
$log = Join-Path $resultsDir "s4-$stamp-$variant-build.log"
$report = Join-Path $resultsDir "s4-$stamp-$variant.md"
New-Item -ItemType Directory -Force -Path $resultsDir | Out-Null

# Clean the spike's own output so every file found afterwards comes from this run.
foreach ($d in 'bin', 'obj') {
    $p = Join-Path $projectDir $d
    if (Test-Path $p) { Remove-Item -Recurse -Force $p }
}

$arguments = @('publish', $project, '-c', 'Release', '-f', 'net10.0-android')
if ($TrimMode) { $arguments += "-p:SpikeTrimMode=$TrimMode" }

& dotnet @arguments 2>&1 | Tee-Object -FilePath $log
$exitCode = $LASTEXITCODE

$logText = Get-Content -Raw $log
# The referenced net10.0 projects log the same message; keep the spike's own line.
$stripLine = ($logText -split "`r?`n" | Where-Object { $_ -match 'StripReleaseDebugArtifacts: removed .*net10\.0-android' } | Select-Object -Unique) -join "`n"
$candidateFile = Get-ChildItem -Path (Join-Path $projectDir 'obj') -Recurse -Filter 's4-strip-candidates.txt' -ErrorAction SilentlyContinue | Select-Object -First 1
$candidates = if ($candidateFile) { @(Get-Content $candidateFile.FullName | Where-Object { $_ }) } else { @() }

$binDir = Join-Path $projectDir 'bin/Release/net10.0-android'
$apk = Get-ChildItem -Path $binDir -Recurse -Filter '*-Signed.apk' -ErrorAction SilentlyContinue | Select-Object -First 1
$leftovers = @(Get-ChildItem -Path $binDir -Recurse -Include '*.pdb', '*.xml' -ErrorAction SilentlyContinue | ForEach-Object { $_.FullName })
$deleted = @($candidates | Where-Object { -not (Test-Path $_) })

$installResult = 'not requested'
if ($Install) {
    if (-not $apk) {
        $installResult = 'skipped: no signed APK'
    }
    elseif (-not (Get-Command adb -ErrorAction SilentlyContinue)) {
        $installResult = 'skipped: adb not on PATH'
    }
    else {
        $out = & adb install -r $apk.FullName 2>&1
        $installResult = "adb install exit $LASTEXITCODE`: $($out -join ' ')"
        if ($LASTEXITCODE -eq 0) {
            & adb shell monkey -p com.vger70.medreminder.spikes -c android.intent.category.LAUNCHER 1 | Out-Null
            $installResult += '; launched'
        }
    }
}

function Rel([string]$path) { $path.Replace($projectDir, '.').Replace('\', '/') }

$lines = @(
    "# S4 — StripReleaseDebugArtifacts on an Android Release build",
    "",
    "- Date: $(Get-Date -Format 'yyyy-MM-dd HH:mm')",
    "- Variant: $variant",
    "- dotnet: $((& dotnet --version) -join '')",
    "- Publish exit code: $exitCode",
    "- Strip target message: $(if ($stripLine) { $stripLine.Trim() } else { 'not found in the log' })",
    "- Signed APK: $(if ($apk) { "$(Rel $apk.FullName) ($([math]::Round($apk.Length / 1MB, 1)) MiB)" } else { 'not found' })",
    "- Install: $installResult",
    "",
    "## Strip candidates ($($candidates.Count)), deleted: $($deleted.Count)",
    ""
)
$lines += if ($candidates.Count) { $candidates | ForEach-Object { "- $(Rel $_)$(if (Test-Path $_) { ' (still present)' } else { ' (deleted)' })" } } else { '- none' }
$lines += @("", "## *.pdb / *.xml left under bin/Release/net10.0-android ($($leftovers.Count))", "")
$lines += if ($leftovers.Count) { $leftovers | ForEach-Object { "- $(Rel $_)" } } else { '- none' }
$lines += @(
    "",
    "## To fill in after running the app from this APK",
    "",
    "- App starts: yes / no",
    "- S1, S2, S3 in the app: attach the shared JSON report"
)
$lines | Set-Content -Encoding utf8 $report

Write-Host ""
Write-Host "S4 report: $report"
exit $exitCode
