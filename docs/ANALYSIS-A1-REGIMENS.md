# ANALYSIS — A1: Complex therapy regimens

Design document, **prior** to implementation. Once approved, work
proceeds on branch `feature/complex-regimens`. Corresponds to
`EVOLUTION.md` §3.1 (Group A, item A1). Follows the structure of
`ANALYSIS-MULTI-USER.md`.

> **This is not a speculative analysis.** Every decision is
> technically motivated and delimits what will be written in code.
> The "Decisions still to confirm" section at the end is the only
> zone of ambiguity that needs input.

Epistemic classification: `[VERIFIED]` (checked against the current
tree), `[INFERRED]` (deduction from verified facts), `[UNCERTAIN]`
(hypothesis pending confirmation).

---

## 1. Scope

### 1.1 Problem

The current consumption model computes a **constant daily rate**:
either `DosePerAdministration × AdministrationsPerDay` (legacy) or
the sum of `MedicationAdministrationSlot.Dose` (Increment 10 slot
model). `[VERIFIED]` — `DailyConsumption.RateOn` in
`src/MedReminder.Domain/Calculations/DailyConsumption.cs`.

Real regimens are frequently non-constant:

- **Weekly** — different quantities per day of week (e.g. warfarin
  on odd days).
- **Cyclic** — N days on / M days off (e.g. hormonal therapies,
  cortisone pulses).
- **Tapering** — dose decreases over time in steps (e.g.
  glucocorticoid down-titration).
- **PRN** (*pro re nata*) — no scheduled consumption, only stock
  tracking (rescue medicines).

With a constant rate, `RunOutForecast.Compute` reports the wrong
run-out ETA for exactly the users who benefit most from a
reminder.

### 1.2 Goal

Introduce a value object `Schedule` in `MedReminder.Domain` that
represents the four non-constant patterns plus the current constant
one, and route the projection engine through it. Preserve
retro-compatibility: a therapy loaded from a pre-A1 DB behaves
byte-for-byte identically, without user intervention.

### 1.3 What A1 is NOT

- **Not a medical-device feature.** No maximum-daily-dose enforcement,
  no overdose alerts, no drug-interaction checks. See `EVOLUTION.md`
  §8.2 and `CLAUDE.md` §1. The app remains a stock-and-reminder
  tool.
- **Not a rewrite of the intake / slot model.** Administration slots
  (Increment 10) stay unchanged. Their interaction with `Schedule`
  is defined in §3.3.
- **Not a scheduler for exact-time reminders.** A1 changes the rate
  the app uses to project stock over time; it does not add
  time-of-day notifications.
- **Not variable dosing within one day.** A single-day quantity is
  a scalar (per-slot doses excepted).

---

## 2. Data model

### 2.1 New value object: `Schedule`

Lives in `MedReminder.Domain/Medicines/Schedule.cs`. Discriminated by
`ScheduleKind`. Pure C#, no EF or Windows dependencies.

```csharp
public enum ScheduleKind : int
{
    FixedDaily = 0,   // dose × administrations/day. Default; pre-A1 rows land here.
    Weekly     = 1,   // seven quantities keyed by day-of-week.
    Cyclic     = 2,   // N on / M off, quantity per on-day.
    Tapering   = 3,   // start dose → end dose, step every K days.
    Prn        = 4,   // as-needed: no automatic consumption.
}

public abstract record Schedule(ScheduleKind Kind)
{
    // day  = the day to evaluate.
    // anchor = the schedule entry's EffectiveFrom. Cyclic and
    // tapering need it to know where the pattern starts; Weekly and
    // FixedDaily ignore it; PRN returns 0.
    public abstract decimal RateOn(DateOnly day, DateOnly anchor);
}

public sealed record FixedDailySchedule(
    decimal DosePerAdministration,
    int     AdministrationsPerDay) : Schedule(ScheduleKind.FixedDaily);

public sealed record WeeklySchedule(
    IReadOnlyList<decimal> QuantitiesByDayOfWeek /* 7 items, Mon..Sun */
    ) : Schedule(ScheduleKind.Weekly);

public sealed record CyclicSchedule(
    int     OnDays,
    int     OffDays,
    decimal QuantityPerOnDay) : Schedule(ScheduleKind.Cyclic);

public sealed record TaperingSchedule(
    decimal StartDose,
    decimal EndDose,
    decimal Step,
    int     IntervalDays) : Schedule(ScheduleKind.Tapering);

public sealed record PrnSchedule() : Schedule(ScheduleKind.Prn);
```

Rules on `RateOn`:

| Kind        | `RateOn(day, anchor)` |
|-------------|-----------------------|
| FixedDaily  | `DosePerAdministration × AdministrationsPerDay` |
| Weekly      | `QuantitiesByDayOfWeek[(int)day.DayOfWeek+6 mod 7]` (Monday = 0) |
| Cyclic      | `day < anchor` → 0; else, with `p = OnDays + OffDays`, `((day-anchor) mod p) < OnDays` → `QuantityPerOnDay`, else 0 |
| Tapering    | `day < anchor` or `IntervalDays ≤ 0` → 0; else `intervals = (day-anchor) / IntervalDays`; `current = StartDose ± Step × intervals` clamped at `EndDose`; negative clamped to 0 |
| Prn         | 0 (always) |

Invariants enforced in the constructors:

- `WeeklySchedule.QuantitiesByDayOfWeek` must have exactly seven
  non-negative entries. At least one must be positive; otherwise
  the schedule is a de-facto PRN and must be modeled as `Prn` for
  clarity.
- `CyclicSchedule.OnDays ≥ 1`, `OffDays ≥ 0`, `QuantityPerOnDay > 0`.
- `TaperingSchedule.IntervalDays ≥ 1`, `Step > 0`,
  `StartDose > 0`, `EndDose ≥ 0`, `StartDose ≠ EndDose`.
- `FixedDailySchedule.DosePerAdministration > 0`,
  `AdministrationsPerDay ≥ 1`.

### 2.2 Impact on `MedicationScheduleHistory`

The entity keeps its versioning (`EffectiveFrom` per history entry).
Two additive fields:

```csharp
public sealed class MedicationScheduleHistory
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required Guid MedicineId { get; init; }
    public required DateOnly EffectiveFrom { get; init; }

    // Legacy fields, still populated for FixedDaily (kept for
    // display and back-fill of pre-A1 rows).
    public required decimal DosePerAdministration { get; init; }
    public required int     AdministrationsPerDay { get; init; }

    // NEW (A1). Default FixedDaily so pre-A1 rows keep behaving
    // as before without a data-fix pass.
    public ScheduleKind ScheduleKind { get; init; } = ScheduleKind.FixedDaily;

    // NEW (A1). JSON of the kind-specific payload; NULL when
    // Kind = FixedDaily (the legacy fields carry all info).
    public string? SchedulePayload { get; init; }
}
```

The `Schedule` value object is not persisted directly; a small
helper (`ScheduleCodec` in `MedReminder.Domain/Medicines/`) knows
how to translate `Schedule ⇄ (ScheduleKind, SchedulePayload?)`
using `System.Text.Json`. `[INFERRED]` — `System.Text.Json` is already
used elsewhere (settings files).

**Rationale for extending the existing table, not a sibling
table.**

- Preserves versioning for free: `EffectiveFrom` already dispatches
  the right entry on the right day.
- Additive columns are trivially idempotent via
  `AddColumnIfMissingAsync` (already used in
  `DatabaseInitializer`).
- Sibling table would require a foreign key and a join in the hot
  `DailyConsumption.RateOn` path; not worth the cost for five
  discriminated shapes with tiny payloads.

### 2.3 Impact on `Medicine`

Only cosmetic: `Medicine.DosePerAdministration` and
`Medicine.AdministrationsPerDay` remain the "current, quick-glance"
values shown in read-only summaries. They are only meaningful for
`FixedDaily`. For other kinds the UI shows a human-readable summary
built from the current schedule entry (see §5.4). The columns stay
on `Medicine` — dropping them would break the existing UI grid and
`NotificationTexts.BuildEmail` in the same PR that lands A1, which
is scope creep.

`[UNCERTAIN]` — a follow-up PR could deprecate them once the UI
migrates fully; not part of A1.

### 2.4 SQLite additive schema patch

Two idempotent ALTER TABLE statements executed by
`DatabaseInitializer.ApplyIdempotentSchemaPatchesAsync` (chronological
list, appended after the M1 catalogue patch). Each is guarded by a
`PRAGMA table_info` check, per the existing pattern
(`AddColumnIfMissingAsync`).

```sql
ALTER TABLE "MedicationScheduleHistories"
    ADD COLUMN "ScheduleKind" INTEGER NOT NULL DEFAULT 0;
ALTER TABLE "MedicationScheduleHistories"
    ADD COLUMN "SchedulePayload" TEXT NULL;
```

Idempotency:

- `AddColumnIfMissingAsync` already skips existing columns.
- The default `0` maps to `ScheduleKind.FixedDaily`, so pre-A1 rows
  are automatically categorized as `FixedDaily` on the first boot
  after A1.
- No `EnsureCreated()` shortcut (per `CLAUDE.md` §8 and
  `ANALYSIS.md` §2.8).
- EF Core `MedicationScheduleHistoryConfiguration` is extended to
  declare both columns so a freshly-created DB via
  `EnsureCreated` also carries them.

---

## 3. Projection engine impact

### 3.1 `DailyConsumption.RateOn`

Current signature stays. Logic changes:

```
Given day D and the schedule history H:
  1. Pick the latest entry E in H with E.EffectiveFrom ≤ D.
  2. Deserialize schedule S from (E.ScheduleKind, E.SchedulePayload,
     E.DosePerAdministration, E.AdministrationsPerDay) via
     ScheduleCodec.
  3. Return S.RateOn(D, E.EffectiveFrom).
```

Slot interaction stays as today:

- If `administrationSlots` is non-empty for the medicine, return the
  sum of slot doses (unchanged from Increment 10).
- Otherwise, follow the new dispatch above.

`[INFERRED]` The slot path is orthogonal to A1: slots express
*where in the day* a dose is taken, not *how much per day varies
over time*. Combining slots with cyclic/tapering/weekly/PRN in one
UI is out of scope for A1 and would multiply the design surface.

### 3.2 `ConsumptionMaterializer.Plan`

Structural changes: **none**. The per-day loop already calls
`DailyConsumption.RateOn` and skips days with `rate <= 0`. PRN
therefore naturally produces zero materialized consumptions; cyclic
"off" days do too. The therapy window (`StartDate` / `EndDate`) and
`MedicationSuspension` semantics are unchanged.

### 3.3 `RunOutForecast.Compute`

Structural changes: **none**. It takes a scalar `dailyRate` from the
caller. Callers now supply the rate for `today`, which may vary
day-over-day — the forecast is a *snapshot on today* and remains
a linear projection using today's rate. This is a deliberate
simplification.

`[INFERRED]` A forward-aware ETA (integrating the schedule day by
day until stock hits zero) is more precise but out of scope. Left
as a follow-up if empirical data justifies it. Documented as a
limitation in the user guide.

### 3.4 `MedicationMonitor`

No changes beyond consuming the new `RateOn` behavior. The monitor
already calls `DailyConsumption.RateOn(today, schedule, slots)`
(`src/MedReminder.Application/Monitoring/MedicationMonitor.cs`).

### 3.5 `ChangeMedicationSchedule`

Signature extends. Current:

```csharp
public sealed record ChangeMedicationScheduleCommand(
    Guid MedicineId,
    decimal NewDosePerAdministration,
    int NewAdministrationsPerDay,
    DateOnly EffectiveFrom);
```

New (additive, keeps the old constructor callable via defaults):

```csharp
public sealed record ChangeMedicationScheduleCommand(
    Guid MedicineId,
    decimal NewDosePerAdministration,
    int NewAdministrationsPerDay,
    DateOnly EffectiveFrom,
    Schedule? NewSchedule = null);   // null → FixedDaily built from the two decimals
```

Same treatment for `AddMedicineCommand` (adds an optional
`Schedule?` parameter). When `NewSchedule` is `null` the use case
constructs a `FixedDailySchedule(dose, admin)` — identical to
today's behavior. When it is set, the use case:

1. Validates the schedule payload via its constructor invariants.
2. Persists the history entry with `ScheduleKind = schedule.Kind`
   and `SchedulePayload = ScheduleCodec.Serialize(schedule)`
   (null for `FixedDaily`).
3. For UI/back-fill only, computes a **display** dose/frequency to
   store on `Medicine.DosePerAdministration` /
   `AdministrationsPerDay`:
   - FixedDaily: as passed.
   - Weekly: `sum(quantities)/7` split over `AdministrationsPerDay=1`.
   - Cyclic: `QuantityPerOnDay × OnDays / (OnDays+OffDays)` over 1.
   - Tapering: `StartDose` over 1.
   - PRN: `0` over 1.
   These display values are not used by the projection engine —
   they only feed the read-only summary column in the main grid.
   `[UNCERTAIN]` The display strategy is a compromise between
   "show a plausible number" and "avoid a misleading average"; the
   summary label in the grid must indicate the schedule kind
   alongside the number (§5.4).

---

## 4. Serialization

`ScheduleCodec` in `MedReminder.Domain/Medicines/`. Signatures:

```csharp
internal static class ScheduleCodec
{
    // Returns (kind, jsonPayload).  jsonPayload is null for FixedDaily.
    public static (ScheduleKind Kind, string? Payload) Serialize(Schedule schedule);

    // Reconstructs a Schedule.  For FixedDaily, uses the legacy
    // dose/admin fields carried on MedicationScheduleHistory.
    public static Schedule Deserialize(
        ScheduleKind kind,
        string? payload,
        decimal legacyDose,
        int     legacyAdministrations);
}
```

JSON shape per kind (`System.Text.Json`, camelCase):

```jsonc
// Weekly
{ "days": [1, 0.5, 1, 0.5, 1, 0, 0] }   // [Mon..Sun]

// Cyclic
{ "onDays": 21, "offDays": 7, "quantityPerOnDay": 1 }

// Tapering
{ "startDose": 4, "endDose": 0.5, "step": 0.5, "intervalDays": 7 }

// Prn
{}
```

Rules:

- Unknown enum value on read → `FixedDaily` fallback (fail-safe,
  same posture as `IProfileRegistry` role parsing in Increment 15).
- Malformed JSON → throw `InvalidOperationException` with the
  `MedicineId`; not silently fall back, because that would drift
  the projection.
- Rounding: quantities use `decimal` throughout, never `double`
  (same convention as `StockMovement.QuantityDelta`).

---

## 5. UI

### 5.1 Location

`MedicineEditDialog` (create + edit modes). No new form.

### 5.2 Simple / Advanced toggle

A new radio-button row at the top of the "Schedule" group:

```
Schedule: (•) Simple    ( ) Advanced
```

- **Simple** (default). Preserves the current form: `DosePerAdministration`,
  `AdministrationsPerDay`, and the existing "Administration slots"
  list. One-click flow — no user visible break with pre-A1.
- **Advanced**. Reveals a `Regime type` dropdown with five entries:
  `FixedDaily`, `Weekly`, `Cyclic`, `Tapering`, `Prn`. Selecting a
  kind shows the kind-specific sub-panel. The slot list is hidden
  in Advanced mode for A1 (slot × non-fixed combinations are
  out of scope, see §3.1).

Editing an existing therapy:

- If the current schedule entry has `Kind = FixedDaily` and no
  payload → dialog opens in **Simple**.
- Otherwise → dialog opens in **Advanced** with the correct kind
  pre-selected and the sub-panel populated.

### 5.3 Kind-specific sub-panels

Compact, reuse of existing controls. All labels come from
`assets/localization/strings.<lang>.json`.

- **FixedDaily**: `DosePerAdministration` + `AdministrationsPerDay`
  (same controls as Simple).
- **Weekly**: seven `NumericUpDown` cells labeled with the
  short weekday names, in a `TableLayoutPanel`.
- **Cyclic**: three `NumericUpDown` (`OnDays`, `OffDays`,
  `QuantityPerOnDay`) with an inline sentence label
  (`{On} days on, {Off} days off — {Quantity} per on-day`).
- **Tapering**: four `NumericUpDown` (`StartDose`, `EndDose`,
  `Step`, `IntervalDays`) with a short summary label
  (`{Start} → {End}, step {Step} every {IntervalDays} days`).
- **PRN**: a paragraph-length help text explaining that the app
  will only track stock and won't materialize automatic
  consumption. No inputs.

### 5.4 Read-only display in `MainForm` grid

The `Consumption/day` column keeps its numeric value on FixedDaily
therapies. For other kinds the same column shows a badge:

- `Weekly` — a compact string `Mo·Tu·We·Th·Fr·Sa·Su` where cells
  with a positive dose are visually highlighted.
- `Cyclic` — `{On}/{Off}` (e.g. `21/7`).
- `Tapering` — `{StartDose}→{EndDose}` (`4→0.5` mg).
- `PRN` — the literal `PRN` in a muted color.

`[INFERRED]` The badges live inside the same cell to avoid a new
column; keeps the grid width unchanged, which is important on
smaller screens.

### 5.5 Validation errors

Every invariant from §2.1 that fires in the constructor is turned
into a `MessageBox` when the user clicks Save. Messages come from
localization keys — no hard-coded strings.

---

## 6. Localization

Six new keys plus their kind-specific labels, added to all five
dictionaries (`de`, `en`, `es`, `fr`, `it`). Sample keys, final
names to be aligned with existing conventions:

- `Ui.MedicineEditDialog.Schedule.Mode` — "Schedule"
- `Ui.MedicineEditDialog.Schedule.Simple` — "Simple"
- `Ui.MedicineEditDialog.Schedule.Advanced` — "Advanced"
- `Ui.MedicineEditDialog.Schedule.Kind.FixedDaily`
- `Ui.MedicineEditDialog.Schedule.Kind.Weekly`
- `Ui.MedicineEditDialog.Schedule.Kind.Cyclic`
- `Ui.MedicineEditDialog.Schedule.Kind.Tapering`
- `Ui.MedicineEditDialog.Schedule.Kind.Prn`
- `Ui.MedicineEditDialog.Schedule.Weekly.Grid.Header.Mon` (…Sun)
- `Ui.MedicineEditDialog.Schedule.Cyclic.OnDays`
- `Ui.MedicineEditDialog.Schedule.Cyclic.OffDays`
- `Ui.MedicineEditDialog.Schedule.Cyclic.QuantityPerOnDay`
- `Ui.MedicineEditDialog.Schedule.Cyclic.Summary` — `"{0} on, {1} off — {2} per on-day"`
- `Ui.MedicineEditDialog.Schedule.Tapering.StartDose`
- `Ui.MedicineEditDialog.Schedule.Tapering.EndDose`
- `Ui.MedicineEditDialog.Schedule.Tapering.Step`
- `Ui.MedicineEditDialog.Schedule.Tapering.IntervalDays`
- `Ui.MedicineEditDialog.Schedule.Tapering.Summary` — `"{0} → {1}, step {2} every {3} days"`
- `Ui.MedicineEditDialog.Schedule.Prn.Help`
- `Ui.MedicineEditDialog.Schedule.Validation.WeeklyAllZero`
- `Ui.MedicineEditDialog.Schedule.Validation.CyclicOnDaysMin`
- `Ui.MedicineEditDialog.Schedule.Validation.TaperingEqualStartEnd`
- `Ui.MainForm.Column.Schedule.Prn` — `"PRN"`

The existing `DictionaryParityTests` fail the build on any missing
key, so parity is enforced automatically.

**Italian strings**: the user decides them (per task disciplina).
Draft placeholders for review:

| Key                                                                 | it (draft, to review) |
|---------------------------------------------------------------------|-----------------------|
| `Ui.MedicineEditDialog.Schedule.Mode`                               | `Schema`              |
| `Ui.MedicineEditDialog.Schedule.Simple`                             | `Semplice`            |
| `Ui.MedicineEditDialog.Schedule.Advanced`                           | `Avanzato`            |
| `Ui.MedicineEditDialog.Schedule.Kind.FixedDaily`                    | `Dose giornaliera fissa` |
| `Ui.MedicineEditDialog.Schedule.Kind.Weekly`                        | `Settimanale`         |
| `Ui.MedicineEditDialog.Schedule.Kind.Cyclic`                        | `Ciclico (N on / M off)` |
| `Ui.MedicineEditDialog.Schedule.Kind.Tapering`                      | `Scalare`             |
| `Ui.MedicineEditDialog.Schedule.Kind.Prn`                           | `Al bisogno (PRN)`    |
| `Ui.MainForm.Column.Schedule.Prn`                                   | `PRN`                 |

**Draft translations** for `en` / `fr` / `es` / `de` to be reviewed
alongside the Italian set once the user approves this analysis.
Not tabulated here to avoid drift with the implementation PR.

---

## 7. Tests

### 7.1 `MedReminder.Domain.Tests`

- `ScheduleTests`
  - `FixedDailySchedule.RateOn` returns `dose × admin` regardless of
    date.
  - `WeeklySchedule.RateOn` returns the entry for `day.DayOfWeek`
    with Monday indexed as 0.
  - `CyclicSchedule.RateOn` progression across two full periods,
    incl. the seam at the period boundary.
  - `TaperingSchedule.RateOn` clamp at `EndDose` reached exactly on
    interval `k = (Start-End)/Step`, and clamp at 0 when the
    projection would go negative.
  - `PrnSchedule.RateOn` always 0.
  - Invariant violations throw in the constructor.
- `ScheduleCodecTests`
  - Round-trip per kind (`Serialize → Deserialize → structural
    equality`).
  - `FixedDaily` payload is `null` and the legacy fields feed
    `Deserialize`.
  - Unknown `ScheduleKind` value → fallback to `FixedDaily`
    using legacy fields.
  - Malformed JSON throws `InvalidOperationException`.

### 7.2 `MedReminder.Application.Tests`

- `DailyConsumptionTests` (extend existing)
  - Latest applicable history entry is picked when several exist,
    for each kind.
  - Slot override still wins when slots are present, whatever the
    schedule kind.
- `ConsumptionMaterializerTests` (extend existing)
  - Cyclic 21/7 over 60 days produces exactly `⌈60/28⌉ × 21`
    materialized entries starting on the anchor.
  - Tapering 4→0.5 step 0.5 interval 7 produces the expected
    dose per week; last week clamps at `EndDose`.
  - PRN materializes zero entries.
- `RunOutForecastTests` (already exist) — no changes; verifies
  scalar behavior is preserved.

### 7.3 `MedReminder.Infrastructure.Tests`

- `DatabaseInitializerScheduleTests`
  - Build a temporary DB on the schema pre-A1 (i.e. without the
    two new columns), apply the initializer, assert both columns
    exist with the expected types.
  - Re-run the initializer, assert no error and no schema change
    (idempotency).
  - Insert a pre-A1 row (no payload) and read it back via EF Core;
    assert `ScheduleKind == FixedDaily` and payload `null`.
- `MedicationScheduleHistoryConfigurationTests`
  - EF Core round-trip on all five kinds via the codec.

`[VERIFIED]` The infrastructure test project already runs on
Windows only (DPAPI / registry are session-bound); the SQLite path
above does not need DPAPI, so it stays with the existing project.

---

## 8. Retro-compatibility

- **On-disk**. Existing DBs get the two new columns via
  `ALTER TABLE … ADD COLUMN` (idempotent). Pre-A1 history rows keep
  `DosePerAdministration` / `AdministrationsPerDay` and receive
  `ScheduleKind = 0` (FixedDaily) by SQLite's `DEFAULT 0`. Payload
  stays `NULL`.
- **In-memory**. `ScheduleCodec.Deserialize(FixedDaily, null, dose,
  admin)` yields the same `FixedDailySchedule(dose, admin)` a fresh
  A1 therapy would produce.
- **UI**. Editing a pre-A1 therapy opens in **Simple**, the current
  layout. The user sees no difference until they toggle **Advanced**.
- **API surface**. `AddMedicineCommand` and
  `ChangeMedicationScheduleCommand` gain a `Schedule?` parameter
  with a default `null`. Existing callers compile without changes.
- **Notifications**. `NotificationTexts.BuildEmail` and
  `.BuildToast` read only `Medicine.Name`, `daysRemaining`,
  `currentStock` and `eta` — no dependency on schedule shape. No
  text change required in A1.

**Failure mode audit.**

| Scenario | Outcome |
|---|---|
| Pre-A1 DB, first boot on A1 build | Two ALTER TABLE calls succeed, DB continues to work |
| Post-A1 DB, boot on A1 build | Column-existence check skips the ALTERs, no writes |
| Pre-A1 DB, first boot on a **later** build that reverts A1 | Extra columns ignored by the older code; safe |
| Malformed `SchedulePayload` on read | Throw with `MedicineId` in the message; the medicine is skipped in the monitor cycle; UI shows a red banner (leverages the existing DB-error banner). |

---

## 9. Non-goals recap

- No maximum daily dose enforcement, no overdose alert, no
  drug-interaction check, no clinical decision support of any kind.
- No slot × non-fixed schedule combinations.
- No exact-time reminders.
- No forward-integrating run-out ETA (scalar snapshot only).
- No auto-inference of a `Schedule` from historical intakes.
- No visualization (graph / chart) of the schedule.
- No PRN "expected daily rate" — PRN reports `0`, per
  `EVOLUTION.md` §3.1 refinement.

---

## 10. Risks and mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| Schedule migration silently mis-categorizes rows | High (wrong projection) | Default `0 → FixedDaily` matches pre-A1 exactly; round-trip test in `DatabaseInitializerScheduleTests` |
| Weekly indexing bug (Sunday vs. Monday first day) | Medium | Explicit test asserting Monday = index 0 for each locale; parity guarded by `DictionaryParityTests` on weekday labels |
| Tapering rounding drift over many weeks | Low | `decimal` arithmetic; explicit clamp at `EndDose` and at 0 |
| UI density regression on the medicine dialog | Medium | New controls only shown in Advanced mode; Simple mode preserves the current layout pixel-for-pixel |
| Slot × non-fixed combination in user data (from another tool) | Low | Impossible via the UI; if inserted by hand, slots win — documented in `docs/USER_GUIDE.*` addendum |
| Payload JSON becomes a dumping ground for future fields | Medium | Kind-specific payload shapes are locked in §4; any extension bumps the `ScheduleKind` set instead of overloading a payload |

---

## 11. Implementation plan

One PR on `feature/complex-regimens`. No sub-increments — the scope
is small enough that splitting adds ceremony without buying
isolation. Order of commits (indicative, may be squashed at PR
time):

1. Domain: `Schedule` value object + `ScheduleKind` +
   `ScheduleCodec` + tests.
2. Domain: extend `MedicationScheduleHistory` with two nullable
   fields; adapt `DailyConsumption.RateOn` to dispatch through
   `ScheduleCodec`.
3. Application: `AddMedicine` and `ChangeMedicationSchedule`
   accept an optional `Schedule`; write it into the history entry.
4. Application: extend `DailyConsumption` /
   `ConsumptionMaterializer` tests.
5. Infrastructure: EF configuration for the two new columns +
   `DatabaseInitializer` idempotent patches + tests.
6. UI: Simple / Advanced toggle in `MedicineEditDialog`,
   kind-specific sub-panels, badges in `MainForm` grid.
7. Localization: keys in all five dictionaries (Italian awaiting
   user sign-off).
8. `CHANGE_LOG.md` entry when the PR opens; `docs/USER_GUIDE.*.md`
   short section per language on regimens.

**Effort.** 2–3 developer-days for a maintainer familiar with the
codebase. `[INFERRED]` — echoes `EVOLUTION.md` §3.1 with the added
familiarity gained from the multi-user refactor.

---

## 12. Confirmed decisions

Everything above is a locked technical proposal. The four review
points that were open at initial draft time are resolved as
follows (maintainer sign-off, 2026-09-19):

1. **Italian labels** (§6). Decided on the actual form during
   implementation review. The implementation PR ships `strings.it.json`
   with `TODO(it): <english fallback>` placeholders so the missing
   translations are visible; final wording is applied in the same PR
   after the form is inspected.
2. **Slot × schedule combinations.** Deliberately out of scope for
   A1. In Advanced mode the slot list is hidden for every kind. See
   §13 for the intended follow-up when the need materializes.
3. **PRN semantics.** `dailyRate = 0` → `RunOutForecastResult(null,
   null)` (existing behavior of `RunOutForecast.Compute`, `[VERIFIED]`)
   → `MainForm` grid shows empty cells for `Days` and `ETA` on a PRN
   therapy. The `Consumption/day` cell shows the localized `PRN`
   badge in place of a number.
4. **Advanced-mode slot behavior.** Slot list is hidden in Advanced
   for every kind, including `FixedDaily`. A user who wants slot-driven
   daily consumption toggles back to Simple.

---

## 13. Future work — slot × non-fixed schedule (deferred, not in A1)

Point 2 above is a deferral, not a closure. When the need arises,
the intended shape is:

- Each `MedicationAdministrationSlot` gains an optional
  `QuantityPattern` (default `null` = same dose every day, current
  behavior).
- `QuantityPattern` reuses the `Schedule` taxonomy in this document
  (`Weekly`, `Cyclic`, `Tapering`), applied to the single slot's
  dose rather than to the daily total.
- `DailyConsumption.RateOn` sums `slot.RateOn(day, slot.AnchorDate)`
  across slots when slots are present, otherwise falls back to the
  medicine-level `Schedule` as defined in this document.
- `PrnSchedule` is not allowed as a slot pattern: PRN belongs at the
  medicine level, not at the slot level.
- UI: the slot list is shown in Advanced too, and each slot row
  gains a "…" affordance opening the pattern editor.

This section is a placeholder to keep the future refactor path
consistent with A1's data model. It is intentionally not planned
in detail — the actual demand will dictate the shape.

---

## Change log for this document

- 2026-09-19 — initial draft (pre-implementation).
- 2026-09-19 — §12 rewritten as confirmed decisions after
  maintainer sign-off; §13 added with the deferred slot × schedule
  follow-up.
