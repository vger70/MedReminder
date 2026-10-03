# ANALYSIS — Intraday consumption: stock that follows the dose times

Design document, **prior** to implementation. Work proceeds on branch
`feature/intraday-consumption`.

Epistemic classification, aligned with the sibling documents:
`[VERIFIED]` (checked against the current tree, commit `6d1aa20`),
`[INFERRED]` (deduction from verified facts), `[UNCERTAIN]` (hypothesis
pending confirmation).

---

## 1. Problem

The stock shown in the main window is the stock **at the start of the
current day**. A user who took this morning's tablet and opens the app
sees one dose more than the box holds, and has to remember that today's
doses are booked only after midnight. The number is correct by its own
definition, but nothing in the UI states that definition, and it
contradicts what the user sees in the box.

Goal: for every scheduled (not as-needed) medicine, the stock shown
drops when each dose time passes. Slots with an explicit time use it.
Slots without a time use the time of their time-of-day preset
("in the morning" = 08:00, "before lunch" = 13:00, ...), which the user
can edit and extend. Slots whose time cannot be resolved keep today's
behavior: the dose is booked at end of day.

## 2. Current behavior

- **Automatic consumption is booked per whole day, after the day
  ends.** `LedgerDeriver.Derive` builds rule 2 with
  `autoThrough: today.AddDays(-1)`
  (`src/MedReminder.Domain/Ledger/LedgerDeriver.cs:41`). One row per day,
  id `auto:{yyyy-MM-dd}`, `OccurredAt` = local midday (`LocalMidday`).
  `[VERIFIED]`
- **The displayed stock is the stored ledger sum.**
  `MedicineOverviewLoader` uses `MedicineStock.Current(movements)`
  (`src/MedReminder.Application/Overview/MedicineOverviewLoader.cs:54`);
  derived rows are written by `ConsumptionCatchUp` /
  `LedgerSynchronizer`. During day D the value excludes every dose of
  D. `[VERIFIED]`
- **Intakes are the exception.** A non-legacy `Taken` intake books its
  quantity on its day immediately (rule 1), and any intake on a day
  suppresses rule 2 for that day. `[VERIFIED]`
- **Slot times are not used by the ledger.**
  `MedicationAdministrationSlot.Time` (`TimeOnly?`) feeds sorting, texts
  and the A5 dose reminders only; `ANALYSIS-A5-DOSE-TIME-REMINDER.md`
  §1.3 explicitly keeps the reminder away from stock. `[VERIFIED]`
- **The time-of-day description is free text.** The slot dialog fills
  its combo with localized preset strings and stores the chosen *text*
  in `TimingLabel` (`src/MedReminder.UI/Forms/AdministrationSlotDialog.cs:87-91`),
  not a preset key. A label typed in Italian cannot be mapped reliably
  once the UI language changes or the user edits the text. `[VERIFIED]`
- **"As needed" is only modeled at schedule level.** `PrnSchedule`
  returns rate 0; a slot labeled "As needed" is summed into the daily
  rate like any other slot (`DailyConsumption.RateOn`). `[VERIFIED]`

## 3. Options

### Option A — intraday rows in the ledger

Rule 2 books today's due slots as separate derived rows
(`auto:{day}:{slotKey}`, `OccurredAt` = day + slot time);
`Derive` takes `DateTimeOffset now` instead of `DateOnly today`.

Consequences:

- The ledger invariant "same facts, same day, same zone: same rows on
  every device" (B.1 §4.3) becomes "same instant". Devices of a
  household converge at end of day but can differ intraday by clock
  skew. `[INFERRED]`
- Every consumer that adds today's plan to the stock double counts:
  `RunOutForecast` (ETA one day early in the evening), `CoveragePlanner`
  (`Plan(today, from-1)`), `LedgerDeriver.CountBaseline` /
  `ReconcileStock` (count-day scheduled quantity). Each must move to
  "remaining doses of today".
- Today's rows collapse into one daily row at midnight: derived-row
  churn every day, and slot-set changes during the day re-key rows.
- Parity harness, B.1 sync documents and the A5 §1.3 boundary must be
  revised.

### Option B — read-side projection (recommended)

The ledger stays exactly as it is. A pure domain function computes the
quantity of today's doses already due at `now`; the overview shows
`max(0, ledgerStock − dueSoFar)`. At midnight rule 2 books the whole
day and `dueSoFar` restarts from zero, so the displayed value is
continuous across the day boundary.

Why it is preferred:

- It solves the reported problem (what the user *sees*) without
  touching the deterministic ledger, stock epochs, sync, counts,
  coverage or the parity harness.
- It is reversible and carries no data migration for stock.
- Forecast and coverage keep using the start-of-day stock, which is
  mathematically consistent with "today's plan still ahead", so they
  need no change.

Cost: the movement history still shows one automatic row per day,
booked after midnight; the code has two named notions of stock
(`LedgerStock`, `EstimatedStockNow`). Both are acceptable for a
display-oriented feature.

**Decision: Option B.** Option A is reconsidered only if a later
feature needs intraday rows in the history or in synced data.

## 4. Effective dose time

### 4.1 Resolution order, per slot

1. `Slot.Time` when set.
2. Otherwise the time of the slot's time-of-day preset (`PresetId`).
3. Otherwise: no intraday time — the dose stays in the end-of-day
   booking (it never contributes to `dueSoFar`).

A slot whose preset is marked as-needed never contributes to
`dueSoFar`.

### 4.2 Medicines without slots

Legacy dose × administrations-per-day and A1 schedules (`Weekly`,
`Cyclic`, `Tapering`) have no slots. Today's rate
(`DailyConsumption.RateOn`) is split in `AdministrationsPerDay` equal
doses at default times:

| Administrations per day | Times |
|---|---|
| 1 | 08:00 |
| 2 | 08:00, 20:00 |
| 3 | 08:00, 13:00, 20:00 |
| 4 | 08:00, 12:00, 16:00, 20:00 |
| > 4 | end-of-day booking (no projection) |

`PrnSchedule` (rate 0) is never projected. `[UNCERTAIN]` Whether
`AdministrationsPerDay` is meaningful for non-FixedDaily schedules must
be checked per kind at implementation time; when it is not, the whole
day's rate is placed at 08:00.

### 4.3 Time-of-day presets (user editable)

New per-profile table `DoseTimePresets`:

| Column | Type | Notes |
|---|---|---|
| `Id` | GUID | Built-ins use fixed, deterministic ids |
| `BuiltInKey` | TEXT NULL | e.g. `Morning`; null for user presets |
| `Label` | TEXT NULL | User text for custom presets; null for built-ins (localized at display time) |
| `Time` | TEXT NULL (`TimeOnly`) | Null = no intraday time |
| `IsAsNeeded` | INTEGER | Excluded from projection |
| `Order` | INTEGER | Display order |
| `IsHidden` | INTEGER | Built-ins cannot be deleted, only hidden |

Built-in defaults, editable by the user:

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

`Night` defaults to 23:30 rather than an early-morning time so that the
dose is projected on the day it belongs to. The user can change it.

`MedicationAdministrationSlot` gains `PresetId GUID NULL`. `TimingLabel`
stays as the display text (and for free-form descriptions); `PresetId`
is the structured reference used for the time.

## 5. Projection rules

`IntradayConsumption.DueSoFar(now, zone, ...)` returns 0 when any of the
following holds for today (local day of `now`):

- the medicine is inactive, suspended today, before `StartDate` or after
  `EndDate`;
- an intake exists for today (rule 1 already booked it; rule 2 will
  skip the day);
- a stock count today materialized the day (rule 3 already booked the
  scheduled quantity);
- legacy consumption exists for today.

Otherwise it sums the doses (§4) whose effective time is `<= now`.

The sum is capped at today's planned quantity used by rule 2
(`ConsumptionMaterializer.Plan` for today) so the projection never
exceeds what midnight will book.

`[UNCERTAIN]` A count recorded today *without* materializing the day
resets the ledger to the counted value while rule 2 will still book the
full day at midnight. The projection then subtracts the doses already
due, which matches what midnight will do. To be confirmed against
`ReconcileStock` semantics with a dedicated test.

## 6. Consumers

| Consumer | Stock used | Change |
|---|---|---|
| `MedicineOverviewLoader` (grid, Empty status) | Estimated now | Yes |
| Overview tooltip | Both | New: "includes N doses due today" |
| `RunOutForecast` / `MedicineForecast` | Ledger (start of day) | None |
| `CoveragePlanner` | Ledger | None |
| `MedicationMonitor` low-stock alerts | Ledger | None (`[INFERRED]` alert timing tied to forecast; changing it is out of scope) |
| `ReconcileStock` / count dialog | Ledger + count-day scheduled | None |
| `AdjustStockDown` | Ledger | None |
| `TherapyTimeline`, calendar export, reports | Ledger | None |
| `DoseReminderService` | Ledger | None |

The overview refreshes on its own timer: the displayed value is
recomputed at the next refresh after a dose time, not exactly at the
minute. `[UNCERTAIN]` Current refresh cadence of the grid to be
checked; a one-minute refresh tick limited to the projection is enough.

## 7. Edge cases

- **DST spring-forward**: a time that does not exist on that day (e.g.
  02:30) counts as due from the first valid local instant after it.
- **DST fall-back**: comparison is on local wall-clock time of `now`;
  a dose at 02:30 is due from the first occurrence.
- **Slot set changed today**: the projection uses the set in force
  today (`SlotsOn(today)` semantics); the value can jump when the user
  edits slots, which is expected.
- **Medicine created today after a dose time**: the projection subtracts
  that dose immediately. The stock entered at creation may already
  exclude it. This is the same double booking rule 2 performs today at
  midnight, only earlier; the creation dialog should say the stock is
  "before today's doses". `[INFERRED]`
- **Time zone change**: `now` and slot times are local; no special
  handling.

## 8. Persistence, sync, export

- **Schema**: `DoseTimePresets` table and `MedicationAdministrationSlots.PresetId`
  added through idempotent boot patches in `DatabaseInitializer`
  (CLAUDE.md §7); built-ins seeded idempotently by deterministic id.
- **Backfill**: existing slots whose `TimingLabel` matches, trimmed and
  case-insensitive, a built-in preset string in any of the five
  dictionaries (`assets/localization/strings.<lang>.json`) get the
  corresponding `PresetId`. Unmatched labels stay without preset (end of
  day). Runs once at startup, idempotent.
- **Sync (B.1 / household)**: `SlotValue` gains `PresetId`. Built-in ids
  resolve on every device. Custom presets need their own sync operation;
  until it exists, a slot that references an unknown preset falls back
  to end of day on that device. Because the projection is display-only,
  this divergence never affects stored stock. `[INFERRED]`
- **Export / import**: `ExportPayload` carries presets and `PresetId`;
  `ExportPayloadUpgrader` treats older payloads as "no preset".
- **Backups**: covered by the profile database.

## 9. UI

- Slot dialog: the description combo lists the presets (built-in names
  localized, custom names as typed, time shown alongside). Selecting a
  preset sets `PresetId` and the description text; typing free text
  clears `PresetId`.
- Settings: new "Dose times" page to edit preset times, add, rename,
  reorder and hide presets, and mark a preset as as-needed.
- Main grid: the stock column shows the estimated stock; a tooltip
  states the start-of-day value and the doses already due today.
- New string keys added to all five `strings.<lang>.json`; user guides
  updated in all languages.

## 10. Medical-device boundary

The projection is an inventory estimate driven by the planned schedule.
It records nothing about whether a dose was taken, adds no
acknowledgement and no missed-dose logic, so the line drawn in
`ANALYSIS-A5-DOSE-TIME-REMINDER.md` §1.3 holds: reminders still do not
book stock, and no stock movement depends on the user's behavior.
UI wording must say "estimated" and must not imply that the app knows
the dose was taken. `[INFERRED — MDR classification depends on the
declared intended use]`

## 11. Out of scope

- Option A (intraday ledger rows).
- Excluding as-needed **slots** from the daily rate in
  `DailyConsumption.RateOn`. Today an "As needed" slot is consumed
  daily; fixing it changes stored stock and forecasts and deserves its
  own change.
- Changing low-stock alert timing.

## 12. Implementation steps

1. **Domain**: `DoseTimePreset` entity; `PresetId` on the slot;
   `EffectiveDoseTime` resolution (§4); `IntradayConsumption.DueSoFar`
   (§5). Pure, unit-tested (DST, caps, exclusions, defaults table).
2. **Persistence**: boot patches, seeding, repository, backfill.
3. **Application**: overview loader exposes `LedgerStock`,
   `EstimatedStockNow`, `DueTodaySoFar`; preset use cases (list, add,
   edit, hide).
4. **Sync / export**: `SlotValue.PresetId`, payload and upgrader.
5. **UI**: slot dialog, settings page, grid column and tooltip, refresh
   tick, localization keys, user guides.

## 13. Tests

- `IntradayConsumption`: before / at / after each slot time; slot with
  time vs preset vs unresolved; as-needed preset; no-slot defaults for
  1–4 and > 4 administrations; suspended, inactive, outside window;
  intake today; materialized count today; cap at planned quantity; DST
  both directions.
- Continuity: estimated stock at 23:59 on D equals estimated stock at
  00:00 on D+1 after the ledger books D.
- Backfill: labels in each of the five languages, trimmed and
  case-varied; unmatched label untouched; second run is a no-op.
- Export round trip with and without presets; old payload upgrade.

## 14. Decisions

Confirmed by the user:

1. Structured preset reference on the slot rather than parsing the
   label text.
2. Preset times editable, and the user can add presets.
3. A slot without a resolvable time keeps the end-of-day booking.

Still to confirm:

4. Option B (read-side projection) instead of Option A (§3).
5. Default times for 1–4 administrations without slots (§4.2).
6. `Night` default at 23:30 (§4.3).
