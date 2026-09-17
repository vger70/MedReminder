# MedReminder — Technical Analysis and Architecture

Document produced during Phase 1 and Phase 2, as required by section 31
of the specification. It contains no implementation code: its purpose is
to lock down requirements, architectural choices and the incremental
plan before implementation starts.

Epistemic classification used: `[VERIFIED]` (established fact),
`[INFERRED]` (deduction from verified facts), `[UNCERTAIN]` (fact I
cannot confirm without tests or without user clarification).

---

## 1. Phase 1 — Analysis

### 1.1 Ambiguous or incomplete requirements

Only the points that affect the architecture or the data model are
listed. For each one the proposed decision and the rationale are given.
If any of these decisions is not acceptable, it must be corrected
before Phase 3.

1. **Definition of "daily consumption"**. The specification does not
   state whether daily consumption is always computed from the schedule
   (`dose × administrations`) or possibly estimated from the intake
   history.
   Decision: for the MVP, daily consumption is **derived from the
   configured schedule**. Actual intakes (section 6 of the spec) stay
   in the data model for a future empirical estimation, but they do
   not influence the MVP calculation.

2. **Daily administrations**. The specification says "number" (an
   integer), not a list of times. Decision: an integer
   `AdministrationsPerDay` in the MVP; a future
   `AdministrationSchedule` with times is anticipated only as an
   extension, not required now.

3. **Warning threshold — single vs. multi-level**. Section 8 shows
   examples 10/7/5/3, but does not clarify whether they are multiple
   configurable levels for the same medicine or alternatives. MVP
   decision: **a single integer threshold value (days)** per medicine.
   The field can be extended later to an array of thresholds without
   breaking the schema (new `MedicineThresholds` table, or JSON).

4. **Notification-cycle reset**. The spec says "after a new refill the
   cycle must be able to restart". What counts as a refill? Decision:
   **every positive stock movement** of kind `NewPackage`, `ManualAdd`,
   `PositiveCorrection` increments a `StockEpoch` counter on the
   medicine. Notifications are keyed on the current epoch; a new epoch
   automatically resets the cycle.

5. **Temporary suspension**. The spec mentions it (section 5) but does
   not define semantics. Decision: an entity `MedicationSuspension`
   with `StartDate` and `EndDate?`. During suspended periods no
   automatic consumption is generated; days remaining and estimated
   run-out ETA are not computed while the medicine is suspended (or
   they are computed skipping the suspended days, if the suspension
   period is closed in the past).

6. **Therapy end date**. If `EndDate` is set and falls before the
   estimated run-out, does it still make sense to warn about "medicine
   running out"? Decision: yes, the spec requires the reminder for the
   prescription regardless of `EndDate`; but if
   `EstimatedRunOutDate > EndDate` then the system **does not raise a
   warning** (the residual is enough). Rule implemented in the domain,
   tested.

7. **Time zone and DST**. The application is a personal desktop app on
   Windows 11. Decision: **local time via `TimeProvider`**. "Logical"
   dates (therapy start, therapy end, suspensions, consumption day)
   are `DateOnly`. "Event" timestamps (movements, notifications, log)
   are `DateTimeOffset` with local offset, so they survive DST
   transitions.

8. **"Modern and clean" WinForms UI**. WinForms does not natively offer
   the same look as WinUI/WPF. Decision: no third-party skin framework
   in the MVP. Use Segoe UI Variable, `HighDpiMode.PerMonitorV2`,
   double-buffered `DataGridView`, custom colors / renderers on
   `ToolStrip`, owner-drawn where needed. If the user wants a full
   Fluent look, a WinUI 3 port could be considered later — but the
   spec has explicitly excluded that.

9. **Windows notifications on an "unpackaged" app**. Interactive toasts
   on Windows 11 require an AUMID and a Start Menu shortcut.
   `[UNCERTAIN]` whether `CommunityToolkit.WinUI.Notifications` (or its
   predecessor `Microsoft.Toolkit.Uwp.Notifications`) is immediately
   compatible with `net10.0`: it will work, but the exact TFM string
   and the COM activator will need to be verified at first
   compilation. Plan B: `System.Windows.Forms.NotifyIcon.ShowBalloonTip`
   (always works, less "modern" but no packaging constraints).
   Decision: implementation behind `IWindowsNotificationService`, with
   two adapters selectable via configuration. The first attempt will
   be toast via `Microsoft.Toolkit.Uwp.Notifications`, with an
   automatic fallback to `NotifyIcon` if initialization fails.

10. **SMTP client**. `[VERIFIED]` `System.Net.Mail.SmtpClient` has been
    marked as "obsoleted for new development" by Microsoft since .NET
    6. Decision: dependency on **MailKit**
    (`MailKit`/`MimeKit`, maintained, de facto standard). Wrapped
    behind `IEmailNotificationService`, so the provider is
    replaceable.

11. **Email credential storage**. Decision: the SMTP password is
    encrypted with **DPAPI**
    (`System.Security.Cryptography.ProtectedData`, `CurrentUser`
    scope) and saved as a base64 blob in the user's configuration
    file. Alternative (Windows Credential Manager via `CredWrite`) is
    formally more correct but requires P/Invoke or an extra NuGet;
    DPAPI is sufficient for a single-user local application.

12. **Auto-start with Windows**. Decision: registry key
    `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` (per-user,
    does not require elevated privileges, reversible). No Windows
    service in the MVP, consistent with section 11.

13. **Data folder**. Decision: `%LOCALAPPDATA%\MedReminder\` for the
    database, logs and settings. Reason: `LocalAppData` is appropriate
    for local non-roaming data, does not require special permissions,
    does not travel over the network like `AppData\Roaming`, and is
    not inside the install directory (respects section 15).

14. **Concurrency**. One process per user. Decision: no distributed
    lock. At most a named mutex at startup to prevent multiple
    instances, plus SQLite in WAL mode to survive abrupt shutdowns.

### 1.2 Edge cases identified

They must be tested in the domain (see section 5 of this document):

- `dailyRate == 0` → no ETA, no automatic warning.
- `currentStock < 0` (manual correction driving it below zero) → clamp
  to 0, log warning, the app does not "recover" the debt.
- Suspended medicine → no automatic consumption during the suspension;
  the daily-consumption catch-up skips suspended days.
- App not opened for N days → daily-consumption catch-up at the next
  startup; missed notifications are generated only once (per epoch),
  not N times.
- Mid-therapy dose / frequency change → new entry in
  `MedicationScheduleHistory` (versioned schedule), daily consumption
  is recomputed forward from the change date.
- Refill during the threshold period → new `StockEpoch`, the next
  notification is allowed once the new epoch falls within the
  threshold.
- Leap year / DST change → covered by using `DateOnly` for day logic
  and by injecting `TimeProvider` in tests.
- Transient SMTP failure → retry with limited back-off (max 3
  attempts, escalating 5s / 30s / 2m), then log a failed
  `NotificationEvent`.
- Read-only database or full disk → the application does not crash;
  the UI shows an error banner, periodic checks continue but cannot
  write.
- Missing or plainly wrong SMTP configuration → the email sub-system
  is marked "disabled"; Windows notifications keep working.

### 1.3 Architectural decisions that need confirmation

The decisions above are autonomous, motivated technical proposals.
Explicit confirmation is requested only for the points that change
the functional perimeter:

- **Q1**: Single threshold per medicine in the MVP (§1.1 item 3),
  multiple thresholds later. OK?
- **Q2**: `EndDate` suppresses the notification when the ETA exceeds
  it (§1.1 item 6). OK?
- **Q3**: Windows toast via `Microsoft.Toolkit.Uwp.Notifications` with
  fallback to `NotifyIcon` (§1.1 item 9). OK?
- **Q4**: DPAPI (`CurrentUser`) for the SMTP password, not Credential
  Manager (§1.1 item 11). OK?

If no feedback is provided, the proposals above are followed.

---

## 2. Phase 2 — Architecture

### 2.1 Solution structure

```
MedReminder.sln
src/
  MedReminder.Domain/            netstandard2.1 or net10.0
  MedReminder.Application/       net10.0
  MedReminder.Infrastructure/    net10.0-windows10.0.19041.0
  MedReminder.UI/                net10.0-windows10.0.19041.0  (WinForms, exe output)
tests/
  MedReminder.Domain.Tests/      net10.0     xUnit
  MedReminder.Application.Tests/ net10.0     xUnit
  MedReminder.Infrastructure.Tests/ net10.0-windows10.0.19041.0 xUnit
```

Rationale:
- `Domain` on `netstandard2.1` (or pure `net10.0`) with no Windows
  dependencies, 100% testable without the Windows SDK.
- `Application` on `net10.0`, contains use cases, services,
  infrastructure interfaces (ports). It does not reference EF Core,
  MailKit or Toast.
- `Infrastructure` on `net10.0-windows10.0.19041.0` so it can use
  Windows APIs (registry, DPAPI, notifications, tray). Contains EF
  Core, MailKit, file logger, DPAPI adapter, auto-start registration.
- `UI` is the only exe. It references `Application` and
  `Infrastructure`.
- No "Shared" or "Common" project: not needed.

### 2.2 Proposed NuGet dependencies

Deliberately minimal in number and motivation.

| Package | Project | Reason |
|---|---|---|
| `Microsoft.Extensions.Hosting` | UI | Generic host: DI, config, logging, hosted services |
| `Microsoft.Extensions.Configuration.Json` | UI | `appsettings.json` + user override |
| `Microsoft.EntityFrameworkCore.Sqlite` | Infrastructure | Persistence + migrations |
| `Microsoft.EntityFrameworkCore.Design` | Infrastructure (tool) | `dotnet ef migrations` |
| `MailKit` | Infrastructure | Modern SMTP (replacement for `SmtpClient`) |
| `Microsoft.Toolkit.Uwp.Notifications` | Infrastructure | Windows toast, unpackaged |
| `Serilog.Extensions.Hosting` + `Serilog.Sinks.File` | Infrastructure | Structured rolling file log |
| `xunit`, `xunit.runner.visualstudio`, `FluentAssertions` | Tests | Standard test framework |

No "skin" WinForms libraries, no AutoMapper, no MediatR. If a specific
case justifies one, the rationale will be documented first.

`[INFERRED]` The EF Core 10 stable tag is available and supports
`net10.0` (EF Core releases align with the .NET major). If, at the
first restore, it is not yet on the public feed, fall back to EF Core
9 (compatible with `net10.0`) and flag it.

### 2.3 Data model

Names in English in code, consistent with section 16 of the spec.
Names in Italian in the UI.

**Medicine**
- `Id: Guid`
- `Name: string`  (required)
- `ActiveIngredient: string?`
- `Package: string?`
- `Unit: string`  (unit code, e.g. "tablets", "ml", …)
- `DosePerAdministration: decimal`  (units per single administration)
- `AdministrationsPerDay: int`
- `StartDate: DateOnly`
- `EndDate: DateOnly?`
- `ThresholdDays: int`
- `DoctorName: string?`
- `Notes: string?`
- `IsActive: bool`
- `StockEpoch: int`  (incremented on every positive movement)
- `NotificationChannels: NotificationChannels`  (flags: Email, Windows)
- `CreatedAt: DateTimeOffset`
- `UpdatedAt: DateTimeOffset`

**StockMovement**
- `Id: Guid`
- `MedicineId: Guid`
- `OccurredAt: DateTimeOffset`
- `Kind: StockMovementKind`  (`InitialLoad`, `NewPackage`, `ManualAdd`,
  `Consumption`, `PositiveCorrection`, `NegativeCorrection`)
- `QuantityDelta: decimal`  (sign consistent with `Kind`)
- `StockEpoch: int`  (the epoch active at the time of the movement)
- `Notes: string?`

**MedicationSuspension**
- `Id: Guid`
- `MedicineId: Guid`
- `StartDate: DateOnly`
- `EndDate: DateOnly?`  (null = open suspension)
- `Reason: string?`

**MedicationScheduleHistory** (to handle mid-therapy dose / frequency
changes without destroying the history)
- `Id: Guid`
- `MedicineId: Guid`
- `EffectiveFrom: DateOnly`
- `DosePerAdministration: decimal`
- `AdministrationsPerDay: int`

Note: `Medicine.Dose*` and `Medicine.AdministrationsPerDay` are the
"current" state; `MedicationScheduleHistory` is the timeline. Daily
consumption is computed from the history, not from the current state.

**MedicationIntake** (planned but not used by the MVP; present so its
future implementation is not blocked, section 6)
- `Id: Guid`
- `MedicineId: Guid`
- `ScheduledAt: DateTimeOffset?`
- `ActualAt: DateTimeOffset?`
- `Quantity: decimal`
- `Status: IntakeStatus`  (`Taken`, `Skipped`, `Cancelled`,
  `ManualCorrection`)
- `Notes: string?`

**NotificationEvent**
- `Id: Guid`
- `MedicineId: Guid`
- `StockEpoch: int`
- `TriggeredAt: DateTimeOffset`
- `Channel: NotificationChannels`
- `DaysRemainingAtSend: int`
- `Success: bool`
- `ErrorMessage: string?`

**ApplicationSetting** (key/value, for global settings)
- `Key: string`  (PK)
- `Value: string`

SMTP settings and application preferences use `IOptions<T>` projected
from this table (or from `appsettings.json` for non-sensitive values).

### 2.4 Consistency rules

- `CurrentStock(medicineId) = Σ StockMovement.QuantityDelta` for that
  medicine. It is not persisted, it is a **function**. If it were
  stored for performance, it would be a denormalized field with a
  consistency test.
- Current `StockEpoch` of the medicine = max epoch on the positive
  movements. The field on `Medicine` is a cache; consistency test in
  `NotificationCycleTests`.
- Generating `StockMovement.Kind = Consumption` is the responsibility
  of the `ConsumptionCatchUpService` (idempotent for
  `(medicineId, date)` thanks to a unique constraint on
  `(MedicineId, OccurredAt.Date, Kind)` for `Kind = Consumption`).
- Suspension prevents consumption from being generated for days that
  fall inside the suspended period.

### 2.5 Main interfaces (ports)

Declared in `MedReminder.Application` or `MedReminder.Domain`
depending on the layer. Signatures only, no implementation — this is
still Phase 2.

- `IMedicineRepository` — CRUD on `Medicine` and its collections.
- `IStockMovementRepository`
- `INotificationEventRepository`
- `IUnitOfWork` — transactions.
- `IEmailNotificationService` — email sending; connection test.
- `IWindowsNotificationService` — toast / tray.
- `IAutoStartService` — register / remove the Run registry entry.
- `ICredentialProtector` — DPAPI wrapping / unwrapping.
- `IClock` ⇄ `TimeProvider` (use `TimeProvider` directly, standard on
  .NET 8+; no custom wrapper).
- `IMedicationMonitoringService` — periodic control cycle.
- `IConsumptionCatchUpService` — daily-consumption materialization.
- `IStockService` — application API to add / correct stock.
- `IBackupService` — export / import the DB.
- `IEmailComposer` — build the email payload from the medicine state
  (separated from transport so it is testable).

### 2.6 Scheduler / hosted service

A single `IHostedService`: `MedicationMonitorHostedService`.

- On `StartAsync`: consumption catch-up + first run of the check.
- Periodic cycle configurable (default 30 minutes in
  `appsettings.json`), implemented with `PeriodicTimer` +
  `CancellationToken`.
- On every run:
  1. Reload active medicines.
  2. Ask `IConsumptionCatchUpService` to materialize any consumption
     days not yet recorded.
  3. For each medicine compute `DaysRemaining` and check the warning
     condition.
  4. Query `NotificationEventRepository` with
     `(MedicineId, StockEpoch)`: if no event exists yet for the
     current epoch within the threshold, compose and send the
     notification (channels configured for the medicine) and record a
     `NotificationEvent`.
  5. On email error, retry with back-off; a definitive failure is
     recorded as a failed `NotificationEvent` (`Success = false`).
- No critical section is needed: the service is single-threaded and no
  other writer acts concurrently on the same DB.
- On `StopAsync`: the `CancellationToken` breaks the cycle within a
  couple of seconds. No pending write is abandoned (transactions are
  per-operation).

### 2.7 WinForms UI

Form structure:

- `MainForm`
  - `DataGridView` for medicines (columns: Name, Remaining,
    Consumption/day, Days, ETA, Status).
  - Toolbar: "New", "Edit", "Add stock", "Register intake",
    "Check now", "Settings".
  - Row status colored: normal / warning (within threshold) /
    depleted / suspended.
- `MedicineEditDialog` — new / edit.
- `StockAdjustmentDialog` — load, correction ±, manual consumption.
- `SettingsDialog` — tabs: General, Notifications
  (Email / Windows / Both / None), Email SMTP (host, port, TLS, user,
  password), Backup, Auto-start.
- `NotifyIcon` + menu (Open, Check now, Settings, Exit).
- `LogViewerDialog` — tail of the current log file.

The ViewModel is kept minimal. Each form receives from its
constructors the application services it needs (DI via the root
`IServiceProvider`). No full MVVM on WinForms: it would be
over-engineering.

### 2.8 Persistence and migrations

- SQLite file `medreminder.db` in `%LOCALAPPDATA%\MedReminder\`.
- EF Core code-first, migrations versioned in the repository under
  `src/MedReminder.Infrastructure/Migrations/`.
- `DbContext` calls `Database.Migrate()` at startup (idempotent).
- WAL mode, `foreign_keys = ON`.
- Backup: copy of the DB file after a `WAL checkpoint TRUNCATE`.
  Import: overwrite of the DB file after confirmation and renaming of
  the existing file to `medreminder.db.bak-yyyyMMddHHmmss`.

### 2.9 Notifications

- `IEmailNotificationService` (MailKit):
  host / port / TLS / user / password / timeout / from / to;
  `SendAsync(subject, body, CancellationToken)`;
  `TestConnectionAsync()`.
- `IEmailComposer`:
  `Compose(medicine, remaining, daysRemaining, eta)` → `EmailMessage`.
  Tested in isolation with a snapshot of the text.
- `IWindowsNotificationService`: `NotifyAsync(title, body)` with toast
  implementation + balloon fallback.
- `NotificationChannels` is a `[Flags]` enum on `Medicine` that
  decides which channels to use. The monitor deduplicates by
  `(MedicineId, StockEpoch)`, not by channel: once an epoch has been
  notified, it is not notified again.

### 2.10 Logging

- Serilog: daily rolling file, 30-day retention, in
  `%LOCALAPPDATA%\MedReminder\logs\medreminder-.log`.
- No email content, no password, no free-form medical note ever
  reaches the log. Only: `MedicineId`, `Name`, quantity, days
  remaining, operation outcome.
- Level configurable in `appsettings.json`. Default `Information`.

### 2.11 Security and privacy

- SMTP password: DPAPI `CurrentUser`, base64, saved in
  `smtp.protected` next to the DB (not in the repository, not in the
  versioned `appsettings.json`).
- The versioned `appsettings.json` contains only harmless defaults.
- `.gitignore` must exclude `bin/`, `obj/`, `*.user`, `.vs/`, `*.db*`,
  `smtp.protected`, `logs/`.
- The email sent contains: medicine name, days remaining, quantity,
  generic suggestion to request a prescription. No free-form clinical
  information.

### 2.12 Auto-start

`IAutoStartService` with `IsEnabled`, `Enable()`, `Disable()`.
Implemented by writing / removing the value
`HKCU\Software\Microsoft\Windows\CurrentVersion\Run\MedReminder`,
pointing to the executable with argument `--minimized`.

### 2.13 Tray

In `MainForm`:
- On close, if the "close to tray" setting is on, `e.Cancel = true`
  and `Hide()`.
- Double-click on the icon → `Show()` + `WindowState = Normal`.
- The "Exit" menu entry calls `Application.Exit()`, bypassing the
  tray.
- The CLI argument `--minimized` starts the app directly in the tray.

---

## 3. Incremental implementation plan

Every increment ends with: `dotnet build` OK, `dotnet test` OK, a
descriptive commit, push. No PR is opened until the user explicitly
requests one.

**Increment 0 — Solution bootstrap**
- Solution file + 4 projects + 3 test projects.
- `Directory.Build.props` with `Nullable`, `ImplicitUsings`,
  `TargetFramework`.
- `.gitignore`, `.editorconfig`, updated `README.md`.
- `dotnet build` green, one placeholder test green.

**Increment 1 — Domain**
- Pure entities: `Medicine`, `StockMovement`,
  `MedicationSuspension`, `MedicationScheduleHistory`,
  `MedicationIntake`, `NotificationEvent`.
- Enumerations.
- `MedicineStock` (value object) with the logic that sums the
  movements, clamps to zero and recomputes the epoch.
- `DailyConsumption` with a versioned schedule.
- `RunOutForecast` that computes `DaysRemaining` and
  `EstimatedRunOutDate` using `TimeProvider`.
- `NotificationCycle` that decides "notify yes / no" given the
  threshold and the existing `NotificationEvent` values for the
  epoch.
- Unit tests for every edge case in section 1.2.

**Increment 2 — Application**
- Repository interfaces, `IEmailNotificationService`,
  `IWindowsNotificationService`, `IAutoStartService`,
  `ICredentialProtector`.
- Use cases: `AddMedicine`, `UpdateMedicine`, `DeactivateMedicine`,
  `AddStock`, `RegisterConsumption`, `AdjustStock`,
  `SuspendMedication`, `ResumeMedication`, `RunPeriodicCheck`.
- `MedicationMonitor` implemented in Application (no scheduler).
- `ConsumptionCatchUp` implemented with `TimeProvider`.
- Tests with in-memory repositories (fakes for increments 1-2).

**Increment 3 — Infrastructure: persistence**
- EF Core Sqlite `DbContext`, entity configurations, initial
  migration.
- Repositories.
- `IUnitOfWork` with an EF Core transaction.
- Integration tests with SQLite in-memory (`:memory:`) or a temporary
  file.
- Base backup / import service.

**Increment 4 — Infrastructure: notifications + credentials + auto-start**
- MailKit adapter + `IEmailComposer`.
- Toast adapter + `NotifyIcon` fallback.
- `DpapiCredentialProtector`.
- `RegistryAutoStartService`.
- Unit tests where meaningful; the toast cannot be tested
  automatically, so the manual test is documented.

**Increment 5 — Hosted service**
- `MedicationMonitorHostedService` + `PeriodicTimer`.
- Composition root in `Program.cs` of `MedReminder.UI` with the
  generic host.
- Test: force a short period in an integration test.

**Increment 6 — UI**
- `MainForm`, edit dialog, stock dialog, settings, log viewer.
- Tray + `--minimized` argument.
- UI ↔ services bindings.

**Increment 7 — Hardening**
- SMTP retry with back-off.
- Single-instance mutex.
- Non-fatal DB errors (UI banner).
- DST and day-change verification with dedicated tests.
- Verification that notifications are not duplicated on restart.

**Increment 8 — Packaging and documentation**
- Publish `net10.0-windows` x64 self-contained.
- Complete README (§28), non-medical-device disclaimer.
- Minimal technical documentation in `docs/` (already started with
  this file).
- No installer in the MVP (outside the minimum scope).

---

## 4. File layout (expected outcome after Increment 0)

```
MedReminder/
  MedReminder.sln
  Directory.Build.props
  .gitignore
  .editorconfig
  README.md
  LICENSE
  docs/
    ANALYSIS.md            (this file)
  src/
    MedReminder.Domain/
      MedReminder.Domain.csproj
    MedReminder.Application/
      MedReminder.Application.csproj
    MedReminder.Infrastructure/
      MedReminder.Infrastructure.csproj
    MedReminder.UI/
      MedReminder.UI.csproj
      Program.cs
  tests/
    MedReminder.Domain.Tests/
    MedReminder.Application.Tests/
    MedReminder.Infrastructure.Tests/
```

---

## 5. What must be approved before proceeding

1. The four decisions marked Q1–Q4 in §1.3.
2. The solution structure in §2.1 and the NuGet dependency list in
   §2.2.
3. The data model in §2.3 (in particular the presence of
   `MedicationScheduleHistory` and `MedicationSuspension`).
4. The incremental plan in §3 and the order of the steps.

Once approval (or corrections) is received, implementation proceeds
with **Increment 0** and then Increment 1, stopping at every
increment with build + test green and a short report of what changed,
as required by section 29 of the specification.
