# CLAUDE.md

Instructions for Claude (or any AI assistant) working on this repository.

---

## 1. Project overview

**MedReminder** is a Windows desktop application, written in **C#** on
**.NET 10** with **WinForms**, that reminds the user to request a new
prescription from their doctor before a medicine's stock runs out.

- Target framework: `net10.0-windows10.0.19041.0` (UI / Infrastructure)
  and `net10.0` (Domain / Application).
- Persistence: SQLite via EF Core 10.
- Distribution: Windows x64, ZIP (official) + optional MSI, self-contained
  build. MSIX is defined but not the primary channel.
- The app is **not a medical device**: it does not provide clinical
  advice. Every therapy decision must be taken with the user's doctor.

Full technical analysis lives in [`docs/ANALYSIS.md`](docs/ANALYSIS.md);
the planned multi-user redesign is in
[`docs/ANALYSIS-MULTI-USER.md`](docs/ANALYSIS-MULTI-USER.md); the release
pipeline is documented in [`docs/PACKAGING.md`](docs/PACKAGING.md).

---

## 2. Language policy — mandatory

Everything committed to this repository **must be written in English**:

- Source code identifiers, comments, log messages, exception messages,
  XML documentation.
- Markdown files (`README.md`, `docs/*.md`, `CLAUDE.md`, `packaging/**/*.md`, …).
  **Exception:** the localized user guides `docs/USER_GUIDE.en.md`,
  `docs/USER_GUIDE.it.md`, `docs/USER_GUIDE.fr.md`,
  `docs/USER_GUIDE.es.md` and `docs/USER_GUIDE.de.md` keep their
  respective languages (English, Italian, French, Spanish and
  German) because they are shipped to the end user and rendered
  inside the app by `HelpViewerForm`.
- Commit messages, PR titles and PR descriptions.
- Build scripts, MSBuild files, CI workflows.
- Localization keys (JSON `strings.<lang>.json` values are the only place
  where non-English text is legitimate — those files carry the
  translations for the UI). The shipped languages are `en`, `it`,
  `fr`, `es`, `de`.

**The only place Italian is allowed is the chat conversation with the
user**. The user writes and reads Italian in chat; every artifact that
lands in the repository is English.

### Exceptions

The following files intentionally contain non-English text and are
NOT subject to the English-only rule:

- `docs/USER_GUIDE.it.md`, `docs/USER_GUIDE.fr.md`,
  `docs/USER_GUIDE.es.md` and `docs/USER_GUIDE.de.md` — the
  Italian, French, Spanish and German user guides shipped inside
  the app.
- `assets/localization/strings.<lang>.json` — the JSON dictionaries
  for the UI languages (en, it, fr, es, de). Values are user-visible
  translated strings by design.

Everything else in the repository — including comments, log messages
and exception messages — must be English.

---

## 3. Repository layout

```
MedReminder.sln
Directory.Build.props           centralized version + build settings
CLAUDE.md                       this file
README.md                       public project README
LICENSE                         MIT

.github/workflows/
  dotnet-desktop.yml            release workflow (triggered by tag v*)

src/
  MedReminder.Domain/           pure entities, value objects, calculations (net10.0)
  MedReminder.Application/      use cases, ports, monitoring (net10.0)
  MedReminder.Infrastructure/   EF Core, MailKit, DPAPI, toast, backup (net10.0-windows)
  MedReminder.UI/               WinForms host + forms + tray (net10.0-windows10.0.19041.0)

tests/
  MedReminder.Domain.Tests/
  MedReminder.Application.Tests/
  MedReminder.Infrastructure.Tests/

assets/
  medreminder.ico               multi-resolution app icon
  localization/                 UI dictionaries (en, it, fr, es, de)

docs/
  ANALYSIS.md                   technical analysis and architecture
  ANALYSIS-MULTI-USER.md        design of the planned multi-user feature
  USER_GUIDE.en.md              shipped user guide (English)
  USER_GUIDE.it.md              shipped user guide (Italian)
  USER_GUIDE.fr.md              shipped user guide (French)
  USER_GUIDE.es.md              shipped user guide (Spanish)
  USER_GUIDE.de.md              shipped user guide (German)
  PACKAGING.md                  build / publish / distribute

packaging/
  msix/                         MSIX manifest, mapping and assets
  wix/                          MSI (WiX v5) project
  scripts/                      build-installer.ps1 + signing helpers
```

Architecture is Clean Architecture: **UI → Application → Domain**, with
**Infrastructure** implementing the ports declared in Application. Do
not add references that violate the direction (Domain never depends on
Infrastructure or Application).

---

## 4. How to build, test, publish

Restore, build and test:

```powershell
dotnet restore MedReminder.sln
dotnet build   MedReminder.sln -c Release
dotnet test    MedReminder.sln -c Release
```

Publish (framework-dependent, small):

```powershell
dotnet publish src/MedReminder.UI -c Release `
  /p:PublishProfile=win-x64-framework-dependent
```

Publish (self-contained, official production build):

```powershell
dotnet publish src/MedReminder.UI -c Release `
  /p:PublishProfile=win-x64-self-contained
```

The infrastructure tests use DPAPI and the Windows registry and
therefore run only on Windows. Domain and application tests run on any
platform with the .NET 10 SDK.

### Release output cleanup

`Directory.Build.props` defines an MSBuild target
`StripReleaseDebugArtifacts` that runs at the end of every **Release**
build and publish and deletes:

- `**/*.pdb` — debug symbols
- `**/*.xml` — XML documentation dumped by referenced packages

This keeps the shipped ZIP / MSI / MSIX free of debug and documentation
noise. The target is scoped by `'$(Configuration)' == 'Release'`, so
Debug builds keep their `.pdb` and `.xml` files. Do **not** remove or
weaken this target without replacing it with an equivalent step in the
release pipeline.

---

## 5. Git and branching

- The current working branch for Claude-driven changes is
  **`claude/translate-in-english`**. Develop, commit and push
  there unless the user explicitly asks otherwise.
- Never push to `main` (or any other branch) without explicit
  permission.
- Commit messages: imperative, English, focused on the "why". End with
  the Claude attribution footer configured for this session.
- Do not open pull requests unless the user asks for one.

### Release procedure

A release is triggered by pushing a `v*` tag. The workflow
`.github/workflows/dotnet-desktop.yml` restores, publishes
self-contained x64 and creates the GitHub Release with
`MedReminder-win-x64.zip` attached. See [`docs/PACKAGING.md`](docs/PACKAGING.md)
for the full procedure.

### CHANGE_LOG.md

Every time a pull request is opened for this repository, prepend an
entry to [`CHANGE_LOG.md`](CHANGE_LOG.md) describing the observable
changes and linking back to the PR. Follow the format and rules that
file itself documents at the top. Update the entry when the PR's
scope changes materially, and mark it merged / closed once the PR
resolves. Do not batch multiple PRs into one entry.

---

## 6. Data locations at runtime

Everything the app writes lives under
`%LOCALAPPDATA%\MedReminder\`:

| File | Content |
|---|---|
| `medreminder.db` (+ `-shm`, `-wal`) | SQLite database |
| `smtp.settings.json` | SMTP configuration (no password) |
| `smtp.protected` | SMTP password, DPAPI-encrypted (CurrentUser scope) |
| `backup.settings.json` / `backup.state.json` | Automatic backup config + state |
| `user.settings.json` | UI language preference |
| `logs/medreminder-YYYYMMDD.log` | Daily rolling log, 30-day retention |

Nothing outside `%LOCALAPPDATA%\MedReminder\` is written by the app.
Sensitive fields (passwords, email bodies, medical notes) never reach
the logs.

---

## 7. Pending / next work items

Kept here so future sessions pick them up without re-deriving them:

1. Multi-user feature (Increment 15) — designed in
   `docs/ANALYSIS-MULTI-USER.md`, not yet implemented.

---

## 8. What to always do

- Read `docs/ANALYSIS.md` before making architectural changes.
- Keep the Domain project free of Windows-specific APIs and of EF Core.
- Keep secrets out of the repository (`smtp.protected`, `*.pfx`, `*.p12`
  are already in `.gitignore`).
- When adding a UI string, add its key to **every** dictionary under
  `assets/localization/`.
- Run `dotnet build` and `dotnet test` before committing anything that
  touches source code.
- Match the tone of the existing Markdown: sober, factual, no marketing
  language, no emoji unless the user explicitly asks.

## 9. What to never do

- Never commit a non-English string to source code, comments, docs
  (except the shipped user guides `USER_GUIDE.it.md`,
  `USER_GUIDE.fr.md`, `USER_GUIDE.es.md` and `USER_GUIDE.de.md`,
  and the localization dictionaries under `assets/localization/`),
  commit messages, or PR text.
- Never remove the Release-time `.pdb` / `.xml` cleanup target.
- Never introduce `SmtpClient` from `System.Net.Mail` — MailKit is the
  only supported SMTP client.
- Never write plaintext passwords or PII to the log or to any file
  other than `smtp.protected` (which is DPAPI-encrypted).
- Never use `EnsureCreated()` for a new schema change — additive
  patches must be idempotent on boot (see `ANALYSIS.md` §2.8).
- Never bypass single-instance mutex or force-close SQLite connections
  outside of the documented backup / restore paths.
