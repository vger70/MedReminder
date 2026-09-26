# ANALYSIS — B.1: Mobile companion client

Design document, **prior** to implementation. Corresponds to
`EVOLUTION.md` §7 (item B.1) and `EVOLUTION.md` §2.0 (second item of
the remaining sequence, after A2). It turns the §7 sketch into a
phased plan with explicit entry criteria, actions and exit criteria
for each phase.

Where this document and `EVOLUTION.md` §7 disagree, this document
wins. Where it disagrees with `ANALYSIS-C3PP-CLOUD-PROVIDERS.md` §6,
this document wins for the mobile client; §15 lists the corrections.

Epistemic classification: `[VERIFIED]` (checked against the current
tree, or against a publicly traceable primary source), `[INFERRED]`
(deduction from verified facts), `[UNCERTAIN]` (hypothesis pending
confirmation, usually by a Phase 0 spike). Untagged statements are
design decisions proposed by this document.

> The "Decisions still to confirm" section (§14) is the only zone of
> open choice. Every phase in §11 names the decisions it depends on;
> a phase does not start while one of them is open.

---

## 1. Scope

### 1.1 Problem

MedReminder runs only on the Windows PC where the profile database
lives. Away from the PC the user cannot check how many days of a
medicine remain, cannot see the next run-out date and receives no
dose reminder unless email is configured. The desktop already
produces encrypted, self-describing `.mrz` snapshots of each profile
into a cloud-synchronized folder (C.3+) `[VERIFIED]` — `docs/ANALYSIS.md`
§8.3, `AutomaticBackupHostedService`, `IArchiveStorage`.

### 1.2 Goal

A phone application that:

1. Opens a `.mrz` snapshot produced by the desktop (the same file
   C.3+ writes to the cloud folder), decrypts it with the archive
   passphrase and keeps a local, read-only replica of that profile.
2. Shows, per medicine, current stock, days remaining, estimated
   run-out date, schedule, administration slots and suspensions,
   computed with the **same** Domain code the desktop uses.
3. Schedules local notifications on the phone: low-stock warnings and,
   for medicines with `RemindOnDose`, dose-time reminders.
4. In a later, separately approved phase (§11, Phase 4), records a
   restricted set of stock operations on the phone and hands them back
   to the desktop as an encrypted journal file.

### 1.3 What B.1 is NOT

- **Not sync.** There is no server, no live replication, no merge of
  two databases. The desktop remains the single writer of the profile
  database. Real multi-device sync is C.1 (`EVOLUTION.md` §8) and is
  explicitly out of scope.
- **Not a port of the WinForms UI.** The mobile UI is a redesign for
  touch and stack navigation (`EVOLUTION.md` §7.4).
- **Not a medical device.** No adherence tracking, no clinical alerts,
  no interaction checks (`EVOLUTION.md` §9.2). Store listings reuse the
  existing non-medical disclaimer.
- **Not an email sender.** The phone does not send low-stock or
  caregiver email: the desktop already does, and a second sender would
  duplicate every message (§6.5).
- **Not a native cloud-provider client** in the first release. Archive
  access goes through the platform file picker (C3PP §6.3, Scenario A).
  Native providers are C.3++ Phase 2 and are triggered by B.1, not part
  of it.
- **Not a catalogue client.** The reference catalogue (AIFA, EMA, AEMPS,
  BDPM snapshots, `ANALYSIS-DRUG-CATALOGUE.md`) is not shipped on the
  phone. Medicines carry their catalogue link fields in the archive and
  display them as text.
- **Not multi-profile administration.** The phone holds replicas of the
  profiles whose archives the user opened; it does not manage the
  desktop profile registry, PINs or roles.

---

## 2. Preconditions — current state

The table lists every precondition that affects B.1, its state as of
2026-09-26, and the phase that closes it when not met.

| # | Precondition | State | Evidence / closing phase |
|---|---|---|---|
| P1 | C.3+ shipped: desktop writes `.mrz` snapshots to a user folder | **Met** | `EVOLUTION.md` §7.7; `ANALYSIS.md` §8.3 `[VERIFIED]` |
| P2 | Public, versioned archive contract | **Met** | `docs/EXPORT-FORMAT.md`; `ExportFormat.CurrentFormatVersion = 1`, `CurrentSchemaVersion = 1` `[VERIFIED]` |
| P3 | Storage port for archives | **Met** | `IArchiveStorage` (Application/Abstractions) `[VERIFIED]` |
| P4 | Domain and Application portable (`net10.0`, no Windows API) | **Met** | `MedReminder.Domain.csproj`, `MedReminder.Application.csproj` target `net10.0`; Application references only `Microsoft.Extensions.*.Abstractions` `[VERIFIED]` |
| P5 | Persistence (EF Core model, configurations, repositories, `DatabaseInitializer`) usable outside Windows | **Not met** | Lives in `MedReminder.Infrastructure`, which targets `net10.0-windows` with `UseWindowsForms` `[VERIFIED]`. Closed by Phase 1 |
| P6 | Archive decrypt + validate + map to entities usable outside Windows | **Not met** | `ImportService`, `ExportService`, `CloudRestoreService` are `[SupportedOSPlatform("windows")]`; `ImportService` depends on `ICredentialProtector` (DPAPI) and `AppDataPaths` `[VERIFIED]`. `ExportMapper` and `ArchiveCipher` are portable code in a Windows project. Closed by Phase 1 |
| P7 | Medicine overview (stock, days left, run-out) computed outside the WinForms project | **Not met** | `MedicineOverviewLoader` is `internal` in `MedReminder.UI/Presentation` `[VERIFIED]`. Closed by Phase 1 |
| P8 | Localization service behind a portable port | **Partly met** | Port `ILocalizationService` is in Application `[VERIFIED]`; the implementation `LocalizationService` is in the Windows Infrastructure project and reads `%LOCALAPPDATA%` overrides. Closed by Phase 1 |
| P9 | AES-GCM available on target mobile platforms | **Met for iOS 13+ on .NET 9+**; Android `[UNCERTAIN]` | iOS/tvOS 13+ and Mac Catalyst support AES-GCM from .NET 9 `[VERIFIED]` — dotnet/runtime issue #91523, Microsoft Learn `AesGcm` page. Android support to be proven by spike S1 (Phase 0) |
| P10 | Argon2id available on mobile | **Probably met** `[INFERRED]` | `Konscious.Security.Cryptography.Argon2` is managed code with no native dependency `[VERIFIED — csproj comment]`; performance at `MemoryKiB = 65536`, `Iterations = 3` on a low-end phone is unmeasured. Spike S2 |
| P11 | EF Core SQLite on Android and iOS (including iOS AOT constraints) | `[UNCERTAIN]` | Spike S3 |
| P12 | Build hosts and accounts: macOS host for iOS builds, Apple Developer Program, Google Play Console | **Not met** | Product-owner action. Needed by Phase 2 (Android) and Phase 3 (iOS) |
| P13 | Product-owner decisions D1–D6 (§14) | **Open** | Phase 0 |
| P14 | A2 completed or explicitly re-ordered | Open | `EVOLUTION.md` §2.0 places A2 before B.1; A2 is independent and may be reordered by the product owner. Not a technical dependency |

Consequence: **B.1 cannot start with mobile code.** Phase 1 is a
desktop-side portability refactor that closes P5–P8 without changing
behavior.

---

## 3. Data exchange model

### 3.1 Options

| Option | Description | Writers | Conflict surface | Verdict |
|---|---|---|---|---|
| **R — read-only replica** | Phone imports the latest desktop snapshot and never writes profile data | Desktop only | None | **Phase 2** |
| **J — replica + operation journal** | Phone records a restricted set of append-only operations, exports them as an encrypted journal; desktop imports and applies them | Desktop (DB), phone (journal only) | Limited, defined by §5 rules | **Phase 4**, separately approved |
| **H — writer handover** | Phone becomes the writer; desktop imports the phone's full snapshot in Overwrite mode | Alternating | Silent loss of every desktop change made meanwhile | Rejected |
| **S — two-way database sync** | Merge two SQLite databases | Both | Unbounded | Rejected: this is C.2/C.1 territory (`EVOLUTION.md` §8.1) |

Option H is rejected because the existing import is overwrite-only
(`IImportService`, `ANALYSIS-C3-EXPORT-IMPORT.md` §1.3) `[VERIFIED]`:
a forgotten handover drops data without warning.

### 3.2 Snapshot freshness

The phone never knows whether its replica is current. Every screen
shows the snapshot creation time (`manifest.createdAtUtc`) and the
replica age. When the age exceeds a threshold (default 2 days, §14 D7)
the list shows a staleness banner. Forecasts remain valid because they
are recomputed daily from the replica (§3.3), but stock changes made
on the desktop after the snapshot are not visible.

### 3.3 Consumption materialization on the phone

The desktop materializes automatic `Consumption` stock movements up to
**yesterday** through `ConsumptionCatchUp` `[VERIFIED]` —
`src/MedReminder.Application/Monitoring/ConsumptionCatchUp.cs`. A
snapshot taken on day T therefore carries consumption up to T-1 at the
latest.

Rule: the phone runs the **same** `ConsumptionCatchUp` against its
replica on app start, on resume and after an import. The generated
movements are replica-local and are discarded by the next import
(Overwrite). They are never journaled (§5.2). This keeps displayed
stock identical to what the desktop would show on the same day, with
no new calculation code.

### 3.4 Profiles on the phone

A `.mrz` archive holds one profile (`scope: "profile"`) `[VERIFIED]` —
`ExportManifest.Scope`. The phone keeps one replica per imported
`profileId`, each in its own SQLite file under the app sandbox,
mirroring the desktop `profiles\<id>\medreminder.db` layout. Importing
an archive whose `profileId` already has a replica replaces that
replica; a new `profileId` adds one. The phone shows the profile
display name from `payload.profile.displayName`.

The desktop PIN is not carried in the archive and is not enforced on
the phone. Phone-level protection relies on the device lock and on an
optional app lock (§8.3).

### 3.5 Archive source

Phase 2: platform file picker only (Android Storage Access Framework,
iOS document picker), which also reaches the folders of installed
cloud clients (OneDrive, Google Drive, Dropbox, iCloud Drive)
`[INFERRED]` — C3PP §6.1. The phone reads the manifest of the picked
file without decrypting (as `IImportService.ReadManifestAsync` does on
desktop) and asks for the passphrase only after showing profile,
creation time and source.

The phone does not list a folder in Phase 2: persistent access to a
picked folder differs between Android (persistable URI permission) and
iOS (security-scoped bookmarks) `[INFERRED]`, and is deferred to a
follow-up (§14 D8).

### 3.6 Settings carried by the archive

`payload.shared` may carry `userSettings`, `backupSettings`,
`smtpSettings` and the SMTP password `[VERIFIED]` — `ExportedShared`.
The phone applies only `userSettings.language` (as the initial app
language, overridable). It ignores `backupSettings` and `smtpSettings`,
and never decrypts `smtpPasswordEncrypted`: the phone sends no email
(§1.3), so holding the SMTP credential would only enlarge the attack
surface.

---

## 4. Target architecture

### 4.1 Solution after Phase 1

| Project | Target framework | Content | Change |
|---|---|---|---|
| `MedReminder.Domain` | `net10.0` | unchanged | none |
| `MedReminder.Application` | `net10.0` | + `MedicineOverviewLoader` (moved), + `IArchiveReader` port, + `IAppDataLocation` port | small additions |
| **`MedReminder.Infrastructure.Portable`** (new) | `net10.0` | EF Core model, configurations, value converters, repositories, `UnitOfWork`, `DatabaseInitializer`, `ExportMapper`, `ExportJson`, `ArchiveCipher`, `ArchiveReader` (new), localization dictionary loader | moved from Infrastructure |
| `MedReminder.Infrastructure` | `net10.0-windows` | DPAPI stores, registry auto-start, balloon notifications, `AppDataPaths`, `ProfileRegistry`, `MigrationV1toV2`, `ImportService` / `ExportService` / `CloudRestoreService` (Windows shells over the portable parts), MailKit, donations, update check, catalogue import | references Portable |
| `MedReminder.UI` | `net10.0-windows10.0.19041.0` | unchanged except for the moved loader | none |
| **`MedReminder.Mobile`** (new, Phase 2) | `net10.0-android`; `net10.0-ios` added in Phase 3 | MAUI app, mobile adapters | new |

Name `MedReminder.Infrastructure.Portable` is a proposal (§14 D9).

Rule carried over from `ANALYSIS.md` §2.1: the portable project must
not reference Windows APIs, WinForms, DPAPI, the registry or
`%LOCALAPPDATA%`. Paths reach it through `IAppDataLocation` or explicit
constructor arguments.

Side benefit `[INFERRED]`: persistence and export round-trip tests move
to a `net10.0` test project and run on Linux CI, which today cannot
run `MedReminder.Infrastructure.Tests` (Windows-only, `CLAUDE.md` §3).

### 4.2 `IArchiveReader` — decrypt and validate without applying

New Application port, implemented in the portable project by extracting
the read half of `ImportService`:

```csharp
namespace MedReminder.Application.Export;

public interface IArchiveReader
{
    // Parses manifest.json without decrypting. Same failure contract
    // as IImportService.ReadManifestAsync.
    Task<ExportManifest> ReadManifestAsync(Stream archive, CancellationToken ct);

    // Derives the key, decrypts payload.enc, checks the SHA-256 and
    // the version gates, and returns the payload. Never writes to
    // disk, never touches a database, never decrypts the SMTP
    // password. The caller owns and zeroes the passphrase.
    Task<ExportPayload> ReadPayloadAsync(Stream archive, char[] passphrase, CancellationToken ct);
}
```

The desktop `ImportService` is refactored to call `IArchiveReader` and
keep only the Windows-specific apply steps (temporary database, file
swap with `.bak-<timestamp>`, `ClearAllPools`, DPAPI rewrap of the SMTP
password, shared settings files). Behavior and failure reasons
(`ImportFailureReason`) stay identical; existing tests are the
regression net.

Stream-based signatures are required on mobile: the platform picker
returns a content stream, not a file path `[INFERRED]`.

### 4.3 Replica builder

`ReplicaBuilder` (portable project) turns an `ExportPayload` into a
fresh SQLite database file through `DatabaseInitializer` and
`ExportMapper.ToEntity`, in one transaction. The desktop `ImportService`
already performs this step against a temporary file; Phase 1 extracts
it so both hosts share one implementation. The mobile host then swaps
the file into place atomically (write to `<db>.new`, close the
replica's connections, rename). Closing connections here is a restore
path and therefore within `CLAUDE.md` §7.

### 4.4 Port mapping

| Application port | Desktop adapter | Mobile adapter |
|---|---|---|
| `IMedicineRepository` and the other repositories, `IUnitOfWork` | EF Core SQLite (moved to Portable) | same, from Portable |
| `IArchiveCipher` | `ArchiveCipher` (moved to Portable) | same |
| `IArchiveReader` (new) | `ArchiveReader` (Portable) | same |
| `ILocalizationService` | `LocalizationService` + `%LOCALAPPDATA%` override | Portable loader over embedded dictionaries; no override directory |
| `ICurrentProfile` | `CurrentProfile` | `MobileCurrentProfile` (selected replica) |
| `IWindowsNotificationService` | toast / balloon | **not used**; mobile uses `ILocalNotificationScheduler` (§6) |
| `IEmailNotificationService` | MailKit | **not registered** (§6.5) |
| `ICloudBackupPassphraseStore` | DPAPI | `SecureStorage` (MAUI Essentials), opt-in (§8.2) |
| `IAutoStartService`, `IApplicationRestarter`, `IBackupService` | registry, process restart, SQLite backup | not applicable |
| `IArchiveStorage` | `LocalFolderArchiveStorage` | not used in Phase 2 (file picker); a mobile adapter arrives with C.3++ Phase 2 |
| `TimeProvider` | `TimeProvider.System` | same |

Hosted services are not ported. `MedicationMonitor` and
`DoseReminderService` are scheduling-free Application services
`[VERIFIED]` — `ANALYSIS.md` §6; on the phone they are replaced by the
notification planner (§6), which reuses the Domain calculations but not
the desktop dispatch loop.

### 4.5 UI framework

**.NET MAUI** (as recommended in `EVOLUTION.md` §7.3), confirmed on two
facts: MAUI 10 ships with .NET 10 and follows the .NET release cadence,
and a MAUI major version is supported for at least 6 months after its
successor ships `[VERIFIED]` — Microsoft .NET MAUI support policy. MAUI
10 binds Android 16 (API 36) and iOS 26 `[VERIFIED]` — "What's new in
.NET MAUI for .NET 10". Minimum platforms for .NET 10: iOS 12.2+,
Android 5+ `[VERIFIED]` — MAUI supported platforms page; the effective
iOS floor for B.1 is **13.0** because of AES-GCM (P9).

Avalonia stays the fallback only if desktop Linux becomes a target
(`EVOLUTION.md` §7.3). Confirmation is decision D2.

---

## 5. Journal write-back (Phase 4 design)

Specified here so Phase 1 does not close doors; not implemented before
decision D4.

### 5.1 Principle

The phone never edits replica rows. It appends **operations** to a
local journal. The user exports the journal as an encrypted file; the
desktop imports it and applies each operation through the existing use
cases, so every desktop invariant and validation applies unchanged.

### 5.2 Allowed operations

Only operations whose effect is **commutative** with any concurrent
desktop change are allowed:

| Operation | Desktop use case | Why safe |
|---|---|---|
| New package | `AddStock` (`NewPackage`) | Positive delta; independent of current stock |
| Manual add | `AddStock` (`ManualAdd`) | Same |
| Positive / negative correction with an explicit delta | `AddStock` (`PositiveCorrection`) / `AdjustStockDown` (`NegativeCorrection`) | Relative delta. The negative case re-runs `MedicineStock.WouldGoNegative` on the desktop; a rejection is reported, not forced |

Applying through the use cases also keeps `StockEpoch` handling
(which drives low-stock notification dedup) on the desktop side; the
phone never computes an epoch `[VERIFIED — AddStock and ReconcileStock
own the epoch logic]`.

Excluded, with reason:

| Operation | Reason |
|---|---|
| Guided stock count (`ReconcileStock`) | The delta is computed against the phone's stale expected stock; applied later it would be wrong. The phone may show the count gap, but the journaled operation would have to be the absolute count, which is not commutative |
| Register intake (`RegisterIntake`) | Double-count risk: if the desktop has already materialized an automatic `Consumption` for that day, adding the intake's movement counts the day twice. Needs a design decision (replace the automatic movement or reject); the same open point is recorded for A5 in `EVOLUTION-PROPOSALS.md`. §14 D5 |
| Add / edit / delete medicine, schedule change, suspension, slots | Conflicts with desktop edits are not resolvable without a merge model |
| Consumption movements | Always regenerated by the desktop catch-up (§3.3) |

### 5.3 Journal file

- Extension `.mrj`; ZIP with `manifest.json` + `payload.enc`, the same
  envelope, KDF and cipher as `.mrz` (`docs/EXPORT-FORMAT.md` §1, §4),
  with `format = "medreminder-journal"` so neither importer accepts the
  other's file.
- Manifest adds `profileId`, `baseSnapshotCreatedAtUtc` (the snapshot
  the phone was on) and a random `deviceId` (not the hostname).
- Payload: `journalSchemaVersion`, `operations[]`, each with
  `operationId` (GUID), `kind`, `medicineId`, `quantityDelta`,
  `occurredAt`, optional `notes`.
- Passphrase: the same archive passphrase the user entered for the
  snapshot.
- The format is documented in `docs/EXPORT-FORMAT.md` as a new section
  before it ships.

### 5.4 Desktop apply rules

1. Reject if `profileId` differs from the active profile (same
   confirmation pattern as the Import dialog for foreign archives).
2. Skip any operation whose `operationId` already exists as the `Id`
   of a stock movement: the journal is idempotent, a re-import is a
   no-op. The operation id becomes the movement id.
3. Skip, and report, operations whose `medicineId` no longer exists or
   is inactive.
4. Apply all remaining operations in one transaction through the use
   cases, then show a summary (applied / skipped / rejected, with
   reasons).
5. No schema change: stock movements already carry a GUID id and
   `Notes` `[VERIFIED]` — `ExportedStockMovement`. Origin marking
   ("recorded on phone") goes into `Notes` or, if D6 prefers a column,
   an idempotent boot patch in `DatabaseInitializer` (`CLAUDE.md` §7)
   plus an `ExportFormat.CurrentSchemaVersion` bump.

The phone clears exported operations only after the user confirms the
journal was imported, or when a newer snapshot shows the operation ids
as stock movements (automatic reconciliation).

---

## 6. Notifications on the phone

### 6.1 Model: plan ahead, no background loop

The desktop polls (`MedicationMonitorHostedService` every 30 minutes,
`DoseReminderHostedService` every 60 seconds) `[VERIFIED]`. A phone app
cannot rely on running in the background. Because the replica does not
change between imports, every future notification is **deterministic**
and can be scheduled with the OS in advance.

`NotificationPlanner` (Application, portable, unit-testable with a
fake `TimeProvider`) produces a list of `PlannedNotification(id,
fireAt, kind, medicineId, text)` from the replica. The mobile adapter
`ILocalNotificationScheduler` cancels all previously scheduled
notifications of the profile and registers the new plan. Re-planning
runs on import, app start, resume, time-zone change and after the user
changes a notification setting.

### 6.2 Low-stock warning

For each active medicine with a run-out forecast, one notification at
`runOutDate - ThresholdDays`, at a configurable local time (default
09:00), computed with `MedicineForecast` / `RunOutForecast`. If that
moment is already past, the warning is shown in the list (badge) and
one immediate notification fires once per replica import. Dedup across
re-plans uses a stable notification id derived from
`(profileId, medicineId, StockEpoch)`, mirroring the desktop
`NotificationEvent` key.

### 6.3 Dose reminders

For medicines with `RemindOnDose` and timed administration slots, one
notification per slot time over a rolling window, skipping suspended
days, days outside Start/End date and days where the projected stock is
zero (same conditions as `DoseReminderService` `[VERIFIED]` —
`ANALYSIS.md` §6). The window is bounded by the iOS limit on pending
local notifications (64 per app) `[UNCERTAIN — Apple UserNotifications
documentation, from training knowledge; confirm in S5]`; the planner
takes a per-platform budget and fills it nearest-first. Re-planning on
each app open keeps the window rolling. If the app is not opened for
longer than the window, reminders stop; the planner schedules a final
"open MedReminder to keep reminders active" notification at the end of
the window.

### 6.4 Platform constraints

- **Android 13+**: runtime permission `POST_NOTIFICATIONS`
  `[VERIFIED — Android developer documentation, training knowledge]`.
- **Android 14+**: `SCHEDULE_EXACT_ALARM` is denied by default for new
  installs of apps targeting API 33+; `USE_EXACT_ALARM` is reserved by
  Google Play policy to alarm-clock and calendar apps `[VERIFIED]` —
  Android 14 behavior changes page. Whether a medication reminder
  qualifies for `USE_EXACT_ALARM` is `[UNCERTAIN]`; the plan assumes it
  does not. Consequence: request `SCHEDULE_EXACT_ALARM` with an
  explanation screen; when denied, fall back to inexact alarms and tell
  the user reminders may be late by several minutes.
- **Reboot**: Android alarms are cleared on reboot `[INFERRED — platform
  behavior, training knowledge]`; the adapter re-plans on
  `BOOT_COMPLETED`.
- **iOS**: explicit authorization request; no exact-alarm distinction.

### 6.5 Library choice

.NET MAUI has **no built-in local-notification API**; `EVOLUTION.md`
§7.2 names a "MAUI `LocalNotification`" that does not exist in the
framework `[INFERRED — MAUI Essentials API surface, training knowledge]`
(correction in §15). Options:

- Community plugin (for example `Plugin.LocalNotification`): license,
  maintenance status and .NET 10 support `[UNCERTAIN]`.
- Thin native adapters: Android `AlarmManager` + `NotificationCompat`,
  iOS `UNUserNotificationCenter`, behind `ILocalNotificationScheduler`.

Recommendation: thin native adapters. The surface is small (schedule,
cancel by profile, permission state), avoids a third-party dependency
on a safety-relevant path and keeps `THIRD-PARTY-NOTICES.md` unchanged.
Spike S5 validates the choice.

### 6.6 Duplication with the desktop

Phone and desktop both notify. That is intended for low-stock and dose
reminders (different devices, the user may be away from the PC). Email
is not duplicated because the phone sends none. A phone setting per
profile turns each notification kind off.

---

## 7. Mobile UI

Screens for Phase 2, all read-only except settings:

1. **Onboarding**: short explanation, non-medical disclaimer, "Open
   backup file" (picker), passphrase entry with manifest summary.
2. **Medicine list**: name, stock, days remaining, run-out date,
   low-stock badge, snapshot age banner (§3.2). Same numbers as the
   desktop main grid, produced by the moved `MedicineOverviewLoader`.
3. **Medicine detail**: package, active ingredient, schedule and slots
   (text from the existing localized schedule descriptions), active and
   planned suspensions, doctor name, catalogue national code if linked.
   `Medicine.Notes` is shown only on this screen, never in a
   notification.
4. **Profiles**: list of replicas, import time, delete replica.
5. **Settings**: language, notification kinds and times, remember
   passphrase (opt-in), app lock, about / licenses / disclaimer.

Accessibility: system font scaling, screen reader labels, no
information conveyed by color alone (as the desktop timeline, PR #75).

### 7.1 Localization

- Reuse `assets/localization/strings.<lang>.json` for all five
  languages, embedded in the mobile app. `CLAUDE.md` §2 allows UI text
  only in these files; no separate mobile dictionary.
- New keys use the prefix `Mobile.` and are added to **all five**
  files in the same PR (`CLAUDE.md` §6).
- Existing keys used by the moved loader and schedule texts are reused
  as they are.
- The mobile user guide is a new section in the five
  `docs/USER_GUIDE.<lang>.md` files, not a separate guide (§14 D10).

---

## 8. Security and privacy

### 8.1 Data at rest

- Replica databases and the journal live only in the app sandbox
  (`FileSystem.AppDataDirectory`). Nothing is written to shared
  storage.
- **Android backup exclusion**: set `android:allowBackup="false"` and
  data-extraction rules that exclude the database directory, so health
  data does not reach Google cloud backup or device-to-device transfer
  outside the user's control `[INFERRED]`.
- **iOS**: files protected with complete-until-first-authentication or
  stronger; database directory excluded from iCloud backup
  `[UNCERTAIN — exact API to confirm in Phase 3]`.

### 8.2 Passphrase

- Default: not stored. The user enters it at every import.
- Opt-in "remember on this device": stored with MAUI `SecureStorage`
  (Android Keystore-backed, iOS Keychain) `[VERIFIED — MAUI Essentials,
  training knowledge]`. The passphrase decrypts every archive of the
  same desktop, so the opt-in screen states that.
- Passphrase, derived key and payload plaintext are never logged and
  are zeroed after use, as on desktop (`ANALYSIS.md` §10).

### 8.3 App lock

Optional biometric / device-credential lock on app open. Not a security
boundary beyond the device lock; a convenience equivalent to the
desktop PIN (`ANALYSIS.md` §10).

### 8.4 Logging and telemetry

- Local rolling log in the sandbox, same content rules as the desktop:
  identifiers and outcomes, never passwords, email bodies or medical
  notes (`CLAUDE.md` §7).
- No analytics, no crash-reporting SDK, no network calls except the
  optional update check (not planned for mobile; stores handle
  updates).

### 8.5 Store disclosures

- Google Play Data safety and Apple privacy labels: no data collected
  or shared by the developer, since nothing leaves the device
  `[INFERRED]`. To be reviewed against the forms before submission.
- Regulatory positioning unchanged: non-medical reminder tool, EU MDR
  scope avoided by not offering clinical functions (`EVOLUTION.md`
  §9.2). Store description reuses the existing disclaimer.

---

## 9. Build, CI and distribution

- **Repository**: same repository, new `src/MedReminder.Mobile` and
  `tests/MedReminder.Mobile.Tests` (logic only; platform adapters are
  tested manually). Shared `VersionPrefix` from
  `Directory.Build.props`.
- **`StripReleaseDebugArtifacts`**: runs on every non-test project in
  Release `[VERIFIED]` — `Directory.Build.props`. Its effect on Android
  and iOS packaging (it deletes `**\*.xml` from `OutputPath` after
  `Build`) is `[UNCERTAIN]`. `CLAUDE.md` §7 forbids weakening it. If
  spike S4 shows it breaks mobile packaging, the only acceptable change
  is a condition that excludes the mobile project while leaving desktop
  behavior identical, and it needs product-owner approval (§14 D11).
- **CI**: current workflow runs on `windows-latest` `[VERIFIED]` —
  `.github/workflows/dotnet-desktop.yml`. Add a job building
  `net10.0-android` (Windows or Linux runner with the MAUI workload)
  and, in Phase 3, a `macos` job for `net10.0-ios`. The portable test
  project also runs on Linux.
- **Signing**: Android keystore and Apple certificates kept outside the
  repository, injected as CI secrets; never committed.
- **Distribution**: Android first (Google Play, one-off 25 USD
  registration; signed APK on GitHub Releases as a sideload option).
  iOS second (Apple Developer Program, about 99 USD/year, TestFlight
  beta, App Store review) `EVOLUTION.md` §7.5.
- **Docs**: new `docs/PACKAGING.md` section for mobile builds; this
  document linked from `ANALYSIS.md` §12.

---

## 10. Tests

| Level | Project | Coverage |
|---|---|---|
| Unit | `MedReminder.Application.Tests` | Moved `MedicineOverviewLoader` (same expectations as today); `NotificationPlanner`: low-stock date, past-threshold case, dose window with suspensions, end date, zero projected stock, budget truncation nearest-first, DST transitions, time-zone change |
| Integration (portable, `net10.0`) | new `MedReminder.Infrastructure.Portable.Tests` | `ArchiveReader`: round-trip with a desktop-produced fixture archive, wrong passphrase, tampered tag, newer `formatVersion` / `schemaVersion`, missing entries, ZIP entries other than the two expected ones ignored; `ReplicaBuilder`: payload → database → repositories return identical data; `ConsumptionCatchUp` on a replica equals desktop result for the same day |
| Regression | `MedReminder.Infrastructure.Tests` (Windows) | Existing import/export tests unchanged and green after the Phase 1 extraction |
| Journal (Phase 4) | Application + Portable tests | Idempotent re-import, unknown medicine, negative correction rejected by `WouldGoNegative`, foreign profile, wrong format identifier |
| Manual | device checklist | Import from picker on each cloud client; notifications after reboot; exact-alarm denied path; permission revoked; language switch; font scaling; app lock; replica deletion |

Fixture: one `.mrz` generated by the desktop with a known passphrase,
stored under `tests/fixtures/` with synthetic data only.

---

## 11. Phases

Each phase lists entry criteria (preconditions), actions, deliverables
and exit criteria. One phase is one or more PRs; no phase starts before
the previous one's exit criteria are met, unless stated.

### Phase 0 — Decisions and feasibility spikes

**Entry criteria**

- Product owner approves starting B.1 (or re-orders it before A2).

**Actions**

1. Product owner answers D1–D4 and D7–D11 (§14). D5 and D6 may wait for
   Phase 4.
2. Spikes, each a throw-away branch with a short written result
   appended to §16 of this document:
   - **S1** — AES-GCM on Android: `AesGcm.IsSupported` and a decrypt of
     the fixture archive on an emulator and one physical device.
   - **S2** — Argon2id cost: time and peak memory of `DeriveKey` with
     `Argon2Params.Default` (3 iterations, 64 MiB, parallelism 1) on a
     low-end Android device and, if D1 includes iOS, an older iPhone.
     Acceptance: under 5 s and no out-of-memory. If it fails, the
     archive already carries its KDF parameters, so the fix is on the
     export side and is a separate product decision, not a mobile
     workaround.
   - **S3** — EF Core SQLite on Android (and iOS if in scope): create
     schema via `DatabaseInitializer`, insert, query, with Release
     trimming/AOT settings; record any trimming warnings.
   - **S4** — `StripReleaseDebugArtifacts` against a Release Android
     build: package installs and runs.
   - **S5** — local notifications: schedule, fire while app is killed,
     survive reboot, exact-alarm denied path, iOS pending limit.
3. Register accounts and prepare build hosts required by D1 (P12).

**Deliverables**: decisions recorded in §14; spike results in §16.

**Exit criteria**: S1, S3, S4 pass or have an accepted mitigation;
D1–D4 decided.

**Effort**: 5–8 days `[INFERRED]`.

### Phase 1 — Desktop portability refactor (no mobile code)

**Entry criteria**: Phase 0 exit; A2 phase in progress merged or
parked (avoid concurrent edits to `Infrastructure`).

**Actions**

1. Create `MedReminder.Infrastructure.Portable` (`net10.0`) and move:
   `Persistence/**`, `Export/ExportMapper.cs`, `Export/ExportJson.cs`,
   `Export/ArchiveCipher.cs`, the Konscious and EF Core SQLite package
   references, and the dictionary-loading part of
   `LocalizationService`. Keep namespaces to minimise churn, or rename
   in one mechanical commit (D9).
2. Introduce `IAppDataLocation` in Application; `AppDataPaths` becomes
   the Windows implementation.
3. Extract `IArchiveReader` / `ArchiveReader` and `ReplicaBuilder` from
   `ImportService`; make `ImportService` a Windows shell over them.
   Same for the manifest-listing part of `CloudRestoreService`.
4. Move `MedicineOverviewLoader` and `MedicineListItem` (or a UI-neutral
   projection of it) to Application; the WinForms grid binds to the
   projection.
5. Add `MedReminder.Infrastructure.Portable.Tests` (`net10.0`) and move
   the tests that do not need Windows; add the fixture archive.
6. Update `ANALYSIS.md` §2, §3 and the solution table; update
   `CHANGE_LOG.md`.

**Deliverables**: one or two PRs, zero user-visible change.

**Exit criteria**

- `dotnet build` and `dotnet test` green on Windows (user runs them
  before commit, `CLAUDE.md` §6).
- Portable tests green on Linux.
- Manual smoke on desktop: export, import, cloud restore, automatic
  cloud snapshot, main grid numbers unchanged.
- No `[SupportedOSPlatform("windows")]` or Windows API reference in the
  portable project.

**Effort**: 6–9 days `[INFERRED]`.

### Phase 2 — Android read-only companion (option R)

**Entry criteria**: Phase 1 merged; D1 names Android; Google Play
account available (for internal testing track) or sideload accepted
for the first beta; D3 (notifications default) decided.

**Actions**

1. Create `MedReminder.Mobile` (MAUI, `net10.0-android`), DI composition
   root registering Application + Portable + mobile adapters.
2. Import flow: picker → manifest summary → passphrase → `ArchiveReader`
   → `ReplicaBuilder` → atomic swap → `ConsumptionCatchUp` → re-plan
   notifications.
3. Screens §7; localization keys `Mobile.*` in all five dictionaries.
4. `NotificationPlanner` (Application) + Android
   `ILocalNotificationScheduler` adapter, boot receiver, permission
   screens (§6.4).
5. `SecureStorage` passphrase store (opt-in), app lock, Android backup
   exclusion (§8.1).
6. CI job for Android; signed Release build; `docs/PACKAGING.md` mobile
   section; user-guide section in five languages; `CHANGE_LOG.md`.

**Deliverables**: installable Android beta.

**Exit criteria**

- Numbers on the phone equal the desktop main grid for the same
  snapshot and day (manual check on the fixture and on a real profile).
- Manual checklist §10 passed on at least one Android 14+ physical
  device and one older supported version.
- No health data in logs; backup exclusion verified with
  `adb backup` / device transfer check `[UNCERTAIN — exact verification
  tool to confirm]`.

**Effort**: 25–40 days `[INFERRED — strongly dependent on MAUI
experience]`.

### Phase 3 — iOS

**Entry criteria**: Phase 2 exit; D1 includes iOS; macOS build host and
Apple Developer Program membership available; S1–S3, S5 repeated on iOS.

**Actions**: add `net10.0-ios`; iOS notification adapter (authorization,
pending-limit budget); document picker; Keychain via `SecureStorage`;
file protection and backup exclusion; macOS CI job; TestFlight;
privacy labels; App Store submission.

**Exit criteria**: manual checklist on a supported iPhone; App Store
review passed.

**Effort**: 12–20 days `[INFERRED]`.

### Phase 4 — Journal write-back (option J)

**Entry criteria**: Phases 2 (and 3 if in scope) in use for at least
one release cycle; product owner approves D4; D5 and D6 decided; the
`.mrj` format section drafted in `docs/EXPORT-FORMAT.md`.

**Actions**: phone "Record new package / correction" screens; local
journal store; `.mrj` export (share sheet or picker save); desktop
"Import phone journal" dialog applying §5.4; automatic journal
reconciliation from newer snapshots; tests §10; user guides;
`CHANGE_LOG.md`.

**Exit criteria**: idempotent re-import proven by tests; no operation
applied twice across import + newer snapshot cycle; rejected operations
reported to the user.

**Effort**: 12–18 days `[INFERRED]`.

### Phase 5 — Native cloud providers (C.3++ Phase 2)

Not part of B.1. B.1 Phase 2 in use satisfies the trigger stated in
`EVOLUTION.md` §6 and `ANALYSIS-C3PP-CLOUD-PROVIDERS.md` §14; the work
follows that document.

### 11.1 Summary

| Phase | Content | Depends on | Effort `[INFERRED]` |
|---|---|---|---|
| 0 | Decisions, spikes S1–S5, accounts | PO approval | 5–8 d |
| 1 | Portability refactor (desktop only) | Phase 0 | 6–9 d |
| 2 | Android read-only companion | Phase 1, D1, D3 | 25–40 d |
| 3 | iOS | Phase 2, macOS host, Apple account | 12–20 d |
| 4 | Journal write-back | Phase 2/3 in use, D4–D6 | 12–18 d |

Total for Phases 0–3: roughly 48–77 developer-days, consistent with the
2–4 developer-month range of `EVOLUTION.md` §7.4.

---

## 12. Risks

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| AES-GCM unavailable on some Android devices | Low `[UNCERTAIN]` | Blocking | Spike S1; `AesGcm.IsSupported` check with a clear error; no silent fallback cipher |
| Argon2id too slow / memory-heavy on low-end phones | Medium | Import unusable | Spike S2; progress UI; parameter change only on the export side via product decision |
| Exact alarms denied → late dose reminders | High on Android 14+ | Degraded reminders | Explain and request permission; inexact fallback stated in UI |
| User stops opening the app → reminders stop after the window | Medium | Missed reminders | End-of-window notification (§6.3); onboarding text |
| Stale replica read as current | Medium | Wrong stock perception | Snapshot age everywhere, staleness banner (§3.2) |
| Phone treated as sync by users | Medium | Data "lost" expectations | Read-only UI in Phase 2; guide text; journal only in Phase 4 |
| Phase 1 refactor regresses desktop import/export | Medium | High | Existing Windows tests as regression net; extraction without behavior change; manual smoke |
| MAUI tooling friction (workloads, iOS on macOS) | Medium `[INFERRED]` | Schedule | Android first; Phase 0 host setup |
| Store rejection over health wording | Low `[INFERRED]` | Delay | Reuse non-medical disclaimer; no clinical claims |
| Passphrase stored on phone widens exposure of every archive | Medium | High | Opt-in only, `SecureStorage`, explicit warning |

---

## 13. Non-goals — restated

Sync or merge; phone as writer of the profile database; email from the
phone; native cloud providers in Phases 0–4; catalogue on the phone;
profile registry, PIN and role management on the phone; medical-device
functions; desktop Linux; tablets optimised layouts (they get the phone
layout).

---

## 14. Decisions still to confirm

| # | Decision | Options | Proposal | Needed by |
|---|---|---|---|---|
| D1 | Platforms and order | Android only; Android then iOS; both at once | Android then iOS | Phase 0 |
| D2 | UI framework | MAUI; Avalonia | MAUI | Phase 0 |
| D3 | Notifications default | Low-stock on, dose off; both on; both off | Low-stock on, dose on only for medicines with `RemindOnDose` | Phase 2 |
| D4 | Approve journal write-back (option J) | Yes; no (stay read-only) | Decide after Phase 2 feedback | Phase 4 |
| D5 | Intake recording on phone | Excluded; allowed with "replace automatic consumption"; allowed with rejection on conflict | Excluded | Phase 4 |
| D6 | Origin marking of journal movements | `Notes` text; new column (schema patch + schema version bump) | `Notes` | Phase 4 |
| D7 | Staleness threshold | 1, 2, 7 days; configurable | 2 days, configurable | Phase 2 |
| D8 | Persistent folder access (auto-pick newest snapshot) | Phase 2; later | Later | Phase 2 |
| D9 | Portable project name and namespace strategy | `MedReminder.Infrastructure.Portable` keeping namespaces; rename | Keep namespaces | Phase 1 |
| D10 | Mobile user documentation | Section in existing guides; separate guide | Section in existing guides | Phase 2 |
| D11 | `StripReleaseDebugArtifacts` exclusion for the mobile project, only if S4 fails | Approve; reject | Decide on S4 evidence | Phase 2 |
| D12 | Minimum OS versions | Android API level; iOS 13 or higher | Android 8.0 (API 26), iOS 15 `[INFERRED — balance of device coverage and API availability; not measured]` | Phase 2 / 3 |

---

## 15. Corrections to other documents

To be applied in the Phase 1 PR:

- `EVOLUTION.md` §7.2: "Local notifications (MAUI `LocalNotification`)"
  — MAUI has no built-in local-notification API; use platform adapters
  or a community plugin (§6.5).
- `EVOLUTION.md` §7.6: the localization loader is already behind the
  Application port `ILocalizationService`, but its implementation is in
  the Windows Infrastructure project, not in `MedReminder.UI`. The
  preparation step is the move to the portable project (§11 Phase 1).
- `ANALYSIS-C3PP-CLOUD-PROVIDERS.md` §6.1: "`ExportService` and
  `ImportService` are already `net10.0` (no Windows dependency)" is
  wrong. Both live in `MedReminder.Infrastructure` (`net10.0-windows`)
  and are `[SupportedOSPlatform("windows")]`; `ImportService` also
  depends on DPAPI through `ICredentialProtector` `[VERIFIED]`.
- `EVOLUTION.md` §7.1 is correct, but reuse of Domain and Application
  alone is not sufficient: persistence and archive reading must also be
  portable (P5, P6).

---

## 16. Spike results

Empty until Phase 0 runs. One subsection per spike (S1–S5): date,
device / OS, result, decision.

---

## 17. Sources

- .NET MAUI support policy — dotnet.microsoft.com/platform/support/policy/maui
- Supported platforms for .NET MAUI apps (.NET 10) — learn.microsoft.com/dotnet/maui/supported-platforms
- What's new in .NET MAUI for .NET 10 — learn.microsoft.com/dotnet/maui/whats-new/dotnet-10
- `AesGcm` class and "Support AES-GCM for iOS-like platforms",
  dotnet/runtime issue #91523 — learn.microsoft.com/dotnet/api/system.security.cryptography.aesgcm; github.com/dotnet/runtime/issues/91523
- Android 14: schedule exact alarms are denied by default — developer.android.com/about/versions/14/changes/schedule-exact-alarms
- Internal: `EVOLUTION.md` §2.0, §6, §7, §8.1, §9.2;
  `ANALYSIS.md` §2, §6, §8, §10; `EXPORT-FORMAT.md`;
  `ANALYSIS-C3-EXPORT-IMPORT.md`; `ANALYSIS-C3PLUS-CLOUD-BACKUP.md`;
  `ANALYSIS-C3PP-CLOUD-PROVIDERS.md` §6, §14;
  `notes/EVOLUTION-PROPOSALS.md` §4.2.

---

## 18. Change log for this document

- 2026-09-26 — initial version. Preconditions audited against the tree
  (P1–P14); data exchange model R (read-only replica) for the first
  release and J (operation journal) as a separately approved phase;
  portable infrastructure split; notification planning model; phases
  0–4 with entry and exit criteria; decisions D1–D12; corrections to
  `EVOLUTION.md` §7.2, §7.6 and `ANALYSIS-C3PP` §6.1.
