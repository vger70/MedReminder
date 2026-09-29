param(
    [Parameter(Position = 0)]
    [ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+(-[a-zA-Z0-9.-]+)?$')]
    [string]$Version,

    [Alias("h")]
    [switch]$Help
)

function Show-Usage {
    Write-Host ""
    Write-Host "Release Script" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "USAGE:"
    Write-Host "  .\release.ps1 <version>"
    Write-Host "  .\release.ps1 -Version <version>"
    Write-Host "  .\release.ps1 -Help"
    Write-Host ""
    Write-Host "EXAMPLES:"
    Write-Host "  .\release.ps1 1.1.0"
    Write-Host "  .\release.ps1 -Version 1.1.0"
    Write-Host "  .\release.ps1 -Help"
    Write-Host ""
    Write-Host "DESCRIPTION:"
    Write-Host "  Esegue la procedura di release Git:"
    Write-Host "    - git status"
    Write-Host "    - git add ."
    Write-Host "    - git commit"
    Write-Host "    - git push"
    Write-Host "    - git tag"
    Write-Host "    - git push origin <tag>"
    Write-Host ""
}

# Mostra help se richiesto o se manca la versione
if ($Help -or [string]::IsNullOrWhiteSpace($Version)) {
    Show-Usage
    exit 0
}

function Set-Version {
	# Aggiorna Directory.Build.props
	$PropsFile = Join-Path $PSScriptRoot "Directory.Build.props"

	if (-not (Test-Path $PropsFile)) {
		Write-Host "File Directory.Build.props non trovato: $PropsFile" -ForegroundColor Red
		exit 1
	}

	Write-Host "==> Aggiorno versione in Directory.Build.props" -ForegroundColor Cyan

	[xml]$xml = Get-Content $PropsFile

	$versionPrefixNode = $xml.SelectSingleNode("//VersionPrefix")

	if ($null -eq $versionPrefixNode) {
		Write-Host "Tag <VersionPrefix> non trovato" -ForegroundColor Red
		exit 1
	}

	$currentVersion = $versionPrefixNode.InnerText

	if ($currentVersion -ne $Version) {
		$versionPrefixNode.InnerText = $Version
		$xml.Save($PropsFile)

		Write-Host "VersionPrefix aggiornato da $currentVersion a $Version" -ForegroundColor Green
	}
	else {
		Write-Host "VersionPrefix già impostato a $Version" -ForegroundColor Yellow
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
        Write-Host "ERRORE durante: $Description" -ForegroundColor Red
        exit $LASTEXITCODE
    }

    Write-Host "OK: $Description" -ForegroundColor Green
}

$CommitMessage = "Prepare release v$Version"
$TagName = "v$Version"

try {
	Set-Version
		
    Invoke-GitCommand "git status" "Verifica stato repository"
    Invoke-GitCommand "git add ." "Aggiunta file"
	
	Write-Host "==> Commit" -ForegroundColor Cyan
    git commit -m "$CommitMessage"
	
	if ($LASTEXITCODE -eq 0) {
		Write-Host "OK: Commit eseguito" -ForegroundColor Green
	}
	else {
		Write-Host "Nessuna modifica da committare, continuo..." -ForegroundColor Yellow
	}	
	
    Invoke-GitCommand "git push" "Push branch"
    Invoke-GitCommand "git tag $TagName" "Creazione tag"
    Invoke-GitCommand "git push origin $TagName" "Push tag"

    Write-Host ""
    Write-Host "Release $TagName completata con successo." -ForegroundColor Green
}
catch {
    Write-Host "Errore imprevisto:" $_.Exception.Message -ForegroundColor Red
    exit 1
}