# ANALYSIS — Intraday consumption and as-needed doses

Design document, **prior** to implementation. Work proceeds on branch
`feature/intraday-consumption`.

Epistemic classification, aligned with the sibling documents:
`[VERIFIED]` (checked against the current tree, commit `6d1aa20`),
`[INFERRED]` (deduction from verified facts), `[UNCERTAIN]` (hypothesis
pending confirmation).

---

## 1. Problems

**P1 — The stock shown lags the doses already taken.** The main window
shows the stock **at the start of the current day**. A user who took
this morning's tablet sees one dose more than the box holds and has to
remember that today's doses are booked only after midnight.

**P2 — As-needed doses are consumed every day.** A medicine taken only
when needed (e.g. paracetamol for a headache) whose slot is described
as "As needed" loses one dose per day, although the user takes it only
on some days and records those intakes by hand.

Goals:

- G1: for every scheduled medicine the stock shown drops when each dose
  time passes. Slots with an explicit time use it; slots without a time
  use the time of their time-of-day preset ("in the morning" = 08:00,
  "before lunch" = 13:00, ...), which the user can edit and extend.
  Doses whose time cannot be resolved keep today's behavior: booked at
  end of day.
- G2: an as-needed dose is never consumed automatically. Stock decreases
  only when the user records it.

## 2. Current behavior

### 2.1 Stock and ledger

- **Automatic consumption is booked per whole day, after the day
  ends.** `LedgerDeriver.Derive` builds rule 2 with
  `autoThrough: today.AddDays(-1)`
  (`src/MedReminder.Domain/Ledger/LedgerDeriver.cs:41`). One row per day,
  id `auto:{yyyy-MM-dd}`, `OccurredAt` = local midday. `[VERIFIED]`
- **The displayed stock is the stored ledger sum.**
  `MedicineOverviewLoader` uses `MedicineStock.Current(movements)`
  (`src/MedReminder.Application/Overview/MedicineOverviewLoader.cs:54`).
  During day D the value excludes every dose of D. `[VERIFIED]`
- **The ledger is re-derived from facts over its whole post-freeze
  history.** Every day after `LedgerCutoff.CutoffDay` is recomputed by
  `LedgerSynchronizer` on each catch-up. A change to how a past day's
  quantity is computed changes stored stock retroactively. `[VERIFIED]`
- **Stock counts store a fixed correction.** `EvaluateCount` stores
  `correction = counted − (startOfDay − takenToday)`; the derivation
  replays the stored value (`CountReevaluation` recomputes it only for
  counts recorded with sync enabled). A retroactive change to automatic
  consumption **before** a count therefore shifts the stock after that
  count by the same amount, and the user's counted value is lost.
  `[VERIFIED]` Consequence: every rule change in this document must be
  **non-retroactive**.
- **The count dialog already computes "doses due so far today".**
  `ReconcileStock.DefaultTakenToday` sums the doses of timed slots whose
  time has passed, capped at today's scheduled quantity
  (`src/MedReminder.Application/UseCases/ReconcileStock.cs:249`). It is
  the seed of the projection in §4 and must share its code. `[VERIFIED]`
- **A count today without materializing the day** leaves the ledger at
  `counted + takenToday`; midnight subtracts the full day.
  `[VERIFIED]` (ReconcileStock header).

### 2.2 Intakes

- A non-legacy `Taken` intake books its quantity immediately (rule 1).
- **Any intake on a day suppresses the whole day's automatic
  consumption** (rule 2 skips days in `IntakeDays`). `[VERIFIED]`
  The intake dialog proposes `DosePerAdministration`
  (`MainForm.cs:2266`). For a medicine with three scheduled doses, one
  recorded intake of one tablet books one tablet for the day instead of
  three. This trap already exists; G2 makes it frequent, because the
  natural way to record an extra as-needed tablet is the intake dialog.
- `AdjustStockDown` writes a `NegativeCorrection` user entry and does
  not interfere with rule 2. `[VERIFIED]`

### 2.3 Slots, schedules and as-needed

- `MedicationAdministrationSlot.Time` (`TimeOnly?`) feeds sorting, texts,
  the count dialog seed and the A5 dose reminders; the ledger ignores
  it. `[VERIFIED]`
- **The time-of-day description is free text.** The slot dialog fills
  its combo with localized preset strings and stores the chosen *text*
  in `TimingLabel` (`AdministrationSlotDialog.cs:87-91`). `[VERIFIED]`
- **Slots take precedence over the schedule.** `DailyConsumption.RateOn`
  returns the sum of slot doses whenever slots exist, before looking at
  the schedule. Consequences: `[VERIFIED]`
  - a slot labeled "As needed" is consumed every day (P2);
  - a medicine switched to `PrnSchedule` through the change-schedule
    dialog keeps its slots (`ChangeMedicationSchedule` does not touch
    slots) and keeps being consumed every day. On creation the edit
    dialog clears slots in advanced mode (`MedicineEditDialog.cs:504`),
    so only a later schedule change hits this path.
- The correct as-needed model already exists: `PrnSchedule`
  ("Al bisogno (PRN)"), rate 0, no automatic consumption, no forecast.
  It applies to the whole medicine only; there is no per-slot
  equivalent. `[VERIFIED]`
- Non-`FixedDaily` schedules (`Weekly`, `Cyclic`, `Tapering`,
  `SteppedTapering`) express a quantity per day, not a number of
  administrations. `[VERIFIED]`
- `DoseReminderService` reminds every timed slot, as-needed ones
  included (`DoseReminderService.cs:110`). `[VERIFIED]`

### 2.4 UI refresh

The main grid reloads on load, after user actions and on F5
(`MainForm.ReloadAsync`); there is no periodic refresh. `[VERIFIED]`
Today the grid can already show yesterday's value after midnight until
something reloads it.

## 3. Options for P1

### Option A — intraday rows in the ledger (rejected)

Rule 2 books today's due slots as separate derived rows. This changes
the deterministic derivation ("same facts, same day" becomes "same
instant"), makes forecast, coverage and counts double count today's
doses, churns derived rows daily, and needs a revision of the B.1 sync
documents and the parity harness.

### Option B — read-side projection (chosen)

The ledger is unchanged. A pure domain function computes the quantity
of today's doses already due at `now`; the grid shows
`EstimatedStockNow = max(0, ledgerStock − dueSoFar)`. At midnight rule 2
books the whole day and `dueSoFar` restarts from zero: the value is
continuous across the day boundary.

Cost: the movement history still shows one automatic row per day,
booked after midnight; the code carries two named notions of stock
(`LedgerStock`, `EstimatedStockNow`).

## 4. Projection (P1)

### 4.1 Effective dose time, per slot

1. `Slot.Time` when set.
2. Otherwise the time of the slot's preset (`PresetId`, §6).
3. Otherwise none: the dose stays in the end-of-day booking and never
   contributes to `dueSoFar`.

As-needed slots (§5) never contribute.

### 4.2 Medicines without slots

- `FixedDaily` (dose × N): N equal doses at default times, editable in
  the same settings page as the presets:

  | Administrations per day | Times |
  |---|---|
  | 1 | 08:00 |
  | 2 | 08:00, 20:00 |
  | 3 | 08:00, 13:00, 20:00 |
  | 4 | 08:00, 12:00, 16:00, 20:00 |
  | > 4 | end-of-day booking (no projection) |

- `Weekly`, `Cyclic`, `Tapering`, `SteppedTapering`: the whole day's
  rate at the single-dose default time (08:00).
- `PrnSchedule`: never projected.

### 4.3 Rules

`IntradayConsumption.DueSoFar(now, zone, ...)` returns 0 when, for
today (local day of `now`), any of these holds:

- the medicine is inactive, suspended, before `StartDate` or after
  `EndDate`;
- a non-extra intake of any status exists for today (rule 2 will skip
  the day). An extra intake (§5.3) does not count;
- a stock count today materialized the day;
- legacy consumption exists for today.

Otherwise it sums the doses (§4.1, §4.2) whose effective time is
`<= now`, capped at today's planned quantity
(`ConsumptionMaterializer.Plan` for today), so it never exceeds what
midnight books.

`ReconcileStock.DefaultTakenToday` is replaced by the same function, so
the count dialog suggests the same quantity the grid subtracts. With a
count today that did not materialize the day, ledger =
`counted + takenToday` and `EstimatedStockNow = counted + takenToday −
dueSoFar`, which equals the counted value when the user accepted the
suggestion. `[VERIFIED by reading ReconcileStock; to be covered by a
test]`

### 4.4 DST and time zone

- Spring-forward: a time that does not exist that day is due from the
  first valid local instant after it.
- Fall-back: comparison on local wall-clock time; due from the first
  occurrence.
- Zone change: `now` and slot times are local; no special handling.

## 5. As-needed (P2)

### 5.1 Model

`MedicationAdministrationSlot` gains `IsAsNeeded` (bool). It is stored
**on the slot**, not read from the preset: slot sets are immutable
history, and a later edit of a preset must not change past days.
Picking the "As needed" preset (or a custom preset marked as-needed)
sets the flag; the user can also tick it directly in the slot dialog.

`DailyConsumption.RateOn`, slot branch:

- slots exist and at least one is not as-needed → sum of the
  non-as-needed doses;
- slots exist and all are as-needed → **0** (the medicine behaves as
  PRN: no automatic consumption, no forecast);
- no slots → schedule, as today.

Every consumer of the rate follows (ledger rule 2, forecast, coverage,
timeline, calendar export, monitor). Additional changes:

- `DoseReminderService`: skip as-needed slots; the "remind on dose"
  option counts only timed, non-as-needed slots.
- Medicine edit dialog: the slot summary's daily total excludes
  as-needed doses.
- `CoveragePlanner.IsPrn`: also true when all current slots are
  as-needed, so the report shows "as needed: not calculated".
- Therapy card and reports: as-needed slots shown as such, without
  contributing to the daily total.

### 5.2 Non-retroactivity

Old slot rows have `IsAsNeeded = false`, so the new rule changes no past
day by itself. Existing data is corrected **from today**:

- **Backfill** (startup, Application layer, idempotent): for each
  medicine whose current slot set contains a slot whose `TimingLabel`
  matches, trimmed and case-insensitive, the "As needed" preset string
  in any of the five dictionaries, record a **new slot set with
  `EffectiveFrom = today`** carrying the same slots with
  `IsAsNeeded = true`, through the regular slot-change path so it
  produces its sync operation. Past days keep their consumption; counts
  keep their meaning.
- Every device of a household runs the backfill (the master role gates
  e-mail only, any device records facts). The new set and its slots use
  deterministic ids derived from the medicine id and the replaced set
  id, so the copies converge: `ApplyRemoteOperations` skips a
  `SlotSetRecorded` whose set id is already known. `[VERIFIED]`
- **PRN with slots**: `ChangeMedicationSchedule` to `PrnSchedule`
  records an empty slot set effective the same day. The backfill does
  the same for medicines whose schedule in force is PRN and that still
  have slots.

The stock already lost to past daily consumption of as-needed doses is
**not** given back automatically. Recovering it retroactively would
shift every later count (§2.1). The user restores it with one stock
count; the release notes and user guides say so.

### 5.3 Recording an as-needed dose

For a PRN-only medicine (schedule PRN or all slots as-needed) the intake
dialog works as today: there is no plan to suppress.

For a **mixed** medicine (scheduled doses plus an as-needed slot) an
extra tablet recorded as an intake would suppress the day's scheduled
consumption (§2.2). Recommended fix:

- `MedicationIntake` and `LedgerIntake` gain `IsExtra` (bool, default
  false), allowed only with status `Taken`. Ledger rule 1 books it as
  today. Every rule that reads "the day has an intake" uses only intakes
  with `IsExtra = false`: rule 2 (automatic consumption), rule 1b (the
  reversal of frozen legacy consumption), rule 3 (an intake removing a
  count-day materialization) and `CountDayScheduled`. Old intakes are
  `false`: non-retroactive.
- The intake dialog shows "Extra dose (as needed)" for medicines with a
  plan; it is preselected when the medicine has an as-needed slot.
- Sync: `IntakeRecorded` carries the flag. JSON deserialization ignores
  unknown properties, so an older device would silently read an extra
  intake as scheduled. The codec already has the remedy (R7,
  `OperationCodec`, precedent: version 6 for
  `EmailNotificationSent.Stage`): an intake with `IsExtra = true` is
  written with a new schema version 9, any other intake keeps version 1.
  An older device stops applying at that operation instead of
  misreading it. `[VERIFIED]`

Alternative without ledger change: guide the user to "Remove stock"
(`AdjustStockDown`) for extra doses. Rejected as primary path: the
intake dialog is where users record a dose, and the trap is silent.

### 5.4 Low-stock alerts for as-needed medicines

With rate 0 there is no forecast, so no low-stock warning: only the
Empty status at zero. This is today's PRN behavior and stays out of
scope; a minimum-quantity threshold is a candidate follow-up.

## 6. Time-of-day presets (user editable)

New per-profile table `DoseTimePresets`:

| Column | Type | Notes |
|---|---|---|
| `Id` | GUID | Built-ins use fixed, deterministic ids |
| `BuiltInKey` | TEXT NULL | e.g. `Morning`; null for user presets |
| `Label` | TEXT NULL | User text for custom presets; null for built-ins (localized at display time) |
| `Time` | TEXT NULL (`TimeOnly`) | Null = no intraday time |
| `IsAsNeeded` | INTEGER | Default for the slot flag only (§5.1) |
| `Order` | INTEGER | Display order |
| `IsHidden` | INTEGER | Built-ins cannot be deleted, only hidden |

Built-in defaults, editable:

| Built-in key | Default time |
|---|---|
| `MorningEmptyStomach` | 07:30 |
| `BeforeBreakfast` | 07:30 |
| `Morning` | 08:00 |
| `AfterBreakfast` | 08:30 |
| `MidMorning` | 10:30 |
| `BeforeLunch` | 13:00 |
| `AfterLunch` | 14:00 |
| `Afternoon` | 16:00 |
| `BeforeDinner` | 19:30 |
| `AfterDinner` | 20:30 |
| `BeforeSleep` | 22:30 |
| `Night` | 23:30 |
| `AsNeeded` | — (`IsAsNeeded = 1`) |

`MedicationAdministrationSlot` gains `PresetId GUID NULL`. `TimingLabel`
stays as display text and for free-form descriptions. Editing a preset
time changes only the projection (display), never stored stock, so it
may apply to existing slots immediately.

Backfill of `PresetId` for timed purposes: existing slots whose label
matches a built-in preset string in any language get the preset id in
place (display-only effect). Unmatched labels stay without preset.

## 7. Consumers

| Consumer | Stock / rate used | Change |
|---|---|---|
| `MedicineOverviewLoader` (grid, Empty status) | Estimated now | Yes |
| Grid tooltip | Both | New: start-of-day value and doses due today |
| Stock adjustment dialog | Shows estimated; validates on ledger | Display only |
| `ReconcileStock` / count dialog | Ledger + `DueSoFar` as default taken | Share function |
| `RunOutForecast` / `MedicineForecast` | Ledger (start of day) for the run-out date; the list's days left use the estimated stock (`floor(estimated / rate)` once a dose is due) | List only |
| `CoveragePlanner` | Ledger | `IsPrn` (§5.1) |
| `MedicationMonitor` low-stock alerts | Ledger | None |
| `DoseReminderService` | — | Skip as-needed slots |
| `TherapyTimeline`, calendar export, reports | Ledger | As-needed display |

Refresh: the grid reloads on window activation and restore from tray,
and on a one-minute tick while visible, recomputing only the projection
when no data changed. This also fixes the stale value after midnight.

## 8. Persistence, sync, export

- **Schema** (idempotent boot patches in `DatabaseInitializer`,
  CLAUDE.md §7): `DoseTimePresets`;
  `MedicationAdministrationSlots.PresetId`, `.IsAsNeeded`;
  `MedicationIntakes.IsExtra`. Built-ins seeded by deterministic id.
- **Backfills**: §5.2 (as-needed, new slot sets, sync operations) and §6
  (`PresetId`, in place). Both idempotent; the as-needed one runs in the
  Application layer because it records facts and operations.
- **Sync**: `SlotValue` gains `PresetId` and `IsAsNeeded`; intake
  operation gains `IsExtra`. A `SlotSetRecorded` with an as-needed slot
  and an `IntakeRecorded` with `IsExtra` are written with schema
  version 9 (§5.3); `PresetId` alone is display-only and needs no
  version bump. The database image a joining device starts from goes
  to schema version 7 for the same reason (an older app would read the
  image without the two flags). Built-in preset ids resolve on every
  device. Custom presets need their own sync operation; until then a
  slot referencing an unknown preset has no projection time on that
  device (display-only divergence).
- **Export / import**: `ExportPayload` carries presets and the new
  fields; `ExportPayloadUpgrader` maps older payloads to defaults
  (`false` / null).
- **Parity harness**: new cases for as-needed slots and extra intakes.

## 9. UI

- Slot dialog: description combo lists presets (built-ins localized,
  custom as typed, time alongside); "As needed" checkbox; choosing a
  preset sets `PresetId`, the label and the as-needed default; typing
  free text clears `PresetId`.
- Settings: "Dose times" page — edit preset times, add, rename,
  reorder, hide, mark as-needed; default times for medicines without
  slots (§4.2).
- Intake dialog: "Extra dose (as needed)" option (§5.3).
- Main grid: estimated stock with tooltip; slot summary excludes
  as-needed doses from the daily total.
- New string keys in all five `strings.<lang>.json`; user guides updated
  in all languages, including the one-time stock count advice (§5.2).

## 10. Medical-device boundary

The projection is an inventory estimate from the planned schedule. It
records nothing about whether a dose was taken; an extra intake is a
stock entry the user makes, as intakes are today. The line in
`ANALYSIS-A5-DOSE-TIME-REMINDER.md` §1.3 holds: reminders do not book
stock. UI wording says "estimated" and never implies the app knows a
dose was taken. `[INFERRED — MDR classification depends on the
declared intended use]`

## 11. Out of scope

- Option A (intraday ledger rows).
- Retroactive restitution of past as-needed consumption (§5.2).
- Minimum-quantity alerts for as-needed medicines (§5.4).
- Per-slot intake tracking (which scheduled dose an intake refers to).
- A5 dose reminders at preset times: reminders keep firing only for
  slots with an explicit time.

## 12. Implementation phases

Status: phases 1, 2 and 3 implemented. Phase 3 resolves the open
point of §5 on counts: a count today that does not materialize the day
leaves the ledger at counted + taken, so the estimate equals the count
when the user accepts the suggested quantity, which is the same
`DueSoFar`. After midnight the list runs the catch-up before it
recomputes, so the estimate never starts from the day before
yesterday. Phase 2 deviates from §9 in one
point: the presets have their own window (Therapy → Dose times…)
instead of a page of the settings dialog. Built-in presets live in code
and the table stores only the user's changes and additions. The backfill runs from `ConsumptionCatchUp`
while the `PendingDataMigrations` marker set by the schema patch (or by
an archive import) is present; imported archives are marked too, since
they may predate the flag.

Each phase is shippable and tested on its own.

1. **As-needed (P2)** — changes stored stock from today, so it ships
   first: slot and intake fields, `RateOn`, ledger rule 2 with
   `IsExtra`, change-schedule to PRN clears slots, backfill, reminder
   and coverage changes, slot and intake dialogs, sync and export.
2. **Presets** — table, seeding, `PresetId` backfill, settings page,
   slot dialog combo.
3. **Projection (P1)** — `IntradayConsumption`, overview fields and
   tooltip, count dialog reuse, grid refresh.

## 13. Tests

- `DailyConsumption`: mixed slots; all as-needed → 0; no slots
  unchanged; old slots (flag false) unchanged.
- Ledger: as-needed slot set effective today leaves past days identical
  (same rows, same ids, same quantities); count before the change keeps
  its counted value; extra intake does not suppress rule 2, does not
  trigger the rule 1b reversal and does not remove a count-day
  materialization; non-extra intake still does all three; old intakes
  unchanged.
- Sync: version 9 written only for as-needed slot sets and extra
  intakes; backfill run on two devices converges to one set.
- Backfill: labels in each of the five languages, trimmed and
  case-varied; unmatched label untouched; PRN with slots; second run
  is a no-op; operation produced once.
- `IntradayConsumption`: before / at / after each slot time; time vs
  preset vs unresolved; as-needed excluded; no-slot defaults 1–4 and
  > 4; non-FixedDaily at 08:00; suspended, inactive, outside window;
  non-extra (any status) vs extra intake today; materialized count
  today; cap;
  DST both directions.
- Continuity: estimated stock at 23:59 on D equals estimated stock at
  00:00 on D+1 after the ledger books D.
- Count dialog default equals grid projection.
- Export round trip with and without the new fields; old payload
  upgrade.

## 14. Decisions

Confirmed by the user:

1. Structured preset reference on the slot rather than parsing the
   label text.
2. Preset times editable; the user can add presets.
3. A dose without a resolvable time keeps the end-of-day booking.
4. Option B (read-side projection).
5. Default times for 1–4 administrations without slots (§4.2).
6. `Night` default at 23:30.
7. As-needed doses are never consumed automatically; the user records
   them.

8. Non-retroactive correction of as-needed data, with a one-time stock
   count to recover past over-consumption (§5.2).
9. `IsExtra` on intakes for mixed medicines (§5.3).
