# CLAUDE.md

## 1. Project Overview
**MedReminder**: Windows desktop app (C# / .NET 10 / WinForms / SQLite via EF Core 10) to remind users about medicine stock & prescriptions. Non-medical device.
- **Frameworks**: `net10.0-windows10.0.19041.0` (UI) | `net10.0-windows` (Infra) | `net10.0` (Domain/App/DataImporter)
- **Architecture**: Clean Architecture (`UI` → `App` → `Domain`, `Infra` implements `App` ports). Domain must not depend on Infra/App or Windows APIs.
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
Note: Infra tests require Windows (DPAPI/Registry). Release build deletes .pdb and .xml via MSBuild target StripReleaseDebugArtifacts (see §7).

---

## 4. Git, Branching & PR Workflow
 * Base Branch: main. Ask before creating a feature branch.
 * Branch Naming: name reflects the request. Prefixes allowed: claude/<name> or feature/<name>.
 * Early PR Requirement: Open PR after the first commit of the session. Do not wait until the end.
 * Commit Messages: Imperative, English, explain "why". Never push to main directly.
 * Changelog: Update CHANGE_LOG.md when opening/updating a PR.

---

## 5. Runtime Data (%LOCALAPPDATA%\MedReminder\)
 * Shared: profiles.json, smtp.settings.json, smtp.protected (DPAPI), backup.*.json, user.settings.json, logs/*.log.
 * Per-Profile (profiles\<id>\): medreminder.db (SQLite), notifications.settings.json.
 * Constraint: Never write outside %LOCALAPPDATA%\MedReminder\. Never log secrets/PII.

---

## 6. DOs (Always Follow)
 * Name branches correctly (claude/ or feature/) and open PR after 1st commit.
 * Ask user to run dotnet build and dotnet test before committing source code.
 * Add new UI string keys to ALL assets/localization/strings.<lang>.json files.
 * Keep tone sober, factual, concise, and no emojis in documentation.

---

## 7. Constraints
 * Non-English text goes only where §2 allows it (user guides, UI dictionaries).
 * Send email through MailKit, not System.Net.Mail.SmtpClient: Microsoft does not recommend SmtpClient for new development, and the email service is built on MailKit.
 * Do not log plaintext passwords, email bodies, or medical notes; logs are plain files under %LOCALAPPDATA%\MedReminder\logs\.
 * Schema changes are idempotent boot patches in DatabaseInitializer. EnsureCreated() only creates the schema of a new, empty database and cannot upgrade an existing one (docs/ANALYSIS.md §8.1).
 * Keep the single-instance mutex, and close SQLite connections only in the backup/restore paths: the in-process database gate relies on one process owning each profile database (docs/ANALYSIS.md §7).
 * Keep StripReleaseDebugArtifacts in Directory.Build.props intact; it removes *.pdb and *.xml from Release build and publish output (docs/ANALYSIS.md).


