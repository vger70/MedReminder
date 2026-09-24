# CLAUDE.md

## 1. Project Overview
**MedReminder**: Windows desktop app (C# / .NET 10 / WinForms / SQLite via EF Core 10) to remind users about medicine stock & prescriptions. Non-medical device.
- **Frameworks**: `net10.0-windows10.0.19041.0` (UI/Infra) | `net10.0` (Domain/App)
- **Architecture**: Clean Architecture (`UI` → `App` → `Domain`, `Infra` implements `App` ports). Domain must not depend on Infra/App or Windows APIs.
- **Docs**: `docs/ANALYSIS.md` (architecture), `docs/PACKAGING.md` (release).

---

## 2. Language Policy (STRICT)
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

Note: Infra tests require Windows (DPAPI/Registry). Release build deletes .pdb and .xml via MSBuild target StripReleaseDebugArtifacts (DO NOT REMOVE).
4. Git, Branching & PR Workflow (MANDATORY)
 * Base Branch: main. Ask before creating a feature branch.
 * Branch Naming: MUST reflect request. Prefixes allowed: claude/<name> or feature/<name>.
 * Early PR Requirement: Open PR after the first commit of the session. Do not wait until the end.
 * Commit Messages: Imperative, English, explain "why". Never push to main directly.
 * Changelog: Update CHANGE_LOG.md when opening/updating a PR.
5. Runtime Data (%LOCALAPPDATA%\MedReminder\)
 * Shared: profiles.json, smtp.settings.json, smtp.protected (DPAPI), backup.*.json, user.settings.json, logs/*.log.
 * Per-Profile (profiles\<id>\): medreminder.db (SQLite), notifications.settings.json.
 * Constraint: Never write outside %LOCALAPPDATA%\MedReminder\. Never log secrets/PII.
6. DOs (Always Follow)
 * Name branches correctly (claude/ or feature/) and open PR after 1st commit.
 * Ask user to run dotnet build and dotnet test before committing source code.
 * Add new UI string keys to ALL assets/localization/strings.<lang>.json files.
 * Keep tone sober, factual, concise, and no emojis in documentation.
7. DONTs (Strictly Forbidden)
 * NEVER commit non-English text to code, logs, or docs (except authorized user guides/JSONs).
 * NEVER use System.Net.Mail.SmtpClient (use MailKit).
 * NEVER log plaintext passwords, email bodies, or medical notes.
 * NEVER use EnsureCreated() for schema updates (must be idempotent boot patches).
 * NEVER bypass single-instance mutex or force-close SQLite outside backup/restore paths.
 * NEVER weaken or remove StripReleaseDebugArtifacts in Directory.Build.props.


