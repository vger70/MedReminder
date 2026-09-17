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
date from dose, frequency, suspensions and schedule changes; it sends
a Windows notification and/or an email when the stock falls below
the threshold.

## Features

- Medicines list with remaining quantity, daily consumption, days
  left, estimated run-out date, colored status badge.
- Dose, frequency, start/end date, warning threshold, reference
  doctor, notes.
- Optional per-medicine administration slots (time + free-form label
  such as "before sleep", "on empty stomach").
- Stock movements: initial load, new package, manual addition,
  positive/negative correction; full history preserved (no
  destructive quantity edits).
- Automatic materialization of daily consumption.
- Mid-therapy dose/frequency changes with versioned schedule
  (history preserved).
- Temporary therapy suspensions with open/closed dates.
- Per-medicine configurable alerts, with independent channels
  (Windows / Email / Both / None).
- Structural notification de-duplication via `StockEpoch`: after a
  refill the warning cycle restarts.
- Internal scheduler with configurable periodic check (default 30
  minutes); "Check now" for an on-demand run.
- Daily automatic backup (folder + preferred time configurable),
  retention in days, one-click restore with auto-restart.
- User guide integrated in the app (F1), rendered with WebView2.
- **UI localized in English, Italian, French, Spanish** — user
  selects the language from Settings → General. Email notifications
  use the user's language; Windows toast notifications follow the
  system language.
- Individual intake registration (taken / skipped / cancelled) with
  automatic stock adjustment when "taken".
- System tray icon with Open / Check now / Settings / Exit menu;
  closing the window minimizes to tray.
- Optional automatic startup with Windows (per-user, no UAC).
- Manual database backup / restore to a local file.
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
| Auto-start | Registry `HKCU\...\Run` |
| Local notifications | Modern Windows toast via CommunityToolkit + tray balloon fallback |
| In-app help | WebView2 + Markdig (MD → HTML) |
| Localization | JSON dictionaries per language, hot-reloadable from disk |
| Logging | Serilog + daily rolling file |
| Tests | xUnit + FluentAssertions |
| Hosting | `Microsoft.Extensions.Hosting` (generic host + BackgroundService) |

4 projects + 3 test projects — details in
[`docs/ANALYSIS.md`](docs/ANALYSIS.md).

## Repository layout

```
MedReminder.sln
src/
  MedReminder.Domain/           pure entities and calculations  (net10.0)
  MedReminder.Application/      use cases and ports             (net10.0)
  MedReminder.Infrastructure/   SQLite/MailKit/DPAPI            (net10.0-windows)
  MedReminder.UI/               WinForms + host                 (net10.0-windows)
tests/
  MedReminder.Domain.Tests/
  MedReminder.Application.Tests/
  MedReminder.Infrastructure.Tests/
assets/
  medreminder.ico               app icon (pill, multi-resolution)
  localization/                 JSON dictionaries (en, it, fr, es)
docs/
  ANALYSIS.md              technical analysis + architecture
  ANALYSIS-MULTI-USER.md   detailed plan for planned multi-user support
  USER_GUIDE.en.md         user guide (English)
  USER_GUIDE.it.md         guida utente (Italiano)
  PACKAGING.md             publishing and distribution
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

## Where data is stored

Everything lives under `%LOCALAPPDATA%\MedReminder\`
(`C:\Users\<user>\AppData\Local\MedReminder\`):

| File | Content |
|---|---|
| `medreminder.db` (+ `-shm`, `-wal`) | SQLite database: medicines, movements, schedule history, suspensions, notifications, intakes, administration slots |
| `smtp.settings.json` | SMTP configuration (without password, overrides appsettings.json) |
| `smtp.protected` | SMTP password encrypted with DPAPI (CurrentUser) |
| `backup.settings.json` | Automatic backup preferences (enable, folder, time, retention) |
| `backup.state.json` | Last-attempt state of the automatic backup |
| `user.settings.json` | UI language preference |
| `logs\medreminder-YYYYMMDD.log` | Daily logs, 30-day retention, 10 MB per file |

No data is sent to external services except the email when SMTP
notifications are configured.

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

Windows notifications need no configuration: they use the app's
shared tray icon and modern Windows toast when available.

## How to back up

**Settings → Backup / Restore**:

- **Automatic daily backup**: enable the checkbox, pick a folder,
  a preferred time and a retention in days. If the PC is off at the
  preferred time, the backup runs at the next start of the day.
- **Run backup now** / **Export to specific folder**: on-demand
  export.
- **Restore backup**: pick a `.db` file; the current DB is renamed
  to `medreminder.db.bak-YYYYMMDDHHMMSS` before replacement, and the
  app restarts automatically to release SQLite locks cleanly.

The daily backup uses SQLite's online backup API
(`SqliteConnection.BackupDatabase`) — safe against concurrent
writes.

## Language

MedReminder is localized in **English (default), Italian, French, Spanish**.
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
- The MVP uses `EnsureCreated()` for the DB schema, not EF Core
  migrations; a future schema evolution will require a baseline
  migration from the MVP version. Schema patches for additive
  changes are applied idempotently on boot.
- Multi-user support (multiple people managed from the same app
  instance) is planned but not yet implemented — see
  [`docs/ANALYSIS-MULTI-USER.md`](docs/ANALYSIS-MULTI-USER.md) for
  the design.
- User guide localized in EN and IT only — FR/ES UI users see the
  guide in English (graceful fallback).
- Email sending depends on Internet connectivity and SMTP server
  reachability; on transient errors the app retries with backoff
  5s → 30s → 2m, then gives up, logging the error.
- Single-instance is per-user (one Windows session). A second
  Windows user on the same machine can run their own instance.
- The app targets single-user desktop use: no sync between devices.

## License

Apache 2.0 — see [LICENSE](LICENSE).
