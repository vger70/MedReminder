# MedReminder

Windows desktop app that reminds the user to request a new prescription
from their doctor in time, before a medicine's stock runs out. Built
with C# on .NET 10 and WinForms.

> **MedReminder is an organizational reminder, not a medical device.**
> It does not provide diagnoses, therapy instructions, therapy changes
> or clinical suggestions. Every therapy decision must be made with
> your doctor.

---

## Purpose

Keep track of the stock of medicines you take regularly and warn you,
with a configurable lead time, when it is time to ask your doctor for
a new prescription. The app computes days left and estimated run-out
date from the therapy schedule, suspensions and schedule changes; it
sends a Windows notification and/or an email when the stock falls
below the threshold. Optionally, it also reminds the user at each
scheduled dose time.

## Features

**Medicines and stock**

- Medicines list with remaining quantity, daily consumption, days
  left, estimated run-out date, colored status badge.
- Dose, frequency, start/end date, warning threshold, reference
  doctor, notes.
- Complex regimens: fixed daily, weekly pattern, on/off cycles,
  linear or stepped tapering, as needed (PRN). The run-out estimate
  follows the regimen day by day.
- Optional per-medicine administration slots (time + free-form label
  such as "before sleep", "on empty stomach").
- Stock movements: initial load, new package, manual addition,
  positive/negative correction; full history preserved (no
  destructive quantity edits).
- Automatic materialization of daily consumption.
- Mid-therapy dose/frequency changes with versioned schedule
  (history preserved).
- Temporary therapy suspensions with open/closed dates.
- Individual intake registration (taken / skipped / cancelled) with
  automatic stock adjustment when "taken".
- Reference catalogue for medicine look-up (Italy, EU centralised
  authorisations, Spain, France), with links to the official
  leaflet for Italian medicines.
- Printable therapy report for the doctor.

**Notifications**

- Per-medicine configurable alerts, with independent channels
  (Windows / Email / Both / None).
- Optional dose-time reminder per medicine (toast and/or email at
  each timed slot, at most once per slot per day).
- Optional caregiver email address per profile: receives a copy of
  every email the profile receives.
- Structural notification de-duplication via `StockEpoch`: after a
  refill the warning cycle restarts.
- Internal scheduler with configurable periodic check (default 30
  minutes); "Check now" for an on-demand run.

**Profiles**

- Several people on one installation, each with a separate
  database; administrator and user roles; optional PIN per profile.

**Backup, export and restore**

- Daily automatic backup of every profile's database to a local
  folder (preferred time and retention configurable), one-click
  restore with auto-restart.
- Encrypted export / import of a profile's data (`.mrz`, Argon2id +
  AES-GCM, user passphrase) for moving to another PC. Format
  documented in [`docs/EXPORT-FORMAT.md`](docs/EXPORT-FORMAT.md).
- Optional encrypted daily snapshot into a folder synchronized by
  the user's own cloud client (OneDrive, Google Drive Desktop,
  Dropbox, iCloud Drive), and **Restore from cloud folder** on a
  second PC. This is a backup, not real-time sync.
- Manual database backup / restore to a local file.

**Application**

- UI localized in English, Italian, French, Spanish and German.
- User guide integrated in the app (F1), rendered with WebView2, in
  the same five languages.
- System tray icon with Open / Check now / Settings / Exit menu;
  closing the window minimizes to tray.
- Optional automatic startup with Windows (per-user, no UAC).
- Update check against GitHub Releases (at startup, can be turned
  off; nothing is downloaded or installed automatically).
- Optional "Support Development" dialog opening Stripe or PayPal
  hosted payment pages; hidden unless configured.
- Structured rolling log (daily, 30-day retention).

## Requirements

- Windows 10 22H2 or Windows 11.
- To build / run from source: **.NET SDK 10.0 or higher**.
- To run a framework-dependent published build: **.NET 10 Desktop
  Runtime** installed.
- For the self-contained build: no external runtime needed.

## Technology stack

| Layer | Technology |
|---|---|
| UI | WinForms on .NET 10 (`net10.0-windows`) |
| Application / Domain | C# 12+, nullable + implicit usings |
| Persistence | SQLite via EF Core 10 |
| SMTP | MailKit 4.x |
| Local credentials | DPAPI (Windows Data Protection API, CurrentUser scope) |
| Export / cloud snapshots | Argon2id (Konscious.Security.Cryptography) + AES-GCM |
| Auto-start | Registry `HKCU\...\Run` |
| Local notifications | Modern Windows toast via CommunityToolkit + tray balloon fallback |
| In-app help | WebView2 + Markdig (MD → HTML) |
| Localization | JSON dictionaries per language, hot-reloadable from disk |
| Logging | Serilog + daily rolling file |
| Tests | xUnit + FluentAssertions |
| Hosting | `Microsoft.Extensions.Hosting` (generic host + BackgroundService) |

5 projects + 5 test projects — details in
[`docs/ANALYSIS.md`](docs/ANALYSIS.md).

## Repository layout

```
MedReminder.sln
src/
  MedReminder.Domain/           pure entities and calculations  (net10.0)
  MedReminder.Application/      use cases and ports             (net10.0)
  MedReminder.Infrastructure/   SQLite/MailKit/DPAPI/export     (net10.0-windows)
  MedReminder.UI/               WinForms + host                 (net10.0-windows)
  MedReminder.DataImporter/     reference-catalogue import tool
tests/
  MedReminder.Domain.Tests/
  MedReminder.Application.Tests/
  MedReminder.Infrastructure.Tests/
  MedReminder.UI.Tests/
  MedReminder.DataImporter.Tests/
assets/
  medreminder.ico               app icon (pill, multi-resolution)
  localization/                 JSON dictionaries (en, it, fr, es, de)
docs/
  ANALYSIS.md                   technical analysis + architecture
  EVOLUTION.md                  open evolution backlog
  EVOLUTION-DONE.md             shipped evolutions
  EXPORT-FORMAT.md              public .mrz archive format
  CATALOGUE-DATA.md             reference-catalogue sources and refresh
  PACKAGING.md                  publishing and distribution
  USER_GUIDE.<lang>.md          user guide (en, it, fr, es, de)
  analysis/                     per-feature design documents
  prompt/                       implementation prompts
```

## How to build

```bat
dotnet restore MedReminder.sln
dotnet build   MedReminder.sln -c Release
```

Output: `src\MedReminder.UI\bin\Release\net10.0-windows*\MedReminder.exe`.

## How to run the tests

```bat
dotnet test MedReminder.sln -c Release
```

Integration tests (`MedReminder.Infrastructure.Tests`) run on Windows
only (they use DPAPI and the registry). Domain and application tests
run on any platform with the .NET 10 SDK.

## How to publish for distribution

See [`docs/PACKAGING.md`](docs/PACKAGING.md) for details. In short:

```bat
:: Framework-dependent (~10 MB, requires .NET 10 Desktop Runtime)
dotnet publish src\MedReminder.UI -c Release ^
  /p:PublishProfile=win-x64-framework-dependent

:: Self-contained (~85 MB, no external runtime required)
dotnet publish src\MedReminder.UI -c Release ^
  /p:PublishProfile=win-x64-self-contained
```

## Download

**Windows x64**

[⬇️ Download MedReminder](https://github.com/vger70/MedReminder/releases/latest/download/MedReminder-win-x64.zip)

[📦 View all releases](https://github.com/vger70/MedReminder/releases)

## Windows SmartScreen warning on first run

The distributed binaries are **not code-signed**. The first time you
launch `MedReminder.exe` — or open the MSI installer — Windows
SmartScreen shows a blue dialog:

> Windows protected your PC
> Microsoft Defender SmartScreen prevented an unrecognized app from
> starting.

Click **More info**, then **Run anyway**. Windows remembers your
choice for that specific file — subsequent launches do not prompt
again.

The UAC dialog raised by the MSI shows "Unknown Publisher" for the
same reason. That is expected.

MedReminder currently opts out of the recurring cost of a code-signing
certificate. This does not affect the functionality or integrity of
the binaries: they are built by the public GitHub Actions workflow
[`dotnet-desktop.yml`](.github/workflows/dotnet-desktop.yml) from the
tagged source in this repository.

## Where data is stored

Everything the app writes lives under
`%LOCALAPPDATA%\MedReminder\`. Since Increment 15 the database is
per-profile — the root holds admin-managed shared files and the
profile registry, while each profile has its own subfolder under
`profiles\`. See `docs/analysis/ANALYSIS-MULTI-USER.md` §3 for the
full layout.

Shared, admin-managed:

| File | Content |
|---|---|
| `profiles.json` | Profile registry (admin/user, PIN hash, LastUsedAt hint) |
| `smtp.settings.json` | Shared SMTP transport (no password, no recipient) |
| `smtp.protected` | SMTP password, DPAPI-encrypted (CurrentUser scope) |
| `backup.settings.json` / `backup.state.json` | Automatic backup config (local and cloud-folder targets) + last-tick state |
| `cloud-backup.protected` | Cloud-folder backup passphrase, DPAPI-encrypted (CurrentUser scope) |
| `donations.settings.json` | Optional public Payment Link URLs for the Support Development dialog |
| `user.settings.json` | UI language, reference-catalogue country, update-check preference |
| `logs/medreminder-YYYYMMDD.log` | Daily rolling log, 30-day retention |

Per-profile, under `profiles\<profile-id>\`:

| File | Content |
|---|---|
| `medreminder.db` (+ `-shm`, `-wal`) | This profile's SQLite database |
| `notifications.settings.json` | This profile's email recipient and optional caregiver address |

Nothing outside `%LOCALAPPDATA%\MedReminder\` is written by the app,
except the backup, export and cloud-folder files written to folders
the user selects.
Sensitive fields (passwords, email bodies, medical notes) never reach
the logs.

## How to configure notifications

Open **Settings → Email SMTP** from the main menu:

1. Fill in host, port, StartTLS, username, sender, recipient.
2. Type the password in the dedicated field (encrypted via DPAPI and
   stored in `smtp.protected`; the settings file never contains the
   plaintext password).
3. **Since 2022, Gmail and Outlook.com require an 'App Password' with
   2FA enabled** — the regular account password does NOT work.
4. "Test connection" verifies the server is reachable.
5. On each medicine, under **Edit → Notification channels**, choose
   Windows / Email / both / none.

Under **Settings → Notifications** each profile sets its own
recipient and an optional caregiver address.

Windows notifications need no configuration: they use the app's
shared tray icon and modern Windows toast when available.

## How to back up

**Settings → Backup / Restore**. Automatic-backup settings are
visible to administrators only; export, import and restore from a
cloud folder are available to every profile.

- **Automatic daily backup**: enable the checkbox, pick a folder,
  a preferred time and a retention in days. Every profile's database
  is copied (unencrypted `.db`). If the PC is off at the preferred
  time, the backup runs at the next start of the day.
- **Backup to a cloud-synced folder (encrypted)**: optional second
  target. Writes an encrypted `.mrz` snapshot of the current profile
  into a folder the user's cloud client synchronizes. Requires a
  backup passphrase; losing it means the snapshots cannot be
  restored.
- **Run backup now** / **Export to specific folder…**: on-demand
  database copy.
- **Restore backup…**: pick a `.db` file; the current DB is renamed
  to `medreminder.db.bak-YYYYMMDDHHMMSS` before replacement, and the
  app restarts automatically to release SQLite locks cleanly.
- **Export all data (encrypted)…** / **Import from export…**: a
  passphrase-protected `.mrz` archive of the current profile,
  portable across Windows accounts and PCs. Import overwrites the
  current profile after a safety copy. Recommended way to move to a
  new PC.
- **Restore from cloud folder…**: lists the `.mrz` snapshots in a
  folder and restores the selected one on this PC.

The daily backup uses SQLite's online backup API
(`SqliteConnection.BackupDatabase`) — safe against concurrent
writes. See the user guide for the full procedure.

## Language

MedReminder is localized in **English (default), Italian, French,
Spanish and German**.
Change the language from **Settings → General**. The app restarts
automatically to apply.

- Interface, email notifications and the integrated user guide use
  the language selected here.
- Windows toast notifications always follow the Windows system
  language (they show up in the OS shell, not inside the app).

The language dictionaries are plain JSON files under
`bin\...\localization\strings.<lang>.json` — you can edit a
translation without recompiling the app. Advanced users can also
override any string by putting a `strings.<lang>.json` in
`%LOCALAPPDATA%\MedReminder\localization\` (user override wins over
the shipped file).

## Logs and diagnostics

Log path: `%LOCALAPPDATA%\MedReminder\logs\`.
Format: `medreminder-YYYYMMDD.log` — one file per day, 30-day
retention, 10 MB max per file (with automatic alphabetic roll past
that size).

Default level: `Information`. Contains executed SQL commands (no
sensitive parameters), scheduler ticks, notification sends, errors
with stack trace. **Passwords, email content and sensitive
dose/quantity values are never written to the logs.**

## Known limitations

- **Not a medical device** — must not be used as a clinical therapy
  management tool. See the disclaimer at the top.
- The database is created with `EnsureCreated()` on first run, not
  EF Core migrations. Later schema changes are additive patches
  applied idempotently on boot; a move to migrations would require a
  baseline migration.
- The database itself is not encrypted; it relies on the Windows
  user account's file permissions. Export archives and cloud-folder
  snapshots are encrypted.
- Email sending depends on Internet connectivity and SMTP server
  reachability; on transient errors the app retries with backoff
  5s → 30s → 2m, then gives up, logging the error.
- Single-instance is per-user (one Windows session). A second
  Windows user on the same machine can run their own instance.
- No real-time sync between devices. The cloud-folder backup is
  single-writer: restoring on a second PC replaces its data, and
  changes made on two PCs between restores are not merged.
- Only the current profile is written to the cloud folder; the local
  automatic backup covers every profile.

## License

Apache 2.0 — see [LICENSE](LICENSE).
