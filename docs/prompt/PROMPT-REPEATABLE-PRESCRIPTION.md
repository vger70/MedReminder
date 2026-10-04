# Implementation prompt — Repeatable prescription with several dispensations

Briefing for the Claude Code session that implements item A1 of the
evolution plan prepared on 2026-10-04 (competitor benchmark and
cost/benefit plan, kept outside this repository). Read it fully, then
read the referenced files before changing code.

---

## 1. Goal

Let one prescription cover several pharmacy dispensations over a long
validity, and use the dispensations left to change what the app tells
the user when stock runs low: "collect the next dispensation" instead of
"ask the doctor for a new prescription".

Today a prescription has one `CollectedOn` and a default validity of 30
days (`PrescriptionRules.DefaultValidityDays`). A prescription that
covers a year of therapy in twelve dispensations can only be recorded as
twelve prescriptions entered by hand.

Why now: Italian Law 182/2025, art. 62 (in force since 2025-12-18),
allows a dematerialised repeatable prescription of up to 12 months for
stabilised chronic patients, dispensed 30 days of therapy at a time. Its
implementing decree was due within 90 days; whether it has been adopted
is **[UNCERTAIN]**. The feature must therefore be generic and
parametric: a number of dispensations and a validity chosen by the user,
with defaults, and no rule hard-coded from the law. The same model
serves other repeatable prescriptions and other countries.

Target effort: 7–10 days **[INFERRED]**.

## 2. Context to read first

- `CLAUDE.md` — §5 (runtime data), §7 (boot patches in
  `DatabaseInitializer`, no codes or medical data in logs, database
  gate).
- `docs/notes/EVOLUTION-PROPOSALS-2.md` §3.2 — the prescription
  lifecycle this item extends.
- `docs/SYNC-FORMAT.md` — operation schema versions, the
  `PrescriptionChanged` and `PackageChanged` registers, the image schema
  and the rule that a new type stops an older app (R7).
- `docs/EXPORT-FORMAT.md` §3.14 (`prescriptions[]`) and §5 (additive
  fields).
- Domain: `src/MedReminder.Domain/Prescriptions/Prescription.cs`,
  `PrescriptionRules.cs`, `PrescriptionReminderEvent.cs`;
  `src/MedReminder.Domain/Sync/SyncOperationBodies.cs`
  (`PrescriptionChanged`, `PackageChanged`).
- Application: `src/MedReminder.Application/Prescriptions/`
  (`PrescriptionUseCases.cs`, `PrescriptionReminders.cs`,
  `PrescriptionListQuery.cs`, `PrescriptionRequestTexts.cs`);
  `Sync/OperationCodec.cs` (`CurrentSchemaVersion` is 11),
  `Sync/SyncRegisters.cs`, `Sync/ApplyRemoteOperations.cs`;
  `Monitoring/MedicationMonitor.cs`; `Notifications/NotificationTexts.cs`;
  `Calendar/CalendarEntries.cs`; `Export/ExportPayload.cs`.
- Infrastructure.Portable: `Persistence/DatabaseInitializer.cs`
  (the `Prescriptions` table patch), `Persistence/Configurations/
  PrescriptionConfiguration.cs`, `Persistence/Repositories/
  PrescriptionRepository.cs`, `Export/ExportMapper.cs`,
  `Export/ProfileDatabaseBuilder.cs`, `Sync/SqliteSyncSnapshotStore.cs`.
- UI: `src/MedReminder.UI/Forms/PrescriptionsDialog.cs`,
  `PrescriptionEditDialog.cs`, and `MainForm.cs`
  (`OfferPrescriptionCollectedAsync`, called after a new package).

## 3. Design

### 3.1 Domain

- `Prescription` gains `Dispensations` (`int?`): the number of
  dispensations the prescription allows. `null` or `1` = a single
  prescription, which keeps today's behaviour and `CollectedOn` exactly
  as it is.
- New entity `PrescriptionDispensation`: `Id`, `PrescriptionId`,
  `CollectedOn` (`DateOnly`), optional `Packages` (`int?`), `RecordedAt`,
  `UpdatedAt`. Used only when `Dispensations > 1`.
- `PrescriptionRules`:
  - `MaxDispensations` (for example 12; check the value against the
    user's wishes, do not cite the law in code).
  - `DefaultRepeatableValidityMonths = 12`: pre-fills "valid until" when
    the user marks the prescription as repeatable; still editable.
  - `DispensationsLeft(prescription, dispensations)`.
  - Status of a repeatable prescription: `ToCollect` while dispensations
    are left and today is within validity; `Collected` when all were
    collected; `Expired` when validity ended with dispensations left.
    Single prescriptions keep the current `StatusOn`.
  - `Validate`: dispensations in range; no dispensation before
    `IssuedOn` or after `ValidUntil`; not more dispensations recorded
    than allowed; `CollectedOn` not set on a repeatable prescription.
- No minimum interval between dispensations is enforced: the rules are
  not verified and differ by country. The UI may show the date of the
  last dispensation, nothing more.

### 3.2 Sync

- Two devices can record a dispensation at the same time, so
  dispensations must not live inside the `PrescriptionChanged` register
  (last writer wins would drop one). Add a register per dispensation,
  `DispensationChanged`, with the rules of `PackageChanged`: last writer
  wins per dispensation id, `Deleted` removes it.
- `PrescriptionChanged` carries the new `Dispensations` field. Follow
  the lowest-version rule of `OperationCodec`: a prescription without
  `Dispensations > 1` keeps version 7, so older apps keep reading single
  prescriptions; only repeatable ones and `DispensationChanged` use the
  new version (12).
- Raise the image schema for the new table and field; update
  `SqliteSyncSnapshotStore`.
- Document both in `docs/SYNC-FORMAT.md`. Every device of a sync group
  must run the new version before anyone records a repeatable
  prescription; say so in the PR and in the release notes text the PR
  proposes.

### 3.3 Persistence and export

- Idempotent boot patch in `DatabaseInitializer`: add the
  `Dispensations` column to `Prescriptions` (nullable) and create the
  `PrescriptionDispensations` table with its index and foreign key, in
  the style of the existing prescription patch. Deleting a medicine or a
  prescription removes its dispensations
  (`MedicineDeletionRepository`).
- Export: additive fields `prescriptions[].dispensations` (int?) and
  `prescriptions[].dispensationRecords[]` (`id`, `collectedOn`,
  `packages`, `recordedAt`, `updatedAt`); archives without them import
  as single prescriptions. Update `docs/EXPORT-FORMAT.md` §3.14.

### 3.4 Application and notifications

- Use cases: save a prescription with `Dispensations`; record, change
  and delete a dispensation. Each write emits its register operation in
  the same unit of work, like the existing prescription writes.
- Low-stock warning: when the medicine has a repeatable prescription in
  `ToCollect` state, the toast and the email say that a dispensation is
  left and how many, with the last valid day, instead of suggesting a
  new prescription. The "prepare the prescription request" toast action
  is replaced by "open the prescription". Texts in
  `NotificationTexts`, generic titles unchanged where the user asked
  for them (caregiver copies, calendar).
- Reminder before "valid until": for a repeatable prescription it fires
  only when dispensations are left, and says how many will be lost.
- `MainForm.OfferPrescriptionCollectedAsync`: after a new package, for a
  repeatable prescription offer "record a dispensation" (pre-filled with
  today and the package count) instead of closing the prescription.
- Calendar export: keep the "valid until" event; no event per
  dispensation.

### 3.5 UI

- `PrescriptionEditDialog`: a "Repeatable" check box; when ticked, a
  "Dispensations" numeric field appears, "valid until" is pre-filled
  with the 12-month default (only if the user has not changed it), and
  the `Collected` field is replaced by a list of dispensations with Add,
  Edit and Remove.
- `PrescriptionsDialog`: a column "Dispensations" showing collected /
  allowed (for example `3 / 12`) for repeatable prescriptions; sorting
  by status unchanged.
- Main list: no new column; the existing prescription state is enough.
- Wording: "repeatable prescription" and "dispensation"; no reference
  to the law or to medical judgement in the UI.

## 4. Constraints

- Not a medical device: nothing about the dose or the appropriateness of
  the therapy; the dispensation count is an organisational record.
- The prescription code is never logged, nor are dispensation dates
  linked to a medicine name in logs.
- Respect the database gate; no write outside
  `%LOCALAPPDATA%\MedReminder\`.
- New UI strings in all five `assets/localization/strings.<lang>.json`;
  update the five `docs/USER_GUIDE.<lang>.md` (the Italian guide may
  mention the 12-month repeatable prescription as an example, without
  legal detail).
- `docs/ANALYSIS.md`: prescription model and new table.

## 5. Workflow

- Ask before creating the branch; proposed name
  `feature/repeatable-prescription`. Open the PR after the first commit,
  prepend a `CHANGE_LOG.md` entry, ask the user to run
  `dotnet build` and `dotnet test` before committing source code.
- Suggested commit order: Domain and rules with tests; persistence and
  boot patch; sync register and codec; use cases and notifications; UI;
  export; documentation.

## 6. Verification

- Domain tests: status truth table for single and repeatable
  prescriptions (dispensations left, all collected, expired with
  dispensations left, `Dispensations` 1 treated as single); `Validate`
  cases; `DispensationsLeft`.
- Sync tests: two devices record a dispensation concurrently and both
  survive; deleting one on a device removes it on the other; a single
  prescription is still written with version 7; an app at schema 11
  stops on a repeatable prescription (R7) and reads a single one.
- Persistence: the boot patch runs twice without error on a database
  created by the previous release.
- Export round trip with and without the new fields.
- Notification tests: low-stock text with and without a repeatable
  prescription in `ToCollect`; reminder before "valid until" only with
  dispensations left.
- Manual check in the running app: create a 12-month prescription with
  12 dispensations, record two, check the list, the low-stock toast,
  the offer after a new package, and sync between two PCs.
