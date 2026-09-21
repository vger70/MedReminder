# Implementation prompt — A5: Dose-time "remind me to take it" reminder

This file is the self-contained briefing for the Claude Code session
that will implement feature A5. Read it completely before touching any
source file.

---

## 0. What you are about to implement

**Feature A5** adds an opt-in, per-medicine dose-time reminder to
MedReminder: at each scheduled slot's wall-clock time the app emits
one toast notification (and optionally an email) so the user
remembers to take the dose — not to refill. This is strictly a
"fire and forget" reminder, not adherence tracking. The medical-device
line must not be crossed (see §1 of this prompt).

The authoritative design lives in two documents — read them before
coding:

- **`docs/ANALYSIS-A5-DOSE-TIME-REMINDER.md`** — the approved
  design. Sections §3–§12 describe the data model, runtime algorithm,
  UI, localization, and implementation order. Where this prompt and
  the analysis document disagree, **the analysis document wins**.
- **`docs/EVOLUTION.md` §3.5** — the original sketch, retained for
  context. Two design decisions in EVOLUTION §3.5 have been explicitly
  superseded by the analysis document (deduplication mechanism and the
  shared-table hint — see `ANALYSIS-A5` §3.2 and §4.6).

Also read before making any architectural change:

- **`CLAUDE.md`** — mandatory rules covering language policy, branch
  naming, PR workflow, localization, and the things to never do.
- **`docs/ANALYSIS.md`** — the base architecture (data model, ports,
  hosted-service pattern, notification dedup precedent).

---

## 1. Hard boundaries — do NOT cross these

A5 is scoped strictly to **emit a reminder**. These four items are
**out of scope by design** (see `ANALYSIS-A5` §1.3). Adding any of
them would push the app under EU MDR 2017/745:

- No acknowledgement UI ("did you take the 08:00 dose?", "taken /
  snooze / skip" buttons).
- No missed-dose logging of any kind.
- No clinical alerting, escalation, or overdose/interaction wording.
- No stock decrement from a reminder — consumption stays owned by
  `ConsumptionMaterializer` / the catch-up service.

These are not stylistic preferences — they are the product-safety
boundary that keeps the app non-clinical. If a reviewer asks for any
of these, decline and reference `ANALYSIS-A5` §1.3.

---

## 2. Preconditions — verify against the tree first

Before writing any code, confirm the following in the actual source:

1. `AdministrationSlotEntry` has a `TimeOnly? Time` property used
   for sorting / rendering (`ANALYSIS-A5` §2, first bullet).
2. `MedicationMonitorHostedService` uses a `PeriodicTimer` at 30
   minutes (`ANALYSIS.md` §2.6 / `ANALYSIS-A5` §4.1).
3. `MedicineEditDialog` has `_channelWindows` and `_channelEmail`
   checkboxes — confirm exact field names (`ANALYSIS-A5` §5.1).
4. `DatabaseInitializer` (or equivalent) applies idempotent schema
   patches at startup — find the existing pattern to append to it
   (`ANALYSIS-A5` §3.3 references `DatabaseInitializer.
   ApplyIdempotentSchemaPatchesAsync`).
5. `MedicationAdministrationSlot` has a persistent `Id` (Guid or
   string). If it does, use it as `SlotKey`. If slots are keyless
   value objects, fall back to the slot's `Time` rendered as `HH:mm`
   (`ANALYSIS-A5` §13 item 2, `[UNCERTAIN]`).

Document what you find (brief inline comments in the commit message
are fine); update the analysis document if any fact is wrong.

---

## 3. Branch and PR

Per `CLAUDE.md` §5:

- Branch name: **`feature/dose-time-reminder`**, based on `main`.
- Open a pull request **after the first commit**, not at the end.
- Prepend a `CHANGE_LOG.md` entry when the PR opens (follow the
  format documented at the top of that file).

---

## 4. Implementation order

Follow `ANALYSIS-A5` §12. Each step must leave `dotnet build` and
`dotnet test` green before the next commit.

### Step 1 — Domain: `RemindOnDose` flag + save-time clamp

File: `src/MedReminder.Domain/Entities/Medicine.cs` (or wherever
`Medicine` is defined).

- Add `public bool RemindOnDose { get; init; } = false;`.
- Add or extend a domain helper that enforces the save-time clamp:
  `RemindOnDose` must be forced to `false` when (a) no slot has a
  `Time` value or (b) `CurrentStock <= 0`. This is a clamp at
  save time, not a runtime gate — `ANALYSIS-A5` §5.3.
- Unit test in `MedReminder.Domain.Tests` (or
  `MedReminder.Application.Tests`): assert the clamp fires for
  each bad condition.

### Step 2 — Domain/Infrastructure: `DoseReminderEvent` entity

`ANALYSIS-A5` §3.2 chose **Option A — dedicated `DoseReminderEvents`
table** (decided 2026-09-20 — not a decision still to make).

New entity (place it where similar domain entities live, e.g.
alongside `NotificationEvent`):

```csharp
public sealed class DoseReminderEvent
{
    public Guid Id { get; init; }
    public Guid MedicineId { get; init; }
    public string SlotKey { get; init; } = string.Empty; // persistent slot Id or "HH:mm"
    public DateOnly LocalDate { get; init; }
    public DateTimeOffset FiredAt { get; init; }
    public NotificationChannels Channel { get; init; }
}
```

EF Core configuration (`DoseReminderEventConfiguration`):

```csharp
builder.HasIndex(e => new { e.MedicineId, e.SlotKey, e.LocalDate })
       .IsUnique();
```

### Step 3 — Infrastructure: idempotent schema patches

Append two idempotent statements to the existing patch list in
`DatabaseInitializer` (`ANALYSIS-A5` §3.3):

```sql
-- Guard with PRAGMA table_info or IF NOT EXISTS patterns
-- already used in the project.

ALTER TABLE "Medicines"
    ADD COLUMN "RemindOnDose" INTEGER NOT NULL DEFAULT 0;

CREATE TABLE IF NOT EXISTS "DoseReminderEvents" (
    "Id"         TEXT NOT NULL PRIMARY KEY,
    "MedicineId" TEXT NOT NULL,
    "SlotKey"    TEXT NOT NULL,
    "LocalDate"  TEXT NOT NULL,
    "FiredAt"    TEXT NOT NULL,
    "Channel"    INTEGER NOT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS "IX_DoseReminderEvents_Dedup"
    ON "DoseReminderEvents" ("MedicineId", "SlotKey", "LocalDate");
```

Use the same column-existence guard pattern the existing patches use
(find it in the codebase — do not use `EnsureCreated()`, per
`CLAUDE.md` §9).

Add a prune step that deletes `DoseReminderEvents` rows older than
30 days, running on the same schedule as log retention
(`ANALYSIS-A5` §3.3 last bullet).

**Infrastructure tests** (`MedReminder.Infrastructure.Tests`):

- Apply the initializer to a pre-A5 schema → assert column and
  table/index exist.
- Re-apply → no error (idempotency).
- Insert a duplicate `(MedicineId, SlotKey, LocalDate)` → assert
  unique constraint violation.
- Rows older than 30 days are pruned; recent rows survive.

### Step 4 — Application: `DoseReminderHostedService`

New file, e.g.
`src/MedReminder.Application/Notifications/DoseReminderHostedService.cs`.

**Tick period: 1 minute** (not 30 minutes). Add to
`appsettings.json`:

```json
"DoseReminder": {
    "GraceWindowMinutes": 30
}
```

Per-tick algorithm (`ANALYSIS-A5` §4.2):

```
now = TimeProvider.GetLocalNow()

1. Load active medicines where RemindOnDose == true.
2. For each medicine:
   a. GATE: skip if CurrentStock <= 0
              OR therapy not active today
              (StartDate / EndDate / suspension /
               DailyConsumption.RateOn(now.Date) == 0).
   b. Enumerate slots that have a Time value.
   c. For each timed slot:
        fireLocal = now.Date @ slot.Time (local wall-clock)
        if fireLocal > now  -> too early, skip
        if now - fireLocal > GraceWindow -> too late (§4.3), skip,
                                            do NOT write a row
        dedup key = (MedicineId, SlotKey, now.Date)
        if DoseReminderEvents row exists for that key -> skip
        emit via medicine's channels (toast + email per flags)
        insert DoseReminderEvents row
```

**DST handling** (`ANALYSIS-A5` §4.5):

- Spring forward: slot.Time in the skipped hour → `fireLocal` never
  satisfies `fireLocal <= now <= fireLocal + GraceWindow` → dropped
  silently. Correct behavior, no special case.
- Fall back: `(MedicineId, SlotKey, LocalDate)` dedup fires once on
  the first pass of the repeated hour; second pass finds the row and
  skips. Correct, no special case.

**`NotificationTexts.BuildDoseReminder`** (new composer method,
place it alongside the existing composers in `NotificationTexts.cs`):

- Accepts medicine name and slot time, returns a `(title, body)`
  pair using the new localization keys (§6 below).
- Unit-tested with a text snapshot.

**Application tests** — cover all cases in `ANALYSIS-A5` §8.1:

- Fires once at slot time within grace window.
- Dedup across ticks (two ticks, one emit, one row).
- Dedup across restart (row already present, no second emit).
- Grace window drop (first tick after window → no emit, no row).
- Stock hard off (stock 0 → no emit on next tick).
- Suspension / inactive therapy → no emit.
- Channel selection (`RemindOnDose = true`, only email flag set →
  email invoked, toast not).
- Independent paths coexist: same medicine low-on-stock and due for
  a dose → low-stock path emits once, dose path emits once, neither
  suppresses the other.
- DST fall-back: simulated repeated hour → single emit.
- DST spring-forward: slot in skipped hour → no emit.

### Step 5 — Composition root

File: `src/MedReminder.UI/Program.cs` (or wherever the active
profile's `IHost` is built).

Register `DoseReminderHostedService` as an `IHostedService` in the
active profile's host, **alongside** (not replacing)
`MedicationMonitorHostedService`.

### Step 6 — UI: `RemindOnDose` checkbox

File: `src/MedReminder.UI/Forms/MedicineEditDialog.cs` (confirm
exact path against the tree).

- Add a `CheckBox _remindOnDose` next to `_channelWindows` /
  `_channelEmail` (`ANALYSIS-A5` §5.1).
- Add a short `Label _remindOnDoseHelp` (one-line description).
- Enable/disable conditions (`ANALYSIS-A5` §5.2), re-evaluated at
  dialog open and whenever relevant fields change:
  - Enabled when: at least one slot has a `Time` AND stock > 0.
  - Disabled + cleared when either condition fails.
  - When disabled: show a localized helper label
    (`DisabledNoTime` / `DisabledNoStock`) and a tooltip on the
    checkbox.
- Save-time clamp (`ANALYSIS-A5` §5.3): before persisting, force
  `RemindOnDose = false` when the checkbox is disabled.

### Step 7 — Localization

Add all six keys to **every** dictionary under `assets/localization/`
(`en`, `it`, `fr`, `es`, `de`). Missing keys fail the build via
`DictionaryParityTests` — do not skip any language.

Keys (`ANALYSIS-A5` §7):

| Key | English value |
|-----|--------------|
| `Ui.MedicineEditDialog.Field.RemindOnDose` | `"Remind me at dose time"` |
| `Ui.MedicineEditDialog.RemindOnDose.Help` | `"Sends a notification at each scheduled dose time."` |
| `Ui.MedicineEditDialog.RemindOnDose.DisabledNoTime` | `"No timed dose slots are configured for this medicine."` |
| `Ui.MedicineEditDialog.RemindOnDose.DisabledNoStock` | `"Not available when stock is zero."` |
| `Notification.DoseReminder.Title` | `"Time to take {0}"` |
| `Notification.DoseReminder.Body` | `"{0} — scheduled dose at {1}"` |

For the four non-English dictionaries, follow the A1 precedent: ship
with `"TODO(<lang>): <english fallback>"` placeholders and finalize
wording after the form is inspected. Italian wording requires
maintainer sign-off before it lands.

### Step 8 — User guide and CHANGE_LOG.md

- **`CHANGE_LOG.md`**: prepend an entry when the PR opens (the PR
  number will not be known until the PR is created; fill it in then).
  Follow the format at the top of `CHANGE_LOG.md`.
- **`docs/USER_GUIDE.en.md`**: add a short "Dose-time reminder"
  section documenting: opt-in per medicine, toast primary / email
  fallback, grace window behavior, DST note (spring forward = missed,
  fall back = fires once). The four localized guides may follow in a
  follow-up.

---

## 5. Decided items — not open for re-debate

These items in `ANALYSIS-A5` §13 were decided on 2026-09-20 and
must not be re-opened without the product owner's explicit sign-off:

- **Item 3** — Dedup mechanism: **Option A (dedicated
  `DoseReminderEvents` table)**. Do not extend `NotificationEvent`.
- **Item 4** — Supersession of EVOLUTION §3.5's "share the
  deduplication history table": **confirmed superseded**. Two paths,
  two tables, two structurally different keys. Both messages ("reorder
  soon" and "take now") may coexist. No per-minute UI throttle unless
  explicitly requested.
- **Item 5** — Quiet hours: **out of scope** in the first cut.

---

## 6. Still open — settle at implementation time

- **Item 2 — `SlotKey` source** (`ANALYSIS-A5` §13 item 2). Prefer
  a persistent `MedicationAdministrationSlot.Id` (Guid → rendered as
  lowercase hex string). Fall back to `HH:mm` if slots are keyless.
  Check the actual slot model in the tree.
- **Item 1 — Grace window default** (`ANALYSIS-A5` §13 item 1).
  Proposed **30 minutes**, configurable in `appsettings.json`.
  Implement 30 minutes and note it in the PR description; the
  maintainer can override it in settings.
- **Item 6 — Localized user guides** (`ANALYSIS-A5` §13 item 6).
  Ship English with the PR; the four localized guides may follow in
  a separate commit or PR.

---

## 7. Conventions and constraints (from `CLAUDE.md`)

- All identifiers, comments, log messages, exception messages, XML
  docs, and commit messages must be in **English**. Italian is used
  only in chat with the user and in the localized `strings.it.json`
  and `USER_GUIDE.it.md`.
- Run `dotnet build` and `dotnet test` before every commit that
  touches source.
- No `EnsureCreated()` for schema changes — idempotent patches only.
- No `SmtpClient` from `System.Net.Mail` — MailKit is the only
  supported SMTP client.
- No plaintext passwords or PII to logs.
- When adding a UI string, add the key to **every** localization
  dictionary.
- Keep Domain free of Windows-specific APIs and EF Core references.

---

## 8. Acceptance criteria

The PR is ready to merge when:

1. `dotnet build MedReminder.sln -c Release` is green.
2. `dotnet test MedReminder.sln -c Release` is green, including all
   new tests in §4 above.
3. All six localization keys are present in all five dictionaries
   (`DictionaryParityTests` passes).
4. `DoseReminderHostedService` is registered in the active profile's
   `IHost` alongside (not replacing) `MedicationMonitorHostedService`.
5. The idempotent schema patches apply cleanly to both a fresh DB and
   a pre-A5 DB.
6. No acknowledgement UI, no missed-dose log, no stock decrement from
   a reminder (§1 of this prompt).
7. `CHANGE_LOG.md` has a new entry for this PR.
8. `docs/USER_GUIDE.en.md` has a "Dose-time reminder" section.

---

*Generated 2026-09-21. Authoritative source: `ANALYSIS-A5-DOSE-TIME-REMINDER.md`.*
