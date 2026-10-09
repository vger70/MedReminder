# ANALYSIS — D.1: Vital-parameter recording (Desktop + Android)

Design document, **prior to implementation**. Once approved, work proceeds
on a branch named `feature/vital-tracking` (per `CLAUDE.md` §5).

> **Placement (product owner, 2026-10-08):** D.1 is **evolution V**. It is
> evaluated, for the desktop, Android and iOS together, only after the
> store release of the Android and iOS apps. It is not part of Android
> release 1 (milestones M0–M5 of `ANALYSIS-B1-ANDROID-PLAN.md`), which
> ships without any vital data, nor of the desktop roadmap before that
> release. This document is the design baseline for evolution V; when
> it is scheduled, re-check it against the shipped apps (entry list in
> `ANALYSIS-B1-ANDROID-PLAN.md` §4.3a) and add the iOS design, which
> this document does not cover.

> **Design objective:** add a personal vital-parameter diary without turning
> MedReminder into a system that remotely processes, synchronizes, interprets,
> or clinically monitors physiological data.

Epistemic classification: `[VERIFIED]`, `[INFERRED]`, `[UNCERTAIN]`, and
`[ANDROID EXPERT NOTE]` retain the terminology used by the sibling B.1
analysis documents.

---

## 1. Scope

### 1.1 Goal

Allow the user to record **vital-parameter readings** (blood pressure,
heart rate, blood glucose, body weight, SpO₂, body temperature, and
user-defined types) in MedReminder and review them as a chronological
list and descriptive time-series chart. Each reading is entered manually
by the user.

The intended purpose of D.1 is **personal recording, storage and display**.
The feature is designed as a health diary, not as a diagnostic, therapeutic,
clinical-decision or physiological-monitoring function.

### 1.2 What D.1 IS

- Manual entry of one reading per vital type, at a user-chosen date and time.
- Chronological list and descriptive time-series chart per vital type.
- Optional free-text note attached to each reading.
- Local storage in a **separate `vitals.db` database** for each profile.
- CSV export of the complete local vital history.
- CSV import as a user-initiated **merge + deduplication** operation.
- PDF export/printing of the selected vital history; PDF is **output-only**.
- Individual, per-type and bulk deletion.
- Local-only operation: vital data is **not synchronized between devices**,
  is **not included in `.mrz`**, and is **not included in cloud backup**.

### 1.3 Explicit non-goals and medical-device boundary

The design intentionally avoids features whose intended purpose could move
software into medical-device qualification under MDR. This is a design
boundary, not a legal guarantee that the product can never be a medical
 device: MDR qualification depends on the documented intended purpose,
including product claims, labeling and marketing. The current MDCG guidance
states that software solely recording, storing or displaying information can
generally fall outside the medical-device definition, while software intended
to monitor physiological processes or provide information for diagnostic or
therapeutic decisions is addressed by Rule 11. citeturn0search34turn0search35

D.1 therefore has **no**:

- reference ranges, clinical thresholds, abnormal/high/low classification;
- threshold or abnormality alerts;
- clinical interpretation or diagnosis;
- risk scores, prognosis or disease prediction;
- treatment or medication recommendations based on readings;
- dose adjustment or therapy adjustment;
- clinical decision support;
- AI/ML interpretation of readings;
- real-time physiological monitoring;
- automatic background acquisition from sensors;
- medical-device/sensor integration as an input source;
- claims that the feature detects, prevents, diagnoses, treats, monitors or
  manages a disease or medical condition.

The UI and store/website documentation must describe the feature as a
**personal diary/recording and visualization tool**. The medical disclaimer
remains available in the feature and Settings/Info, but it must not be used
to contradict medical claims elsewhere in product positioning.

### 1.4 MDR change-control trigger matrix

| Proposed future change | Action before implementation |
|---|---|
| New descriptive field or local export format | Engineering/privacy review |
| Reference ranges or calculated clinical indicators | Formal MDR qualification review |
| Trend interpretation, anomaly detection or health score | Formal MDR qualification review |
| Sensor/BLE/device import | Formal MDR qualification review and cybersecurity/privacy review |
| Abnormality or threshold alert | Formal MDR qualification review |
| Diagnosis, prognosis, treatment or dose recommendation | Stop feature development pending formal MDR assessment |
| Remote synchronization/cloud backup of vitals | New privacy/security assessment and updated DPIA; MDR review if purpose changes |
| AI analysis of vitals | Formal MDR + AI Act/privacy assessment before implementation |

A material change to intended purpose must be assessed before release; a
medical disclaimer alone is not a substitute for that assessment.

---

## 2. Domain model

### 2.1 New entities

`VitalType` and `VitalReading` remain domain entities, but their persistence
belongs to the **Vitals bounded context** and is not part of the B.1 sync
aggregate.

#### `VitalType`

| Property | Type | Notes |
|---|---|---|
| `Id` | `Guid` | Profile-local identity |
| `BuiltInKey` | `string?` | Non-null for built-in types |
| `Name` | `string` | Localized display name for built-ins; user-supplied for custom types |
| `PrimaryUnit` | `string` | e.g. `mmHg`, `bpm`, `mg/dL`, `kg`, `%`, `°C` |
| `HasSecondaryValue` | `bool` | `true` only for blood pressure |
| `SecondaryUnit` | `string?` | `mmHg` for BP |
| `DisplayOrder` | `int` | Presentation order |
| `IsHidden` | `bool` | Hides a type without deleting its history |
| `IsCustom` | `bool` | User-defined type marker |

#### `VitalReading`

| Property | Type | Notes |
|---|---|---|
| `Id` | `Guid` | Stable identity used for CSV deduplication |
| `VitalTypeId` | `Guid` | FK to `VitalTypes` in **`vitals.db` only** |
| `Value` | `decimal` | Primary value, precision `(18,4)` |
| `SecondaryValue` | `decimal?` | Diastolic BP when applicable |
| `Unit` | `string` | Snapshot of the unit at record time |
| `RecordedAt` | `DateTimeOffset` | User-selected measurement date/time |
| `Note` | `string?` | Optional, max 500 chars; never logged |
| `CreatedAt` | `DateTimeOffset` | Local insertion time, UTC |
| `UpdatedAt` | `DateTimeOffset` | Local edit time, UTC; **not a sync register** |

No foreign key exists from `vitals.db` to `medreminder.db`. The two databases
are joined only by the profile directory/installation context, never by a
SQLite cross-database FK.

### 2.2 Invariants

- `Value` is finite and >= 0.
- `SecondaryValue` is finite and >= 0 when the type requires it; otherwise null.
- `RecordedAt` must not be more than five minutes in the future.
- `Note` is trimmed; empty text becomes null.
- A `VitalReading.Id` is immutable and globally unique within the profile.
- Import never creates a second row with an existing `Id`.

### 2.3 Built-in vital types

| `BuiltInKey` | Display name (EN) | Unit | Secondary |
|---|---|---|---|
| `bp` | Blood pressure | mmHg | mmHg (diastolic) |
| `hr` | Heart rate | bpm | — |
| `bg` | Blood glucose | mg/dL | — |
| `weight` | Body weight | kg | — |
| `spo2` | Oxygen saturation | % | — |
| `temp` | Body temperature | °C | — |

Unit alternatives are outside D.1 to avoid conversion ambiguity.

---

## 3. Application layer

### 3.1 Use cases (`MedReminder.Application/Vitals/`)

| Use case | Input | Output |
|---|---|---|
| `LogVitalReading` | `VitalTypeId`, `Value`, `SecondaryValue?`, `RecordedAt`, `Note?` | `Guid` |
| `UpdateVitalReading` | `ReadingId`, same fields | — |
| `DeleteVitalReading` | `ReadingId` | — |
| `GetVitalHistory` | `VitalTypeId`, `From`, `To` | readings |
| `GetVitalTypes` | — | types |
| `HideVitalType` | `VitalTypeId`, `IsHidden` | — |
| `AddCustomVitalType` | `Name`, `Unit` | `Guid` |
| `DeleteCustomVitalType` | `VitalTypeId` | —; fails if readings exist |
| `ExportVitalCsv` | profile + destination | CSV file |
| `ImportVitalCsv` | CSV file | import report |
| `ExportVitalPdf` | selection/date range + destination | PDF file |
| `DeleteAllVitalData` | profile | — |

### 3.2 Ports

```text
IVitalReadingRepository
    GetByIdAsync(Guid id)
    GetHistoryAsync(Guid typeId, DateOnly from, DateOnly to)
    GetAllAsync()                 // never subject to a UI/free-tier visibility cap
    AddAsync(VitalReading reading)
    UpdateAsync(VitalReading reading)
    DeleteAsync(Guid id)
    DeleteByTypeAsync(Guid typeId)
    DeleteAllAsync()

IVitalTypeRepository
    GetAllAsync()
    GetByIdAsync(Guid id)
    GetByBuiltInKeyAsync(string key)
    AddAsync(VitalType type)
    UpdateAsync(VitalType type)
    DeleteAsync(Guid id)

IVitalTransferService
    ExportCsvAsync(...)
    ImportCsvMergeAsync(...)
    ExportPdfAsync(...)
```

`ImportCsvMergeAsync` returns counts for inserted, updated, skipped-invalid
and duplicate rows. It never writes to the medicine database.

---

## 4. Infrastructure layer

### 4.1 Separate database boundary

D.1 **must not add `VitalTypes` or `VitalReadings` to
`MedReminderDbContext`**. Instead introduce a dedicated `VitalsDbContext`
backed by a separate SQLite file:

```text
profiles/<profile-id>/
    medreminder.db        # medicines, stock, syncable profile data
    vitals.db             # VitalTypes + VitalReadings only
```

The exact root path follows the existing platform-specific profile layout.
The invariant is the two-file separation, not the absolute path.

The Vitals database has:

- a separate SQLite connection and EF Core context;
- a separate encryption key from `medreminder.db`;
- no cross-database foreign keys;
- no sync metadata tables;
- no archive/export metadata required by B.1;
- no telemetry or analytics tables.

### 4.2 Encryption

**Desktop:** SQLCipher (or an equivalent vetted encrypted SQLite provider) is
a D.1 release prerequisite. The existing `ICredentialProtector`/DPAPI path
stores the `vitals.db` key. The key is not stored inside `vitals.db`.

**Android:** `vitals.db` is encrypted with SQLCipher or the selected
supported encrypted-SQLite implementation. Its database key is protected by
Android Keystore-backed secure storage. Do not use a deprecated convenience
API as an architectural requirement.

The medicine database and vitals database use **different keys**. Compromise
of one key must not automatically expose the other database.

### 4.3 Schema

```sql
CREATE TABLE IF NOT EXISTS VitalTypes (
    Id TEXT NOT NULL PRIMARY KEY,
    BuiltInKey TEXT NULL,
    Name TEXT NOT NULL,
    PrimaryUnit TEXT NOT NULL,
    HasSecondaryValue INTEGER NOT NULL DEFAULT 0,
    SecondaryUnit TEXT NULL,
    DisplayOrder INTEGER NOT NULL DEFAULT 0,
    IsHidden INTEGER NOT NULL DEFAULT 0,
    IsCustom INTEGER NOT NULL DEFAULT 0
);

CREATE UNIQUE INDEX IF NOT EXISTS IX_VitalTypes_BuiltInKey
    ON VitalTypes (BuiltInKey) WHERE BuiltInKey IS NOT NULL;

CREATE TABLE IF NOT EXISTS VitalReadings (
    Id TEXT NOT NULL PRIMARY KEY,
    VitalTypeId TEXT NOT NULL REFERENCES VitalTypes (Id) ON DELETE RESTRICT,
    Value TEXT NOT NULL,
    SecondaryValue TEXT NULL,
    Unit TEXT NOT NULL,
    RecordedAt INTEGER NOT NULL,
    Note TEXT NULL,
    CreatedAt INTEGER NOT NULL,
    UpdatedAt INTEGER NOT NULL
);

CREATE INDEX IF NOT EXISTS IX_VitalReadings_VitalTypeId_RecordedAt
    ON VitalReadings (VitalTypeId, RecordedAt DESC);
```

Decimal values are stored as text through the existing EF conversion strategy
to preserve precision. `DatabaseInitializer` for the Vitals context seeds the
six built-in types idempotently by `BuiltInKey`.

### 4.4 CSV format and import semantics

The canonical CSV is UTF-8 with header:

```text
id,type_key,type_name,value,secondary_value,unit,recorded_at_utc,note,created_at_utc,updated_at_utc
```

Export always writes the **complete local history**, regardless of any
presentation limit. This prevents a UI limit from becoming a
data-portability limit.

Import is always explicit and local:

1. user selects a CSV through the platform file picker;
2. the file is parsed and validated before changes are committed;
3. built-in types are matched by `type_key`;
4. custom types are created when necessary, using stable `id` where available;
5. a reading whose `id` does not exist is inserted;
6. a reading whose `id` already exists is deduplicated and is not inserted
   twice; if the imported row is a newer user-edited representation, the
   application may update the existing row after deterministic field
   validation;
7. invalid rows are rejected and reported without partially applying an
   invalid row;
8. the operation produces an import summary.

No automatic folder watching, background import or cloud ingestion exists.
The CSV file remains under the user's control.

### 4.5 PDF export

PDF is a **presentation/export format only**. It contains the selected vital
type(s), date range, values, units and optional notes according to the user's
selection. It is generated locally and saved through the normal file picker.
It is never accepted by the import pipeline and is not a backup format.

### 4.6 `.mrz`, sync and cloud-backup exclusion

D.1 vital data is **completely excluded** from the existing `.mrz` archive.
The archive may continue to contain medicines, regimens, stock and other
B.1-defined data, but it contains no `VitalType`, `VitalReading` or
`vitals.db` bytes.

Consequently:

- `.mrz` restore never creates or overwrites vital data;
- cloud backup never uploads `vitals.db`;
- B.1 sync never emits or applies vital operations;
- generation reset/compaction never touches `vitals.db`;
- the vital database is not copied into any automatic backup path.

A future decision to include vitals in any remote or replicated flow is a
material architectural and regulatory change and requires a new review.

### 4.7 Deletion

`DeleteAllVitalData` deletes all rows in `VitalReadings` and removes custom
types that have no remaining history, while preserving built-in seed types.
Deletion is local and irreversible from the application's perspective.
It does not delete CSV/PDF files the user previously exported elsewhere.

---

## 5. Desktop UI — WinForms

### 5.1 Entry points

- New **Vitals** section in the main `NavigationPane`.
- `LogVitalReadingDialog` for manual entry.
- Chronological list with edit/delete actions.
- Explicit **Export CSV**, **Import CSV**, and **Export PDF** actions.
- Settings → Privacy contains **Delete all vitals data**.

### 5.2 Charting

Charts are descriptive only. Presets remain **7 d / 30 d / 90 d / 1 y / All**.
Blood pressure may show systolic and diastolic as separate lines. No visual
state such as green/red/normal/abnormal is introduced.

### 5.3 First-use notice

At first use the feature displays the medical/privacy notice and requires a
positive acknowledgment before entry. The checkbox is an **in-app privacy
acknowledgment**, not described in the UI as the developer's GDPR legal basis.

If the product later needs a true GDPR consent mechanism for a processing
activity performed by the developer, that mechanism must be designed
separately with the applicable controller role, purpose, withdrawal and
record-keeping requirements.

### 5.4 Localization

The existing five-language policy applies. Suggested keys include:

| Key | English |
|---|---|
| `vitals_section_title` | `Vital Parameters` |
| `vitals_log_reading` | `Log reading` |
| `vitals_import_csv` | `Import CSV` |
| `vitals_export_csv` | `Export CSV` |
| `vitals_export_pdf` | `Export PDF` |
| `vitals_delete_all` | `Delete all vitals data` |
| `vitals_import_summary` | `Import completed: {inserted} inserted, {duplicates} duplicates, {invalid} invalid.` |

The disclaimer and privacy notice must also be localized before release.

---

## 6. Android

### 6.1 D.1b placement

Android vital tracking belongs to evolution V (see the placement note
at the top), not to Android release 1 (M0–M5); the Android app ships
first without it. The rest of this section is the design baseline for
evolution V. No Android-specific sync work is required for D.1.

### 6.2 Platform implementation

| Concern | Android solution |
|---|---|
| Database | Separate encrypted `vitals.db` in app-private storage |
| Key protection | Android Keystore-backed secure storage |
| UI | Defined in `ANALYSIS-B1-UI-REQUIREMENTS.md`, which is authoritative for the Android UI (MAUI app of the B.1 plan); exact chart library decided at implementation |
| File export/import | Storage Access Framework (`ACTION_CREATE_DOCUMENT` / `ACTION_OPEN_DOCUMENT`) |
| CSV import | Explicit local merge + deduplication by `VitalReading.Id` |
| PDF | Local generation and share/save; never an import source |
| Sync/cloud | **None for vital data** |

The Android application must not place `vitals.db` under a directory covered
by an automatic cloud backup mechanism. The B.1 cloud backup provider receives
only the non-vital `.mrz` archive.

### 6.3 App lock and lifecycle

The existing Android app-lock/biometric controls protect access to the app,
while database encryption protects data at rest. Android lifecycle handling
must never export or upload vital data in background work.

---

## 7. Privacy and GDPR

### 7.1 Data classification

Vital readings can constitute **health data** under GDPR Article 4(15) when
they relate to a natural person. They are therefore special-category data
under Article 9 when GDPR applies.

However, the presence of health data on a user's own device does **not by
itself establish that the application developer is the controller of that
local processing**. The GDPR household exemption in Article 2(2)(c) can be
relevant to purely personal/household activity, but it is fact-specific and
must not be treated as an automatic exemption for every use case. The
architecture therefore minimizes the question by ensuring the developer has
no technical path to receive D.1 data.

### 7.2 Developer processing boundary

For D.1 the developer has no server-side vital-data processing purpose and
receives no vital readings, vital CSVs, PDFs, database files or telemetry
containing them.

Separate MedReminder services may have their own GDPR roles for account,
support or non-vital cloud operations. Those activities are outside
the D.1 local-vitals boundary and must not be conflated with it.

### 7.3 Data minimization

Only the fields required for a personal diary are stored. No diagnosis,
clinical interpretation, location, sensor identifier, national identifier or
remote device identifier is required by D.1.

### 7.4 Retention and deletion

There is no server-side retention period because the developer does not
receive the data. Local retention is controlled by the user until deletion.
The app must make deletion straightforward and must not silently delete
history merely because an app setting or a provider account changes.

Exported CSV and PDF files are separate user-controlled copies. Deleting the
records from the app does not delete copies the user saved elsewhere.

### 7.5 Portability and export

CSV is the canonical machine-readable vital-data transfer format. It is
available independently of `.mrz` and cloud services. This supports
user control and, where applicable, GDPR data portability. The application
must not make an unsupported blanket claim that Article 20 applies to every
local-only use case.

### 7.6 Logging

Vital values, notes and CSV/PDF contents must never be written to logs,
crash messages, analytics, telemetry or diagnostic identifiers. Import errors
must report row numbers and validation reasons without echoing health values
or notes.

### 7.7 Privacy Policy requirements

Before release, `PRIVACY.md` and translations must accurately state that:

1. vital readings are health data;
2. D.1 stores them locally in a separate protected database;
3. D.1 does not synchronize or cloud-backup them;
4. CSV and PDF exports are user-initiated local file operations;
5. exported copies are under the user's control;
6. the developer has no D.1 server-side vital-data processing path.

The policy must not claim that the developer is automatically the controller
of local diary data merely because it publishes the application.

---

## 8. Security and threat model

| Threat | Primary control |
|---|---|
| Physical access to Desktop files | SQLCipher + DPAPI-protected key |
| Physical access to Android files | Encrypted DB + app sandbox + Keystore |
| Accidental inclusion in `.mrz` | Separate DB/context and explicit archive exclusion tests |
| Accidental sync | No `SyncOperation` types; compile/test boundary |
| Cloud backup leakage | Backup code receives only `medreminder.db`/`.mrz`; never `vitals.db` |
| Exported CSV/PDF disclosure | User-controlled destination; explicit warning that exported files are plaintext unless user protects them |
| Malicious/tampered CSV import | Parse/validate before commit; deterministic ID handling; transaction |
| Log leakage | Field-level logging prohibition and tests |
| Wrong-profile import | Import is executed against the explicitly selected profile and shows profile context before commit |
| MDR scope drift | Change-control trigger matrix and release checklist |

### 8.1 Required automated tests

- `vitals.db` schema is created independently of `medreminder.db`.
- A `.mrz` export contains no `VitalType`, `VitalReading` or `vitals.db` bytes.
- Cloud backup tests prove that `vitals.db` is not handed to any
  `IArchiveStorage` provider.
- `SyncOperation` serialization has no vital operation types.
- CSV round-trip preserves IDs and decimal precision.
- Re-importing the same CSV produces zero duplicate rows.
- Import is atomic for invalid rows/failed validation.
- PDF generation never registers an import handler.
- Logs do not contain vital values or notes.
- Deleting app data removes local vital data, while exported external files
  remain outside the app's control.

---

## 9. Decisions

The prefix DV (renumbered from DA on 2026-10-08) keeps these decisions
apart from the DA decisions of `ANALYSIS-B1-ANDROID-PLAN.md`.

| # | Decision | Resolution |
|---|---|---|
| DV1 | Android vital-data entitlement | Superseded on 2026-10-09: the mobile apps are entirely free (`ANALYSIS-B1-ANDROID-PLAN.md` A6), so no tier limits vital history. **Export always includes the complete local history.** |
| DV2 | Vital-data synchronization | **No sync.** `VitalType` and `VitalReading` are outside B.1. |
| DV3 | Vital database | **Separate `vitals.db`**, separate EF Core context, separate encryption key, no FK to `medreminder.db`. |
| DV4 | `.mrz` treatment | **Vitals excluded completely** from `.mrz` export/import and cloud backup. |
| DV5 | CSV import | **Manual merge + deduplication by `VitalReading.Id`**. Existing IDs are not duplicated. |
| DV6 | PDF import | **Not supported.** PDF is export/print only. |
| DV7 | Desktop encryption | **Required before D.1 release**: SQLCipher/equivalent for `vitals.db`, with key protected through DPAPI. |
| DV8 | Android encryption | **Required**: encrypted `vitals.db`, key protected by Android Keystore-backed secure storage. |
| DV9 | MDR boundary | Manual diary, local storage, descriptive charts, CSV/PDF only; no clinical interpretation, alerts, diagnosis, therapy or remote monitoring. Material changes require formal assessment. |
| DV10 | First-use acknowledgement | Required as a privacy/safety acknowledgement; it is not presented as a universal GDPR legal basis. |

---

## 10. Files touched

| File / folder | Change |
|---|---|
| `src/MedReminder.Domain/Vitals/VitalType.cs` | New entity |
| `src/MedReminder.Domain/Vitals/VitalReading.cs` | New entity |
| `src/MedReminder.Application/Vitals/*.cs` | New use cases/DTOs |
| `src/MedReminder.Application/Abstractions/Vitals/*.cs` | Repository/transfer ports |
| `src/MedReminder.Infrastructure.Portable/Persistence/VitalsDbContext.cs` | **New separate EF Core context** |
| `src/MedReminder.Infrastructure.Portable/Persistence/MedReminderDbContext.cs` | **Do not add vital entities** |
| `src/MedReminder.Infrastructure.Portable/Persistence/Configurations/Vital*.cs` | Vitals DB mappings |
| `src/MedReminder.Infrastructure.Portable/Persistence/DatabaseInitializer.cs` | Vitals DB schema/seed path |
| `src/MedReminder.Infrastructure.Portable/Repositories/Vital*.cs` | New repositories |
| `src/MedReminder.Infrastructure.Portable/Export/` | CSV/PDF vital transfer services; `.mrz` explicitly excludes vitals |
| `src/MedReminder.Infrastructure/Security/DatabaseEncryptionMigrator.cs` | Vitals DB encryption/key migration as required |
| `src/MedReminder.UI/Forms/VitalsForm.cs` | Desktop vitals UI |
| `src/MedReminder.UI/Forms/LogVitalReadingDialog.cs` | Manual entry |
| `src/MedReminder.UI/Controls/VitalsChartControl.cs` | Descriptive chart |
| Android Vitals feature | Separate local DB, CSV/PDF transfer, no sync |
| `PRIVACY.md` and translations | Local-only health-data disclosure and export boundary |
| `docs/DPIA-VITAL-TRACKING.md` | Revised DPIA/assessment |

---

## 11. Release gates

D.1 cannot ship until all of the following are true:

1. `vitals.db` is separate from `medreminder.db` on Desktop and Android.
2. Separate database encryption keys are implemented and tested.
3. `.mrz` export/import contains no vital data.
4. Cloud backup contains no vital data.
5. Sync contains no vital operation types.
6. CSV export/import merge + deduplication is covered by tests.
7. PDF export is output-only.
8. Logs/telemetry/crash reporting cannot contain vital values or notes.
9. Privacy Policy accurately describes the local-only boundary.
10. Product/store/website copy has been reviewed against the MDR intended-purpose
    boundary; no unsupported medical claims are present.
11. The DPIA/assessment is approved internally and reviewed by legal/privacy
    counsel where appropriate before release.

---

*Document status: revised 7 October 2026 after product-owner confirmation of local-only vital storage, manual CSV merge/deduplication, and PDF export-only semantics. Revised 8 October 2026: D.1 placed as evolution V, after the store release of the Android and iOS apps; decisions renumbered DV1–DV10; Android UI defined by `ANALYSIS-B1-UI-REQUIREMENTS.md`.*
