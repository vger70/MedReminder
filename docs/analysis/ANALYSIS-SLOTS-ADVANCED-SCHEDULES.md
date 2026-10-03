# Administration slots with advanced schedules

Status: implemented (2026-10).
Supersedes the slot rule of `ANALYSIS-A1-REGIMENS.md` §3.1.

## 1. Problem

Until this change a medicine's administration slots took precedence
over its schedule whatever the schedule kind: daily consumption was the
sum of the slot doses every day. A cyclic regime with slots was
therefore consumed on its pause days too, and a tapering regime with
slots never tapered. The edit form disabled the slots in Advanced mode
and dropped them on save, so the combination could only arise through
**Change dose/frequency…**, and was lost on the next edit.

Times of administration matter for every regime: a tapering dose is
still taken at given times of day.

## 2. Rule

The schedule says how much, the slots say when.

- **FixedDaily schedule (or no schedule row)**: unchanged. Each slot
  takes its own dose; daily consumption is the sum of the regular
  (not as-needed) slot doses.
- **Any other schedule** (weekly, cyclic, linear or stepped tapering,
  PRN): the schedule gives the day's quantity (`Schedule.RateOn`). The
  regular slots split it in proportion to their doses, which act as
  weights. Two slots of 1 on a tapering day of 4 take 2 + 2; slots of 2
  and 1 take 2/3 and 1/3 of the day.
- **As-needed slots** never take a share and are never consumed
  automatically (`ANALYSIS-INTRADAY-CONSUMPTION.md` §5.1). A medicine
  whose slots are all as-needed has no automatic consumption, whatever
  its schedule.
- A day on which the schedule gives 0 (a cyclic pause, a weekly day
  off, PRN) has nothing to split: no dose is due and no dose reminder
  is shown.

## 3. Implementation

| Area | Change |
|---|---|
| `DailyConsumption.RateOn` | Slots set the rate only under a FixedDaily row or without a row; otherwise the schedule's rate (0 when every slot is as-needed). |
| `DailyConsumption.SlotQuantities` | New: the quantity of each slot on a day (its dose, or its share). |
| `IntradayConsumption.DueSoFar` | Doses due so far use the slot quantities. |
| `ReconcileStock` | The "taken today" suggestion uses the slot quantities. |
| `DoseReminderService` | Unchanged: it already skips a day whose rate is 0. |
| `TherapyTimelineBuilder` | The "daily quantity from the administration times" note appears only when the slots set the quantity. |
| `MedicineEditDialog` | The slot panel stays enabled in Advanced mode; slots are saved with the advanced schedule; the summary says the schedule sets the quantity (`Ui.MedicineEditDialog.Slots.SummaryShared`). |

No schema, stored data or sync format change: the rule is evaluated
on read, so the ledger derived from the facts follows it at the next
catch-up.

## 4. Effects on existing data

A medicine that already has slots and a non-FixedDaily schedule now
consumes the schedule's quantity instead of the slot sum. Its derived
stock changes accordingly: cyclic pause days are no longer consumed and
a taper follows its doses. This corrects the previous behavior.

`ChangeMedicationSchedule` still records an empty slot set when the
schedule becomes PRN (`ANALYSIS-INTRADAY-CONSUMPTION.md` §5.2).

## 5. Limits

- The therapy report and the low-stock email list each slot with its
  stored dose, which under an advanced schedule is a weight, not the
  quantity of a given day.
- The intake dialog suggests the medicine's summary dose, not the
  share of the current slot.
