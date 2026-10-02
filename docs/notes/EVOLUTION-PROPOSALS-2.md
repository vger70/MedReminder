# Evolution proposals, second round — benchmarked against similar apps

Working note prepared on 2026-10-01 at v2.12.1. It proposes new
features after a review of the shipped state (`docs/STATUS.md`,
`docs/EVOLUTION-DONE.md`), the open backlog (`docs/EVOLUTION.md`,
`docs/notes/EVOLUTION-PROPOSALS.md`) and a survey of comparable
medication apps. It is **not** a commitment: an item becomes work only
once the product owner approves it and it gets an analysis document or
an issue, as for the first round.

---

## 0. Conventions

- **Ranking criterion.** Same as `EVOLUTION-PROPOSALS.md` §0: expected
  benefit for the people who use the app, weighted by how many benefit
  and how often. Effort only breaks ties.
- **Effort.** One developer familiar with the codebase; every estimate
  is **[INFERRED]**.
- **Tags.** **[INFERRED]** for deductions, **[UNCERTAIN]** for claims
  not verified against a primary source.
- **Boundaries.** Local-first; not a medical device
  (`EVOLUTION.md` §9.2); no writes outside `%LOCALAPPDATA%\MedReminder\`;
  no secrets or medical data in logs; new UI strings in all five
  `strings.<lang>.json` files.
- **Cross-cutting rules for any item that adds data.**
  - Schema change: idempotent boot patch in `DatabaseInitializer`.
  - Replicated data: a new profile operation schema, an update of
    `docs/SYNC-FORMAT.md`, and every device of a sync group must run
    the new version before the feature is used.
  - Export: coverage in `docs/EXPORT-FORMAT.md`.

---

## 1. Survey of similar apps

| App | Features relevant here |
|---|---|
| Medisafe | Refill reminders, dependants, 20+ measurements, appointment manager, drug interaction checker (US, English only), reports for the doctor |
| MyTherapy | Symptom diary, measurements, PDF health report with adherence, reminders for next packs and follow-up prescriptions |
| Dosecast | Prescription number and linked doctor / pharmacy per drug, time-zone aware reminders when travelling, missed / late dose tracking, refill alert |
| Medicine-cabinet apps (Medkit and others) | Package expiry dates, stock levels |

Observations:

- The adherence and clinical functions (missed-dose alerts, adherence
  scores, interaction checks) are outside the MedReminder positioning
  (`EVOLUTION.md` §9.2) and are not proposed.
- The remaining gap is on **stock and prescription logistics**, the
  app's core purpose: what happens after the low-stock warning, supply
  problems, absences from home, administrative deadlines.

---

## 2. Ranking summary

| # | Proposal | Benefit | Effort | Sync / schema impact |
|---|---|---|---|---|
| 1 | Escalation warning before run-out | High | 3–5 days | Column + operation schema change |
| 2 | Prescription lifecycle: requested, issued, collected | High | 1.5–2.5 weeks | New table, new operations |
| 3 | AIFA shortage notice | High (Italy) | 1–1.5 weeks | None (reference data) |
| 4 | Actions in toast notifications | Medium-high | ~1 week | None |
| 5 | Coverage planner (trip or pharmacy pickup) | Medium-high | 3–5 days | None |
| 6 | Recurring administrative deadlines | Medium-high (subset) | ~1 week | New table, new operations |
| 7 | Calendar export (`.ics`) | Medium | 2–4 days | None |
| 8 | Caregiver per-event opt-in and periodic digest | Medium | 3–5 days | Settings only |

Recommended order: **1 → 5 → 2 → 4 → 3**, then the rest. Items 1 and 5
need little or no sync change; items 2 and 6 should share one
operation schema bump so that devices must be updated only once.

---

## 3. Proposals

### 3.1 Escalation warning before run-out

**Status.** Implemented in PR #162 with the threshold derived from
`ThresholdDays` (half, rounded down) instead of a per-medicine
setting: nothing new to replicate or configure.

**Problem (verified in the tree).** `NotificationCycle.ShouldNotify`
suppresses any further low-stock warning once a successful event
exists for the current `StockEpoch`. A user who ignores the first
warning hears nothing more until the next refill.

**Benefit — high.** Missing the window to ask for a prescription is the
main failure the app exists to prevent; every profile with a running
therapy is affected.

**Plan.**

1. Domain: add `Stage` (1, 2) to `NotificationEvent`; `ShouldNotify`
   returns stage 2 when days remaining fall below a second threshold
   (per medicine, with a default) within the same epoch and no
   stage-2 event succeeded.
2. Persistence: boot patch adding the column (default 1, so existing
   rows read as stage 1).
3. Sync: extend the `EmailNotificationSent` body with the stage so the
   per-group email deduplication (household step H1) keeps working;
   new operation schema.
4. UI: second threshold in the notification section of
   `MedicineEditDialog`; stage-specific toast and email texts.
5. Tests: truth table of `ShouldNotify` (stages, epoch change, therapy
   end date before run-out, failed events).

**Effort.** 3–5 days.

### 3.2 Prescription lifecycle: requested, issued, collected

**Status.** Implemented in PR #164 (Therapy → Prescriptions…). The whole prescription is one replicated
register (`PrescriptionChanged`, operation schema 7, image schema 5);
the reminder is sent 3 days before "valid until", once per date and
device; after a new package the app asks whether an open prescription
was collected instead of closing it on its own.

**Benefit — high** for chronic patients. The current flow ends at the
request draft (`PrescriptionRequestDialog`). The app does not know that
a prescription was issued and not yet collected, nor warn before it
lapses. An Italian electronic prescription is valid for 30 days; for
chronic therapies one prescription can cover up to 6 packs and 180
days of therapy (consumer sources, §6).

**Plan.**

1. Domain: `Prescription` entity — medicine, requested on, issued on,
   optional code (NRE), number of packs, valid until, collected on.
   Validity is a parameter with a 30-day default, not a hard-coded
   rule: the rules change and differ outside Italy.
2. Application use cases: mark as requested (offered by the request
   dialog), record issue, record collection; recording a new package
   offers to close the open prescription.
3. Notification: reminder before `valid until` when not collected, on
   the existing channels, deduplicated like dose reminders.
4. Persistence and sync: new table with boot patch; LWW operations in
   the profile operation log; export coverage.
5. Privacy: the NRE is health data; never logged.
6. UI: "Prescriptions to collect" summary card; state shown in the
   medicine list.
7. Tests: state transitions, validity computation, notification
   deduplication, sync apply and export round-trip.

**Effort.** 1.5–2.5 weeks.

### 3.3 AIFA shortage notice

**Status.** Implemented in PR #166: workflow
and script publishing `data/it/shortages/` (first list, 29/09/2026,
published from the reviewed file), client refresh with the catalogue
feeds when Italy is the reference country, a Supply column with the
detail as tooltip, and one notification per shortage start. Phase 2
(EMA and national catalogues for France and Spain) is not started.

**Benefit — high for Italian users**, and a differentiator: none of the
surveyed apps shows it **[INFERRED — survey not exhaustive]**. When a
medicine is in shortage the user must act earlier than the normal
threshold suggests. The list also announces shortages and permanent
withdrawals **before** they start (see below), which gives the user
time to talk to the doctor.

**Data (verified on the file of 29/09/2026, 2 514 rows).**

| Aspect | Finding | Consequence for the parser |
|---|---|---|
| Encoding | Windows-1252 (curly quotes, accented letters); not UTF-8 | Decode as cp1252 in the workflow; publish UTF-8 |
| Preamble | Two free-text lines (disclaimer, "aggiornato al dd/mm/yyyy") before the header | Skip to the header row; take the list date from line 2 |
| Format | `;` separator, 13 columns, quoted fields with embedded CR, LF and doubled quotes (42 rows); mixed line endings | Real CSV parser, never split on lines |
| Columns | Nome medicinale; Codice AIC; Principio attivo; Forma farmaceutica e dosaggio; Titolare AIC; Data inizio; Fine presunta; Equivalente; Motivazioni; Suggerimenti/Indicazioni AIFA; Nota AIFA; Classe di rimborsabilità; Codice ATC | — |
| Codice AIC | Always 9 digits with leading zeros (package level), the same key as `CODICE_AIC` in the shipped AIFA catalogue and as `Medicine.NationalCode`; 2 508 of 2 512 distinct codes are in catalogue 202609 | Exact string match; no product-level fallback needed for a first cut |
| Duplicates | 2 codes appear twice with identical content | Deduplicate on the code |
| Data inizio | Always present (`dd/mm/yyyy`); 294 rows start after the list date, up to 2029, mostly announced production problems (130) or permanent withdrawal (116) | Show "shortage expected from …" for a future start |
| Fine presunta | Empty in 2 020 rows (80 %); 27 rows carry a date already past, and AIFA states that rows stay listed after that date until the holder reports the actual end | Show the end date only as "expected", never as "resolved"; presence in the list is the only status |
| Equivalente | `Sì` (1 930) / `No` (584) | Neutral wording only (below) |
| Motivazioni | Free text, 41 distinct values, 28 of them on 5 or more rows (production problems, high demand, permanent or temporary withdrawal, commercial or regulatory reasons) | Map the recurring values to localised categories; fall back to the raw Italian text |
| Suggerimenti, Nota AIFA | Free Italian text (19 distinct suggestions; notes on 139 rows), partly addressed to hospitals | Do not show in the first cut; link to the AIFA page instead |
| Codice ATC | Always present | Not needed for matching |

**Plan.**

1. Workflow `download_aifa_shortages.yaml` and a parser in
   `scripts/feeds/` next to `aifa.py`, with tests on a fixture that
   reproduces the anomalies above (cp1252, preamble, embedded line
   breaks, duplicates). Output: a small UTF-8 JSON feed under
   `data/it/` with the list date and, per AIC, start, expected end,
   equivalent flag and reason category; manifest like `latest.json`.
   Documented in `docs/CATALOGUE-DATA.md`.
2. Frequency: the list is updated more often than monthly (search
   results showed versions of 25/09 and 29/09/2026); a weekly workflow
   run is enough **[INFERRED]**. The feed is about 0.9 MB as CSV, much less
   as filtered JSON.
3. Client: reuse the remote-feed refresh (same setting, data only,
   nothing executed, `docs/ANALYSIS.md` §9.5). Store the feed with the
   reference catalogue, not in the profile database: no sync, no
   export, no schema patch on the profile side.
4. Matching on `Medicine.NationalCode` for Italian profiles only.
5. UI: badge "in shortage" or "shortage expected from …" in the list
   and the summary; detail with expected end (or "not communicated"),
   category of reason, and the fixed text "ask your doctor or
   pharmacist". When `Equivalente = Sì`, add "AIFA reports that
   equivalent medicines are available". No product is named or
   suggested, to stay clear of any clinical role.
6. Notification: one toast or email per medicine when it enters the
   list (and once more when a future shortage starts), deduplicated
   per AIC and start date.
7. Phase 2: EMA shortage catalogue (ESMP) and the national catalogues
   for France and Spain; access format not verified **[UNCERTAIN]**.

**Effort.** 1–1.5 weeks for Italy.

### 3.4 Actions in toast notifications

**Status.** Implemented in PR #165 with the safe
actions only: body click (open the app on the medicine, or the
prescriptions), "Prepare request" on low-stock warnings and "Remind me
in 15 minutes" on dose reminders (a toast scheduled with Windows).
"Taken" and "Skipped" were left out by decision: the ledger treats
intakes per day (`LedgerDeriver` rule 2), so one intake recorded from a
toast would cancel the automatic consumption of every other dose of
that day. Recording intakes per slot needs its own analysis.

**Benefit — medium-high**, mainly for elderly users: fewer steps.
Dose reminder: "Taken", "Skipped", "Remind me in 15 minutes". Low-stock
warning: "Prepare request".

**Positioning.** Recording an intake as taken or skipped already exists
(`IntakeDialog`); the toast only shortens the path. No adherence
statistics or missed-dose alerts are added.

**Plan.**

1. Toasts currently carry no buttons (verified).
   `Microsoft.Toolkit.Uwp.Notifications` 7.1.3, already referenced,
   supports buttons and `OnActivated` for unpackaged desktop apps.
2. Arguments carry identifiers only, never medicine names.
3. App closed: activation relaunches the executable with arguments; the
   single-instance mutex path must forward them to the running instance
   (`CLAUDE.md` §7).
4. Snooze: one in-memory or persisted re-fire per slot and day,
   consistent with the A5 deduplication on
   `(MedicineId, SlotKey, LocalDate)`.
5. Tests: argument parsing, forwarding, A5 deduplication with snooze.

**Effort.** About 1 week.

### 3.5 Coverage planner (trip or pharmacy pickup)

**Status.** Implemented in PR #163 (Therapy → Plan supply…). Package counts use the quantity of the last
new package recorded; as-needed medicines are listed, not computed.

**Benefit — medium-high, low cost.** Given a date range, compute per
medicine the units needed, the projected stock and the shortfall, and
print a list ("to pack" or "to collect"). Dosecast covers travel only
for time zones.

**Plan.**

1. Application query over the existing projection engine
   (`Schedule.RateOn`, suspensions, schedule history).
2. No schema change, no sync.
3. Printing reuses the therapy-card print path (table layout, PDF via
   "Microsoft Print to PDF").
4. Tests: regimens (cyclic, tapering, PRN excluded or shown apart),
   suspensions inside the range.

**Effort.** 3–5 days.

### 3.6 Recurring administrative deadlines

**Status.** Implemented in PR #168 (Therapy → Administrative
deadlines…). The whole deadline is one replicated register
(`DeadlineChanged`, operation schema 8, image schema 6), with its own
channels and an optional medicine; the reminder starts from a notice
period set by the user, once per deadline and date and device; "Done"
closes a one-off deadline and moves a recurring one to its next date,
counted from the previous date. It did not ship with §3.2 under one
schema bump: §3.2 merged first, so devices of a sync group must be
updated once more.

Therapeutic plan (piano terapeutico), exemption renewal, periodic
check-ups tied to a medicine.

**Benefit — medium-high** for the users concerned: an expired
therapeutic plan stops dispensing of the medicine. These are date
reminders with no clinical content.

**Plan.**

1. Domain: `Deadline` entity — optional medicine, kind, date, lead
   time, optional recurrence.
2. Notification on the existing channels, deduplicated per deadline
   and date.
3. New table, operations and export coverage; ship with §3.2 under the
   same operation schema bump.
4. Validity periods vary by plan and region: entered by the user, no
   regulatory default **[UNCERTAIN]**.

**Effort.** About 1 week.

### 3.7 Calendar export (`.ics`)

**Status.** Implemented in PR #169 (Therapy → Export to calendar…).
Events: request-by day (run-out minus warning threshold) and run-out
date per active medicine, last day of each prescription to collect,
open deadlines; stable UIDs, so a new export updates the events. The
writer is pure formatting and lives in `MedReminder.Application`
(`Calendar/IcsWriter`), shared by the file export and the MailKit
adapter. The low-stock email always carries the run-out event with a
generic title, without a setting: the notification settings are
replicated and a new device-local flag there was not worth the cost.

**Benefit — medium.** An immediate mobile substitute while B.1 waits for
its spikes: run-out dates and "request prescription by" dates reach the
phone through Outlook or Google Calendar.

**Plan.**

1. iCalendar writer in `MedReminder.Infrastructure.Portable`.
2. Two paths: export to a file, or an `.ics` attachment on the
   low-stock email (MailKit).
3. Privacy: generic titles by default ("MedReminder reminder"); the
   medicine name only on explicit opt-in, because calendars live on
   third-party clouds.

**Effort.** 2–4 days.

### 3.8 Caregiver per-event opt-in and periodic digest

**Status.** Implemented in PR #170 (Settings → Notifications).
`EmailKind` on every automated email; `CaregiverEmails` (copied kinds,
all by default) and `CaregiverDigest` (weekly) are replicated profile
settings, like the addresses, and need no operation schema bump; the
weekly summary goes to the caregiver only, from a device that sends
email, and `CaregiverDigestSentOn` keeps the other devices of a synced
profile from sending it again once they have synced.

**Benefit — medium** for relatives following from a distance. The
per-event opt-in is the item deferred in
`ANALYSIS-A3-CAREGIVER-NOTIFICATIONS.md` §11 item 4.

**Plan.**

1. `EmailMessage.Kind` discriminator (anticipated in A3 §4.4) and
   per-kind flags in `notifications.settings.json`.
2. Digest: a scheduled job sent by the master device only (household
   step H4 rule); content is stock status only, no dose statistics.

**Effort.** 3–5 days.

---

## 4. Re-assessment of two open backlog items

- **Package expiry tracking (`EVOLUTION-PROPOSALS.md` #1).** A cheaper
  first step exists: `BarcodeParser` already exposes expiry (AI 17)
  and batch (AI 10) from the FMD DataMatrix, mandatory in Italy from
  9 February 2027 (`ANALYSIS-A2-BARCODE-SCAN.md`). Store the earliest
  expiry per medicine, filled by the restock scan; per-package tracking
  later. The batch would also allow matching recalls, but an AIFA
  recall feed in machine-readable form was not verified
  **[UNCERTAIN]**.
- **Weekly pill-organizer preparation (#5).** The open conflict with A5
  disappears if the view is **read-only**: consumption is already
  materialised daily, so a printable 7-day by slot grid needs no stock
  movement. Effort 3–4 days.

---

## 5. Evaluated and not recommended

- **Measurements (blood pressure, glucose).** Plain logging is probably
  not medical-device software, but thresholds and interpretive charts
  would be; the feature also drifts from the app's purpose
  **[INFERRED — MDCG 2019-11 makes qualification depend on the
  declared intended purpose]**.
- **Missed-dose caregiver alerts, adherence reports, interaction
  checks.** Excluded by `EVOLUTION.md` §9.2.

---

## 6. Sources

- Medisafe: <https://healthify.nz/apps/m/medisafe-meds-pill-reminder-app>,
  <https://play.google.com/store/apps/details?id=com.medisafe.android.client>
- MyTherapy: <https://apps.apple.com/us/app/pill-reminder-mytherapy/id662170995>,
  <https://techguide.parkinsons.org.uk/catalogue/my-therapy/review>
- Dosecast: <https://dosecast.com/features/>
- Medicine-cabinet apps: <https://mojapteczka.pl/blog/en/best-medicine-cabinet-apps-2026-ranking/>,
  <https://caringvillage.com/blog/caregiver-tech/medication-management-apps/>
- AIFA shortages (CSV of 29/09/2026 reviewed): <https://www.aifa.gov.it/en/farmaci-carenti>,
  <https://www.aifa.gov.it/documents/20142/847339/elenco_medicinali_carenti.csv>
- EMA ESMP: <https://www.ema.europa.eu/en/human-regulatory-overview/post-authorisation/medicine-shortages-availability-issues/european-shortages-monitoring-platform-esmp>
- Italian electronic prescription validity (consumer sources):
  <https://www.altroconsumo.it/salute/farmaci/speciali/ricetta-medica-per-farmaci>,
  <https://www.laleggepertutti.it/496176_ricetta-dematerializzata-cosa-ce-da-sapere>
- MDCG 2019-11: <https://health.ec.europa.eu/system/files/2020-09/md_mdcg_2019_11_guidance_en_0.pdf>
