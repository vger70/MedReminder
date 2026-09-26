# Implementation prompt — Therapy calendar / timeline view

Briefing for the Claude Code session that implements proposal 4.3 of
`docs/notes/EVOLUTION-PROPOSALS.md` (ranking #8). Read it fully, then
read the referenced files before changing code.

---

## 1. Goal

Show at a glance what the medicine table cannot: when each therapy
started and ends, planned suspensions, dosage changes over time, and the
estimated run-out date. The view is read-only and helps the user plan
(refills before a holiday, a suspension around surgery).

Target effort: 1–2 weeks.

## 2. Context to read first

- `CLAUDE.md`, `docs/ANALYSIS.md` (layering: computation in
  Domain/Application, rendering in UI).
- `docs/notes/EVOLUTION-PROPOSALS.md` §4.3.
- `docs/analysis/ANALYSIS-A1-REGIMENS.md` and
  `ANALYSIS-A1-STEPPED-TAPER.md` — regimens and schedule history.
- Domain: `Medicine` (`StartDate`, `EndDate`, `IsActive`),
  `MedicationScheduleHistory`, `MedicationSuspension`, `Schedule`,
  `Calculations/SuspensionState.cs`, `Calculations/RunOutForecast.cs`,
  `Calculations/DailyConsumption.cs`.
- Repositories: `IMedicationScheduleHistoryRepository`,
  `IMedicationSuspensionRepository`, `IMedicineRepository`,
  `IStockMovementRepository`.
- UI: `MainForm.cs`, `Presentation/MedicineOverviewLoader.cs` (how the
  table view gathers its data).

## 3. Design

- **Model.** A pure builder in the Application layer produces, for a date
  window, one row per medicine with typed segments: active periods,
  suspensions, schedule changes (markers with the new dosage summary),
  and the forecast run-out date. It reuses `RunOutForecast` and the
  suspension/schedule logic; no second forecast implementation. The
  builder is unit-testable without WinForms.
- **Rendering.** A custom-drawn WinForms control (Gantt-like: medicines as
  rows, days on a horizontal axis, a "today" line), scrollable in time,
  with a tooltip per segment and a legend. No new third-party dependency;
  if you believe one is warranted, stop and ask with the licence and
  tradeoff.
- **Entry point.** A view or tab reachable from MainForm; selecting a row
  should be able to take the user to the existing medicine actions if
  that is cheap.
- **Window.** Default a range around today (for example 60 days back, 120
  forward), with controls to move it. Past consumption is not drawn day
  by day.

## 4. Accessibility and wording

- Must stay readable with the large-text mode if it has shipped, and with
  Windows scaling at 200 %; derive fonts from the form font.
- Do not rely on colour alone: segments differ by pattern or label too.
- Keyboard navigation across rows, and a textual alternative (for example
  the tooltip content available via a details pane) so the information is
  not only visual.
- Label the run-out date as an estimate. The view is organizational, not
  clinical advice.

## 5. Out of scope

- Editing from the timeline (drag to move a suspension, etc.).
- Per-dose history or adherence visualisation (excluded by
  `docs/EVOLUTION.md` §9.2).
- Printing the timeline.

## 6. Constraints and workflow

- Read-only: no schema change, no new writes.
- New UI strings in all five `assets/localization/strings.<lang>.json`;
  update the five `docs/USER_GUIDE.<lang>.md`.
- Ask before creating the branch; proposed name
  `feature/therapy-timeline`. Open the PR after the first commit, prepend a
  `CHANGE_LOG.md` entry, ask the user to run build and tests before
  committing source.

## 7. Verification

- Builder tests: medicine with no end date, with end date inside the
  window, with overlapping and open-ended suspensions, with a stepped
  taper (several schedule changes), inactive medicine, window boundaries.
- Forecast date in the timeline equals the one shown in the table for the
  same data.
- Manual check with a profile holding several medicines, at 100 % and
  200 % scaling; screenshots in the PR.
