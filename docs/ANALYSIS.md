# MedReminder — Architecture

This document describes the architecture of MedReminder **as built**
(release line 2.10.x). It is the entry point for anyone changing the
code: it states the layering rules, where each responsibility lives,
how data is stored and how the background work is scheduled.

Feature-level design lives in the per-feature analyses under
[`docs/analysis/`](analysis/) (§12). The original pre-implementation
plan (Phase 1 and Phase 2, before Increment 0) is preserved unchanged
in [`docs/analysis/ANALYSIS-MVP.md`](analysis/ANALYSIS-MVP.md). Code
comments that cite `ANALYSIS §1.x` or `ANALYSIS §2.x` refer to the
numbering of that file; §11 below summarizes which of its decisions
still hold.

When a change alters anything described here (a project, a
dependency, a table, a runtime file, a hosted service, a port), update
this document in the same pull request.

---

## 1. Scope

- Windows desktop application (Windows 10 22H2 or Windows 11, x64),
  single process per Windows user, WinForms UI.
- Tracks medicine stock, estimates the run-out date from the therapy
  schedule and warns the user (Windows notification and/or email)
  before the medicine runs out. Optional reminder at each dose time.
- Local-first: one SQLite database per profile under
  `%LOCALAPPDATA%\MedReminder\`. No server component. The only
  outbound network calls are SMTP (user-configured), the passive
  GitHub Releases update check and the remote AIFA catalogue feed
  (§9.5) and, only when the user signs
  in to one, the OneDrive (Microsoft Graph) or Google Drive APIs for
  cloud backups and sync (§8.3, §4.4). Sync data leaves the device
  encrypted end to end.
- Not a medical device: the application reminds, it does not advise.

---

## 2. Solution structure

| Project | Target framework | Output | References |
|---|---|---|---|
| `MedReminder.Domain` | `net10.0` | library | — |
| `MedReminder.Application` | `net10.0` | library | Domain |
| `MedReminder.Infrastructure.Portable` | `net10.0` | library | Domain, Application |
| `MedReminder.Infrastructure` | `net10.0-windows` (WinForms enabled) | library | Domain, Application, Infrastructure.Portable |
| `MedReminder.UI` | `net10.0-windows10.0.19041.0` | `WinExe` | Application, Infrastructure |
| `MedReminder.DataImporter` | `net10.0` | console exe | — (standalone tool) |

| Test project | Target framework | Under test |
|---|---|---|
| `MedReminder.Domain.Tests` | `net10.0` | Domain |
| `MedReminder.Application.Tests` | `net10.0` | Application (in-memory fakes) |
| `MedReminder.Infrastructure.Portable.Tests` | `net10.0` | Infrastructure.Portable (SQLite persistence, archive cipher and reader, localization) — runs on any OS |
| `MedReminder.Infrastructure.Tests` | `net10.0-windows` | Infrastructure (DPAPI, registry, catalogue import, export / import shells — Windows only) |
| `MedReminder.UI.Tests` | `net10.0-windows10.0.19041.0` | Hosted services, UI controls |
| `MedReminder.DataImporter.Tests` | `net10.0` | AIFA CSV loader |

All test projects use xUnit and FluentAssertions.

### 2.1 Layering rules

```
UI  ──►  Application  ──►  Domain
 │            ▲
 └──►  Infrastructure ──►  Infrastructure.Portable
       (both implement Application ports)
```

- **Domain** contains entities and pure calculations. No I/O, no
  logging, no Windows API, no reference to Application or
  Infrastructure. Time is always passed in (`DateOnly today`), never
  read.
- **Application** contains use cases, the monitoring services and the
  ports (`Abstractions/`) that Infrastructure implements. It
  references only `Microsoft.Extensions.*.Abstractions`: no EF Core,
  no MailKit, no Windows API.
- **Infrastructure.Portable** implements the platform-neutral ports:
  EF Core / SQLite persistence and `DatabaseInitializer`, the archive
  cipher, `ArchiveReader` and `ProfileDatabaseBuilder` (read half of
  the `.mrz` import), the localization loader. No Windows API, no
  `%LOCALAPPDATA%`: paths come from `IAppDataLocation` or arguments.
  `AddMedReminderPortableInfrastructure` registers it. Namespaces stay
  `MedReminder.Infrastructure.*`. Introduced by Phase 1 of
  [`analysis/ANALYSIS-B1-MOBILE-SYNC.md`](analysis/ANALYSIS-B1-MOBILE-SYNC.md)
  so a mobile host can reuse it.
- **Infrastructure** implements the Windows-bound ports: MailKit,
  DPAPI, registry, file-based stores under `%LOCALAPPDATA%`
  (`AppDataPaths`, `AppDataLocation`), the export / import services
  (Windows shells over the portable reader), catalogue import, GitHub
  update check.
- **UI** is the composition root (`Program.cs`) and the only process
  entry point. It owns the WinForms forms, the tray icon, the toast
  adapter, the webcam capture adapter (`WindowsCameraCaptureService`,
  the only project whose TFM carries the WinRT projections it needs)
  and the five hosted services (§6). It changes profile data
  only through Application use cases, never through repository write
  methods, `IUnitOfWork` or the `DbContext`;
  `UiWritePathGuardTests` in `MedReminder.Application.Tests` enforces
  it (B.1 Phase 2a, precondition P8).
  Every form derives from `MedReminderFormBase`, which on load scales
  fonts by the profile's text size and bounds, grid rows and list
  columns by text size times display DPI / 96: the forms are built in
  96-DPI pixels and do not set `AutoScaleMode`. Text colours come from
  `UiColors`, which yields to the Windows high-contrast theme.
- **DataImporter** is a maintainer tool that loads AIFA CSV files into
  PostgreSQL. It shares no code with the runtime and is not shipped.
  See [`DATA_IMPORTER.md`](DATA_IMPORTER.md).

### 2.2 Shared build settings

`Directory.Build.props` sets `Nullable` (nullable warnings are
errors), `ImplicitUsings`, `EnforceCodeStyleInBuild`, the centralized
`VersionPrefix`, the assembly metadata, and the
`StripReleaseDebugArtifacts` target that deletes `*.pdb` and `*.xml`
from Release build and publish output. That target must not be
removed or weakened. Publishing is described in
[`PACKAGING.md`](PACKAGING.md).

---

## 3. Dependencies

| Package | Project | Purpose |
|---|---|---|
| `Microsoft.Extensions.Hosting` | UI, DataImporter | Generic host: DI, configuration, logging, hosted services |
| `Microsoft.Extensions.Configuration.Json` | UI, DataImporter | JSON configuration chain (§5.3) |
| `Microsoft.Extensions.Logging.Abstractions`, `DependencyInjection.Abstractions` | Application, Infrastructure, Infrastructure.Portable | Logging and DI contracts without implementations |
| `Microsoft.Extensions.Options` | Infrastructure.Portable | `IOptions<T>` consumption |
| `Microsoft.Extensions.Options.ConfigurationExtensions` | Infrastructure | `IOptions<T>` binding |
| `Microsoft.EntityFrameworkCore.Sqlite` | Infrastructure.Portable | Persistence |
| `Microsoft.EntityFrameworkCore.Design` | Infrastructure | Design-time only (`PrivateAssets`) |
| `MailKit` | Infrastructure | SMTP. `System.Net.Mail.SmtpClient` must not be used |
| `Microsoft.Identity.Client` (MSAL) | Infrastructure | OneDrive sign-in, public client, system browser (B.1 Phase 4a). Graph itself is called with plain `HttpClient`. Google Drive (Phase 4b) uses no package: its OAuth flow and the Drive REST API are plain `HttpClient` too |
| `Konscious.Security.Cryptography.Argon2` | Infrastructure.Portable | Argon2id key derivation for `.mrz` archives |
| `Microsoft.Toolkit.Uwp.Notifications` | UI | Windows toasts for an unpackaged app |
| `Microsoft.Web.WebView2` | UI | Rendering of the embedded user guide |
| `Markdig` | UI | Markdown to HTML for the embedded user guide |
| `QRCoder` (MIT) | UI | Sync pairing QR code (B.1 Phase 4c), rendered as PNG bytes without System.Drawing |
| `ZXing.Net` (Apache 2.0) | UI | Webcam barcode decoding (A2 phase 2); frames reach it as BGRA bytes, no Bitmap binding |
| `Serilog.Extensions.Hosting`, `Serilog.Sinks.File` | UI | Rolling file log |
| `CsvHelper`, `Npgsql`, `Microsoft.Data.Sqlite`, `Serilog.Sinks.Console` | DataImporter | CSV streaming, PostgreSQL, SQLite export, console log |
| `xunit`, `xunit.runner.visualstudio`, `FluentAssertions`, `Microsoft.NET.Test.Sdk` | Tests | Test framework |

AES-GCM, PBKDF2, SHA-256 and DPAPI (`ProtectedData`) come from the
.NET base class library. No mapper, mediator or WinForms skin library
is used.

---

## 4. Domain model

### 4.1 Persisted entities (EF Core, one database per profile)

| Entity (table) | Role | Key fields |
|---|---|---|
| `Medicine` (`Medicines`) | Aggregate root | `Name`, `Unit`, `DosePerAdministration`, `AdministrationsPerDay`, `StartDate`, `EndDate?`, `ThresholdDays`, `IsActive`, `StockEpoch`, `NotificationChannels`, `RemindOnDose`, catalogue link (`NationalCode`, `AtcCode`, `LinkedReferenceMedicineId`) |
| `StockMovement` (`StockMovements`) | Immutable stock ledger | `Kind`, `QuantityDelta`, `OccurredAt`, `StockEpoch`, `Origin` |
| `MedicationScheduleHistory` (`MedicationScheduleHistories`) | Versioned schedule | `EffectiveFrom`, legacy dose × frequency, `ScheduleKind`, `SchedulePayload` (JSON) |
| `MedicationAdministrationSlot` (`MedicationAdministrationSlots`) | Individual daily intakes | `SetId`, `Dose`, `Time?`, `TimingLabel`, `Order` |
| `MedicationAdministrationSlotSet` (`MedicationAdministrationSlotSets`) | Recorded version of a medicine's slots | `EffectiveFrom`, `RecordedAt`; the current slots are those of the latest recorded set |
| `StockCount` (`StockCounts`) | Stock-count fact written by `ReconcileStock` (B.1 Phase 2c-2) | `CountDay`, `CountedQuantity`, `TakenToday`, `ThresholdAtCount`, `RecordedAt`, stored outcome (`Correction`, `MaterializesCountDay`, `AdvancesEpoch`, ...) |
| `LedgerCutoff` (`LedgerCutoff`) | Single row: ledger freeze of a pre-B.1 database | `CutoffDay`, `FrozenAt` |
| `MedicationSuspension` (`MedicationSuspensions`) | Therapy pause | `StartDate`, `EndDate?` (null = open) |
| `MedicationIntake` (`MedicationIntakes`) | User-recorded intake | `Day`, `Status` (`Taken`, `Skipped`, `Cancelled`, `ManualCorrection`), `Quantity`, `RecordedAt` |
| `MedicineActivityChange` (`MedicineActivityChanges`) | Dated activation / deactivation (B.1 Phase 2c-2) | `Day`, `Active`, `RecordedAt` |
| `FactRetraction` (`FactRetractions`) | Tombstone of a retracted fact (B.1 Phase 2d) | `FactId` (unique), `Kind`, `RecordedAt` |
| `SyncOperation` (`SyncOperations`) | Local operation log for sync (B.1 Phase 3a); empty while sync is disabled. From Phase 3b it also records the operations applied from other devices | HLC (`HlcPhysicalMs`, `HlcCounter`, `DeviceId`), `Generation`, `Type`, `SchemaVersion`, `MedicineId`, `Payload` (JSON), `SegmentSeq?` |
| `SyncFieldVersion` (`SyncFieldVersions`) | Every version of a last-writer-wins register (B.1 Phase 3b) | `EntityId`, `Register`, HLC, `Value`, base HLC |
| `SyncConflict` (`SyncConflicts`) | Local conflict list, §4.5 cases only (B.1 Phase 3b) | `Kind`, `SubjectId`, `Register`, winning / losing value and device |
| `SyncPeer` (`SyncPeers`) | Sync progress per device of the group (B.1 Phase 3c) | `DeviceId`, `Generation`, `Seq` (applied, or published for this device), checkpoint counters |
| `NotificationEvent` (`NotificationEvents`) | Low-stock notification log | `StockEpoch`, `Channel`, `DaysRemainingAtSend`, `Success` |
| `DoseReminderEvent` (`DoseReminderEvents`) | Dose-time reminder dedup | unique `(MedicineId, SlotKey, LocalDate)` |

`StockMovementKind`: `InitialLoad`, `NewPackage`, `ManualAdd`,
`Consumption`, `PositiveCorrection`, `NegativeCorrection`.
`StockMovementOrigin`: `Legacy` (written before the B.1 ledger patch),
`User` (initial load, `AddStock`, `AdjustStockDown`), `Derived`
(automatic and intake consumption, backdated reversals, count
corrections). Enum values are persisted as integers and must stay
stable.

Foreign keys to `Medicines` use `Restrict`: a medicine with history is
deactivated, not deleted. `DeleteMedicine` removes a medicine only
while no fact was recorded for it (no `User` or `Legacy` stock entry,
intake, count or suspension); `MedicineDeletionRepository` then
removes the medicine and every row that refers to it in one unit of
work, and keeps its `SyncOperations` rows. The main window hides
deactivated medicines unless *Therapy → Show inactive medicines* is
checked (session only).

### 4.2 Reference catalogue (outside the EF model)

`ReferenceMedicine` and `ReferenceActiveIngredient` are read models
over three tables created by raw DDL (`reference_medicines`,
`reference_active_ingredients`, `reference_medicine_ingredients`, see
`Catalogue/CatalogueSchema.cs`). They are populated from snapshots
embedded in the Infrastructure assembly (IT/AIFA, EU/EMA, ES/AEMPS,
FR/BDPM) and are refreshed idempotently by snapshot version.
`Medicine.LinkedReferenceMedicineId` is a weak reference: no physical
foreign key, because catalogue rows are replaced on refresh. See
[`analysis/ANALYSIS-DRUG-CATALOGUE.md`](analysis/ANALYSIS-DRUG-CATALOGUE.md)
and [`CATALOGUE-DATA.md`](CATALOGUE-DATA.md).

### 4.3 Calculations (`Domain/Calculations`)

- **`MedicineStock`** — current stock = Σ `QuantityDelta`, clamped at
  zero. Never stored.
- **`DailyConsumption`** — daily rate for a given day, resolved in
  this order:
  1. if the medicine has administration slots: sum of slot doses;
  2. otherwise the `MedicationScheduleHistory` entry with the latest
     `EffectiveFrom <= day`, dispatched through `ScheduleCodec` to a
     `Schedule` shape: `FixedDaily`, `Weekly`, `Cyclic`, `Tapering`,
     `SteppedTapering`, `Prn` (PRN always yields zero);
  3. no positive rate → 0 (no forecast).
- **`ConsumptionMaterializer`** — plans automatic consumption days
  within the therapy window, skipping suspended days.
- **`RunOutForecast`** — days remaining and run-out date; `null` when
  suspended today or when the rate is zero; `(0, today)` when stock is
  zero.
- **`NotificationCycle`** — decides whether a low-stock warning is
  due: inside `ThresholdDays`, not suppressed by `EndDate` (therapy
  ending before run-out), and no successful `NotificationEvent` on the
  current `StockEpoch`.
- **`SuspensionState`** — whether a date falls in a suspension.
- **`LedgerDeriver`** (`Domain/Ledger`, B.1 Phase 2c) — derives a
  medicine's ledger and `StockEpoch` from its facts (stock entries,
  intakes, counts, schedule, slots, suspensions, activity) after the
  ledger cutoff. Used through `Application/Ledger/LedgerSynchronizer`;
  tested against the use cases in
  `tests/MedReminder.Application.Tests/Ledger`
  ([`analysis/ANALYSIS-B1-MOBILE-SYNC.md`](analysis/ANALYSIS-B1-MOBILE-SYNC.md) §4.3).

### 4.4 Invariants

- Stock is a function of the movement ledger. Corrections are new
  movements, never edits. Every writer sets `Origin`; `Legacy` rows
  are never rewritten.
- Slot changes append a `MedicationAdministrationSlotSet`, never delete
  slots; every calculation reads the latest recorded set.
- **Facts and derived rows** (B.1 Phase 2c-2,
  [`analysis/ANALYSIS-B1-MOBILE-SYNC.md`](analysis/ANALYSIS-B1-MOBILE-SYNC.md)
  §4.3). Facts: `User` stock entries (initial load, `AddStock`,
  `AdjustStockDown`), intakes, `StockCount`s, schedule rows,
  suspensions, slot sets, activity changes. `Derived` rows
  (consumption, reversals, count corrections) are produced only by
  `LedgerDeriver` and written by `LedgerSynchronizer`, which replaces
  them by deterministic id; `Legacy` rows (before the ledger freeze)
  are never touched and days up to the cutoff are never derived.
- `ConsumptionCatchUp` synchronizes every medicine, active or not:
  automatic consumption up to **yesterday** on days with no intake of
  any status, when the medicine is active on that day (activity
  history), in the therapy window and not suspended. `RegisterIntake`
  and `ReconcileStock` synchronize their medicine at once.
- The first intake recorded for a day carrying `Legacy` consumption
  reverses it with a `PositiveCorrection`; on a derived day the
  automatic row is simply no longer produced. `StockEpoch` is not
  incremented.
- `ReconcileStock` (guided stock count) stores the count as a fact with
  its outcome: correction `counted - expected`, where expected is the
  derived start-of-day stock minus the part of today's scheduled
  consumption already taken. When all of today's quantity is taken,
  today's consumption is booked with the quantity fixed at count time;
  a later intake for today removes it. A positive correction advances
  the epoch unless the forecast after it is still within
  `ThresholdDays`; a negative one never does.
- `StockEpoch` = the epoch baseline at the freeze + positive `User`
  entries + counts that advanced it; the synchronizer keeps
  `Medicine.StockEpoch` and `Medicine.StockEpochFactId` (the fact that
  opened the epoch) equal to the derivation. A new epoch restarts the
  warning cycle.
- A fact (stock entry, intake, count, suspension) can be retracted
  (`RetractFact`, *Stock → History*) only if it is not `Legacy` and no
  stock count was recorded after it; the row is removed, a tombstone is
  kept and the ledger is derived again.
- A medicine can be deleted (`DeleteMedicine`, *Therapy → Delete*)
  only while it has no recorded fact; schedule rows, slot sets,
  activity changes and derived rows go with it. With sync enabled the
  deletion is a `MedicineDeleted` operation that wins over any
  operation for the medicine (`docs/SYNC-FORMAT.md` §6).
- Of two schedule rows with the same `EffectiveFrom`, the later
  recorded one wins (`DailyConsumption`, `LedgerDeriver`).
- Every use case (`UseCases/`, `RetractFact`,
  `LinkMedicineToReferenceUseCase`), `ConsumptionCatchUp.RunAsync` and
  `MedicationMonitor.RunAsync` run under `WriteGate`, a process-wide
  semaphore (named `MonitoringGate` before B.1 Phase 3a, when it covered
  only the catch-up, the monitor and the ledger use cases). Each writer
  reads, decides and saves in its own scope; the gate keeps another
  commit from landing in between. There is no database unique
  constraint on consumption: several manual `Consumption` rows per day
  are legitimate. One process per Windows session (single-instance
  mutex) makes the in-process gate sufficient. The gate is not
  reentrant.
- Operation capture (B.1 Phase 3a): each use case adds the facts it
  writes to the local operation log (`IOperationLog`, table
  `SyncOperations`) in the same unit of work, stamped with a hybrid
  logical clock (`HybridTimestamp`, `HybridClock`). Derived rows and
  notification bookkeeping are never logged. Nothing is recorded until
  sync is enabled for the profile (`sync.settings.json`, Tools → Sync…);
  `OperationEmissionGuardTests` requires every Application service that
  saves user facts to take `IOperationLog`.
- Merge (B.1 Phase 3b-1): `ApplyRemoteOperations` applies operations
  from other devices under `WriteGate`, with merge rules only: facts
  are a union by id, a retraction wins over its fact, registers (the
  replicated medicine fields, `IsActive`, a suspension's `EndDate`, the
  schedule summary, the slot set, the schedule row of a date) are last
  writer wins by HLC (`SyncRegisters`, `RegisterMerge`). A write that
  did not see the winning one is listed in `SyncConflicts`. Each
  operation commits with its `SyncOperations` record, so re-delivery is
  idempotent; the touched medicines are then derived again. The sync
  engine (below) calls it for the segments of the other devices.
- Count re-evaluation (B.1 Phase 3b-2): with sync enabled,
  `LedgerFactsLoader` evaluates every synced stock count again on the
  facts recorded before it by HLC (`CountReevaluation`), with end dates
  as of that instant; `SyncGenesis` writes the genesis register
  versions when sync is enabled. With sync disabled the stored outcome
  is used, as before.
- Sync engine (B.1 Phase 3c, format in [`SYNC-FORMAT.md`](SYNC-FORMAT.md)):
  `SyncEngine.RunAsync` publishes pending operations as an encrypted
  segment, applies the other devices' segments in causal order,
  writes the device record, checkpoints and deletes covered segments,
  over `ISyncTransport`: `LocalFolderSyncTransport`, or
  `OneDriveSyncTransport` (Phase 4a: Graph REST under `sync/` in the
  app folder, a `delta` index, MSAL tokens from
  `MsalCloudAccountService`), or `GoogleDriveSyncTransport` (Phase 4b:
  Drive REST v3, files flat in the app data folder tagged by a property,
  tokens from `GoogleCloudAccountService`), built by `ISyncTransportFactory` from the
  `SyncTarget` in `sync.settings.json`. `CreateSyncGroup`,
  `JoinSyncGroup` and `ResetSyncGeneration` create, join and restart a
  group; genesis and checkpoints are database images
  (`SqliteSyncSnapshotStore`). Desktop (Phase 3d): `SyncHostedService`
  runs the engine 15 s after start, every `Sync:IntervalMinutes`
  (default 5) and 10 s after local changes; Tools → Sync… shows status,
  devices and conflicts; `ISyncSetupService` (`SyncSetupService`)
  creates, joins and rebuilds, swapping the database like an import
  (`ProfileDatabaseSwap`). An import or a restore on a synced profile
  marks a pending reset in `sync.settings.json` before the swap; the
  next run starts a new generation (§5.7 of the B.1 analysis).
  Every profile manages the sync of its own data; no sync action
  depends on the role.
- Pairing and key rotation (B.1 Phase 4c): *Pair a device…* shows a
  pairing code and QR, valid 10 minutes while the window is open; the
  code opens an ephemeral pairing file, not the group key
  (`SyncPairingOffers`, `SyncPairingCode`). `RotateSyncKey` (*Change key
  and passphrase…*, *Remove device…*) starts a new generation sealed
  with a new group key; devices that see it publish nothing until they
  enter the new key, and `JoinSyncGroup.RekeyAsync` carries their own
  changes over.
- Profile settings (P8): the profile display name and the notification
  recipients (to, caregiver, doctor) are replicated as
  `ProfileSettingChanged` operations, last writer wins per setting
  (`RenameProfile`, `UpdateNotificationSettings`,
  `ProfileSettingsProjection`); a joining device takes the group's
  values. Operation schema versions are listed in
  [`SYNC-FORMAT.md`](SYNC-FORMAT.md).
- `UpdateMedicine` takes an optional `Baseline` (the values the edit
  dialog loaded): with it, only the fields the user changed are written,
  and unchanged slots record no new slot set.
- One low-stock notification per epoch that succeeded on at least one
  channel, the epoch identified by `EpochFactId` when the event has one
  (B.1 Phase 2d), else by `StockEpoch`; a failed attempt does not block
  a retry on the next tick.
- One dose reminder per `(MedicineId, SlotKey, LocalDate)`; slots
  older than the grace window are dropped without a dedup row.
- Logical dates are `DateOnly`; event timestamps are
  `DateTimeOffset`. All time comes from an injected `TimeProvider`.

---

## 5. Runtime data and configuration

### 5.1 On-disk layout

Everything lives under `%LOCALAPPDATA%\MedReminder\`
(`Storage/AppDataPaths.cs`):

```
%LOCALAPPDATA%\MedReminder\
  profiles.json                  profile registry (roles, PIN hashes, active-profile hint)
  smtp.settings.json             SMTP transport (admin-managed)
  smtp.protected                 SMTP password, DPAPI CurrentUser, base64
  cloud-backup.protected         cloud-backup passphrase, DPAPI CurrentUser, base64
  onedrive.protected             MSAL token cache for OneDrive, DPAPI CurrentUser (B.1 Phase 4a)
  googledrive.protected          Google refresh tokens per account, DPAPI CurrentUser (B.1 Phase 4b)
  backup.settings.json           automatic backup settings (admin-managed)
  backup.state.json              last successful backup timestamp
  user.settings.json             UI language, reference country, update check
  localization\strings.<lang>.json   optional user overrides of the UI dictionaries
  logs\medreminder-<date>.log    Serilog, daily files
  catalogue\staging\             remote AIFA archive while it is downloaded and imported; emptied every run
  backups\pre-migration-<ts>\    one-off V1→V2 migration snapshot
  profiles\<profileId>\
    medreminder.db               SQLite database of the profile (+ -wal, -shm)
    notifications.settings.json  per-profile recipient, caregiver and doctor address
    ui.settings.json             per-profile text size (Normal / Large / ExtraLarge; absent = Normal)
    sync.settings.json           sync group, device, generation, folder or cloud account (B.1; absent while sync is off)
    sync.protected               sync group key, DPAPI CurrentUser (B.1)
```

Backup files (§8) are written to the folders the administrator
chooses in Settings, not under this root.

No file is written to the installation directory. Nothing under this
root is committed to the repository.

### 5.2 Profiles

- A profile has an immutable `Id` (GUID "N", or `default` for a
  profile migrated from V1), a display name, a role and an optional
  PIN (PBKDF2, 100 000 iterations, per-profile salt).
- **Roles**: `User` manages its own medicines and recipient address;
  `Admin` also manages SMTP, backup and the profile registry. The role
  is enforced by the UI only: anyone with file-system access can edit
  `profiles.json`. Unknown role values deserialize to `User`.
- The profile is chosen once at boot (§7) and exposed as the
  singleton `ICurrentProfile`. Switching profile restarts the process
  (`IApplicationRestarter`).
- `MigrationV1toV2` converts a pre-multi-profile install (database at
  the root) into a single `default` admin profile, after a full
  snapshot into `backups\`. It is idempotent and rolls back on
  failure.

See [`analysis/ANALYSIS-MULTI-USER.md`](analysis/ANALYSIS-MULTI-USER.md).

### 5.3 Configuration chain

`Program.BuildHost` layers, later sources winning:

1. `appsettings.json` next to the executable (defaults only:
   `Monitoring:IntervalMinutes` = 30, `DoseReminder:IntervalSeconds` =
   60, `DoseReminder:GraceWindowMinutes` = 30, empty `Smtp`, `Backup`
   disabled, `UI:Language` = `en`, `Catalogue:Enabled` = true);
2. `smtp.settings.json`, `backup.settings.json`, `user.settings.json`
   (shared);
3. the profile's `notifications.settings.json`.

Each file wraps a single section (`Smtp`, `Backup`, `UI`,
`Notifications`) and is bound through `IOptions<T>`. Secrets are
never stored in these files.

The profile's `ui.settings.json` is not part of this chain: it is read
once by `Program`, after the profile is chosen and before the main
window exists (`ProfileUiSettingsFile`, Infrastructure.Portable). A
missing, unreadable or unknown value reads as Normal.

The donation configuration is read from `assets/donations.settings.json`,
embedded in the Infrastructure assembly; a missing or invalid section
disables the feature.

---

## 6. Background processing

Five `BackgroundService`s in `MedReminder.UI/Hosting`. Each tick opens
its own DI scope, so the scoped `DbContext` is never shared between
ticks. A failing tick is logged and does not stop the service.

| Service | Cadence | Work |
|---|---|---|
| `MedicationMonitorHostedService` | `PeriodicTimer`, `Monitoring:IntervalMinutes` (default 30, min 1) | `MedicationMonitor`: consumption catch-up, forecast, `NotificationCycle`, dispatch per channel, `NotificationEvent` |
| `DoseReminderHostedService` | `PeriodicTimer`, `DoseReminder:IntervalSeconds` (default 60, min 10) | `DoseReminderService`: fires due timed slots for medicines with `RemindOnDose`, positive stock and an active therapy; prunes dedup rows older than 30 days |
| `AutomaticBackupHostedService` | 30 s initial delay, then every 15 min | Daily backup at or after `Backup:PreferredTime` (§8) |
| `CatalogueRefreshHostedService` | Once at startup; registered only when `Catalogue:Enabled` is true | Imports each embedded catalogue snapshot whose version is newer, one transaction per country; then, after the startup update check, `RemoteCatalogueRefresher` for the AIFA feed (§9.5) |
| `SyncHostedService` | 15 s initial delay, then every `Sync:IntervalMinutes` (default 5, min 1) and 10 s after local changes; idle while sync is off | Starts a pending new generation, then `SyncEngine.RunAsync` (B.1 Phase 3d) |

The Application services (`MedicationMonitor`, `DoseReminderService`,
`ConsumptionCatchUp`) have no scheduling code and are tested with a
fake `TimeProvider`. The monitor pass and the catch-up are serialized
with the "Check now" command and with every use case through
`WriteGate` (§4.4).

---

## 7. Process lifecycle

`Program.Main`:

1. Configure Serilog and a bootstrap `LocalizationService` (for
   messages shown before the host exists).
2. Acquire the single-instance mutex
   `Local\MedReminder.SingleInstance.<guid>` (per Windows session). A
   second instance shows a message and exits. The mutex must not be
   bypassed.
3. Run `MigrationV1toV2` if applicable.
4. Choose the profile: first-run wizard when none exists (UI language
   taken from the Windows UI culture), else `--profile <id>`, else the
   active-profile hint when started with `--minimized`, else the only
   profile, else the profile picker. A profile with a PIN requires
   `PinPromptForm`.
5. Read the profile's text size (`ui.settings.json`) into
   `MedReminderFormBase.TextScale`; the windows of step 4 use Normal.
6. Build the host (§5.3, DI registration via
   `AddMedReminderApplication` and `AddMedReminderInfrastructure`),
   run `DatabaseInitializer` (§8.1), start the hosted services.
7. Run `MainForm` on the WinForms message loop. `--minimized` starts
   in the tray. Closing the window hides it to the tray; the tray
   menu's Exit ends the process.
8. Stop the host with a 5-second timeout and release the mutex.

Unhandled UI-thread exceptions are logged and shown in a message box;
print cancellations are reported as information, not errors.

---

## 8. Persistence and backup

### 8.1 Schema management

There are no EF Core migrations. `DatabaseInitializer` runs on every
start:

1. `EnsureCreatedAsync` creates the full schema **only** for a new,
   empty database.
2. On an existing database, `ApplyIdempotentSchemaPatchesAsync` adds
   the objects introduced after the first release (`AddColumnIfMissing`
   guarded by `PRAGMA table_info`, `CREATE TABLE/INDEX IF NOT
   EXISTS`). Current patches: `MedicationIntakes.Day`,
   `MedicationAdministrationSlots`, the three catalogue-link columns
   on `Medicines`, `ScheduleKind` and `SchedulePayload` on
   `MedicationScheduleHistories`, `Medicines.RemindOnDose`,
   `DoseReminderEvents` with its unique index, and the B.1 ledger
   patch (one transaction): `StockCounts`, `LedgerCutoff`,
   `MedicationAdministrationSlotSets`, `StockMovements.Origin` (existing
   rows become `Legacy` and the cutoff is set to the day before),
   `MedicationAdministrationSlots.SetId` (existing slots become one set
   per medicine); then the B.1 Phase 2c-2 patch (one transaction):
   `MedicineActivityChanges`, `StockCounts` outcome columns,
   `MedicationScheduleHistories.RecordedAt`,
   `Medicines.LedgerBaselineEpoch`, and `MedicationIntakes.RecordedAt`,
   whose absence triggers the re-freeze (`LedgerFreeze`, also applied
   to every import); then the B.1 Phase 2d patch: `FactRetractions`,
   `MedicationSuspensions.RecordedAt`, `Medicines.StockEpochFactId`,
   `NotificationEvents.EpochFactId`; then `SyncOperations` with its two
   indexes (B.1 Phase 3a); then `SyncFieldVersions` and `SyncConflicts`
   (B.1 Phase 3b); then `SyncOperations.EntityId` (B.1 Phase 3b-2);
   then `SyncPeers` (B.1 Phase 3c).
3. The catalogue DDL runs unconditionally (idempotent).
4. `PRAGMA journal_mode = WAL`, `foreign_keys = ON`,
   `synchronous = NORMAL`.

Rules for a schema change: add the property to the entity and its
`IEntityTypeConfiguration`, then append an idempotent patch with a
default that preserves existing semantics. Never rely on
`EnsureCreated` to upgrade an existing database, and never drop or
rename a column. If the change affects exported data, bump
`ExportFormat.CurrentSchemaVersion` (§8.3).

### 8.2 Local backup (raw database copy)

`BackupService` copies each profile's database through the SQLite
online-backup API (consistent under concurrent writes) to
`medreminder-<profileId>-<yyyyMMdd-HHmmss>.db` in `Backup:Directory`.
The automatic service backs up **every** profile once per day at or
after `PreferredTime`, catching up at the first tick if the app was
off. Retention (`RetentionDays`) is applied per profile. Restore
replaces the database file after renaming the current one to
`<db>.bak-<timestamp>`.

### 8.3 Encrypted export and cloud folder

- **`.mrz` archive** (`ExportService` / `ImportService`): ZIP with
  `manifest.json` and `payload.enc`. Each archive holds one profile
  (`scope: "profile"`): the active one, or `ExportOptions.ProfileId`
  for an admin or for the automatic backup. The payload (JSON of every
  entity of that profile, plus opt-in settings files and SMTP
  password) is encrypted with AES-GCM under a key derived by Argon2id
  from a passphrase of at least 12 characters, and hashed with
  SHA-256. Import is overwrite-only: it builds a new database in a
  temporary file, swaps it in with a `.bak-<timestamp>` safety copy,
  and asks for a restart. Import always targets the **active**
  profile; the Import and Restore dialogs ask for confirmation when
  the archive comes from another profile. The public format is
  specified in [`EXPORT-FORMAT.md`](EXPORT-FORMAT.md).
- **Admin export of every profile**: the Export dialog of an admin
  profile, when more than one profile exists, writes one
  single-profile `.mrz` per profile into a chosen folder, all under
  the same passphrase. The profile registry is never exported.
- **Cloud folder**: when `Backup:CloudFolderEnabled` is set, the
  automatic service also writes one `.mrz` snapshot per registered
  profile into `CloudFolderDirectory` (a folder synchronized by a
  third-party client), using the DPAPI-cached passphrase from
  `cloud-backup.protected`. Delivery goes through the `IArchiveStorage`
  port: `CloudArchiveStorage` picks `LocalFolderArchiveStorage` or,
  with `Backup:CloudProvider` = `OneDrive` and `CloudAccountId`,
  `OneDriveArchiveStorage` (`backups/` in the OneDrive app folder;
  B.1 Phase 4a) or, with `GoogleDrive`, `GoogleDriveArchiveStorage` (a
  visible `MedReminder/backups` folder in My Drive; Phase 4b). A
  provider session that needs a new sign-in is
  checked with a silent token before the export and recorded as the
  run's error, so Settings shows it; the restore dialog asks for a new
  sign-in. A written snapshot counts as the
  day's backup; a missing folder or passphrase skips the run and
  leaves the day open for retry; a failure on one profile does not
  stop the others. `ICloudRestoreService` lists and restores
  snapshots; the dialog preselects the active profile's newest one.
  OneDrive snapshots are listed from their file names and downloaded
  under `%LOCALAPPDATA%\MedReminder` only when restored; the
  other-profile confirmation then uses the downloaded manifest, not
  the file name.

See [`analysis/ANALYSIS-C3-EXPORT-IMPORT.md`](analysis/ANALYSIS-C3-EXPORT-IMPORT.md),
[`analysis/ANALYSIS-C3PLUS-CLOUD-BACKUP.md`](analysis/ANALYSIS-C3PLUS-CLOUD-BACKUP.md),
[`analysis/ANALYSIS-C3PP-CLOUD-PROVIDERS.md`](analysis/ANALYSIS-C3PP-CLOUD-PROVIDERS.md).

Force-closing SQLite connections (`ClearAllPools`) is allowed only in
the restore and import paths.

---

## 9. Notifications and external services

### 9.1 Windows notifications

`IWindowsNotificationService` is implemented in the UI by
`ToastWindowsNotificationService` (`Microsoft.Toolkit.Uwp.Notifications`;
AUMID and Start Menu shortcut are registered at runtime). On any
failure it falls back to `TrayBalloonNotificationService`, which
reuses the main tray icon. `BalloonTipNotificationService` is the
default registration in Infrastructure; the UI replaces it. Toast
text uses the language selected in the application (`NotificationTexts`),
not the Windows UI language.

### 9.2 Email

`IEmailNotificationService` = `RetryingEmailNotificationService`
wrapping `MailKitEmailNotificationService`. Transient failures (SMTP
4xx, network) are retried after 5 s, 30 s and 2 min; permanent
failures (5xx) are not retried. Mail goes to the profile's
`ToAddress` and, when set, to `CaregiverAddress`. Email text uses the
language selected in the application. Channels are chosen per
medicine (`NotificationChannels` flags). A failure on one channel
does not prevent the other.

The prescription request (`PrescriptionRequestDialog`,
`SendPrescriptionRequest`) is the only user-initiated send. It sets
`EmailMessage.ExplicitRecipient` to the profile's `DoctorAddress`:
the adapter sends to that address only (no primary, no caregiver),
the retry decorator does not back off, and the subject, body and
recipient are never logged; only the outcome and the exception type
are.

### 9.3 Localization

UI strings are keyed dictionaries in `assets/localization/strings.<lang>.json`
for `en`, `it`, `fr`, `es`, `de`, embedded in the UI assembly and
copied next to the executable. `LocalizationService`
(Infrastructure.Portable) loads them. Files under
`<data directory>\localization\` override single keys; on Windows the
data directory is `%LOCALAPPDATA%\MedReminder\` (`IAppDataLocation`). The
language is read once at startup; a change requires a restart. Every
new key must be added to all five files.

### 9.4 Auto-start

`RegistryAutoStartService` writes
`HKCU\Software\Microsoft\Windows\CurrentVersion\Run\MedReminder`
with the `--minimized` argument. Per-user, no elevation.

### 9.5 Update check and donations

- `GitHubUpdateChecker` queries the GitHub Releases API (8 s timeout)
  at startup when `UI:CheckForUpdatesOnStartup` is true, and on demand.
  It only reports the release URL; nothing is downloaded or executed.
- Remote AIFA feed: after that check, and under the same
  `CheckForUpdatesOnStartup` setting plus `Catalogue:RemoteFeed:Enabled`,
  `RemoteCatalogueRefresher` reads `data/it/latest.json` from the
  repository and, when its version is newer than the open profile's
  Italian catalogue, downloads `aifa-<yyyymm>.zip` (HTTPS, no redirects,
  size cap, SHA-256 when published), imports it and deletes it. Data
  only; nothing is executed. See
  [`analysis/ANALYSIS-CATALOGUE-REMOTE-FEED.md`](analysis/ANALYSIS-CATALOGUE-REMOTE-FEED.md).
- `DonationService` opens Stripe or PayPal payment links in the
  default browser. See [`analysis/ANALYSIS-A6-DONATION-SUPPORT.md`](analysis/ANALYSIS-A6-DONATION-SUPPORT.md).

### 9.6 Therapy card (print and PDF)

`TherapyCardBuilder` (`Application/Reporting`) builds the localized
card model of the active profile; `TherapyReport` renders it as plain
text and `TherapyCardPrintDocument` (UI) as a paginated table. *Save
as PDF…* prints to the built-in "Microsoft Print to PDF" printer; no
PDF package is used. Medicine notes are included only on request.

---

## 10. Security and privacy

- Secrets (SMTP password, cloud-backup passphrase, OneDrive and Google
  Drive tokens, sync group key) are stored only as DPAPI `CurrentUser`
  blobs (§5.1). Export archives carry them only inside
  the encrypted payload.
- Passphrases, derived keys and payload plaintext are never logged;
  key buffers are zeroed after use.
- The webcam is opened only while the barcode dialog shows its webcam
  panel and is released as soon as a code is read, the user switches
  back, the timeout fires or the dialog closes. Frames stay in memory;
  they are never written to disk or logged, and neither is a barcode
  payload (it may carry an FMD serial number).
- Logs contain identifiers, medicine names, quantities and outcomes.
  They never contain passwords, email bodies or free-text medical
  notes.
- Low-stock emails contain the medicine name and active ingredient,
  remaining quantity, days remaining, estimated run-out date, the
  dosage slots, the doctor name when set, and a generic prompt to
  renew the prescription. `Medicine.Notes` is never included.
- The profile PIN and role are access conveniences, not a security
  boundary against a user with file-system access (§5.2). The local
  raw backups and the cloud snapshots include every profile; whoever
  knows the cloud-backup passphrase can read every profile. Real
  separation between people needs separate Windows accounts, as the
  user guides state.
- Serilog: `Information` level, daily files, 30 files retained, 10 MB
  per file.

---

## 11. Original design decisions (status)

The pre-implementation plan ([`analysis/ANALYSIS-MVP.md`](analysis/ANALYSIS-MVP.md))
asked for confirmation of four decisions. Their current status:

| # | Decision | Status |
|---|---|---|
| Q1 | One warning threshold (days) per medicine | Kept: `Medicine.ThresholdDays` |
| Q2 | No warning when the therapy `EndDate` precedes the estimated run-out | Kept: `NotificationCycle` |
| Q3 | Toast via `Microsoft.Toolkit.Uwp.Notifications`, balloon fallback | Kept: §9.1 |
| Q4 | DPAPI `CurrentUser` for the SMTP password | Kept, and extended to the cloud-backup passphrase |

The other Phase 1 decisions (§1.1 of that file) still hold: daily
consumption derived from the schedule, `StockEpoch` on positive
movements, `MedicationSuspension` semantics, `DateOnly` for logical
dates with `TimeProvider`, MailKit instead of `SmtpClient`, per-user
`Run` key, `%LOCALAPPDATA%` data folder, single-instance mutex with
SQLite WAL.

Where the implementation departed from the plan:

| Plan (ANALYSIS-MVP) | As built |
|---|---|
| `AdministrationsPerDay` integer only | Administration slots with times and labels, plus six schedule shapes (A1) |
| `MedicationIntake` modelled but unused | Used: intakes recorded from the UI suppress automatic consumption for that day |
| EF Core migrations, `Database.Migrate()` | `EnsureCreated` for new databases, idempotent boot patches otherwise (§8.1) |
| One database at the data-folder root | One database per profile (§5.2) |
| `ApplicationSetting` key/value table | JSON settings files bound through `IOptions<T>` (§5.3) |
| Backup = file copy after WAL checkpoint | SQLite online-backup API, plus encrypted `.mrz` export and cloud folder (§8) |
| One hosted service | Five hosted services (§6) |
| Italian-only UI | Five UI languages (§9.3) |
| 4 projects, 3 test projects | 6 projects, 6 test projects (§2) |

---

## 12. Feature analyses

| Document | Topic |
|---|---|
| [`ANALYSIS-MVP.md`](analysis/ANALYSIS-MVP.md) | Original Phase 1 / Phase 2 plan (historical) |
| [`ANALYSIS-MULTI-USER.md`](analysis/ANALYSIS-MULTI-USER.md) | Profiles, roles, PIN, V1→V2 migration |
| [`ANALYSIS-MULTI-USER-ROLES-OVERVIEW.md`](analysis/ANALYSIS-MULTI-USER-ROLES-OVERVIEW.md) | Profile role change and read-only all-profiles view (analysis only, decisions pending) |
| [`ANALYSIS-A1-REGIMENS.md`](analysis/ANALYSIS-A1-REGIMENS.md) | Weekly, cyclic, tapering and PRN schedules |
| [`ANALYSIS-A1-STEPPED-TAPER.md`](analysis/ANALYSIS-A1-STEPPED-TAPER.md) | Multi-stage tapering |
| [`ANALYSIS-A2-BARCODE-SCAN.md`](analysis/ANALYSIS-A2-BARCODE-SCAN.md) | Barcode scanning: USB HID scanner (phase 1), webcam (phase 2), restock by scan (phase 3) |
| [`ANALYSIS-B1-MOBILE-SYNC.md`](analysis/ANALYSIS-B1-MOBILE-SYNC.md) | Mobile client with desktop synchronization: desktop side shipped (phases 1–4), mobile open |
| [`ANALYSIS-A3-CAREGIVER-NOTIFICATIONS.md`](analysis/ANALYSIS-A3-CAREGIVER-NOTIFICATIONS.md) | Caregiver email recipient |
| [`ANALYSIS-A5-DOSE-TIME-REMINDER.md`](analysis/ANALYSIS-A5-DOSE-TIME-REMINDER.md) | Dose-time reminder |
| [`ANALYSIS-A6-DONATION-SUPPORT.md`](analysis/ANALYSIS-A6-DONATION-SUPPORT.md) | Donation links |
| [`ANALYSIS-C3-EXPORT-IMPORT.md`](analysis/ANALYSIS-C3-EXPORT-IMPORT.md) | Encrypted export / import |
| [`ANALYSIS-C3PLUS-CLOUD-BACKUP.md`](analysis/ANALYSIS-C3PLUS-CLOUD-BACKUP.md) | Cloud-folder backup and restore |
| [`ANALYSIS-C3PP-CLOUD-PROVIDERS.md`](analysis/ANALYSIS-C3PP-CLOUD-PROVIDERS.md) | Storage abstraction and native cloud providers |
| [`ANALYSIS-DRUG-CATALOGUE.md`](analysis/ANALYSIS-DRUG-CATALOGUE.md) | Reference medicine catalogue |
| [`ANALYSIS-WEBSITE.md`](analysis/ANALYSIS-WEBSITE.md) | Public website |

Backlog: [`EVOLUTION.md`](EVOLUTION.md); shipped items:
[`EVOLUTION-DONE.md`](EVOLUTION-DONE.md).

---

## 13. Known limitations

- **Role and PIN are not enforced on disk** (§5.2, §10). Accepted:
  within one Windows account no software-only mechanism can enforce
  them. Encrypting each profile with a PIN-derived key (SQLCipher)
  was considered and not pursued: native dependency, data loss on a
  forgotten PIN, rework of backup, export and restore.
- **Import is overwrite-only and targets the active profile.** An
  archive of another profile is applied to the active one after a
  confirmation; restoring into a different profile requires switching
  to it first. Merge mode is out of scope
  ([`ANALYSIS-C3-EXPORT-IMPORT.md`](analysis/ANALYSIS-C3-EXPORT-IMPORT.md)).
- **Historical ledger anomalies are not repaired.** Days skipped or
  double-counted by the catch-up behavior fixed in PR #64 before that
  release stay as they are; only new days follow the corrected rules.
- **`IStockMovementRepository.GetLastConsumptionDayAsync`** is no
  longer used by production code (only by `RoundTripTests`).
