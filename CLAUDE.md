# CLAUDE.md

## 1. Project Overview
**MedReminder**: Windows desktop app (C# / .NET 10 / WinForms / SQLite via EF Core 10) remind users medicine stock & prescriptions. Non-medical device.
- **Frameworks**: `net10.0-windows10.0.19041.0` (UI) | `net10.0-windows` (Infra) | `net10.0` (Domain/App/Infra.Portable/DataImporter)
- **Architecture**: Clean Architecture (`UI` → `App` → `Domain`, `Infra` and `Infra.Portable` implement `App` ports). Domain no depend Infra/App or Windows APIs; `Infra.Portable` no depend Windows APIs.
- **Docs**: `docs/ANALYSIS.md` (architecture), `docs/PACKAGING.md` (release).

---

## 2. Language Policy
- **EN-ONLY**: Code, comments, logs, exceptions, Markdown docs, commit messages, PRs, build scripts.
- **EXCEPTIONS**: 
  - Chat with user (Italian allowed).
  - Shipped guides: `docs/USER_GUIDE.{it,fr,es,de}.md`.
  - UI dictionaries: `assets/localization/strings.<lang>.json` (en, it, fr, es, de).

---

## 3. Build, Test & Publish
```powershell
# Build & Test
dotnet restore MedReminder.sln
dotnet build MedReminder.sln -c Release
dotnet test MedReminder.sln -c Release

# Publish
dotnet publish src/MedReminder.UI -c Release /p:PublishProfile=win-x64-framework-dependent
dotnet publish src/MedReminder.UI -c Release /p:PublishProfile=win-x64-self-contained
```
Note: Infra tests need Windows (DPAPI/Registry). Release build deletes .pdb and .xml via MSBuild target StripReleaseDebugArtifacts (see §7).

---

## 4. Git, Branching & PR Workflow
 * Base Branch: main. Ask before creating feature branch.
 * Exception, household / master device feature (docs/analysis/ANALYSIS-HOUSEHOLD-MASTER-DEVICE.md): every PR of that feature targets integration branch `feature/master-slave`, not main. Only final PR (after step H5) merges `feature/master-slave` into main. Step H0 is the one exception and targets main.
 * Branch Naming: name MUST reflect request. Prefixes allowed: claude/<name> or feature/<name>.
 * Early PR Requirement: Open PR after first commit of session. No ask to follow PR. No wait until end.
 * Commit Messages: Imperative, English, explain "why". Never push to main directly.
 * Changelog: Update CHANGE_LOG.md when opening/updating PR.

---

## 5. Runtime Data (%LOCALAPPDATA%\MedReminder\)
 * Shared: profiles.json, smtp.settings.json, smtp.protected (DPAPI), backup.*.json, user.settings.json, logs/*.log, catalogue\staging\ (transient remote AIFA archive, deleted after each run), catalogue\shortages\ (AIFA shortage list), catalogue\equivalents\ (AIFA transparency list), household/ (household.db, household.settings.json, household.protected and device.protected (DPAPI)), setup/ (transient, first-run join only).
 * Per-Profile (profiles\<id>\): medreminder.db (SQLite), notifications.settings.json, ui.settings.json.
 * Constraint: Never write outside %LOCALAPPDATA%\MedReminder\. Never log secrets/PII.

---

## 6. DOs (Always Follow)
 * Name branches correctly (claude/ or feature/) and open PR after 1st commit.
 * Ask user run dotnet build and dotnet test before committing source code.
 * Add new UI string keys to ALL assets/localization/strings.<lang>.json files.
 * Keep tone sober, factual, concise, no emojis in documentation.

---

## 7. Constraints
 * Non-English text goes only where §2 allows (user guides, UI dictionaries).
 * Send email through MailKit, not System.Net.Mail.SmtpClient: Microsoft no recommend SmtpClient for new development, email service built on MailKit.
 * No log plaintext passwords, email bodies, medical notes; logs plain files under %LOCALAPPDATA%\MedReminder\logs\.
 * Schema changes idempotent boot patches in DatabaseInitializer. EnsureCreated() only creates schema of new empty database, cannot upgrade existing one (docs/ANALYSIS.md §8.1).
 * Keep single-instance mutex, close SQLite connections only in backup/restore paths: in-process database gate relies on one process owning each profile database (docs/ANALYSIS.md §7).
 * Keep StripReleaseDebugArtifacts in Directory.Build.props intact; removes *.pdb and *.xml from Release build and publish output (docs/ANALYSIS.md).