# ANALYSIS — D.1: Vital-parameter tracking (Desktop + Android)

Design document, **prior** to implementation. Once approved, work
proceeds on a branch named `feature/vital-tracking` (per `CLAUDE.md`
§5). Corresponds to the new item D.1 of the evolution backlog.

> **This is not a speculative analysis.** Every decision is
> technically motivated and delimits what will be written in code.
> The "Decisions still to confirm" section at the end is the only
> zone of ambiguity that needs product-owner input.

Epistemic classification (aligned with sibling documents):
`[VERIFIED]` (checked against the current tree), `[INFERRED]`
(deduction from verified facts), `[UNCERTAIN]` (hypothesis pending
confirmation). Untagged statements are design proposals.

---

## 1. Scope

### 1.1 Goal

Allow the user to record **vital-parameter readings** (blood pressure,
heart rate, blood glucose, body weight, SpO₂, body temperature, and
user-defined types) in MedReminder and review them as a **chronological
time-series chart**. Each reading is entered manually by the user.

The feature extends MedReminder as a **personal health diary**. It does
not extend it as a medical device.

### 1.2 What D.1 IS

- Manual entry of one reading per vital type, at a user-chosen date
  and time.
- Chronological list and time-series line chart per vital type.
- Optional free-text note attached to each reading.
- Export of vital readings as part of the existing profile export
  (`.mrz` archive, CSV/JSON).
- Deletion of individual readings and bulk deletion ("delete all
  vitals data").
- Sync of vital readings between devices (Desktop ↔ Android, within
  the existing sync group), once sync is enabled by the user (B.1).

### 1.3 What D.1 is NOT — the medical-device boundary

This is the single most important constraint of the feature. Any
deviation from these non-goals would move MedReminder towards EU MDR
2017/745 Class IIa territory and require a full conformity assessment.

- **No reference ranges, no threshold alerts.** The app does not mark
  a reading as "high", "low" or "abnormal". It does not notify the user
  when a value is outside a clinical range.
- **No clinical interpretation.** No trend analysis presented as a
  clinical recommendation. A chart shows the historical line; the
  clinical meaning is the user's and their doctor's responsibility.
- **No therapy adjustment.** The app does not link a vital reading to
  a medication change, dose suggestion or therapy recommendation.
- **No connection to medical-grade sensors.** D.1 reads no data from
  BLE/Bluetooth devices that hold a CE-MDR or FDA clearance mark. Manual
  entry only. (Sensor integration, if ever desired, requires a separate
  MDR assessment and is explicitly out of scope here.)
- **No real-time monitoring.** No polling, no background sensor feed.
  The reading is a snapshot the user provides.

The medical disclaimer required by the legal-compliance guide (§1 of
that document) must be displayed at first use of the vital-tracking
feature and accessible from the Settings / Info section.

---

## 2. Domain model

### 2.1 New entities

Two new aggregate roots are added to `MedReminder.Domain`. They live
in a new folder `Domain/Vitals/`.

#### `VitalType`

A vital-parameter kind. The application ships a set of built-in types
(see §2.3); the user may add custom ones.

| Property | Type | Notes |
|---|---|---|
| `Id` | `Guid` | |
| `BuiltInKey` | `string?` | Non-null for built-in types; code uses this key, not the name |
| `Name` | `string` | Localised display name for built-ins; user-supplied for custom ones |
| `PrimaryUnit` | `string` | e.g. `"mmHg"`, `"bpm"`, `"mg/dL"`, `"kg"`, `"%"`, `"°C"` |
| `HasSecondaryValue` | `bool` | `true` only for blood pressure (systolic + diastolic) |
| `SecondaryUnit` | `string?` | Non-null when `HasSecondaryValue`; `"mmHg"` for BP |
| `DisplayOrder` | `int` | Presentation order; built-ins get low values, custom ones append |
| `IsHidden` | `bool` | User hides a built-in type they do not use |
| `IsCustom` | `bool` | `true` for user-defined types |

`VitalType` is a profile-local configuration entity: two profiles can
have different custom types. Built-in types are seeded by
`DatabaseInitializer` on first access; the seed is idempotent and
matches the `BuiltInKey` `[VERIFIED — same pattern as `DoseTimePreset`
seeds]`.

#### `VitalReading`

One measurement, entered by the user.

| Property | Type | Notes |
|---|---|---|
| `Id` | `Guid` | |
| `VitalTypeId` | `Guid` | FK → `VitalTypes`; `Restrict` (deactivate, do not delete the type) |
| `Value` | `decimal` | Primary value; stored with precision (18, 4) |
| `SecondaryValue` | `decimal?` | Non-null when `VitalType.HasSecondaryValue`; diastolic BP |
| `Unit` | `string` | Copied from `VitalType.PrimaryUnit` at record time; preserved even if the type's unit is later edited |
| `RecordedAt` | `DateTimeOffset` | User-chosen date and time; stored as UTC ticks in SQLite |
| `Note` | `string?` | Free-text, max 500 characters; never logged |
| `CreatedAt` | `DateTimeOffset` | Server-side insertion time (UTC); used for sync conflict resolution |

`VitalType` deletion is blocked when readings exist (`Restrict`). The
user deactivates a type instead (`IsHidden = true`).

### 2.2 Invariants (enforced in the domain)

- `Value` must be a positive finite decimal.
- `SecondaryValue` must be a positive finite decimal when
  `HasSecondaryValue`, null otherwise.
- `RecordedAt` must not be in the future by more than 5 minutes
  (tolerates clock skew; prevents accidental future entries).
- `Note`, when present, is trimmed; empty string is stored as null.

### 2.3 Built-in vital types

| `BuiltInKey` | Display name (EN) | Unit | Secondary |
|---|---|---|---|
| `bp` | Blood pressure | mmHg | mmHg (diastolic) |
| `hr` | Heart rate | bpm | — |
| `bg` | Blood glucose | mg/dL | — |
| `weight` | Body weight | kg | — |
| `spo2` | Oxygen saturation | % | — |
| `temp` | Body temperature | °C | — |

Unit alternatives (e.g. `mg/dL` vs `mmol/L` for glucose, `°C` vs `°F`
for temperature, `kg` vs `lbs` for weight) are **not implemented in
D.1**. The preferred unit is fixed per built-in type. This avoids
conversion bugs and the resulting clinical misreading risk. Unit choice
can be reconsidered in a later item with explicit product-owner
approval.

---

## 3. Application layer

### 3.1 New use cases (`MedReminder.Application/Vitals/`)

| Use case | Input | Output |
|---|---|---|
| `LogVitalReading` | `VitalTypeId`, `Value`, `SecondaryValue?`, `RecordedAt`, `Note?` | `Guid` (new reading id) |
| `UpdateVitalReading` | `ReadingId`, same fields as above | — |
| `DeleteVitalReading` | `ReadingId` | — |
| `GetVitalHistory` | `VitalTypeId`, `From` (`DateOnly`), `To` (`DateOnly`) | `IReadOnlyList<VitalReadingDto>` |
| `GetVitalTypes` | — | `IReadOnlyList<VitalTypeDto>` |
| `HideVitalType` | `VitalTypeId`, `IsHidden` | — |
| `AddCustomVitalType` | `Name`, `Unit` | `Guid` |
| `DeleteCustomVitalType` | `VitalTypeId` | — (fails if readings exist) |

### 3.2 New ports (`Application/Abstractions/Vitals/`)

```
IVitalReadingRepository
    Task<VitalReading?> GetByIdAsync(Guid id);
    Task<IReadOnlyList<VitalReading>> GetHistoryAsync(Guid typeId, DateOnly from, DateOnly to);
    Task AddAsync(VitalReading reading);
    Task UpdateAsync(VitalReading reading);
    Task DeleteAsync(Guid id);
    Task DeleteAllAsync();                         // for right-to-erasure

IVitalTypeRepository
    Task<IReadOnlyList<VitalType>> GetAllAsync();
    Task<VitalType?> GetByIdAsync(Guid id);
    Task<VitalType?> GetByBuiltInKeyAsync(string key);
    Task AddAsync(VitalType type);
    Task UpdateAsync(VitalType type);
    Task DeleteAsync(Guid id);
```

---

## 4. Infrastructure layer

### 4.1 Persistence (`MedReminder.Infrastructure.Portable`)

Two new EF Core entity configurations in
`Infrastructure.Portable/Persistence/Configurations/`:

- `VitalTypeConfiguration` — maps to table `VitalTypes`
- `VitalReadingConfiguration` — maps to table `VitalReadings`

Both are added to `MedReminderDbContext` and their schema is created by
a new idempotent patch in `DatabaseInitializer` `[VERIFIED — same
pattern used by every schema extension since B.1]`.

```sql
-- patch applied once on first open of the database after the update
CREATE TABLE IF NOT EXISTS VitalTypes (
    Id          TEXT    NOT NULL PRIMARY KEY,
    BuiltInKey  TEXT    NULL,
    Name        TEXT    NOT NULL,
    PrimaryUnit TEXT    NOT NULL,
    HasSecondaryValue INTEGER NOT NULL DEFAULT 0,
    SecondaryUnit TEXT  NULL,
    DisplayOrder INTEGER NOT NULL DEFAULT 0,
    IsHidden    INTEGER NOT NULL DEFAULT 0,
    IsCustom    INTEGER NOT NULL DEFAULT 0
);
CREATE UNIQUE INDEX IF NOT EXISTS IX_VitalTypes_BuiltInKey
    ON VitalTypes (BuiltInKey) WHERE BuiltInKey IS NOT NULL;

CREATE TABLE IF NOT EXISTS VitalReadings (
    Id              TEXT    NOT NULL PRIMARY KEY,
    VitalTypeId     TEXT    NOT NULL REFERENCES VitalTypes (Id) ON DELETE RESTRICT,
    Value           TEXT    NOT NULL,        -- decimal stored as TEXT
    SecondaryValue  TEXT    NULL,
    Unit            TEXT    NOT NULL,
    RecordedAt      INTEGER NOT NULL,        -- UTC ticks
    Note            TEXT    NULL,
    CreatedAt       INTEGER NOT NULL
);
CREATE INDEX IF NOT EXISTS IX_VitalReadings_VitalTypeId_RecordedAt
    ON VitalReadings (VitalTypeId, RecordedAt DESC);
```

After schema creation, `DatabaseInitializer` seeds the six built-in
`VitalType` rows (§2.3), matching by `BuiltInKey` so the seed is safe
to re-run on every app start.

### 4.2 Repository implementation

`VitalReadingRepository` and `VitalTypeRepository` implement the ports
from §3.2. They follow the same pattern as `MedicineRepository`: EF
Core queries, no raw SQL, one `DbContext` per scope `[VERIFIED]`.

### 4.3 Export and deletion

`GetVitalHistory` with the full date range feeds the existing export
path. The `.mrz` archive format gains a new section
`vitals/<profile-id>/vital_types.json` and
`vitals/<profile-id>/vital_readings.json` (same JSON convention as
`medicines.json`). The export schema version in the archive header is
bumped. Import is additive and idempotent (merge-or-skip by `Id`).

`DeleteAllAsync` is called when the user clicks "Delete all vitals data"
(distinct button in Settings, separate from "Delete profile") or when
the profile itself is deleted.

---

## 5. UI layer (Desktop — WinForms)

### 5.1 Entry points

- A new **"Vitals" section** added to the main-window `NavigationPane`,
  between the existing sections (exact position decided at
  implementation). The section shows the vital-type list and the chart
  for the selected type.
- A **"Log reading"** button opens a modal dialog
  (`LogVitalReadingDialog`) pre-filled with the current date and time.
- Readings in the list are editable (double-click) and deletable
  (Delete key, with confirmation).

### 5.2 Charting

The chart renders a line series of `(RecordedAt, Value)` pairs for the
selected type over a configurable date range (7 d / 30 d / 90 d / 1 y /
all). For blood pressure, two lines (systolic, diastolic) share the same
Y axis.

**Charting library**: `System.Windows.Forms.DataVisualization` (the
`Microsoft.Windows.Compatibility` package), already transitively
available on Windows `[VERIFIED — MedReminder.UI targets
net10.0-windows10.0.19041.0]`. No new external package is introduced at
this stage. The chart control is wrapped in a thin
`VitalsChartControl : UserControl` to keep the form testable.

If `DataVisualization` proves insufficient for the required rendering
quality (label overlap, touch-zoom on future Android port), OxyPlot
(MIT, v2.x) is the preferred alternative. That decision is deferred to
implementation.

### 5.3 Consent screen (first use)

At the first opening of the Vitals section, before any data is entered,
the user sees a non-skippable acknowledgment panel containing:

1. The medical disclaimer from the legal-compliance guide §1 (verbatim,
   all four paragraphs).
2. A **non-pre-selected checkbox**: "I have read and understood the above
   notice."
3. A separate **non-pre-selected checkbox**: "I consent to MedReminder
   storing my vital-parameter data on this device." (GDPR Art. 9.2.a
   separate opt-in, not bundled with the general Terms.)
4. A "Continue" button, enabled only when both checkboxes are ticked.

The consent state is stored in `ui.settings.json` per profile, as a
timestamped boolean (`vitalsConsentGivenAt`). If the user has not
consented, the Vitals section is locked and shows only the consent
panel. The user may revoke consent from Settings → Privacy, which hides
the section but does not delete data (a separate "Delete all vitals
data" button does that).

### 5.4 Localization

New string keys added to all five `strings.<lang>.json` files
`[VERIFIED — CLAUDE.md §6 requirement]`:

| Key | English value |
|---|---|
| `vitals_section_title` | `Vital Parameters` |
| `vitals_log_reading` | `Log reading` |
| `vitals_type_bp` | `Blood pressure` |
| `vitals_type_hr` | `Heart rate` |
| `vitals_type_bg` | `Blood glucose` |
| `vitals_type_weight` | `Body weight` |
| `vitals_type_spo2` | `Oxygen saturation` |
| `vitals_type_temp` | `Body temperature` |
| `vitals_unit_mmhg` | `mmHg` |
| `vitals_unit_bpm` | `bpm` |
| `vitals_unit_mgdl` | `mg/dL` |
| `vitals_unit_kg` | `kg` |
| `vitals_unit_pct` | `%` |
| `vitals_unit_celsius` | `°C` |
| `vitals_consent_title` | `Vital-parameter diary — Important notice` |
| `vitals_consent_notice_checkbox` | `I have read and understood the above notice.` |
| `vitals_consent_data_checkbox` | `I consent to MedReminder storing my vital-parameter data on this device.` |
| `vitals_consent_continue` | `Continue` |
| `vitals_delete_all` | `Delete all vitals data` |
| `vitals_delete_all_confirm` | `This will permanently delete all vital-parameter readings for this profile. This cannot be undone.` |
| `vitals_disclaimer_accessible` | `Medical disclaimer` |

Italian, French, Spanish and German translations must be added by the
same PR. Machine-translation drafts are acceptable for non-English;
a native-speaker review is recommended before release.

---

## 6. Sync (B.1 integration)

Vital readings are profile data and must sync between devices in the
same household. The sync model (CRDTs / HLC / LWW registers, B.1
§5) `[VERIFIED]` applies without changes to the merge logic.

### 6.1 Sync operations for vitals

`SyncOperation` gains two new `Type` values:

| Type | Payload | Meaning |
|---|---|---|
| `VitalReadingUpsert` | `VitalReadingPayload` (full reading) | Create or update a reading (last-writer-wins on `CreatedAt`) |
| `VitalReadingDelete` | `{ "id": "<guid>" }` | Tombstone: the reading is deleted on all peers |

`VitalTypeUpsert` and `VitalTypeDelete` are added for custom types.
Built-in types are never synced (they are seeded locally).

### 6.2 Conflict rule

`VitalReading` is treated as an immutable fact once recorded: its only
conflict scenario is a concurrent delete on one peer and edit on another.
The delete wins (same rule as `FactRetraction` `[VERIFIED]`).

### 6.3 Schema version bump

The sync schema version is incremented so that a device running an older
version without the `vitals` operations ignores them gracefully (existing
unknown-type handling in the apply loop `[INFERRED — to verify at
implementation]`).

---

## 7. Android (B.1 milestone mapping)

The Android implementation reuses every portable layer: `VitalType`,
`VitalReading`, `DatabaseInitializer` patches, the repository
implementations, all use cases. None of this requires platform-specific
code.

Platform-specific work on Android:

| Concern | Android solution |
|---|---|
| Charting | MPAndroidChart (Apache 2.0) or Vico (Apache 2.0); no OxyPlot on Android (WinForms binding). Decision in the Android milestone. |
| Consent screen | Jetpack Compose dialog with the same two checkboxes; consent flag stored in the Android equivalent of `ui.settings.json` (a JSON file in internal storage, same format). |
| Keystore | No additional keystore needed: vital readings go into the same SQLite database protected by the existing `EncryptedSharedPreferences` + `AndroidKeystore` key. |
| Export | Vital data is included in the `.mrz` export via the existing SAF `ACTION_CREATE_DOCUMENT` path (§4.3 of `ANALYSIS-B1-ANDROID-PLAN.md`). |

**Milestone placement** (relative to B.1):

| Milestone | Desktop | Android |
|---|---|---|
| D.1a | Desktop: Domain, Application, Infrastructure.Portable, full UI with consent screen and chart | — |
| D.1b | — | Android UI, MPAndroidChart or Vico integration, consent screen |

D.1a is a self-contained Desktop release. D.1b follows after the B.1 M1
Android baseline is stable and can be scheduled independently.

---

## 8. Privacy and GDPR compliance

### 8.1 Data classification

`VitalReading` rows are **special-category data (Art. 9 GDPR)** —
health data — regardless of the fact that the user enters them manually.
This is not in question.

### 8.2 Legal basis

Art. 9.2.a GDPR: **explicit consent** of the data subject, collected via
the first-use consent screen (§5.3). The consent is specific (vitals
only), informed (medical disclaimer shown), freely given (the user can
use MedReminder without ever opening the Vitals section) and
unambiguous (active double opt-in, no pre-ticking).

### 8.3 Data minimization

The feature collects only: type, value(s), timestamp, optional note.
No user identifier beyond the profile context; no device identifier;
no geolocation; no source device metadata.

### 8.4 Data retention and deletion

No automated retention policy: the data is kept until the user deletes
it. The user can:

- Delete individual readings (in the list).
- Delete all readings for a type (via type management).
- Delete all vitals data (Settings → Privacy → Delete all vitals data).
- Delete the profile, which cascades to all its vitals data.

The DPIA (companion document) confirms that manual-retention with
user-controlled deletion satisfies the storage-limitation principle
for a personal diary application with no server component.

### 8.5 Data portability (Art. 20 GDPR)

Vital readings are included in the profile `.mrz` export (§4.3) and,
for Desktop, in the plain-text CSV export from Settings → Backup /
Restore → Export all data. The CSV columns are:
`type_key`, `type_name`, `value`, `secondary_value`, `unit`,
`recorded_at_utc`, `note`.

### 8.6 Log policy

`Note` values and raw `Value` readings must **never appear in log
output**, consistent with the existing prohibition on logging medical
notes `[VERIFIED — CLAUDE.md §7]`. A lint rule (Roslyn analyzer or
naming-convention test) is added to enforce this at build time.

### 8.7 Desktop database encryption (status quo and plan)

The current Desktop SQLite database is **not encrypted at rest**
`[VERIFIED — PRIVACY.md §3]`. The Privacy Policy already discloses
this. The Vitals feature increases the sensitivity of the data in the
same database; this does not by itself justify introducing SQLCipher on
the desktop in D.1, but the DPIA records it as a residual risk and
recommends evaluating database encryption as a follow-on item.

On Android the database is protected by the existing `EncryptedSharedPreferences`-backed SQLCipher key in the app sandbox `[INFERRED
from §4.7 of ANALYSIS-B1-ANDROID-PLAN.md]`.

---

## 9. Decisions still to confirm

Before implementation begins, the product owner must confirm or reject
each of the following open points.

| # | Question | Default assumed in this document |
|---|---|---|
| DA1 | Is vital tracking free or premium on Android? | **Free core** (non-paying users also benefit from the personal diary) |
| DA2 | Are vitals synced between devices from day one of D.1b, or added later? | **Synced from D.1b** (M2 Android milestone) |
| DA3 | Should the user be able to rename built-in types and change their units? | **No in D.1** (adds conversion-error risk; deferred) |
| DA4 | Export format for vitals in plain text: CSV only, or also JSON? | **CSV only** in D.1; JSON already available via `.mrz` |
| DA5 | Desired date-range options for the chart? | 7 d / 30 d / 90 d / 1 y / all |
| DA6 | Is the "Delete all vitals data" button under Settings → Privacy or in the Vitals section itself? | **Settings → Privacy** (consistent with "Delete profile") |
| DA7 | Does Desktop need database encryption (SQLCipher) as part of D.1 or as a separate item? | **Separate item**; DPIA records it as residual risk |
| DA8 | Is iOS in scope for D.1 or a later item? | **Later item** (D.1c, after B.1 Phase 6) |

---

## 10. Files touched

| File / folder | Change |
|---|---|
| `src/MedReminder.Domain/Vitals/VitalType.cs` | New entity |
| `src/MedReminder.Domain/Vitals/VitalReading.cs` | New entity |
| `src/MedReminder.Application/Vitals/*.cs` | New use cases and DTOs |
| `src/MedReminder.Application/Abstractions/Vitals/IVitalReadingRepository.cs` | New port |
| `src/MedReminder.Application/Abstractions/Vitals/IVitalTypeRepository.cs` | New port |
| `src/MedReminder.Infrastructure.Portable/Persistence/MedReminderDbContext.cs` | Two new `DbSet<>` |
| `src/MedReminder.Infrastructure.Portable/Persistence/Configurations/VitalTypeConfiguration.cs` | New |
| `src/MedReminder.Infrastructure.Portable/Persistence/Configurations/VitalReadingConfiguration.cs` | New |
| `src/MedReminder.Infrastructure.Portable/Persistence/DatabaseInitializer.cs` | Schema patch + built-in seed |
| `src/MedReminder.Infrastructure.Portable/Repositories/VitalReadingRepository.cs` | New |
| `src/MedReminder.Infrastructure.Portable/Repositories/VitalTypeRepository.cs` | New |
| `src/MedReminder.Infrastructure.Portable/Export/` | Extend `.mrz` writer/reader for vitals section |
| `src/MedReminder.UI/Forms/VitalsForm.cs` (and partials) | New main form section |
| `src/MedReminder.UI/Forms/LogVitalReadingDialog.cs` | New dialog |
| `src/MedReminder.UI/Controls/VitalsChartControl.cs` | New chart wrapper |
| `assets/localization/strings.en.json` … `strings.de.json` | New keys (§5.4) |
| `PRIVACY.md` and translations | New §3 paragraph for vital readings |
| `docs/DPIA-VITAL-TRACKING.md` | Companion DPIA (companion document) |

---

*Document status: draft, pending product-owner sign-off on §9.*
