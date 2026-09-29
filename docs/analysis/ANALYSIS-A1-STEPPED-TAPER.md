# ANALYSIS — A1 follow-up: multi-stage (stepped) tapering

Design document, **prior** to implementation. Extends `A1` (complex
therapy regimens, `docs/ANALYSIS-A1-REGIMENS.md`, marked [DONE] in
`EVOLUTION.md` §3.1). Follows the same structure as that document and
as `ANALYSIS-MULTI-USER.md`.

> **This is not a speculative analysis.** Every decision is
> technically motivated and delimits what will be written in code.
> The "Decisions still to confirm" section at the end is the only
> zone of ambiguity that needs input. Nothing here authorizes
> implementation — the document exists to be reviewed and signed off
> first, per the repository convention (`CLAUDE.md` §7).

Epistemic classification: `[VERIFIED]` (checked against the current
tree), `[INFERRED]` (deduction from verified facts), `[UNCERTAIN]`
(hypothesis pending confirmation).

---

## 1. Scope

### 1.1 Problem

The tapering regime shipped with A1 is
[`TaperingSchedule`](../src/MedReminder.Domain/Medicines/Schedule.cs)
(`ScheduleKind.Tapering = 3`). It models a **linear, constant-step**
down-titration: from `StartDose`, subtract `Step` every
`IntervalDays` days, clamped at `EndDose`. `[VERIFIED]` — see
`TaperingSchedule.RateOn` in
`src/MedReminder.Domain/Medicines/Schedule.cs`.

A frequently requested real-world regime does not fit this shape: a
sequence of **arbitrary stages**, each with its own dose and its own
duration, where the doses do not follow an arithmetic progression and
the durations differ from one another. The canonical example from the
open issue:

1. **Initial stage:** dose `D` for `X` days.
2. **Intermediate stage:** reduced dose (e.g. `D/2`) for `Y` days.
3. **Final stage:** final / maintenance dose `D_final` for `Z` days.

`TaperingSchedule` cannot represent this: it has a single `Step` and a
single `IntervalDays`, so it cannot express two different reductions
(`D → D/2` then `D/2 → D_final`) over two different durations
(`X ≠ Y ≠ Z`). Encoding it as a linear taper would require irregular
steps and irregular intervals, which the value object does not carry.

### 1.2 Goal

Introduce a new schedule shape, **stepped tapering**, that represents
an ordered list of `(dose, durationDays)` stages, and route it through
the existing A1 projection plumbing with **no structural change** to
the engine and **no new database column**. Preserve retro-compatibility
byte-for-byte: existing linear tapers (`ScheduleKind.Tapering`) and
every other kind keep behaving exactly as today.

### 1.3 What this is NOT

- **Not a replacement for `TaperingSchedule`.** The linear taper stays.
  Stepped tapering is an additional shape for the irregular case. Users
  choose Linear or Stepped inside the "Tapering" regime (see §5).
- **Not a medical-device feature.** No maximum-dose enforcement, no
  overdose alert, no clinical validation of the schedule the user
  types. Same posture as A1 (`ANALYSIS-A1-REGIMENS.md` §1.3,
  `EVOLUTION.md` §8.2, `CLAUDE.md` §1). The app remains a
  stock-and-reminder tool.
- **Not a slot-level pattern.** Stepped tapering is a medicine-level
  schedule, like every other `ScheduleKind`. Slot × non-fixed
  combinations remain out of scope (`ANALYSIS-A1-REGIMENS.md` §13).
- **Not a forward-integrating run-out ETA.** The scalar-snapshot
  forecast from A1 (`ANALYSIS-A1-REGIMENS.md` §3.3) is unchanged: the
  ETA uses today's rate. A stepped taper makes the day-to-day rate
  vary, so the snapshot ETA is an approximation on stage-boundary
  days, exactly as it already is for linear tapering and cyclic
  schedules. Documented as a known limitation.

---

## 2. Data model

### 2.1 New value object: `SteppedTaperingSchedule`

Lives in `src/MedReminder.Domain/Medicines/Schedule.cs`, alongside the
existing five records. Pure C#, no EF or Windows dependencies.

```csharp
// One stage of a stepped taper: a flat dose held for a whole number
// of consecutive days.
public sealed record TaperStage
{
    public decimal Dose { get; }
    public int DurationDays { get; }

    public TaperStage(decimal dose, int durationDays)
    {
        if (dose <= 0m)
            throw new ArgumentException(
                "Taper stage dose must be positive.", nameof(dose));
        if (durationDays < 1)
            throw new ArgumentException(
                "Taper stage duration must be at least 1 day.",
                nameof(durationDays));
        Dose = dose;
        DurationDays = durationDays;
    }
}

public sealed record SteppedTaperingSchedule : Schedule
{
    public IReadOnlyList<TaperStage> Stages { get; }

    // When true, the LAST stage's DurationDays is ignored and its dose
    // is held indefinitely (maintenance regime). When false, the
    // course ends after the last stage's DurationDays elapse and the
    // rate returns to 0.
    public bool MaintainLastDose { get; }

    public SteppedTaperingSchedule(
        IReadOnlyList<TaperStage> stages,
        bool maintainLastDose = false)
        : base(ScheduleKind.SteppedTapering)
    {
        ArgumentNullException.ThrowIfNull(stages);
        if (stages.Count < 2)
            throw new ArgumentException(
                "Stepped tapering requires at least two stages; " +
                "use FixedDailySchedule for a single constant dose.",
                nameof(stages));
        // Defensive copy so the caller cannot mutate after construction
        // (same discipline as WeeklySchedule).
        Stages = stages.ToArray();
        MaintainLastDose = maintainLastDose;
    }

    public override decimal RateOn(DateOnly day, DateOnly anchor)
    {
        if (day < anchor) return 0m;
        var elapsed = day.DayNumber - anchor.DayNumber; // 0-based
        var cursor = 0;
        for (var i = 0; i < Stages.Count; i++)
        {
            var stage = Stages[i];
            var isLast = i == Stages.Count - 1;
            if (isLast && MaintainLastDose)
                return stage.Dose;          // held indefinitely
            if (elapsed < cursor + stage.DurationDays)
                return stage.Dose;          // inside this stage
            cursor += stage.DurationDays;
        }
        return 0m;                          // course finished
    }

    // Records auto-generate equality from declared properties, but
    // IReadOnlyList uses reference equality — override, as WeeklySchedule
    // already does.
    public bool Equals(SteppedTaperingSchedule? other) { /* structural */ }
    public override int GetHashCode() { /* structural over stages + flag */ }
}
```

`RateOn` semantics table (to append to
`ANALYSIS-A1-REGIMENS.md` §2.1):

| Kind             | `RateOn(day, anchor)` |
|------------------|-----------------------|
| SteppedTapering  | `day < anchor` → 0; else with `elapsed = day − anchor` walk stages accumulating `DurationDays`: return the dose of the stage containing `elapsed`; if `MaintainLastDose` return the last stage's dose for any `elapsed` past the previous stages; otherwise 0 once all stages elapsed |

Invariants enforced in the constructors:

- `TaperStage.Dose > 0`, `TaperStage.DurationDays ≥ 1`.
- `SteppedTaperingSchedule.Stages.Count ≥ 2` (a single stage is a
  `FixedDaily`; enforcing ≥ 2 keeps the kinds non-overlapping, the
  same rationale that forbids all-zero `Weekly` and equal
  start/end `Tapering`).
- No monotonicity constraint on the doses. A stepped taper is not
  required to be strictly decreasing — the app does not validate the
  clinical shape of the regime (non-goal §1.3). This means a user
  could type an increasing sequence; that is accepted and projected
  faithfully. `[INFERRED]` — consistent with the app's non-clinical
  posture; flag for confirmation in §12 if a soft warning is wanted.

### 2.2 `ScheduleKind` extension

```csharp
public enum ScheduleKind
{
    FixedDaily      = 0,
    Weekly          = 1,
    Cyclic          = 2,
    Tapering        = 3,   // existing LINEAR taper, unchanged
    Prn             = 4,
    SteppedTapering = 5,   // NEW — multi-stage taper
}
```

`= 5` is appended; values 0–4 are untouched, so every persisted row
keeps its meaning (`ScheduleKind.cs` documents that the integer values
are persisted verbatim and must stay stable). `[VERIFIED]` against
`src/MedReminder.Domain/Medicines/ScheduleKind.cs`.

### 2.3 Impact on `MedicationScheduleHistory` — none

**No new column.** The A1 groundwork already added `ScheduleKind`
(INTEGER) and `SchedulePayload` (TEXT NULL) to
`MedicationScheduleHistories`
(`ANALYSIS-A1-REGIMENS.md` §2.2 / §2.4). A stepped taper is stored as
`ScheduleKind = 5` plus its JSON payload in the existing
`SchedulePayload` column. There is therefore **no SQLite schema patch**
for this feature — the additive-column work was done once, in A1, and
this is exactly the extensibility A1 was designed for. `[VERIFIED]`
against `DatabaseInitializer` and
`MedicationScheduleHistoryConfiguration`.

The legacy `DosePerAdministration` / `AdministrationsPerDay` columns
carry a **display** value (see §3.2), never used by the projection.

### 2.4 Impact on `Medicine` — cosmetic only

Same treatment A1 gives every non-`FixedDaily` kind
(`ANALYSIS-A1-REGIMENS.md` §2.3): `Medicine.DosePerAdministration` and
`Medicine.AdministrationsPerDay` keep a quick-glance display value; for
a stepped taper the natural choice is the first stage's dose over 1
administration (see §3.2). The projection engine ignores these fields
for any kind that carries a payload.

---

## 3. Projection engine impact

### 3.1 `DailyConsumption`, `ConsumptionMaterializer`, `RunOutForecast`, `MedicationMonitor` — no structural change

The A1 contract is `Schedule.RateOn(day, anchor)`, and the engine is
already agnostic to the concrete kind:

- `DailyConsumption.RateOn` picks the latest applicable
  `MedicationScheduleHistory` entry, deserializes via `ScheduleCodec`,
  and calls `RateOn`. Adding a codec branch (§4) is enough. `[VERIFIED]`
  against `src/MedReminder.Domain/Calculations/DailyConsumption.cs`.
- `ConsumptionMaterializer.Plan` already loops day-by-day and skips
  days where `rate <= 0`, so a finished stepped course (rate 0 after
  the last stage) naturally materializes nothing beyond its end, and a
  maintenance tail materializes the flat dose forever — the same
  behavior linear tapering and cyclic already exhibit.
  `[VERIFIED]` against
  `src/MedReminder.Domain/Calculations/ConsumptionMaterializer.cs`.
- `RunOutForecast.Compute` stays scalar-snapshot
  (`ANALYSIS-A1-REGIMENS.md` §3.3). No change.
- `MedicationMonitor` consumes the new `RateOn` behavior transparently.

This is the whole point of the A1 design: a new discriminated shape is
a domain-only addition plus a codec branch plus UI, with the
Application layer untouched.

### 3.2 Display dose / frequency back-fill

`AddMedicine` / `ChangeMedicationSchedule` compute a display
dose/frequency to store on `Medicine` for the read-only summary
(`ANALYSIS-A1-REGIMENS.md` §3.5). Proposed rule for the new kind,
appended to that table:

- **SteppedTapering:** `Stages[0].Dose` over `AdministrationsPerDay = 1`.

Rationale: the first stage is the most recognizable number ("started at
D"). Like every other kind's display value, it is not used by the
projection and the grid summary must indicate the kind alongside the
number (the badge work is deferred in A1 §5.4 and stays deferred here).

### 3.3 `AddMedicine` / `ChangeMedicationSchedule` — already accept a `Schedule`

Both commands already carry an optional `Schedule?`
(`AddMedicineCommand.InitialSchedule`,
`ChangeMedicationScheduleCommand.NewSchedule`) added by A1. A stepped
taper flows through the existing path with no signature change:
validate via constructor invariants → persist `ScheduleKind = 5` +
`ScheduleCodec.Serialize(schedule)`. `[VERIFIED]` against
`src/MedReminder.Application/UseCases/`.

---

## 4. Serialization

One new branch in
[`ScheduleCodec`](../src/MedReminder.Domain/Medicines/ScheduleCodec.cs),
mirroring the existing per-kind branches (`System.Text.Json`,
camelCase).

JSON shape:

```jsonc
// SteppedTapering
{
  "maintainLastDose": false,
  "stages": [
    { "dose": 4, "days": 7 },
    { "dose": 2, "days": 7 },
    { "dose": 1, "days": 14 }
  ]
}
```

- `Serialize`: `SteppedTaperingSchedule` → `(ScheduleKind.SteppedTapering,
  json)`.
- `Deserialize`: validate `stages` present and non-null; reconstruct
  `TaperStage[]`; let the constructor enforce invariants (≥ 2 stages,
  positive doses, ≥ 1 day). Malformed / missing payload →
  `InvalidOperationException`, same posture as the other kinds
  (`ANALYSIS-A1-REGIMENS.md` §4).
- Unknown enum value on read (a `5` read by an **older**, pre-this-work
  build) → the existing `!Enum.IsDefined` fallback in `ScheduleCodec`
  already downgrades to `FixedDaily` using the legacy dose/admin
  columns, so an older binary degrades safely instead of throwing.
  `[VERIFIED]` — that fallback path exists today in
  `ScheduleCodec.Deserialize`.

`decimal` throughout for doses; `int` for durations. Never `double`.

---

## 5. UI

### 5.1 Placement — sub-choice inside "Tapering"

**Decided (2026-09-21):** the stepped taper is exposed as a
Linear / Stepped sub-choice **inside** the existing "Tapering" regime,
not as a sixth dropdown entry. The user thinks in one concept
("scalare / tapering") and picks the shape underneath. Two value
objects (`TaperingSchedule`, `SteppedTaperingSchedule`) sit behind one
dropdown item.

Concretely, in
[`SchedulePanel`](../src/MedReminder.UI/Controls/SchedulePanel.cs), the
existing `_taperingPanel` (shown when `_kindBox` selects
`ScheduleKind.Tapering`) gains a radio pair at its top:

```
Tapering:  (•) Linear    ( ) Stepped
```

- **Linear** (default): the current four `NumericUpDown`
  (`StartDose`, `EndDose`, `Step`, `IntervalDays`) + summary label —
  unchanged, builds a `TaperingSchedule`.
- **Stepped**: reveals the dynamic-stage editor (§5.2), builds a
  `SteppedTaperingSchedule`.

`_kindBox` keeps exactly five entries; the dropdown surface does not
grow. The Simple / Advanced outer toggle is unchanged — stepped taper
lives entirely under Advanced → Tapering → Stepped.

### 5.2 Dynamic-stage editor

A `TableLayoutPanel` with one row per stage, plus controls to grow and
shrink the list, as the issue's "dynamic stage inputs" asks for:

```
Stage 1:  Dose [ 4.00 ]   Duration (days) [  7 ]   [Remove]
Stage 2:  Dose [ 2.00 ]   Duration (days) [  7 ]   [Remove]
Stage 3:  Dose [ 1.00 ]   Duration (days) [ 14 ]   [Remove]
                                           [ + Add stage ]

[x] Keep the last dose as maintenance (continues indefinitely)
```

- Each row: a decimal `Dose` (`NumericUpDown`, min 0.01), an integer
  `DurationDays` (`NumericUpDown`, min 1), and a `Remove` button.
- `+ Add stage` appends a row. Two rows are present at first reveal
  (the minimum), so the control cannot be saved below the invariant.
- `Remove` is disabled when only two rows remain (the ≥ 2 invariant is
  enforced in the UI, not only at Save).
- The **"Keep the last dose as maintenance"** checkbox maps to
  `MaintainLastDose`. When checked, the last row's Duration field is
  visually de-emphasized / disabled with a tooltip ("held
  indefinitely"), since its duration is ignored by `RateOn`.

### 5.3 Schedule preview / summary

The issue explicitly asks for "a summary/preview of the schedule
breakdown before saving". A read-only multi-line label under the stage
list, recomputed on every value change (same pattern as the existing
`_taperingSummary` / `_cyclicSummary` labels):

```
Stage 1: 4 /day × 7 days  = 28   (days 1–7)
Stage 2: 2 /day × 7 days  = 14   (days 8–14)
Stage 3: 1 /day × 14 days = 14   (days 15–28)
Total: 28 days, 56 units
```

With `MaintainLastDose` checked, the last line reads
`(from day 15 onward, maintenance)` instead of a bounded range, and
the totals label states the total covers the tapering phase only. All
numbers are `decimal`, formatted with `CultureInfo.CurrentUICulture`
like the existing summaries. Units are the therapy's own unit — the
preview uses a neutral "/day" wording; no clinical unit is invented.

### 5.4 Seeding on edit (`ApplySchedule`)

`SchedulePanel.ApplySchedule` gains a `case SteppedTaperingSchedule`:
select kind `Tapering`, flip the inner radio to Stepped, clear and
rebuild the stage rows from `Stages`, set the maintenance checkbox.
The existing `case TaperingSchedule` selects Tapering + Linear. A
pre-existing linear taper therefore still opens exactly as today.
`[VERIFIED]` — `ApplySchedule` already switches on the concrete record
type.

### 5.5 `TryBuildSchedule`

Under `ScheduleKind.Tapering`, branch on the inner radio: Linear →
`new TaperingSchedule(...)` (unchanged); Stepped → collect the rows
into `TaperStage[]` and `new SteppedTaperingSchedule(stages,
maintainLastDose)`. Constructor `ArgumentException` is caught and
surfaced as the localized validation message, exactly as the existing
code does.

### 5.6 Validation errors

Every invariant from §2.1 that fires in a constructor becomes a
`MessageBox` on Save, sourced from localization keys — no hard-coded
strings (`ANALYSIS-A1-REGIMENS.md` §5.5). New cases: fewer than two
stages (should be unreachable via the UI but validated defensively),
non-positive dose, non-positive duration.

### 5.7 Grid badge — deferred

Consistent with A1 §5.4, the `MainForm` grid keeps showing the
current-day rate; a stepped-taper badge is deferred with the rest of
the badge work. No change to the grid in this feature.

---

## 6. Localization

New keys, added to **all five** dictionaries under
`assets/localization/` (`en`, `it`, `fr`, `es`, `de`) — the
`DictionaryParityTests` fail the build on any missing key
(`ANALYSIS-A1-REGIMENS.md` §6). Draft key names, aligned with the
existing `Ui.Schedule.*` convention:

| Key | en (draft) |
|-----|-----------|
| `Ui.Schedule.Tapering.Mode.Linear`      | `Linear` |
| `Ui.Schedule.Tapering.Mode.Stepped`     | `Stepped` |
| `Ui.Schedule.Tapering.Stepped.Stage`    | `Stage {0}` |
| `Ui.Schedule.Tapering.Stepped.Dose`     | `Dose` |
| `Ui.Schedule.Tapering.Stepped.Duration` | `Duration (days)` |
| `Ui.Schedule.Tapering.Stepped.AddStage` | `Add stage` |
| `Ui.Schedule.Tapering.Stepped.Remove`   | `Remove` |
| `Ui.Schedule.Tapering.Stepped.Maintain` | `Keep the last dose as maintenance (continues indefinitely)` |
| `Ui.Schedule.Tapering.Stepped.PreviewRow`      | `Stage {0}: {1} /day × {2} days = {3} (days {4}–{5})` |
| `Ui.Schedule.Tapering.Stepped.PreviewRowMaint` | `Stage {0}: {1} /day (from day {2} onward, maintenance)` |
| `Ui.Schedule.Tapering.Stepped.PreviewTotal`    | `Total: {0} days, {1} units` |
| `Ui.Schedule.Tapering.Stepped.Validation.MinStages`     | `A stepped taper needs at least two stages.` |
| `Ui.Schedule.Tapering.Stepped.Validation.DosePositive`  | `Each stage dose must be greater than zero.` |
| `Ui.Schedule.Tapering.Stepped.Validation.DurationMin`   | `Each stage must last at least one day.` |

Italian ships with `TODO(it): <english fallback>` placeholders and the
final wording is applied in the same PR after the form is inspected —
the exact convention A1 used (`ANALYSIS-A1-REGIMENS.md` §12 point 1).
Draft `fr` / `es` / `de` reviewed alongside once this analysis is
approved; not tabulated here to avoid drift with the implementation PR.

---

## 7. Tests

### 7.1 `MedReminder.Domain.Tests`

- `ScheduleTests` (extend)
  - `SteppedTaperingSchedule.RateOn`: dose of the correct stage across
    all three stages of `[(4,7),(2,7),(1,14)]`, including the exact
    seam days (day 7 vs day 8, day 14 vs day 15).
  - Rate is 0 before `anchor` and 0 after the last stage when
    `MaintainLastDose = false`.
  - Rate holds the last dose indefinitely when `MaintainLastDose =
    true` (assert a day far past the nominal end).
  - Constructor throws on: < 2 stages, non-positive stage dose,
    non-positive stage duration.
  - `TaperStage` constructor invariants throw as specified.
  - Structural `Equals` / `GetHashCode` over the stage list and the
    flag (two structurally identical schedules compare equal;
    differing flag or differing stage compares unequal).
- `ScheduleCodecTests` (extend)
  - Round-trip `Serialize → Deserialize → structural equality`, with
    `MaintainLastDose` both true and false.
  - Missing / malformed `stages` payload → `InvalidOperationException`.
  - A `SteppedTapering` payload read by the `!Enum.IsDefined` path is
    not exercised here (that path is for unknown *future* kinds); a
    dedicated test asserts the reverse — an unknown kind integer still
    falls back to `FixedDaily`.

### 7.2 `MedReminder.Application.Tests`

- `DailyConsumptionTests` / `ConsumptionMaterializerTests` (extend)
  - A stepped taper `[(4,7),(2,7),(1,14)]` from a given anchor
    materializes exactly `7×4 + 7×2 + 14×1 = 56` units over 28 days
    and nothing afterwards (course finished).
  - The same with `MaintainLastDose = true` continues materializing
    `1/day` past day 28.
  - Slot override still wins when slots are present, whatever the kind
    (unchanged A1 rule).

### 7.3 `MedReminder.Infrastructure.Tests`

- `DatabaseInitializerScheduleTests` / EF round-trip (extend)
  - Persist and re-read a `SteppedTapering` history entry via EF Core;
    assert `ScheduleKind == 5` and the payload round-trips through the
    codec. **No schema-patch test is needed** — this feature adds no
    column (§2.3).

---

## 8. Retro-compatibility

- **On-disk.** No schema change. Existing DBs already carry
  `ScheduleKind` + `SchedulePayload` from A1. A stepped taper is just a
  new `(kind=5, payload)` pair in the existing columns.
- **Existing linear tapers.** Untouched: `ScheduleKind.Tapering = 3`
  still deserializes to `TaperingSchedule`. Nothing re-categorizes
  them.
- **In-memory.** Every other kind's `Deserialize` path is unchanged.
- **API surface.** No command signature changes (`Schedule?` already
  optional on both use cases since A1).
- **Older binary reads a `5`.** The `!Enum.IsDefined` fallback in
  `ScheduleCodec.Deserialize` downgrades to `FixedDaily` using the
  legacy dose/admin columns — safe degradation, no throw. `[VERIFIED]`.
- **Notifications.** `NotificationTexts` reads name / daysRemaining /
  stock / eta only; no dependency on schedule shape. No change.

**Failure-mode audit** (extends `ANALYSIS-A1-REGIMENS.md` §8):

| Scenario | Outcome |
|---|---|
| Post-A1 DB, first boot on this build | No ALTER TABLE at all — no schema work; kind 5 read normally |
| Stepped-taper row read by an older, pre-this-work A1 build | `!Enum.IsDefined(5)` → FixedDaily fallback on legacy columns; projection stays finite |
| Malformed `stages` payload on read | Throw with context; medicine skipped in the monitor cycle; existing DB-error banner in the UI |
| `MaintainLastDose = true` with a course meant to end | User choice, projected faithfully as an indefinite maintenance dose; documented in the user guide |

---

## 9. Non-goals recap

- No maximum-dose enforcement, no overdose / interaction alerts, no
  clinical validation of the stage sequence (increasing sequences are
  accepted).
- No new database column (reuses A1's `SchedulePayload`).
- No slot × stepped-taper combination (medicine-level only).
- No forward-integrating run-out ETA (scalar snapshot unchanged).
- No auto-inference of stages from historical intakes.
- No chart / graph of the schedule (a text preview only, §5.3).
- No grid badge (deferred with the rest of A1 §5.4).

---

## 10. Risks and mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| Off-by-one at a stage seam (day X vs X+1) | Medium (wrong dose on boundary days) | 0-based `elapsed`, half-open interval `[cursor, cursor+duration)`; explicit seam tests at every boundary (§7.1) |
| `MaintainLastDose` makes stock projection run forever | Low | Same as linear tapering with `EndDose > 0` or cyclic — the materializer already bounds work by the therapy window / forecast horizon; documented |
| List-based `Equals` regressions (reference vs structural) | Medium | Custom `Equals`/`GetHashCode`, mirrored on `WeeklySchedule`; dedicated equality tests |
| UI lets the user save < 2 stages | Low | Two rows present at reveal; Remove disabled at two; Save-time validation as backstop |
| Payload becomes a dumping ground | Low | Locked shape in §4; any further extension bumps `ScheduleKind`, per A1 §10 |
| User types a clinically implausible taper | Out of scope | Non-clinical positioning; no validation beyond structural invariants (§1.3). Confirm in §12 whether a soft, non-blocking hint is wanted |

---

## 11. Implementation plan

One PR on `feature/stepped-tapering` (this analysis lands first, on
`feature/stepped-tapering-analysis`, docs-only). Indicative commit
order, mirroring A1 §11:

1. Domain: `TaperStage` + `SteppedTaperingSchedule` +
   `ScheduleKind.SteppedTapering = 5`; custom equality; tests.
2. Domain: `ScheduleCodec` branch + round-trip tests.
3. Application: display-dose back-fill rule for the new kind; extend
   `DailyConsumption` / `ConsumptionMaterializer` tests. (No command
   signature change.)
4. Infrastructure: EF round-trip test for kind 5. (No schema patch.)
5. UI: Linear / Stepped radio inside `_taperingPanel`; dynamic stage
   editor; preview label; `ApplySchedule` / `TryBuildSchedule`
   branches; validation.
6. Localization: keys in all five dictionaries (Italian `TODO(it):`
   placeholders per A1 convention).
7. `CHANGE_LOG.md` entry when the PR opens; short addendum to the
   "Complex regimens" section of `docs/USER_GUIDE.en.md` and, if the
   A1 localized-guide backlog is picked up, the four other guides.

**Effort.** 2–3 developer-days for a maintainer familiar with the
codebase — smaller than A1 because the engine, schema and command
layers are already in place; the weight is domain + UI + localization.
`[INFERRED]`.

---

## 12. Decisions still to confirm

1. **Fine sequence — CONFIRMED (2026-09-21).** Both behaviors ship:
   default = the course ends (rate → 0) after the last stage; an
   opt-in "keep the last dose as maintenance" checkbox
   (`MaintainLastDose`) holds the last dose indefinitely.
2. **UI placement — CONFIRMED (2026-09-21).** A Linear / Stepped
   sub-choice inside the existing "Tapering" regime, not a sixth
   dropdown entry (§5.1).
3. **Monotonicity — OPEN.** Should the app show a *non-blocking* hint
   when the entered stages are not monotonically decreasing? Default
   proposal: **no** — stay strictly non-clinical, accept any positive
   sequence. A soft hint is cheap to add later if users ask.
4. **Display back-fill value — OPEN (low stakes).** §3.2 proposes the
   first stage's dose as the grid quick-glance number. Alternative:
   an average over the whole taper. Recommendation: first-stage dose
   (most recognizable, matches how a taper is prescribed).
5. **Italian final wording — deferred to the implementation PR**, per
   the A1 convention (§6).

---

## Change log for this document

- 2026-09-21 — initial draft (pre-implementation). Follow-up to
  A1; introduces `SteppedTaperingSchedule` (`ScheduleKind = 5`) for
  multi-stage tapers. Reuses A1's `SchedulePayload` column (no schema
  patch). §12 records the two confirmed decisions (both end-behaviors
  via `MaintainLastDose`; Linear/Stepped sub-choice inside "Tapering")
  and the open low-stakes points.
