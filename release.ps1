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
    Invoke-GitCommand "git status" "Verifica stato repository"
    Invoke-GitCommand "git add ." "Aggiunta file"

    # Verifica modifiche da committare
    git diff --cached --quiet
    if ($LASTEXITCODE -eq 0) {
        Write-Host "Nessuna modifica da committare." -ForegroundColor Yellow
        exit 0
    }

    Invoke-GitCommand "git commit -m `"$CommitMessage`"" "Commit"
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