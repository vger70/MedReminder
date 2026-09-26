# Implementation prompt — Guided stock count and reconciliation

Briefing for the Claude Code session that implements proposal 4.1 of
`docs/notes/EVOLUTION-PROPOSALS.md` (ranking #6). Read it fully, then
read the referenced files before changing code.

---

## 1. Goal

Keep the run-out forecast accurate when the physical count drifts from
the recorded stock. The user counts what is in the cabinet, types the
number, sees the gap against what the app expected, and confirms; the
app records one correction and the forecast is right again.

Today the user must compute the difference and pick "positive" or
"negative" correction by hand. The new value is the guided count and
the gap display. Target effort: 3–5 days.

## 2. Context to read first

- `CLAUDE.md`, `docs/ANALYSIS.md` (stock model, database gate).
- `docs/notes/EVOLUTION-PROPOSALS.md` §4.1.
- `src/MedReminder.Domain/Stock/StockMovementKind.cs` — `PositiveCorrection`
  and `NegativeCorrection` already exist.
- `src/MedReminder.Domain/Calculations/` — `MedicineStock`,
  `ConsumptionMaterializer`, `RunOutForecast`.
- `src/MedReminder.Application/Monitoring/ConsumptionCatchUp.cs` — how
  automatic consumption is materialized up to "now".
- `src/MedReminder.Application/UseCases/AddStock.cs`,
  `AdjustStockDown.cs`, `RegisterIntake.cs` — existing correction writes
  and the `StockEpoch` rule (corrections are not refills).
- `src/MedReminder.UI/Forms/StockAdjustmentDialog.cs` and the stock
  actions in `MainForm.cs`.

## 3. Design

- **Expected stock.** The gap is only meaningful against stock with
  consumption materialized up to the moment of the count. Reuse the
  existing catch-up / materialization path; do not write a second stock
  calculation.
- **Use case.** A new Application use case (for example
  `ReconcileStock`) takes medicine id and counted quantity, materializes
  consumption, computes `delta = counted - expected`, and writes a single
  `PositiveCorrection` or `NegativeCorrection` movement in one unit of
  work. Zero delta writes nothing. Does not change `StockEpoch`. Negative
  counted quantities are rejected. Reuse or consolidate with the existing
  correction use cases rather than duplicating their validation.
- **UI.** Either a mode of `StockAdjustmentDialog` ("I counted…") or a
  small dedicated dialog, whichever keeps the code simpler. Show expected,
  counted, gap (with sign and unit), and the effect on the run-out date
  before confirming. Consider a "count all active medicines" variant (a
  grid with an input column, apply only rows the user filled) if it falls
  out of the same use case cheaply; otherwise leave it for later and say
  so in the PR.
- **Wording.** Present the gap as a stock discrepancy. Do not present it
  as missed or extra doses, and do not compute any adherence score:
  adherence tracking is excluded by `docs/EVOLUTION.md` §9.2.

## 4. Constraints

- Respect the in-process database gate and single-process ownership of
  the profile database (`CLAUDE.md` §7); the use case must not race with
  the monitor's own materialization.
- No schema change is expected. If one turns out necessary, it is an
  idempotent boot patch in `DatabaseInitializer`, and export coverage
  follows.
- Default movement note should identify the correction as coming from a
  count, localized; user notes are not logged.
- New UI strings in all five `assets/localization/strings.<lang>.json`;
  update the five `docs/USER_GUIDE.<lang>.md`.

## 5. Workflow

- Ask before creating the branch; proposed name
  `feature/guided-stock-count`. Open the PR after the first commit,
  prepend a `CHANGE_LOG.md` entry, ask the user to run build and tests
  before committing source.

## 6. Verification

- Domain/Application tests: counted above, below and equal to expected;
  materialization happens before the gap is computed (count taken days
  after the last movement); decimal units; suspended medicine; rejection
  of negative input; `StockEpoch` unchanged.
- Test that the forecast after reconciliation matches a forecast computed
  from the counted quantity.
- Manual check of the dialog in the running app.
