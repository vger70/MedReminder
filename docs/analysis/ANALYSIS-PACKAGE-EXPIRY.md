# ANALYSIS — Package expiry tracking

Design document, **prior** to implementation. Corresponds to
`docs/notes/EVOLUTION-PROPOSALS.md` §3.1 (backlog item #1, "Package
expiry tracking") and to the re-assessment in
`docs/notes/EVOLUTION-PROPOSALS-2.md` §4. Supersedes the "store the
earliest expiry per medicine" first step proposed there (§2.1 explains
why).

Epistemic classification, as in the sibling documents: `[VERIFIED]`
(checked against the current tree, v2.14.2), `[INFERRED]` (deduction
from verified facts), `[UNCERTAIN]` (hypothesis pending confirmation).

---

## 1. Scope

### 1.1 Problem

A package of a medicine has a printed expiry date, almost always as
month and year (`MM/YYYY`): the medicine may be used up to the **last
day of that month**. Some medicines also have a shorter validity once
the package is opened (eye drops, oral solutions, syrups, insulin pens
in use): "use within N days of first opening". The effective expiry of
an opened package is then the earlier of the two dates.

MedReminder tracks how much of a medicine is left, never which
physical package it is in. Households keep expired packages in the
cabinet, and opened bottles are used well past their in-use period
because nobody remembers when they were opened.

### 1.2 Goal

- Record, optionally, the printed expiry of each new package, and the
  in-use period after opening when the medicine has one.
- Record when a package is opened, finished or thrown away.
- Warn the user (toast and/or email, on the medicine's channels) when
  a package still in stock is about to expire and when it has
  expired.
- Show the expiry state in the main window and in a per-medicine
  package list.

### 1.3 What this is NOT

- **No medical judgement.** The app reminds the user of a date they
  entered or scanned. It never says whether an expired medicine is
  still safe, never suggests a replacement. Texts say "check the
  package" and nothing more.
- **No lot-based stock.** The stock quantity stays a fungible sum of
  movements (§2.1). Packages are a parallel inventory of physical
  boxes; they do not drive the quantity.
- **No recall matching.** The batch (AI `10`) is stored when scanned,
  so a later feature can match recalls; no AIFA recall feed in
  machine-readable form has been verified `[UNCERTAIN]`.
- **No regulatory defaults.** In-use periods differ by product and are
  printed in the leaflet. The user enters them; the app pre-fills
  only what the user entered before for the same medicine.

---

## 2. Preconditions — what already exists

### 2.1 Stock model `[VERIFIED]`

- `StockMovement` (`src/MedReminder.Domain/Stock/StockMovement.cs`) is
  an **immutable fact**: kind, signed quantity, epoch, notes. Current
  stock is the sum of the deltas (`MedicineStock.Current`).
- `StockMovementKind.NewPackage` and `InitialLoad` are the two kinds
  that bring physical packages into the cabinet; `ManualAdd` and
  `PositiveCorrection` are adjustments.
- Movements are replicated as `StockEntryRecorded` facts
  (`SyncOperationBodies.cs`) and re-derived by `LedgerDeriver`
  (B.1). Facts can be retracted (`FactRetraction`).

Consequences:

- Expiry **cannot live on `StockMovement`**: opening, finishing and
  discarding are later changes of state, and the movement is
  immutable. Option B of `ANALYSIS-A2-BARCODE-SCAN.md` §12.2 is
  therefore rejected.
- Expiry **should not live on `Medicine`** ("earliest expiry per
  medicine", `EVOLUTION-PROPOSALS-2.md` §4). It cannot express the
  in-use period of one opened package next to sealed ones, it goes
  stale as soon as the earliest package is finished, and it gives the
  user no way to say which package was thrown away. It saves little:
  the persistence, sync and notification work is the same.

### 2.2 Reminder pattern `[VERIFIED]`

Two date reminders already exist and set the pattern to follow:

| | Prescriptions | Deadlines |
| --- | --- | --- |
| Entity | `Prescription` (mutable) | `Deadline` (mutable) |
| Rules | `PrescriptionRules` (pure) | `DeadlineRules` (pure) |
| Reminder | `PrescriptionReminders` | `DeadlineReminders` |
| Dedup | `PrescriptionReminderEvent`, device-local, key (id, date) | `DeadlineReminderEvent`, device-local, key (id, date) |
| Sync | `PrescriptionChanged` v7, whole record, LWW register | `DeadlineChanged` v8, idem |
| Export | `prescriptions[]` | `deadlines[]` |

Both reminders run inside `MedicationMonitor`'s WriteGate pass (every
30 minutes and at start), send email only where `sendsEmail` is true
(the master device of a shared installation), mark the event only when
at least one channel succeeded (a failure is retried on the next
pass), and never log names, codes or labels.

### 2.3 Barcode `[VERIFIED]`

`BarcodeParser` already extracts AI `17` (expiry, `YYMMDD`, `DD = 00`
read as the last day of the month) and AI `10` (batch) from the GS1
DataMatrix into `BarcodeContent.Expiry` / `Batch`. Nothing uses them:
the restock-by-scan flow passes only the quantity of the last package
to `StockAdjustmentDialog`. The EU FMD DataMatrix is the only allowed
code on Italian packs from 9 February 2027
(`ANALYSIS-A2-BARCODE-SCAN.md` §1), so scanned expiry will become the
common case for Italian users.

### 2.4 Channels `[VERIFIED]`

- Toast: `IWindowsNotificationService.ShowAsync(title, body,
  NotificationTarget)`; the target decides what a click opens.
- Email: `IEmailNotificationService.SendAsync(EmailMessage)`, with an
  `EmailKind` that decides whether the caregiver gets a copy
  (`CaregiverEmails`).
- Per-medicine choice: `Medicine.NotificationChannels`
  (`None` / `Email` / `Windows` / `Both`).

---

## 3. Domain model

### 3.1 New entity `StockPackage`

`src/MedReminder.Domain/Stock/StockPackage.cs` — one physical box.

| Field | Type | Notes |
| --- | --- | --- |
| `Id` | `Guid` | |
| `MedicineId` | `Guid` | parent medicine |
| `MovementId` | `Guid?` | the `NewPackage` / `InitialLoad` movement that brought it in; weak link, null for a package recorded afterwards |
| `Quantity` | `decimal` | units in the box when full, > 0 |
| `ExpiresOn` | `DateOnly?` | printed expiry, stored as a date (§3.2); null when not entered |
| `UseWithinDays` | `int?` | in-use period after opening, 1 to 365; null when the medicine has none |
| `OpenedOn` | `DateOnly?` | first opening (or, for insulin, first day out of the fridge) |
| `Batch` | `string?` | lot, at most 20 characters (GS1 AI `10` limit); never logged |
| `ClosedOn` | `DateOnly?` | day it was finished or discarded |
| `Closure` | `PackageClosure?` | `Finished` or `Discarded`; set together with `ClosedOn` |
| `RecordedAt`, `UpdatedAt` | `DateTimeOffset` | |

A load creates packages only when the user fills something in the
expiry section (expiry, in-use period, batch or "opened today");
otherwise it stays a plain stock movement, so users who never use the
feature keep today's behaviour. Stock left untracked this way is
consumed after the tracked packages (§3.4), which errs toward warning
(P2, 2026-10-03).

### 3.2 Effective expiry — `PackageExpiryRules` (pure)

```
EffectiveExpiry(p) =
    min( p.ExpiresOn,
         p.OpenedOn + (p.UseWithinDays - 1) days )   // each term optional
```

- **Printed `MM/YYYY`** → `ExpiresOn` = last day of that month
  (`DateTime.DaysInMonth`). The UI asks for month and year only; a
  scanned AI `17` with a real day keeps that day.
- **In-use period** counts the opening day as day 1: opened on 1 March,
  "use within 28 days" → last day of use 28 March. Same convention as
  `PrescriptionRules.DefaultValidUntil` (`DefaultValidityDays - 1`).
  The leaflet wording varies; this is the conservative reading
  `[INFERRED]`.
- No term known → no effective expiry → never warned.

Status on a given day:

| Status | Rule |
| --- | --- |
| `Closed` | `ClosedOn` set |
| `UsedUp` | derived by the allocation (§3.4): stock no longer covers it |
| `Expired` | `today > EffectiveExpiry` |
| `ExpiringSoon` | `today >= EffectiveExpiry - LeadDays` |
| `Valid` | otherwise, or no effective expiry |

Lead days are two profile settings (product owner, 2026-10-03: they
must be configurable), passed to the rules as parameters:

| Setting | Applies when the effective expiry comes from | Default | Range |
| --- | --- | --- | --- |
| `PackageExpiryLeadDays` | the printed date | 30 | 0–180 |
| `PackageInUseLeadDays` | the opening | 3 | 0–30 |

- 30 days before a printed expiry leaves time to use that box first or
  get a new one.
- The in-use lead is separate because a 28-day period with a 30-day
  lead would warn the day the bottle is opened.
- 0 turns the "expiring soon" notice off for that source; the
  "expired" notice stays.

Defaults and ranges are constants in `PackageExpiryRules`; a stored
value outside the range is clamped on read.

Validation (`PackageExpiryRules.Validate`, `null` when consistent, like
`DeadlineRules.Validate`): `Quantity > 0`; `UseWithinDays` 1–365;
`OpenedOn` not in the future; `ClosedOn >= OpenedOn`; `ClosedOn` and
`Closure` both set or both null; `Batch` length.

### 3.3 Default in-use period

The in-use period of a new package is pre-filled from the latest
package recorded for the same medicine, and stays editable. No field
on `Medicine` (revised during P1, 2026-10-03).

A medicine field was considered and dropped: an older app **cannot**
ignore an unknown medicine field. `MedicineFieldCodec.Set` throws
`ArgumentException` on an unknown name, and `ApplyRemoteOperations`
calls it for every `MedicineFieldChanged` and for the fields of every
`MedicineCreated` `[VERIFIED]`. A new field would need its own
operation type, as `MedicineStartChanged` did, for a default the last
package already gives.

The reference catalogue does not carry in-use periods (AIFA open data
has no such column) `[UNCERTAIN]`: no automatic fill.

### 3.4 Which boxes are still in the cabinet — allocation

Without this rule the feature is noise: a chronic patient finishes a
box every month, never marks it finished, and two years later gets an
"expired" warning for a box thrown away 23 months earlier.

`PackageAllocation.Allocate(packages, currentStock)` — pure, recomputed
on every read, **never stored** (no sync conflict, follows every stock
count and retraction automatically):

1. Take the open packages (`ClosedOn` null).
2. Order them by **consumption order**: opened packages first (by
   `OpenedOn`), then sealed ones by effective expiry ascending (no
   expiry last), then by `RecordedAt`. This is "first expiring, first
   out", which is also what the warning asks the user to do.
3. Walk the list in **reverse** consumption order and give each
   package `min(Quantity, remaining stock)`; the remaining stock
   decreases.
4. A package that receives 0 is `UsedUp`.

Stock not covered by any package (legacy stock, manual additions) is
assumed to be consumed **after** the tracked packages: the stock is
assigned to tracked packages first. That errs toward warning about a
box rather than hiding one `[INFERRED]`.

Known limit: a user who opens a newer box before an older one makes
the allocation keep the wrong box. Mitigations: an opened box is
always first in the order (step 2), and the package list lets the user
close the right one. Documented in the user guide.

### 3.5 Closing a package

- **Finished**: `ClosedOn` + `Closure = Finished`. No stock movement:
  consumption already removed the units.
- **Discarded** (thrown away, typically expired): `ClosedOn` +
  `Closure = Discarded`, and a `NegativeCorrection` of the quantity
  still in the box. The dialog pre-fills it with the allocated
  quantity (§3.4); the user confirms or edits it (0 allowed: the box
  was already empty). One use case, `DiscardPackage`, writes both in
  one unit of work under `WriteGate`, like `AddStock`.

### 3.6 Dedup — `PackageExpiryNoticeEvent`

Device-local, not replicated, like `DeadlineReminderEvent`:

| Field | Notes |
| --- | --- |
| `PackageId`, `MedicineId` | `MedicineId` lets medicine deletion clean up |
| `EffectiveExpiry` | a changed date (box opened, date corrected) gets new notices |
| `Stage` | `1 = ExpiringSoon`, `2 = Expired` |
| `Channel` | `Windows` or `Email`: each channel records its own notice, so a failed email is retried without showing the toast again |
| `FiredAt` | |

Unique on (`PackageId`, `EffectiveExpiry`, `Stage`, `Channel`). A
notice names the expired packages of a medicine when it has any,
otherwise those expiring soon; only the packages it names are recorded,
so the others get their own notice on a later pass.

---

## 4. Notifications

### 4.1 When

Two notices per package and effective expiry, each at most once per
device:

1. **Expiring soon** — first pass where the status is `ExpiringSoon`.
   Skipped when the package is already `Expired` at the first pass
   (app off for weeks, or a date entered in the past): the expired
   notice covers it, as the second low-stock stage covers the first
   (`NotificationCycle`).
2. **Expired** — first pass where `today > EffectiveExpiry`: the
   notice that the expiry has happened.

No daily repetition: an expired box keeps a visible red state in the
main window and in the package list until it is closed (§5.4). A
repeating toast for a box the user decided to keep would train them to
ignore all toasts `[INFERRED]`.

`UsedUp` and `Closed` packages are never notified.

### 4.2 Which channel — toast, email or both

The same rule as every other reminder: the medicine's
`NotificationChannels`. No new per-kind setting.

- **Toast** reaches the user at the PC, and a click opens the package
  list of that medicine (`NotificationTarget.PackageExpiry(medicineId)`,
  new `NotificationKind.PackageExpiry`). It is the main channel: the
  action (throw away, use first) happens at home.
- **Email** reaches a caregiver who manages the cabinet of an elderly
  person, and arrives even if the PC was off on the date (the app
  catches up at the next start). New `EmailKind.PackageExpiry`, added
  to `CaregiverEmails.Choices`; a stored `""` already means "every
  kind", so existing caregivers get it, and explicit lists keep their
  choice. Email only where `sendsEmail` is true (household master).
- **Weekly caregiver digest** (`CaregiverDigest`): one line per
  expired or expiring package, so a caregiver who receives only the
  digest still sees it.

### 4.3 Inactive medicines

Unlike prescription reminders, expiry notices are sent for **inactive**
medicines too: a suspended or finished therapy often leaves boxes in
the cabinet, which is exactly where expired medicines accumulate. A
medicine with `NotificationChannels.None` is never notified (the user
opted out). Confirmed (§10, Q1).

### 4.4 Batching

Several packages expiring the same day (a household buying in bulk)
would produce several toasts. One toast per medicine per pass, with
the count ("2 packages of X expire on …"); one email per pass listing
all of them. Dedup events are still written per package.

### 4.5 Texts

New keys in all five `assets/localization/strings.<lang>.json`, sober,
no advice:

- `Notifications.PackageExpiry.Soon.Title` — "{0}: a package expires
  soon"
- `Notifications.PackageExpiry.Soon.Body` — "A package of {0} expires
  on {1}. Use it first or check it."
- `Notifications.PackageExpiry.Expired.Title` — "{0}: a package has
  expired"
- `Notifications.PackageExpiry.Expired.Body` — "A package of {0}
  expired on {1}. Check it and mark it in MedReminder."

The email footer stays `Notifications.Email.Footer` ("organizational
reminder, not a medical device"). Logs carry the package and medicine
ids only, never name or batch.

### 4.6 Lead-day settings

`NotificationSettings` (per profile, `notifications.settings.json`)
gains `PackageExpiryLeadDays` and `PackageInUseLeadDays` (§3.2), next
to `CaregiverEmails`. They are replicated like the other profile
settings: a `ProfileSettingChanged` per changed value, last writer
wins, projected by `ProfileSettingsProjection`. An app that does not
know a setting keeps its versions and does not project it
(`docs/SYNC-FORMAT.md`) `[VERIFIED]`, so no schema version is needed.
An empty value (files written before the settings) reads as the
default.

Changing a lead day does not reset notices: the dedup key (§3.6) is
the package, its effective expiry and the stage, not the lead. A
longer lead makes packages due that were not yet notified; a shorter
one only delays future notices.

---

## 5. UI

### 5.1 Adding a package — `StockAdjustmentDialog`

When the kind is `NewPackage`, optional package fields (enabled only
for that kind, so the dialog keeps its size). The initial load of
`MedicineEditDialog` has none: packages already in the cabinet are
entered with **New…** in the package list (§5.3), which records a box
without a stock movement.

- **Number of packages** (default 1). The quantity stays the total;
  each box gets `total / n`. Creates `n` `StockPackage` rows sharing
  the expiry and batch. Boxes bought together usually share both
  `[INFERRED]`.
- **Expiry** — checkbox + `DateTimePicker` with custom format
  `MM/yyyy` (month/year only; stored as last day of month). Unchecked
  = not entered.
- **Use within … days after opening** — pre-filled from the latest
  package of the medicine (§3.3); empty = none.
- **Opened today** — checkbox, for a box opened right away.
- **Batch** — optional text.

Restock by scan pre-fills expiry and batch from `BarcodeContent`
(already parsed, §2.3). A 1D Code 32 scan has no expiry: fields stay
empty.

### 5.2 Medicine dialog

No change (§3.3).

### 5.3 Package list — new `PackagesDialog`

Per medicine, opened from the main window context menu and from the
toast. Same layout family as `PrescriptionsDialog`: one row per
package with expiry (`MM/yyyy`), opened on, effective expiry, status
(colour and text, never colour alone), batch. Actions: **Opened
today** (or another date), **Finished**, **Discard…** (§3.5),
**Edit**, **Delete** (a wrong entry; no stock movement). Toggle "show
closed / used up".

### 5.4 Main window

- New column **Next expiry**: the earliest effective expiry among the
  medicine's packages in stock, with status colour and text
  ("expired", "in 12 days"). Empty when nothing is tracked.
- Menu **Stock → Expiring packages**: one list across all medicines of
  the profile (the cabinet view), expired first. Later phase (§10,
  Q7).

### 5.5 Settings

`SettingsDialog`, Notifications tab: a "Package expiry" group with two
`NumericUpDown` controls, "Warn N days before the printed expiry"
(0–180) and "Warn N days before the end of the in-use period" (0–30),
with a note that 0 keeps only the "expired" notice. Saved through
`ProfileSettingsUseCases`, like the caregiver options.

### 5.6 Accessibility

Status in text as well as colour; date pickers with the static Segoe
UI font of #181; every control reachable by keyboard.

---

## 6. Persistence

Additive, idempotent boot patches in `DatabaseInitializer`
(`CLAUDE.md` §7; `EnsureCreated()` cannot upgrade):

- `CREATE TABLE IF NOT EXISTS "StockPackages" (...)`, index on
  `MedicineId` (P1). Quantity as text, like
  `StockMovements.QuantityDelta`; `Closure` as an integer.
- `CREATE TABLE IF NOT EXISTS "PackageExpiryNoticeEvents" (...)`,
  unique index on (`PackageId`, `EffectiveExpiry`, `Stage`) (P3).

EF configurations next to `DeadlineConfiguration`. Repositories
`IStockPackageRepository` (P1) and `IPackageExpiryNoticeEventRepository`
(P3) in `Application/Abstractions`, implemented in
`Infrastructure.Portable`.

`MedicineDeletionRepository` deletes both tables' rows of the
medicine. A retracted `NewPackage` movement leaves its package in
place, with `MovementId` pointing to nothing: the user deletes it from
the list. Deleting it automatically would turn a sync retraction into
a lost record on another device `[INFERRED]`.

---

## 7. Sync, export, household

### 7.1 Sync (B.1)

New operation **`PackageChanged`**, schema version 11 (current
`OperationCodec.CurrentSchemaVersion = 10` `[VERIFIED]`): the whole
state of one package plus `deleted`, last writer wins per package
(register `Package` of the package id), no conflict entry — the rules
of `PrescriptionChanged` / `DeadlineChanged`. `medicineId` is the
parent. The snapshot image gains `StockPackages`, with an image
version bump (an older app would drop them).
`PackageExpiryNoticeEvent` is device-local and not in the image. The
two lead-day settings are new `ProfileSettingChanged` names, no schema
version (§4.6). `docs/SYNC-FORMAT.md` updated.

Concurrent edits on two devices (one marks opened, the other
discarded) resolve by LWW on the whole record. Acceptable for a
two-to-three device household; a field-level merge is not justified
`[INFERRED]`.

### 7.2 Export / import

`stockPackages[]` additive in the archive (`docs/EXPORT-FORMAT.md`
§3.17; archives without it import with no packages), and the two
lead-day settings in the profile settings section (additive, missing =
default). Notice events are not exported, like the other reminder
events.

### 7.3 Household / master device

Nothing new: packages belong to the profile database; emails follow
`sendsEmail` like the other reminders; toasts appear on every device
that has the profile open.

---

## 8. Tests

- **Domain** — `PackageExpiryRules`: last day of month for every month
  and leap February; in-use period (day 1 = opening day); `min` of the
  two terms; status boundaries at lead day, expiry day, day after,
  with the default leads, custom leads and lead 0; out-of-range lead
  clamped; validation. `PackageAllocation`: order (opened first, expiry
  ascending, null last), partial last box, stock 0 → all used up,
  untracked stock, closed boxes ignored.
- **Application** — `PackageExpiryNotices`: one notice per stage,
  expired skips soon, failed channel retried, `sendsEmail` false drops
  email, `None` channel skipped, inactive medicine notified, used-up
  skipped, batching per medicine; `AddStock` with packages (n boxes,
  quantity split); `DiscardStockPackage` atomic movement + closure;
  `PackageChanged` encode/decode and LWW apply; lead-day settings saved
  as `ProfileSettingChanged`,
  projected from a remote operation, empty value read as default, a
  longer lead making a not-yet-notified package due; export
  round-trip.
- **Infrastructure.Portable** — boot patch on a v2.14 database and on a
  new one, run twice; deletion cascade.
- **Barcode** — scan pre-fill with a DataMatrix carrying AI `17` with
  `DD = 00` and with a real day.

---

## 9. Implementation plan

Each phase is one PR to `main`, buildable and shippable on its own.

| Phase | Content | Effort |
| --- | --- | --- |
| P1 | Domain (`StockPackage`, rules, allocation), `StockPackages` table, repository, use cases (save, discard, delete), sync op v11 and image v8, export, deletion; no UI | 4–5 days |
| P2 | Package list query (allocation, default in-use period and size from the latest package), `StockAdjustmentDialog` package fields with packages linked to their movement, `PackagesDialog` and `PackageEditDialog`, main-window column, scan pre-fill, localization; lead days at their defaults until P3 | 4–5 days |
| P3 | `PackageExpiryNoticeEvents` table, `PackageExpiryNotices` in `MedicationMonitor`, toast target (opens the package list), `EmailKind.PackageExpiry`, caregiver digest line, lead-day settings (profile setting, sync, export, settings tab) also used by the package list and the main list | 3–4 days |
| P4 | User guides (5 languages), `ANALYSIS.md`, cross-medicine "Expiring packages" view if kept | 1–2 days |

Total about 3 weeks, in line with the 2–3 weeks of
`EVOLUTION-PROPOSALS.md`.

---

## 10. Decisions

Settled by the product owner on 2026-10-03.

| # | Question | Decision |
| --- | --- | --- |
| Q1 | Notify packages of inactive medicines? | Yes (§4.3) |
| Q2 | Lead days fixed or a profile setting? | Profile settings, defaults 30 printed and 3 in-use (§3.2, §4.6) |
| Q3 | Repeat the expired notice? | No; persistent UI state instead (§4.1) |
| Q4 | Discard writes the negative correction? | Yes, pre-filled with the allocated quantity, user confirms (§3.5) |
| Q5 | Accept the FEFO allocation to hide used-up boxes? | Yes (§3.4) |
| Q6 | Several boxes in one addition → one row each? | Yes (§5.1) |
| Q7 | Cross-medicine "Expiring packages" view in P2 or later? | Later (P4), the column first |

---

## 11. Risks

| Risk | Mitigation |
| --- | --- |
| Allocation keeps the wrong box (out-of-order use) | Opened box first; manual close in the list; user guide note |
| Stock drift (missed intakes) hides or shows boxes wrongly | Stock count (`ReconcileStock`) already corrects the stock; the allocation follows it |
| Notification fatigue | One notice per stage, batching per medicine, no repetition |
| `MM/yyyy` picker misread as a full date | Picker shows month/year only; list shows `MM/yyyy`, and the effective expiry as a full date only when it comes from the opening |
| Older app drops packages | Operation schema version 11 and image version 8 stop the older app, as with v7/v8 |
| Wrong in-use convention (day 1 vs day 0) | Conservative reading; user can edit the date |
