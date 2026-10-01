# Evolution proposals — consolidated and ranked by user value

Working note that merges the two proposal drafts previously kept in
`docs/notes/EVOLUZIONI-1.md` and `docs/notes/EVOLUZIONI-2.md` into a
single list, ranked by value to the end user. It is **not** a
commitment and does not replace `docs/EVOLUTION.md`, which remains
the authoritative backlog and records the product owner's decided
sequence (§2.0). An item here becomes work only once it is approved
and moved into `EVOLUTION.md` or a dedicated analysis document.

Prepared on 2026-09-25 and reconciled against the tree at that date.
A second round of proposals, benchmarked against similar apps, is in
`docs/notes/EVOLUTION-PROPOSALS-2.md` (2026-10-01).
Status marks updated on 2026-10-01 at v2.12.0 (§8); the ranking itself
is unchanged.

---

## 0. Conventions

- **Ranking criterion.** Expected benefit for the people who use the
  app (chronic patients, elderly users, caregivers, families with
  several profiles), weighted by how many of them benefit and how
  often. Development effort is shown but does **not** drive the
  rank; it only breaks ties.
- **Effort.** Grossly indicative, taken from the source drafts unless
  corrected in the notes. One developer familiar with the codebase.
- **Tags.** Same as `EVOLUTION.md` §0: **[INFERRED]** for deductions,
  **[UNCERTAIN]** for claims not verified against a primary source.
- **Boundaries that apply to every item.** Local-first, not a medical
  device (`EVOLUTION.md` §9.2), no writes outside
  `%LOCALAPPDATA%\MedReminder\`, no secrets or medical data in logs.

---

## 1. Already delivered — removed from the ranking

The first draft predates several merges. These items are shipped and
are not ranked:

| Item | Status | Reference |
| --- | --- | --- |
| A3 — Caregiver notifications | Shipped | `EVOLUTION-DONE.md` §3.3 |
| C.3++ Phase 1 — `IArchiveStorage` + `LocalFolderArchiveStorage` | Shipped (PR #56) | `EVOLUTION-DONE.md` §6 |
| Public website v1 | Shipped in `vger70/medreminder-website` | `EVOLUTION-DONE.md` §10 |
| C.3++ Phase 2 — OneDrive and Google Drive (ranked #17 below) | Shipped inside B.1 Phase 4 (v2.8.0) | `EVOLUTION-DONE.md` §6 |

The website content refresh is already prepared in
`docs/prompt/PROMPT-WEBSITE-CONTENT-REFRESH.md` and is not repeated
here.

The closing section of `EVOLUZIONI-2.md` (summary of the export,
backup and pluggable-storage features) was a verbatim copy of
`docs/notes/PROPRIETA-BACKUP.md`. It describes shipped behaviour, not
a proposal, and is not merged here.

---

## 2. Ranking summary

| # | Proposal | Tier | Effort | Primary beneficiary | Source |
| --- | --- | --- | --- | --- | --- |
| 1 | Package expiry tracking | 1 | 2–3 weeks | All users | EV-2 |
| **2** | Accessible "large text" mode **[DONE]** | 1 | 1 week | Elderly, low-vision users | EV-2 |
| 3 | A2 — AIC / barcode scan **[DONE]** | 1 | 1–2 weeks | All users (Italy) | EV-1 |
| **4** | Prescription request draft for the doctor **[DONE]** | 1 | 2–3 days | Chronic patients, caregivers | EV-2 |
| 5 | Weekly pill-organizer preparation | 1 | 1 week | Elderly users, caregivers | EV-2 |
| **6** | Guided stock count and reconciliation **[DONE]** | 2 | 3–5 days | All users | EV-2 |
| 7 | B.1 — Mobile companion (.NET MAUI) **[desktop side DONE, mobile open]** | 2 | 2–4 person-months | Users away from the PC | EV-1 |
| **8** | Therapy calendar / timeline view **[DONE]** | 2 | 1–2 weeks | All users | EV-2 |
| **9** | Printable medication card (PDF) **[DONE]** | 2 | 1 week | Patients seeing several clinicians | EV-2 |
| 10 | Storage location per medicine | 3 | 1–2 days | Families, many medicines | EV-2 |
| 11 | Shared household stock | 3 | 3–4 weeks | Families with several profiles | EV-2 |
| 12 | Database encryption at rest | 3 | 1–2 weeks | Users on shared PCs | EV-2 |
| 13 | Spoken dose reminder (text-to-speech) | 3 | 3–5 days | Users with reading difficulties | EV-2 |
| 14 | Medicine cost tracking | 3 | 3–5 days | Users tracking health expenses | EV-2 |
| 15 | Windows 11 widget | 4 | 1–2 weeks | Desktop-centric users | EV-2 |
| 16 | WebDAV backup target (self-hosted) | 4 | 1 week | Privacy-focused, NAS owners | EV-2 |
| **17** | C.3++ Phase 2 — native cloud providers **[DONE]** | 4 | [UNCERTAIN] | Mobile companion users | EV-1 |
| 18 | Command palette (`Ctrl+K`) | 4 | 3–5 days | Power users | EV-2 |
| 19 | Command-line interface | 4 | 1–2 weeks | Power users, administrators | EV-2 |
| 20 | C.1 — End-to-end encrypted sync service **[reduced to optional relay]** | 4 | Months + running cost | Multi-device users | EV-1 |

Tiers:

- **Tier 1** — addresses a daily need of the core audience, or removes
  a frequent source of error.
- **Tier 2** — clear value for most users, but either less frequent or
  much more expensive.
- **Tier 3** — valuable for a specific segment.
- **Tier 4** — convenience, niche audience, or infrastructure whose
  user value only materialises through another item.

Source: EV-1 = `EVOLUZIONI-1.md`, EV-2 = `EVOLUZIONI-2.md`.

---

## 3. Tier 1 — high value for the core audience

### 3.1 Package expiry tracking

**Goal.** Warn the user before a package in stock reaches its expiry
date, separately from the low-stock reminder.

**Proposal.** Record an optional expiry date when a new package is
added (`StockMovementKind` new-package movement). Notify when a
package approaches its expiry date, through the existing notification
channels.

**Why first.** Every household keeps medicines past their expiry
date; the reminder is useful to every profile, including those with
no chronic therapy.

**Notes.**

- Stock is currently a single quantity per medicine. Knowing *which*
  package expires requires tracking packages (or lots) individually
  and a consumption rule (oldest first). This is a domain change, not
  a field addition, so the draft's "low, 1–2 weeks" is optimistic
  [INFERRED]. A simpler first step: store the earliest expiry date per
  medicine and ask the user to update it when a package is finished.
- Schema change needs an idempotent boot patch (no `EnsureCreated()`)
  and export format coverage (`docs/EXPORT-FORMAT.md`).

### 3.2 Accessible "large text" mode **[DONE]**

Shipped in PR #108 (v2.9.1): per-profile text size (Normal / Large /
Extra large), display-DPI scaling and high-contrast colours.

**Goal.** Make the WinForms UI usable by elderly and low-vision users,
the main audience for chronic-therapy management.

**Proposal.** A setting that enlarges fonts in grids and dialogs,
increases button size and contrast, and simplifies navigation.

**Follow-up.** The UI modernisation (v2.12.0) added a per-profile
appearance (Same as Windows, Light, Dark), larger default buttons and
inline validation errors (`EVOLUTION-DONE.md` §12.4).

**Notes.**

- Before adding a custom mode, verify how the current forms behave
  with Windows display scaling and the Windows high-contrast themes;
  part of the need may be met by honouring system settings
  [UNCERTAIN].
- Fixed-pixel layouts in the dialogs (explicit `Location` points)
  will need rework to scale cleanly [INFERRED].
- Every new UI string goes into all five
  `assets/localization/strings.<lang>.json` files.

### 3.3 A2 — AIC / barcode scan **[DONE]**

**Goal.** Faster, error-free entry of a new medicine by scanning the
package barcode.

**Status.** Already the next item in the decided sequence
(`EVOLUTION.md` §2.0, §3.2). Approved design:
`docs/analysis/ANALYSIS-A2-BARCODE-SCAN.md`, covering both the USB
HID-scanner and the webcam variant.

**Notes.** Value is highest for users in Italy, where the AIC code
maps to the local catalogue. Other countries depend on catalogue
coverage (`ANALYSIS-DRUG-CATALOGUE.md`).

### 3.4 Prescription request draft for the doctor **[DONE]**

**Goal.** When a medicine is running low, help the user ask the GP for
a new prescription.

**Proposal.** Generate a pre-filled message (medicine name, AIC code,
profile name) that the user can copy, open in the mail client, or send
through the existing MailKit path.

**Why high.** It closes the loop of the app's core purpose
(stock and prescription reminders) at very low cost.

**Notes.**

- Sending must always be an explicit user action; never automatic.
- The message contains health data and a personal name: it must not be
  logged, consistent with `CLAUDE.md` §7.
- The doctor's address is new per-profile personal data and must be
  covered by export/import.

### 3.5 Weekly pill-organizer preparation

**Goal.** Support users and caregivers who fill a weekly pill
organizer once a week instead of taking from the package every day.

**Proposal.** A preparation view that computes the doses for the next
seven days per medicine (respecting A1 regimens and suspensions),
shows the quantities to take from each package, and records the stock
decrease in one operation.

**Notes.** The single stock movement must not double-count with doses
later recorded through A5 dose reminders; the interaction needs a
design decision before implementation [INFERRED].

---

## 4. Tier 2 — broad value, lower frequency or higher cost

### 4.1 Guided stock count and reconciliation **[DONE]**

**Goal.** Keep the forecast accurate when the physical count drifts
from the recorded stock.

**Proposal.** A dialog where the user enters the counted quantity; the
app shows the gap against the expected stock and records the
correction.

**Notes.**

- Correction movements already exist (`StockMovementKind`,
  `StockAdjustmentDialog`). The new part is the guided count and the
  gap display, which lowers the effort.
- Present the gap as a **stock** discrepancy, not as a dose-adherence
  score: adherence tracking is excluded by `EVOLUTION.md` §9.2.

### 4.2 B.1 — Mobile companion (.NET MAUI)

**Goal.** Consult stock and receive reminders on the phone.

**Status.** Documented in `EVOLUTION.md` §7; second in the decided
sequence. Precondition (C.3+) is met. Data moves between PC and phone
through `.mrz` archives.

**Why not Tier 1.** High value, but 2–4 person-months, and without
live sync the phone and PC diverge between manual transfers.

**Update 2026-10-01.** B.1 was approved with mandatory sync
(`ANALYSIS-B1-MOBILE-SYNC.md`): the desktop side (encrypted sync over a
folder, OneDrive or Google Drive) shipped in v2.7.0–v2.9.0, and the
household of devices in v2.12.0. The phone client (Phases 5–7, with
household step H6) is open and waits for the Android spikes S1–S4
(draft PR #106).

### 4.3 Therapy calendar / timeline view **[DONE]**

**Goal.** See at a glance what the table view cannot show: planned
suspensions, estimated run-out dates, dosage changes over time.

**Proposal.** A calendar or timeline view fed by
`MedicationSuspension`, the stock forecast and
`MedicationScheduleHistory`.

### 4.4 Printable medication card (PDF) **[DONE]**

**Goal.** A compact list of active medicines and daily dosages to hand
to a GP, emergency room or pharmacist.

**Notes.**

- The draft's QR code carrying the therapy as "anonymised JSON" is not
  anonymous in practice: the medicine list is health data and a printed
  QR is readable by anyone. Recommend a plain printed list, with the QR
  as an explicit opt-in or dropped [INFERRED].
- Requires a PDF generation library; licence must be checked and added
  to `THIRD-PARTY-NOTICES.md`.

**Outcome (PR #107).** Printed as a paginated table and saved as PDF
through the built-in "Microsoft Print to PDF" printer, so no PDF
library was added. Notes are opt-in. The QR code was not implemented.

---

## 5. Tier 3 — valuable for a specific segment

### 5.1 Storage location per medicine

Optional free-text location ("bathroom cabinet", "fridge") with search
and filter. Very low cost; useful in households with many medicines or
several profiles.

### 5.2 Shared household stock

**Goal.** One stock for a medicine used by several profiles (for
example an over-the-counter painkiller).

**Notes.** Each profile has its own database
(`profiles\<id>\medreminder.db`). A shared stock needs a store outside
the per-profile databases and a rule for concurrent updates.
The household of devices (v2.12.0) shares profiles and installation
settings between PCs, not the stock of one medicine between profiles;
its household store and operation log are a possible home for a shared
stock [INFERRED]. This
contradicts the multi-user isolation baseline (`ANALYSIS-MULTI-USER.md`)
and is far more than the draft suggests; the effort above is an
estimate [INFERRED]. Needs an analysis document before any work.

### 5.3 Database encryption at rest

**Goal.** Protect the SQLite file from other local accounts on a
shared PC.

**Proposal.** Optional SQLCipher encryption, key derived from a
passphrase or protected with DPAPI / Windows Hello.

**Notes.**

- Interacts with backup/restore, export/import and the profile PIN;
  key loss means data loss.
- The .NET packaging of SQLCipher for EF Core and its licensing must be
  verified before committing to it [UNCERTAIN].
- On a PC where each person has their own Windows account, the file is
  already under that user's `%LOCALAPPDATA%`, which limits the benefit
  to shared-account or compromised-account scenarios [INFERRED].

### 5.4 Spoken dose reminder (text-to-speech)

Read the A5 dose reminder aloud. Useful for users with reading
difficulties.

**Notes.** Speaking the medicine name exposes health data to anyone in
the room: must be opt-in per profile. `System.Speech` is Windows-only
and belongs in Infrastructure behind an Application port.

### 5.5 Medicine cost tracking

Optional cost per package on new-package movements, with monthly and
yearly totals per profile.

**Notes.** For the Italian tax deduction the pharmacy receipt is the
supporting document; the report is only an aid and must not be
presented as fiscal documentation [INFERRED].

---

## 6. Tier 4 — convenience, niche, or enabling infrastructure

### 6.1 Windows 11 widget

Show low-stock and next-dose information in the Windows widgets board.

**Notes.** Widget providers are expected to require package identity
(MSIX); the current distribution is not MSIX (`docs/PACKAGING.md`)
[UNCERTAIN]. Windows 11 only.

### 6.2 WebDAV backup target (self-hosted)

An `IArchiveStorage` backend for Nextcloud, Synology or other WebDAV
servers. Fits C.3++ Phase 3, which already lists Nextcloud
(`EVOLUTION.md` §6). Note that many of these servers also ship a
desktop sync client, which already works with the C.3+ synced-folder
model.

### 6.3 C.3++ Phase 2 — native cloud providers **[DONE]**

Shipped for OneDrive and Google Drive inside B.1 Phase 4 (v2.8.0),
for both cloud backups and sync (`EVOLUTION-DONE.md` §6). Dropbox and
the enterprise providers remain C.3++ Phase 3, optional
(`EVOLUTION.md` §6).

OneDrive, Google Drive, Dropbox adapters behind `IArchiveStorage`.
Decided: do not start before B.1 creates a concrete requirement
(`EVOLUTION.md` §6). User value on desktop is marginal while the
synced-folder model works.

### 6.4 Command palette (`Ctrl+K`)

Keyboard-driven actions ("record intake", "add stock", "switch
profile"). Low cost, but benefits a small share of the target audience.

### 6.5 Command-line interface

Scripted backup and export for power users and administrators.

**Notes.** Must respect the single-instance mutex and must not open the
SQLite database while the UI holds it (`CLAUDE.md` §7). Headless
unlock of PIN-protected profiles and passphrase handling need design.

### 6.6 C.1 — End-to-end encrypted sync service

Zero-knowledge sync with a dedicated backend. Documented in
`EVOLUTION.md` §8. Turns the app into a service with operating cost and
GDPR obligations; decided not to evaluate before B.1.

**Update 2026-10-01.** B.1 reaches end-to-end encrypted sync without a
backend, through the user's own storage; C.1 is reduced to an optional
hosted relay (`EVOLUTION.md` §2.0, §8).

---

## 7. Relation to the decided sequence

The decided sequence in `EVOLUTION.md` §2.0 (A2 → B.1 → C.3++ Phase 2
→ C.1 at the time of writing; B.1 mobile phases → C.1 relay as of
2026-10-01) is unaffected by this note. If the product owner adopts this
ranking, the Tier 1 items that are new (3.1, 3.2, 3.4, 3.5) are
independent of the multi-device track and can be inserted before or
after A2 without breaking any precondition.

---

## 8. Status on 2026-10-01 (v2.12.0)

| # | Proposal | Status |
| --- | --- | --- |
| 1 | Package expiry tracking | Not started |
| 2 | Large text mode | Shipped (#108, v2.9.1); appearance and dark mode added in v2.12.0 |
| 3 | A2 — barcode scan | Shipped (#72, #110, #111; v2.5.2, v2.10.0) |
| 4 | Prescription request draft | Shipped (#74) |
| 5 | Weekly pill-organizer preparation | Not started; interaction with A5 needs a decision |
| 6 | Guided stock count | Shipped (#73) |
| 7 | B.1 — mobile companion | Desktop side shipped (v2.7.0–v2.9.0, household v2.12.0); mobile phases 5–7 open |
| 8 | Therapy timeline | Shipped (#75) |
| 9 | Printable medication card | Shipped (#107, v2.9.1) |
| 10–16 | Storage location, shared household stock, encryption at rest, text-to-speech, cost tracking, Windows 11 widget, WebDAV target | Not started |
| 17 | C.3++ Phase 2 | Shipped inside B.1 Phase 4 (v2.8.0) |
| 18–19 | Command palette, CLI | Not started |
| 20 | C.1 | Reduced to an optional hosted relay; not started |

Shipped in the meantime without a rank here: the remote catalogue
feeds (v2.11.0), the household of devices with a master device and
the UI modernisation (v2.12.0); see `EVOLUTION-DONE.md` §12.
