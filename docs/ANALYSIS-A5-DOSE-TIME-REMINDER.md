# ANALYSIS — A5: Dose-time "remind me to take it" reminder

Design document, **prior** to implementation. Once approved, work
proceeds on branch `feature/dose-time-reminder` (per `CLAUDE.md` §5).
Corresponds to `EVOLUTION.md` §3.5 (Group A, item A5). Follows the
structure of `ANALYSIS-A1-REGIMENS.md` and `ANALYSIS-MULTI-USER.md`.

> **This is not a speculative analysis.** Every decision is
> technically motivated and delimits what will be written in code.
> The "Decisions still to confirm" section at the end is the only
> zone of ambiguity that needs input.

Epistemic classification, aligned with the sibling documents:
`[VERIFIED]` (checked against the current tree), `[INFERRED]`
(deduction from verified facts), `[UNCERTAIN]` (hypothesis pending
confirmation). This analysis was originally prepared from the
documentation set and **not** re-checked against a working copy of
`src/`. Since the 2026-09-20 revision, `ANALYSIS.md` (the Phase 1/2
baseline) is available and has been cross-checked: references to
content that `ANALYSIS.md` specifies **and that A1 did not later
change** are tagged `[VERIFIED against ANALYSIS.md]` — this confirms
the design was specified that way, not that the current tree still
implements it verbatim; a tree check at implementation time still
applies. Claims about A1-era additions (the `Schedule` value object,
slot-level `Time`, `MedicationAdministrationSlot`) predate `ANALYSIS.md`'s
MVP model — `ANALYSIS.md` §1.1 item 2 explicitly deferred timed
administrations to a future extension — so they remain carried forward
as `[VERIFIED in EVOLUTION.md §3.5 at the time of that writing]` or
`[INFERRED from docs]` and must be confirmed against the tree.

---

## 1. Scope

### 1.1 Problem

The current notification path fires only around **stock
exhaustion**: low-stock, tolerated-delay and reorder events, keyed
on `(MedicineId, StockEpoch)` in the `NotificationEvent` table
(`ANALYSIS.md` §2.3, §2.9). `[VERIFIED against ANALYSIS.md §2.9]`

Some users do not forget the *reorder* — they forget the *dose*.
There is today no path that says "take your 08:00 tablet now". A5
closes that gap by emitting a punctual reminder at the scheduled
time of each dose, reusing the transport already in place.

### 1.2 Goal

Add an **opt-in, per-medicine dose-time reminder** that, at each
slot's wall-clock time, emits one notification per
`(medicine, slot, local-day)` through the channels already
configured for that medicine. The feature *extends* existing
plumbing; it introduces **no new schedule model** and **no new
transport**.

### 1.3 What A5 is NOT — the medical-device line

This is the single most important boundary of the item. A5 is
scoped strictly to **emit a reminder**. It must not become
adherence tracking, which would push the app under EU MDR 2017/745
(`EVOLUTION.md` §8.2, `CLAUDE.md` §1). Concretely, A5 does **not**
add:

- **No acknowledgement UI.** No "did you take the 08:00 dose?"
  prompt, no "taken / snooze / skip" buttons.
- **No missed-dose logging.** The reminder is fire-and-forget; the
  app never records whether a dose was taken or missed.
- **No clinical alerting.** No escalation, no "you are late", no
  overdose / interaction wording.
- **No stock decrement.** A dose reminder does **not** generate a
  `Consumption` movement. Consumption stays owned by
  `ConsumptionMaterializer` / the catch-up service exactly as
  today (`ANALYSIS.md` §2.4). The reminder is a UX signal, not a
  stock event.

The disclaimer copy in `CLAUDE.md` §1 remains sufficient **only as
long as this line holds**. Any of the four items above turns A5
into a regulated feature and is out of scope by design.
`[INFERRED — MDR classification depends on the declared intended
use]`

---

## 2. Preconditions — what already exists

A5 sits on top of groundwork that already shipped. The following
are stated as verified by `EVOLUTION.md` §3.5 and
`ANALYSIS-A1-REGIMENS.md`; re-confirm against the tree before
coding.

- **Slot-level time anchor.** `AdministrationSlotEntry` already
  carries an optional `Time` (`TimeOnly?`), used today for sorting
  and report rendering (`NotificationTexts.cs`,
  `Reporting/TherapyReport.cs`). `[VERIFIED in EVOLUTION.md §3.5 at
  the time of that writing]` This is an A1-era addition: the MVP
  model in `ANALYSIS.md` §1.1 item 2 / §2.3 used an integer
  `AdministrationsPerDay` with **no** slot times and explicitly
  deferred timed administrations to a future extension. A5 depends
  on the A1 extension, not on the `ANALYSIS.md` baseline.
- **Schedule value object + UI.** The `Schedule` value object
  (`FixedDaily`, `Weekly`, `Cyclic`, `Tapering`, `Prn`) and the
  `SchedulePanel` UI landed with A1 (`ANALYSIS-A1-REGIMENS.md`,
  Implementation status). A5 therefore has a stable notion of
  "is this therapy active on day D" via `DailyConsumption.RateOn`.
- **Periodic host.** A single `MedicationMonitorHostedService`
  runs a `PeriodicTimer` (default 30 minutes) inside the active
  profile's `IHost` (`ANALYSIS.md` §2.6). `[VERIFIED against
  ANALYSIS.md §2.6]`
- **Channels are already per-medicine.** `Medicine.NotificationChannels`
  is a `[Flags]` enum (`Email`, `Windows`) chosen by the record
  creator (`ANALYSIS.md` §2.3, §2.9, `ANALYSIS-MULTI-USER.md` §14a
  point K). A5 reuses it unchanged. `[VERIFIED against ANALYSIS.md
  §2.9]`
- **Notification dedup precedent.** Two idempotency patterns
  already exist and inform §3.2: the `NotificationEvent` table
  keyed by `(MedicineId, StockEpoch)` for low-stock dedup, and the
  unique constraint on `(MedicineId, OccurredAt.Date, Kind)` for
  `Consumption` movements (`ANALYSIS.md` §2.4, §2.9). `[VERIFIED
  against ANALYSIS.md §2.4]`
- **Per-profile isolation.** Only the **active** profile emits
  notifications; the scheduler lives in that profile's `IHost`
  (`ANALYSIS-MULTI-USER.md` §9.1). A5 inherits this for free — it
  never fires for inactive profiles.
- **Local-time discipline.** "Logical" dates are `DateOnly`,
  event timestamps are `DateTimeOffset` with local offset, and
  time flows through `TimeProvider` (`ANALYSIS.md` §1.1 item 7).
  A5's scheduler anchors on **local** wall-clock via
  `TimeProvider`, never UTC. `[VERIFIED against ANALYSIS.md §1.1
  item 7]`

---

## 3. Data model

### 3.1 New per-medicine flag `RemindOnDose`

A single additive boolean on `Medicine`, default `false`, next to
the existing per-medicine notification-channel flags.

```csharp
// On Medicine (MedReminder.Domain)
public bool RemindOnDose { get; init; } = false;
```

Slot-level granularity (a flag per slot) is **not** recommended for
the first cut — it doubles the UI complexity for marginal value
(`EVOLUTION.md` §3.5). Reopen only if users ask. The flag is
therapy-wide: when set, *every* slot that has a `Time` becomes a
firing anchor.

### 3.2 Deduplication table — corrected design

**`EVOLUTION.md` §3.5 says the reminder should be "deduplicated
via the existing `MedicationScheduleHistory` table pattern". That
reference is imprecise and must not be implemented literally.**
`MedicationScheduleHistory` is the *schedule-versioning* table
(`ANALYSIS.md` §2.3 — it holds `EffectiveFrom`,
`DosePerAdministration`, `AdministrationsPerDay`, extended by A1);
it has no notion of "a notification was sent".
`[VERIFIED against ANALYSIS.md §2.3]` Likewise the existing
`NotificationEvent` table is keyed by `(MedicineId, StockEpoch)`
(`ANALYSIS.md` §2.9) — an epoch changes only on a refill, so it is
the **wrong** key for a reminder that must fire **once per day per
slot**. Keying dose reminders on `StockEpoch` would fire at most
once per epoch (i.e. essentially never repeat), which defeats the
feature. `[VERIFIED against ANALYSIS.md §2.9]`

The correct idempotency key for A5 is
**`(MedicineId, SlotKey, LocalDate)`**, where `SlotKey` identifies
the firing anchor within the day. Two options:

| Option | Shape | Verdict |
|---|---|---|
| **A — new dedicated table `DoseReminderEvent`** | `Id`, `MedicineId`, `SlotKey`, `LocalDate (DateOnly)`, `FiredAt (DateTimeOffset)`, `Channel`, unique index `(MedicineId, SlotKey, LocalDate)` | ✅ Recommended. Keeps the low-stock and dose-reminder paths in separate tables, so their retention, querying and future evolution do not entangle. |
| B — extend `NotificationEvent` | add nullable `SlotKey`, `LocalDate`; make the dedup key discriminate on event type | ❌ Overloads one table with two unrelated dedup semantics; the `StockEpoch`-keyed queries and the `(SlotKey, LocalDate)`-keyed queries would coexist awkwardly. |

`SlotKey` is the stable identity of the slot. If
`MedicationAdministrationSlot` has a persistent `Id`
(`[INFERRED from docs]` — no such entity exists in the `ANALYSIS.md`
MVP model; it is an A1-era addition, confirm at implementation
time), use it. If slots are value objects without a stable id, fall
back to the slot's `Time` rendered as `HH:mm` — a therapy will not
have two distinct dose anchors at the same minute, so `Time` is a
sufficient key within `(MedicineId, LocalDate)`.

`LocalDate` is the `DateOnly` of the firing in **local** time (not
UTC) so the "once per day" guarantee matches the user's calendar
day across DST.

This table is what makes the runtime idempotent: a process restart
within the same minute, or a second tick inside the same minute,
finds the `(MedicineId, SlotKey, LocalDate)` row already present
and does not re-fire.

### 3.3 SQLite additive schema patch

Two idempotent statements, applied by
`DatabaseInitializer.ApplyIdempotentSchemaPatchesAsync` (appended
to the chronological patch list after the A1 patches), each guarded
by the existing `PRAGMA table_info` / column-existence helper
(`ANALYSIS-A1-REGIMENS.md` §2.4, `ANALYSIS.md` §2.8). **No
`EnsureCreated()` shortcut** (`CLAUDE.md` §9).

```sql
-- Flag on Medicine
ALTER TABLE "Medicines"
    ADD COLUMN "RemindOnDose" INTEGER NOT NULL DEFAULT 0;

-- Dedup table (Option A). Created only if absent.
CREATE TABLE IF NOT EXISTS "DoseReminderEvents" (
    "Id"         TEXT    NOT NULL PRIMARY KEY,
    "MedicineId" TEXT    NOT NULL,
    "SlotKey"    TEXT    NOT NULL,
    "LocalDate"  TEXT    NOT NULL,
    "FiredAt"    TEXT    NOT NULL,
    "Channel"    INTEGER NOT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS "IX_DoseReminderEvents_Dedup"
    ON "DoseReminderEvents" ("MedicineId", "SlotKey", "LocalDate");
```

- `DEFAULT 0` maps `RemindOnDose` to `false` for every pre-A5 row —
  no data-fix pass, and no reminder is ever "armed" by the upgrade.
- The EF Core `MedicineConfiguration` and a new
  `DoseReminderEventConfiguration` declare the same columns/index so
  a freshly-created DB via `EnsureCreated` also carries them
  (`ANALYSIS-A1-REGIMENS.md` §2.4 precedent).
- **Retention.** `DoseReminderEvents` grows by at most
  `(#armed medicines × #slots)` rows per day. A cheap prune of rows
  older than, say, 30 days runs on the same schedule as the log
  retention (`ANALYSIS.md` §2.10). Old rows carry no value once the
  day has passed.

---

## 4. Runtime / scheduler

### 4.1 Dedicated 1-minute service, not the 30-minute monitor

The existing `MedicationMonitorHostedService` ticks every 30
minutes (`ANALYSIS.md` §2.6). `[VERIFIED against ANALYSIS.md §2.6]`
That cadence is fine for low-stock projection but **too coarse for
a punctual dose reminder** — an 08:00 dose could fire up to 30
minutes late. `EVOLUTION.md` §3.5 already anticipates "on each
minute tick" and "split a sibling `DoseReminderService`".

**Decision:** add a dedicated `DoseReminderHostedService` with its
own `PeriodicTimer` at a **1-minute** period, running alongside the
existing monitor inside the active profile's `IHost`. Rationale:

- Keeps the low-stock monitor's 30-minute cadence untouched (no
  reason to poll stock every minute).
- Keeps the two concerns in separate, independently testable code
  paths (`EVOLUTION.md` §3.5: "keep them independent code-paths").
- A 1-minute timer is negligible cost on a desktop app.

The two services **share the DB** but write to **different** dedup
tables (`NotificationEvent` vs `DoseReminderEvents`). See §4.6 for
the full treatment of how this relates to `EVOLUTION.md` §3.5's
"share the deduplication history table" instruction — which this
analysis deliberately supersedes, with rationale.

### 4.2 Per-tick algorithm

On each 1-minute tick, with `now = TimeProvider.GetLocalNow()`:

```
1. Load active medicines where RemindOnDose == true.
2. For each such medicine:
   a. GATE — skip if any of:
        - CurrentStock <= 0                     (hard off, §4.4)
        - therapy not active on now.Date        (StartDate/EndDate,
          suspension, or DailyConsumption.RateOn(today) resolves
          the therapy as inactive)
   b. Enumerate its slots that have a Time value.
   c. For each such slot:
        - fireLocal = today's date @ slot.Time  (local wall-clock)
        - fire only if  fireLocal <= now  AND
          now - fireLocal <= GraceWindow        (§4.3)
        - dedup key = (MedicineId, SlotKey, now.Date)
          if a DoseReminderEvents row exists for that key -> skip
        - otherwise: emit via the medicine's channels (§5),
          then insert the DoseReminderEvents row (same
          transaction / immediately after a successful emit).
```

The gate is re-evaluated **on every tick**, not once per day — so a
mid-day stock drop to zero silently stops the rest of the day's
reminders (§4.4), and a mid-day suspension does likewise.

### 4.3 Grace window — avoid stale reminder floods

A punctual reminder that fires hours late is worse than one that
does not fire: a user who opens the PC at 18:00 does not want a
burst of "take your 08:00 / 12:00 / 14:00 dose" toasts. Yet the
dedup table alone would happily fire every un-fired slot of the
day on the first tick after launch.

**Decision:** a slot fires only if `now` is within a small
`GraceWindow` of `fireLocal` (recommended **default 30 minutes**,
configurable in `appsettings.json`). Past that window the slot is
considered missed and is **silently dropped** — consistent with
§1.3 (no missed-dose logging). No `DoseReminderEvents` row is
written for a dropped slot, so it is simply never fired for that
day.

`[UNCERTAIN]` — the exact default (15 / 30 / 60 min) is a UX call;
30 minutes is the proposed default and is called out in §13.

### 4.4 `Stock <= 0` is a hard off

When on-hand stock reaches zero mid-day, all remaining reminders
for that medicine for the rest of the day must stop (`EVOLUTION.md`
§3.5). Because the gate (§4.2 step 2a) is checked every tick and
reads `CurrentStock = Σ StockMovement.QuantityDelta`
(`ANALYSIS.md` §2.4), this is automatic: the next tick after the
stock hits zero skips the medicine entirely. No special-casing
needed. `[VERIFIED against ANALYSIS.md §2.4]`

### 4.5 DST and clock edge cases

Slot `Time` values are wall-clock; the scheduler anchors on local
time via `TimeProvider` (`ANALYSIS.md` §1.1 item 7). Documented
behavior on the two DST transitions:

- **Spring forward (missing hour).** A slot whose `Time` falls in
  the skipped hour has no local instant that day. It is treated as
  missed (the grace window never contains it) and dropped for that
  day. Documented in the user guide.
- **Fall back (repeated hour).** The `(MedicineId, SlotKey,
  LocalDate)` dedup key fires the slot **once**: the first tick in
  the first pass of the repeated hour writes the row; the second
  pass finds it and skips. No double-fire.

### 4.6 Interaction with the low-stock path — superseding EVOLUTION §3.5

`EVOLUTION.md` §3.5 gives two coupled instructions on this point,
quoted in full so the deviation below is explicit:

> "The two notification paths must not compete on the same slot
> tick. Keep them independent code-paths **but share the
> deduplication history table** so a 'reorder soon' and a 'take
> now' for the same medicine at the same minute do not stack into
> two toasts."

This analysis honours the first half ("must not compete", "keep
them independent code-paths") and **deliberately supersedes the
second half** ("share the deduplication history table"; "do not
stack into two toasts"). This is a conscious deviation, not an
oversight, and it needs sign-off (§13).

**Why the shared-table instruction is rejected.**

- The two paths dedup on **structurally different keys**:
  `(MedicineId, StockEpoch)` for low-stock (`ANALYSIS.md` §2.9) and
  `(MedicineId, SlotKey, LocalDate)` for dose reminders (§3.2).
  `[VERIFIED against ANALYSIS.md §2.9]` A single shared table would
  carry two disjoint key schemas with mutually-null columns — the
  Option B anti-pattern already rejected in §3.2 for the same
  reason.
- Sharing a table to prevent "two toasts at the same minute" only
  works if the two paths are also made to share a key, otherwise a
  common table changes nothing. Forcing a shared key across a
  refill-scoped epoch and a per-day-per-slot anchor is not
  meaningful.

**Why allowing both messages is the correct product behaviour.** A
"reorder soon" (low-stock, 30-min monitor, `NotificationEvent`) and
a "take now" (dose reminder, 1-min service, `DoseReminderEvents`)
are **semantically different messages for different purposes**.
Collapsing or suppressing one because the other fired in the same
minute would hide information the user needs — the whole point of
the dose reminder is that it is a distinct, punctual signal.
`EVOLUTION.md`'s concern was **duplicate** toasts (the *same*
message twice); the real fix for that is per-path keyed dedup,
which both paths already have. Two *different* messages legitimately
co-existing is not the duplication EVOLUTION was guarding against.

**Net effect.** Independent code paths (honoured), separate dedup
tables each keyed correctly (no same-message double-fire), and the
two distinct messages may both appear when a medicine is
simultaneously low on stock and due for a dose. If the maintainer
instead wants at-most-one-toast-per-minute-per-medicine coalescing,
that is a small additional UI-layer throttle on top of this design,
not a reason to merge the dedup tables — it is listed as an open
decision in §13.

---

## 5. UI

### 5.1 The checkbox

Add a checkbox `Ui.MedicineEditDialog.Field.RemindOnDose` next to
the existing `_channelWindows` / `_channelEmail` checkboxes in
`MedicineEditDialog` (`EVOLUTION.md` §3.5). `[INFERRED from docs]` —
confirm the field names against the tree.

### 5.2 Enable/disable conditions

The checkbox is **enabled only** when both conditions hold, at
dialog open and whenever they change during editing:

1. **The medicine is active in the therapy** and has at least one
   slot with a `Time` value. An entry with no timed slot has no
   dose-time anchor to fire on.
2. **Current on-hand stock is `> 0`.**

When either condition fails, the checkbox is **cleared and
disabled**, and a short localized helper label states the reason
(`Ui.MedicineEditDialog.RemindOnDose.DisabledNoTime` /
`...DisabledNoStock`). A tooltip on the disabled checkbox repeats
the reason.

### 5.3 Save behavior

Saving the medicine while the checkbox is disabled forces
`RemindOnDose` back to `false` — never persist an "armed" reminder
that has no chance of firing (`EVOLUTION.md` §3.5). This keeps the
persisted state and the runtime gate consistent: the flag is only
ever `true` for a medicine that had, at save time, both a timed
slot and positive stock.

Note this is a *save-time* clamp, not a runtime guarantee: stock
can drop to zero after save. The runtime gate (§4.4) is the
authoritative "will it fire" check; the save-time clamp only
prevents obviously-dead flags.

---

## 6. Channels

Reuse the existing toast (WinRT) and MailKit paths unchanged
(`ANALYSIS.md` §2.9). The per-medicine `_channelWindows` /
`_channelEmail` flags already select channels and are respected
as-is (`EVOLUTION.md` §3.5).

- **Toast is the primary channel.** It is the punctual, local
  signal the feature is about.
- **Email is an opt-in fallback only.** Delivery latency defeats a
  punctual dose reminder; email is meaningful only for a PC that is
  on but unattended (`EVOLUTION.md` §3.5). The email body must stay
  within the existing non-clinical envelope (`CLAUDE.md` §6,
  `ANALYSIS.md` §2.11): medicine name and dose-time only, no
  free-form medical note, no PII beyond what the primary user has
  already accepted.

A new composer method (e.g. `NotificationTexts.BuildDoseReminder`)
produces the short "time to take {medicine}" text; it is unit
tested with a text snapshot like the existing composers
(`ANALYSIS.md` §2.9). `[INFERRED from docs]`

---

## 7. Localization

New keys added to **every** dictionary under
`assets/localization/` (`en`, `it`, `fr`, `es`, `de`) per
`CLAUDE.md` §8. The existing `DictionaryParityTests` fail the build
on any missing key, so parity is enforced automatically
(`ANALYSIS-A1-REGIMENS.md` §6). Proposed keys (final names to be
aligned with existing conventions):

- `Ui.MedicineEditDialog.Field.RemindOnDose` — checkbox label.
- `Ui.MedicineEditDialog.RemindOnDose.Help` — one-line description.
- `Ui.MedicineEditDialog.RemindOnDose.DisabledNoTime` — helper when
  no timed slot exists.
- `Ui.MedicineEditDialog.RemindOnDose.DisabledNoStock` — helper
  when stock is zero.
- `Notification.DoseReminder.Title` — toast/email subject,
  e.g. `"Time to take {0}"`.
- `Notification.DoseReminder.Body` — body, e.g.
  `"{0} — scheduled dose at {1}"`.

**Italian strings**: follow the A1 precedent
(`ANALYSIS-A1-REGIMENS.md` §12 point 1) — the implementation PR may
ship `strings.it.json` with `TODO(it): <english fallback>`
placeholders, final wording applied after the form is inspected,
per the maintainer's stated preference.

Shipped user guides (`USER_GUIDE.en.md` and the four localized
guides) get a short "Dose-time reminder" section documenting the
opt-in nature, the toast-primary/email-fallback split, and the DST
behavior (§4.5). The four non-English guides may follow in a
follow-up, mirroring the A1 approach.

---

## 8. Tests

### 8.1 `MedReminder.Application.Tests`

Drive `DoseReminderHostedService` (or an extracted testable core)
with an injected `TimeProvider` and in-memory / fake repositories,
so no real timer or toast is needed.

- **Fires once at the slot time.** A slot at 08:00, armed medicine,
  stock > 0, active therapy → exactly one emit at the first tick in
  `[08:00, 08:00 + GraceWindow]`.
- **Dedup across ticks.** Two ticks within the grace window → one
  emit, one `DoseReminderEvents` row.
- **Dedup across restart.** Re-instantiate the service in the same
  minute (row already present) → no second emit.
- **Grace window drop.** First tick at `slot.Time + GraceWindow +
  1min` → no emit, no row (stale reminder suppressed, §4.3).
- **Stock hard off.** Stock goes to 0 between two ticks → the later
  slot does not fire (§4.4).
- **Suspension / inactive therapy.** Suspended or out-of-window
  therapy → no emit (gate, §4.2).
- **Channel selection.** `RemindOnDose == true` but only
  `_channelEmail` set → email path invoked, toast path not
  (respects per-medicine channels).
- **Independent paths coexist (§4.6).** Same medicine low-on-stock
  and due for a dose in the same minute → the low-stock path and
  the dose path each emit once on their own key; neither suppresses
  the other, and neither double-fires its own message.
- **DST fall-back.** Simulated repeated hour via `TimeProvider` →
  single emit (§4.5).
- **DST spring-forward.** Slot time inside the skipped hour → no
  emit that day (§4.5).

### 8.2 `MedReminder.Domain.Tests`

- Save-time clamp logic (if the clamp lives in a domain/use-case
  method rather than the form): `RemindOnDose` forced to `false`
  when no timed slot or stock ≤ 0.

### 8.3 `MedReminder.Infrastructure.Tests`

- **Idempotent patch.** Build a temp DB on the pre-A5 schema, apply
  the initializer, assert `Medicines.RemindOnDose` and the
  `DoseReminderEvents` table + unique index exist; re-run, assert no
  change (idempotency) — mirrors
  `DatabaseInitializerScheduleTests` from A1
  (`ANALYSIS-A1-REGIMENS.md` §7.3).
- **Unique index enforced.** Inserting a duplicate
  `(MedicineId, SlotKey, LocalDate)` row throws — proves the dedup
  guarantee at the storage layer.
- **Retention prune.** Rows older than the retention window are
  deleted; recent rows survive.

The infrastructure project already runs on Windows only; the SQLite
path here needs no DPAPI, so it stays with the existing project
(`ANALYSIS-A1-REGIMENS.md` §7.3).

---

## 9. Retro-compatibility

- **On-disk.** Existing DBs get `Medicines.RemindOnDose` via
  `ALTER TABLE … ADD COLUMN … DEFAULT 0` and the `DoseReminderEvents`
  table via `CREATE TABLE IF NOT EXISTS` — both idempotent. No row
  is armed by the upgrade.
- **Behavior.** With `RemindOnDose` defaulting to `false` for every
  existing medicine, an upgraded install behaves **identically**
  until the user opts in per medicine.
- **Revert-safety.** A later build that reverts A5 ignores the extra
  column and table; safe (same posture as A1,
  `ANALYSIS-A1-REGIMENS.md` §8).
- **Per-profile.** Because the service runs in the active profile's
  `IHost`, upgrading does not arm reminders for other profiles
  (`ANALYSIS-MULTI-USER.md` §9.1).

---

## 10. Non-goals recap

- No acknowledgement UI, no "taken/skip/snooze", no missed-dose
  logging, no clinical alert wording (§1.3 — the MDR line).
- No stock decrement from a reminder; consumption stays owned by
  the catch-up path.
- No slot-level flag in the first cut (therapy-wide flag only).
- No quiet-hours / silent-hours window in the first cut — a 06:00
  dose fires at 06:00. Quiet hours are an optional later refinement
  (`EVOLUTION.md` §3.5), not a first-cut requirement.
- No monitoring of inactive profiles (inherited from
  `ANALYSIS-MULTI-USER.md` §9.1).
- No exact-time reminder for `PRN` therapies — PRN has no scheduled
  dose time, so it never arms (falls out of §4.2 naturally).

---

## 11. Risks and mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| Wrong dedup key (EVOLUTION's `MedicationScheduleHistory` / `StockEpoch` hint) → reminder never repeats or double-fires | High | `(MedicineId, SlotKey, LocalDate)` key + DB unique index (§3.2); cross-tick and cross-restart dedup tests (§8.1) |
| Superseding EVOLUTION's "share the dedup table" without sign-off | Medium | Deviation documented in full and flagged for confirmation (§4.6, §13); rationale is the key mismatch already rejected as Option B (§3.2) |
| Stale reminder flood after the PC was off | Medium | Grace window drops slots older than the window; no row written for dropped slots (§4.3) |
| 30-min monitor cadence makes reminders late | Medium | Dedicated 1-minute `DoseReminderHostedService` (§4.1) |
| Feature drifts into adherence tracking (MDR) | High (regulatory) | Hard scope in §1.3: no ack, no missed-dose log, no clinical wording; disclaimer copy unchanged |
| DST double / 'missing' fire | Low | Local-time anchoring + local-date dedup key; documented behavior (§4.5); explicit tests (§8.1) |
| Email latency defeats punctuality | Low | Toast primary; email opt-in fallback only, documented (§6) |
| `DoseReminderEvents` grows unbounded | Low | Per-slot-per-day cardinality is tiny; 30-day prune on the log-retention schedule (§3.3) |
| Armed flag persisted for a dead therapy | Low | Save-time clamp forces `false` when no timed slot / no stock (§5.3); runtime gate is authoritative (§4.4) |

---

## 12. Implementation plan

One PR on `feature/dose-time-reminder`. Per `CLAUDE.md` §5, the PR
is opened **after the first commit**, and a `CHANGE_LOG.md` entry is
prepended when the PR opens. Indicative commit order:

1. Domain: `Medicine.RemindOnDose` flag; save-time clamp helper +
   tests.
2. Domain/Infrastructure: `DoseReminderEvent` entity + EF Core
   configuration; `ScheduleKind`-independent — no A1 change needed.
3. Infrastructure: `DatabaseInitializer` idempotent patches
   (column + table + unique index) + prune; tests.
4. Application: `DoseReminderHostedService` (1-min `PeriodicTimer`),
   per-tick gate + grace window + dedup; `NotificationTexts.
   BuildDoseReminder` composer; tests with injected `TimeProvider`.
5. Composition root (`MedReminder.UI/Program.cs`): register the new
   hosted service in the active profile's host.
6. UI: `RemindOnDose` checkbox in `MedicineEditDialog`, enable /
   disable conditions, helper label, save-time clamp wiring.
7. Localization: keys in all five dictionaries (Italian awaiting
   sign-off per §7).
8. `CHANGE_LOG.md` entry when the PR opens; short "Dose-time
   reminder" section in `docs/USER_GUIDE.*.md`.

Run `dotnet build` and `dotnet test` before every commit that
touches source (`CLAUDE.md` §8).

**Effort.** 1–1.5 developer-weeks including schema patch, UI
wiring, scheduler, dedup test, five-language localization and
user-guide updates (`EVOLUTION.md` §3.5). `[INFERRED]`

---

## 13. Decisions still to confirm

1. **Grace window default** (§4.3). Proposed **30 minutes**,
   configurable in `appsettings.json`. Confirm 15 / 30 / 60.
2. **`SlotKey` source** (§3.2). Prefer a persistent
   `MedicationAdministrationSlot.Id`; fall back to `HH:mm` if slots
   are keyless value objects. To be settled against the actual slot
   model at implementation time. `[UNCERTAIN]`
3. **Dedup table vs. extend `NotificationEvent`** (§3.2).
   **DECIDED 2026-09-20 — Option A (dedicated `DoseReminderEvents`
   table).** Selected by the product owner over extending
   `NotificationEvent`.
4. **Superseding EVOLUTION §3.5's "share the deduplication history
   table" (§4.6).** **DECIDED 2026-09-20 — supersession confirmed.**
   The two paths stay on **separate** dedup tables (each correctly
   keyed) and **both messages** ("reorder soon" and "take now") are
   allowed to appear when a medicine is both low on stock and due for
   a dose, because they are distinct messages, not duplicates. The
   optional at-most-one-toast-per-minute-per-medicine UI throttle was
   **not** requested; it can be added later as a pure UI-layer
   refinement without touching the tables.
5. **Quiet hours** (§10). Confirmed **out** of the first cut; reopen
   only on user demand.
6. **Localized user guides.** English section ships with the PR; the
   four localized guides may follow, mirroring A1
   (`ANALYSIS-A1-REGIMENS.md` Implementation status). Confirm this
   is acceptable.

---

## Change log for this document

- 2026-09-20 — initial draft (pre-implementation). Derived from
  `EVOLUTION.md` §3.5, `ANALYSIS.md`, `ANALYSIS-A1-REGIMENS.md` and
  `ANALYSIS-MULTI-USER.md`. Notes and corrects the imprecise
  `MedicationScheduleHistory` / `StockEpoch` deduplication hint in
  `EVOLUTION.md` §3.5, proposing a `(MedicineId, SlotKey,
  LocalDate)` key in a dedicated `DoseReminderEvents` table. Adds a
  grace window to suppress stale reminders and a dedicated
  1-minute hosted service.
- 2026-09-20 — revision after `ANALYSIS.md` (the Phase 1/2 baseline)
  became available. (1) Upgraded the epistemic tags on the
  `NotificationEvent` `(MedicineId, StockEpoch)` key, the
  `MedicationScheduleHistory` schedule-versioning role, the
  `(MedicineId, OccurredAt.Date, Kind)` consumption constraint, the
  30-minute `MedicationMonitorHostedService`, the `NotificationChannels`
  `[Flags]` enum and `CurrentStock = Σ movements` from
  `[INFERRED from docs]` to `[VERIFIED against ANALYSIS.md]`, and
  clarified that slot-`Time` / `Schedule` / `MedicationAdministrationSlot`
  are A1-era additions absent from the `ANALYSIS.md` MVP model
  (`ANALYSIS.md` §1.1 item 2). (2) Rewrote §4.6 to quote
  `EVOLUTION.md` §3.5's "share the deduplication history table"
  instruction in full and to declare openly that this analysis
  supersedes it, with rationale; added the corresponding open
  decision as §13 item 4 and a matching risk row in §11.
- 2026-09-20 — decision recorded. Product owner selected **Option A
  (dedicated `DoseReminderEvents` table)** for §13 item 3 and
  **confirmed the supersession** of `EVOLUTION.md` §3.5 for §13 item
  4; the optional per-minute UI throttle was not requested. Both
  items move from "to confirm" to "decided".
