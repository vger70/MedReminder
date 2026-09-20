# EVOLUTION — Prospective work beyond Increment 15

Working document listing candidate evolutions of MedReminder, prepared
in September 2026. It records options, tradeoffs and rejection
rationale so that future sessions and future maintainers do not
re-derive them from scratch. **It is not a commitment.** Individual
items become work only once explicitly approved and turned into a
dedicated analysis document (see `ANALYSIS-MULTI-USER.md` for the
pattern) or a GitHub issue.

The two open non-goals from `ANALYSIS-MULTI-USER.md` §16 (profile
promote/demote and the consolidated admin view) remain deferred and
are **not** covered here — this document looks past the multi-user
baseline that landed with PRs #22–#26.

---

## 0. Reading conventions

Where a claim in this document could be wrong or is not verified
against a primary source, it is tagged:

- **[VERIFIED]** — established, publicly traceable knowledge.
- **[INFERRED]** — logical deduction from verified facts, flagged as
  inference.
- **[UNCERTAIN]** — no sufficient data at the time of writing; treat as
  a hypothesis, not a plan input.
- **[DONE]** - alredy shipped.

Untagged claims are ordinary design opinion.

---

## 1. Scope

Included:

- **Group A** — low-friction extensions of the existing app.
- **Group C** — options for multi-device data movement
  (export/import, personal-cloud sync, end-to-end encrypted sync).
- **Item B.1** — mobile companion client.

Deliberately excluded from this document:

- **Group D** — integrations with national health-system services
  (electronic prescription, Fascicolo Sanitario Elettronico, pharmacy
  reservation). Access to these APIs by third-party consumer apps is
  today uncertain and region-dependent [UNCERTAIN]. When the picture
  stabilizes, they will be picked up in a dedicated analysis
  document, not here.
- **Dose-adherence tracking, clinical alerts, drug-interaction
  checks.** These would move MedReminder into the medical-device
  category (EU MDR 2017/745) and are outside the current product
  positioning stated in `CLAUDE.md` §1. Reopening them requires an
  explicit decision to become a CE-marked medical-device software.

---

## 2. Priority ordering

Recommended by this document, in ascending ambition and cost:

1. **Group A** — each item independently deliverable. Start here.
2. **C.3** — manual export/import (GDPR portability + migration).
3. **C.3+** — automatic backup to a user-controlled cloud folder with
   explicit restore on a second device.
4. **B.1** — mobile companion client that consumes C.3+.
5. **C.1** — end-to-end encrypted sync with a dedicated backend.
   Only if the product accepts becoming a service.

**C.2** (raw file-sync of the live SQLite database) is documented but
**rejected** on technical grounds — see §7.

The ordering across groups is not a Gantt chart. Group A items can
proceed in any order relative to each other, and C.3 can ship before
A completes. The rule is: **do not skip C.3 → C.3+ before attempting
B.1, and do not skip B.1 before deciding on C.1**.

**Inside Group A**, the recommended order — by ascending cost, with
dependencies respected — is:

1. **A6 — donation/support UI.** 3–5 days. No dependencies on any
   other item, no schema patch, no domain or monitor changes; pure
   UI + configuration. Ship first as a low-risk baseline that also
   opens a voluntary funding channel for the maintenance costs the
   later items will accrue. Indirectly de-risks §7 (C.1) by
   exercising monetization plumbing before committing to a service.
2. **A2 — AIC / barcode scan.** 1–2 weeks. Isolated, high
   user-visible value, no cross-item preconditions. Good pairing
   with A6 in the same release.
3. **A3 — caregiver notifications.** 1 week. Reuses the existing
   MailKit transport and per-profile notification settings; no
   dependency on A1.
4. **A1 — complex therapy regimens.** 2–3 weeks. Highest single-item
   value of Group A, but also the largest and the one that must
   land before A5 can be attempted. [DONE]
5. **A5 — dose-time reminder.** 1–1.5 weeks. Hard-depends on A1
   for a reliable wall-clock anchor on every therapy — without
   stabilized dose times the reminder has no time to fire on.

Two hard dependencies to respect inside Group A: **A5 must not ship
before A1** (as above), and everything else in Group A is free of
cross-item preconditions — reorder freely if funding, contributor
availability or user demand suggests it.

---

## 3. Group A — low-friction extensions

Common properties: each item is small, self-contained, does not
change the app's architectural posture (still local-first, single
Windows binary, not a medical device). Effort estimates are
grossly indicative and assume one developer familiar with the
codebase.

### 3.1 A1 — Complex therapy regimens [DONE]

**Motivation.** The current model appears to assume linear
consumption (X units per day). Real regimens include cycles
(7 days on, 7 days off), tapering doses (decreasing over weeks) and
as-needed (PRN) doses. Without support for these, the "days
remaining" projection is wrong for the very users who most need a
reminder.

**Design sketch.**

- Extend the therapy entity in `MedReminder.Domain` with a
  `Schedule` value object able to represent:
  - Fixed daily quantity (existing behavior).
  - Weekly pattern (7-day mask + per-day quantity).
  - Cyclic pattern (N days on / M days off).
  - Tapering (start dose, end dose, step interval).
  - PRN (no scheduled consumption, only stock tracking; the
    projection engine falls back to "user-declared expected rate").
- Update the projection engine in `MedReminder.Application` to sum
  over the schedule instead of multiplying by a constant.
- Additive schema patch, idempotent on boot per `ANALYSIS.md` §2.8.

**Effort.** 2–3 weeks including UI, tests and localization. [INFERRED]

**Risks.** UI complexity — the therapy form is already dense.
Consider a "simple / advanced" toggle so that the linear case stays
one-click.

**Verdict.** Highest-value item in Group A. The current linear
model is the most frequently cited limitation of reminder apps of
this class.

### 3.2 A2 — AIC / barcode scan of medicine package

**Motivation.** Reduce data-entry errors and friction when adding a
medicine. The Italian AIC code (Autorizzazione all'Immissione in
Commercio) is printed as a barcode on every package sold in Italy
and uniquely identifies the medicinal product.

**Design sketch.**

- On desktop: use the PC camera or a handheld USB barcode scanner
  as HID keyboard input.
- On the mobile companion (see §6): the phone camera is the
  natural scanner; `ZXing.Net.Maui` or equivalent handles the
  decode.
- Once the AIC is captured, look it up in the local reference
  catalogue (see `ANALYSIS-DRUG-CATALOGUE.md` — the mechanism
  already exists) and populate the medicine record.

**Effort.** 1–2 weeks for the desktop path. Mobile path lands
together with §6. [INFERRED]

**Risks.** Camera access adds a new permission surface on Windows;
handheld scanners are the safer default for the desktop MVP.

**Verdict.** Small, isolated, high user-visible value. Good
candidate to ship alongside A1.

### 3.3 A3 — Caregiver notifications

**Motivation.** The elderly are frequently the real end-user of a
medication reminder, but a family member or paid caregiver often
manages the actual reorder. The multi-user work of Increment 15
covers "several patients on one PC", but not "notification reaches
the caregiver's inbox".

**Design sketch.**

- Per profile, allow a second email recipient (`CaregiverAddress`)
  in `notifications.settings.json`.
- When stock crosses the low-threshold or a tolerated-delay
  threshold, send the same notification to that address too.
- MailKit path is already in place; no new transport.
- Optional refinement: independent per-event opt-in (low stock
  yes, generic reminder no).

**Effort.** 1 week. [INFERRED]

**Risks.** Sending medical-relevant information to a second
recipient must be an explicit user choice — the UI copy has to
make that clear. Do not enable by default. Nothing in the mail
body reveals sensitive medical details beyond what the primary
user has already accepted (see `CLAUDE.md` §6).

**Verdict.** Low cost, high real-world usefulness. Ship after A1.

### 3.4 A4 — Data export/import (see §4 — moved to C.3)

Originally sketched as an A-group item, but the mechanism is the
foundation of the C.3 track. It is described in §4 rather than
duplicated here.

### 3.5 A5 — Dose-time "remind me to take it" notification

**Motivation.** The current notification path only fires around
stock exhaustion (low-stock / tolerated-delay / reorder). Some
users forget the *dose*, not the *reorder*. A toast (and optional
email) delivered at the scheduled time of each dose closes the
gap without changing the product's non-clinical positioning.

**Preconditions — already in place.** The A1 groundwork has
partially landed: `AdministrationSlotEntry` already carries an
optional `Time` (`TimeOnly?`), used today for sorting and
report rendering (see
`src/MedReminder.Application/Notifications/NotificationTexts.cs`
and `src/MedReminder.Application/Reporting/TherapyReport.cs`).
The `Schedule` value object and `SchedulePanel` UI likewise exist
(see `src/MedReminder.UI/Forms/MedicineEditDialog.cs`). This
feature therefore *extends* existing plumbing — no new schedule
model is required. [VERIFIED against the current tree at the
time of writing]

**Design sketch.**

- **Data.** Add a per-medicine boolean `RemindOnDose` (default
  `false`) alongside the existing per-medicine notification-channel
  flags. Additive, idempotent schema patch per `ANALYSIS.md` §2.8.
  Slot-level granularity (a flag per slot) is *not* recommended for
  the first cut — it doubles the UI complexity for marginal value;
  reopen only if users ask.
- **UI (`MedicineEditDialog`).** Add a checkbox
  `Ui.MedicineEditDialog.Field.RemindOnDose` next to the existing
  `_channelWindows` / `_channelEmail` checkboxes. The checkbox is
  **enabled only** when both conditions hold at the moment the
  dialog is opened or when they change during editing:
  - The medicine is *active in the therapy* (has an active
    schedule / has slots with a `Time` value — an entry with no
    time cannot be a dose-time anchor and grays the checkbox out
    with a tooltip explaining why).
  - Current on-hand stock is `> 0`.
  When either condition fails, the checkbox is cleared and
  disabled, and a short helper label states the reason
  (localized). Saving the medicine while disabled forces the flag
  back to `false` — never persist an "armed" reminder that has no
  chance of firing.
- **Runtime.** Extend `MedicationMonitor` (or split a sibling
  `DoseReminderService`) that, on each minute tick:
  1. Enumerates medicines with `RemindOnDose = true` and
     `Stock > 0` whose therapy is active.
  2. For each slot with a `Time`, computes the next local
     wall-clock firing today.
  3. Emits one notification per (medicine, slot, day) at the
     configured time, deduplicated via the existing
     `MedicationScheduleHistory` table pattern so that a
     restart within the same minute does not double-fire.
- **Channels.** Reuse the existing toast (WinRT) and MailKit paths.
  The per-medicine `_channelWindows` / `_channelEmail` flags
  already control channel selection and are respected as-is.
- **Localization.** Add the new key to every dictionary under
  `assets/localization/` (en, it, fr, es, de) per `CLAUDE.md` §8.

**Effort.** 1–1.5 weeks including schema patch, UI wiring,
scheduler extension, deduplication test, five-language
localization and shipped-user-guide updates. [INFERRED]

**Risks and constraints.**

- **Medical-device drift — the real risk.** A "did you take the
  08:00 dose?" acknowledgement, retention of missed-dose events,
  or any alert on missed doses would push the app into EU MDR
  2017/745 adherence-tracking territory (see §8.2). This item is
  scoped strictly to *emit a reminder*: no ack UI, no missed-dose
  logging, no clinical alert wording. The disclaimer copy in
  `CLAUDE.md` §1 remains sufficient only as long as this line
  holds. [INFERRED — MDR classification depends on the declared
  intended use]
- **Local time / DST.** Slot `Time` values are wall-clock. The
  scheduler must anchor on local time, not UTC, and behave
  documented-ly on DST transitions (skip the missing hour,
  fire once on the repeated hour). Document in the user guide.
- **Silent hours.** A 06:00 dose fires at 06:00. Consider a
  per-profile quiet-hours window as an optional refinement, not a
  first-cut requirement.
- **Email as a dose channel is fragile.** Delivery latency
  defeats the point of a punctual dose reminder. Toast is the
  primary channel; email should be an opt-in fallback for
  scenarios where the PC is on but unattended.
- **Interaction with the existing low-stock reminder.** The two
  notification paths must not compete on the same slot tick.
  Keep them independent code-paths but share the deduplication
  history table so a "reorder soon" and a "take now" for the
  same medicine at the same minute do not stack into two toasts.
- **Stock = 0 is a "hard off".** When stock drops to zero mid-day,
  in-flight reminders for the rest of the day must stop. The
  scheduler evaluates the gate on every tick, not once at the
  start of day.

**Verdict.** Small, isolated, high user-visible value; sits
cleanly on top of already-shipped groundwork; must be scoped
narrowly to stay non-clinical. Ship after A1 stabilizes (dose
times must be a first-class, always-present part of the
therapy) and before or alongside A3 (caregiver notifications) —
the same channel plumbing carries both.

### 3.6 A6 — Donation / support UI

**Motivation.** MedReminder is distributed free of charge under
Apache-2.0 (see `LICENSE`) and has no monetization channel today.
Perpetual maintenance costs — code signing certificate renewals,
tooling, developer time, and the operational costs any future
service item in this document (C.1 in particular) would incur —
are today absorbed by the maintainer. A voluntary, unobtrusive
"support development" surface inside the app closes that gap
without changing the product's positioning: the app remains free,
local-first, and non-clinical. The full requirement draft lives
in `docs/DONATION-SUPPORT-FEATURE.md`.

**Preconditions — already in place.** WinForms host, per-profile
`user.settings.json` reader, MailKit-independent logging
infrastructure, five-language localization pipeline. No new
NuGet dependency, no schema patch, no domain change.
[VERIFIED against `CLAUDE.md` §§3, 6, 8]

**Design sketch.**

- **Model.** New `DonationOptions` in `MedReminder.Application`
  holding `Enabled`, `Currency`, and a per-provider block with
  `Enabled` + a `PaymentLinks` map `{amount → URL}`. Only public
  Payment Link URLs; no client secret, no access token, no API
  key of any kind lives in the client — `CLAUDE.md` §9 already
  forbids plaintext secrets, and this feature must not weaken
  that stance.
- **Storage.** A new admin-managed
  `%LOCALAPPDATA%\MedReminder\donations.settings.json` alongside
  the other shared JSON files listed in `CLAUDE.md` §6. Public
  URLs only — do not route through `smtp.protected` or any
  DPAPI-encrypted store; there is nothing sensitive to protect.
- **Provider abstraction.** `IDonationProvider` port in
  `MedReminder.Application`, with `StripeDonationProvider` and
  `PayPalDonationProvider` adapters in
  `MedReminder.Infrastructure`. The port returns a
  `DonationLaunchResult` describing the URL to open and the
  provider that produced it. The abstraction is intentionally
  designed so that a later backend (see §7 — C.1) can swap the
  "open a hosted URL" adapter for a "call our checkout API +
  verify via webhook" adapter without touching the UI or the
  service layer.
- **Amount tiers.** €2 / €5 / €10 / €20 as fixed Payment Links
  (one URL per amount per provider). A custom-amount field is
  offered **only** where the provider natively supports amount
  selection on a public hosted page without client credentials.
  [UNCERTAIN — Stripe Payment Links today configure the amount
  server-side per link; PayPal's hosted donate flow can accept
  an `amount` query parameter on the classic donate button. Both
  behaviors depend on the account configuration used to mint the
  link and must be verified at implementation time against the
  provider's current documentation, not memory.]
- **UI.** A dedicated `DonateForm` reachable from a single Help
  menu entry — `Ui.MenuHelp.SupportDevelopment` — never from a
  startup prompt. Voluntary tone; explicit "not required to use
  the application" copy. Localize the new strings across en / it
  / fr / es / de per `CLAUDE.md` §8. No `WebBrowser` control, no
  embedded browser: the URL opens in the system default browser
  via `Process.Start` with `UseShellExecute = true`.
- **Validation before launch.** The service must reject non-HTTPS
  URLs, syntactically malformed URLs, disabled providers, unknown
  amounts, and missing configuration. User-facing errors stay
  short and non-technical; the real reason lands only in the
  daily rolling log under `logs/`.
- **Post-launch messaging.** The app must not claim the payment
  succeeded. The confirmation copy is strictly of the form "a
  payment page has been opened in your browser". No webhook, no
  order capture, no URL inspection, no browser scraping. This is
  a hard product-safety line, not a nice-to-have.
- **Logging.** Reuse the existing logger. Log provider, amount,
  launch attempt and browser-launch errors. Never log the URL's
  query string beyond the amount tier, never log payment
  metadata, never log card data (the app does not see it).

**Effort.** 3–5 developer-days including the provider port, the
two adapters, the WinForms form, HTTPS validation, error paths,
unit tests (per §Testing in the requirement draft), five-language
localization and shipped-user-guide updates. [INFERRED]

**Risks and constraints.**

- **False confirmation is the real risk.** Without a backend the
  desktop cannot know whether the payment completed. Any UI
  wording that implies otherwise is a defect, not a polish item.
  The requirement draft is explicit on this point and this
  document endorses it.
- **Secrets must not enter the client.** Payment Link URLs are
  public identifiers, not credentials, and are the only piece of
  provider configuration allowed in the shipped artifact. Any
  drift here undoes the security posture the rest of the app
  enforces (`CLAUDE.md` §9).
- **Nagware would sink adoption.** No dialog on launch, no
  countdown, no periodic prompt. The entry point is a single Help
  menu item; that is the entire surface.
- **Store-channel policy.** If the app is ever redistributed
  through the Microsoft Store, the store's rules on third-party
  payments outside its own commerce system may restrict or
  forbid this UI. [UNCERTAIN — Store policy varies by app
  category and evolves] The primary distribution channels today
  are the ZIP and MSI produced by the release workflow (see
  `docs/PACKAGING.md`), so this is not a blocker but a note for
  a future MSIX push.
- **Regional payment failure.** Not every user's card / PayPal
  region will accept every Payment Link. The graceful
  degradation is the browser's own error page; do not attempt to
  detect or handle it inside the app.
- **Not a medical-device concern.** The donation surface is
  strictly commercial UX — it does not touch therapy data, does
  not read stock, does not read schedules. It stays well clear
  of EU MDR 2017/745 (§8.2).

**Verdict.** The smallest and most self-contained item in Group A.
No dependencies on A1..A5, no schema patch, no monitor changes;
purely UI + configuration + a provider port. Ship first inside
Group A as a low-risk baseline that also opens a voluntary
funding channel for the maintenance costs the later items in
this document will accrue.

---

## 4. C.3 — Manual export/import

### 4.1 Motivation

Two distinct concerns solved by one mechanism:

- **GDPR art. 20** — the user has a right to receive their data in
  a structured, commonly used, machine-readable format.
- **Device migration** — moving the app from an old PC to a new one
  today relies on manually copying `%LOCALAPPDATA%\MedReminder\`.
  That is possible for a technical user, unfriendly to everyone
  else.

### 4.2 Design sketch

- Command **Export all data** in Settings → File.
- Output: an encrypted zip archive containing a documented JSON
  structure covering all entities of the current profile (or all
  profiles, admin-only variant), plus a schema-version header.
- Encryption: passphrase chosen by the user, keyed via Argon2id,
  AES-GCM for the payload. Do **not** reuse DPAPI here — DPAPI is
  bound to the Windows account and defeats the migration use case.
- Command **Import from export** with two modes:
  - **Overwrite** (replace the profile's DB with the imported
    content).
  - **Merge** (deferred — merge rules are non-trivial; see §4.4).
- Format documented publicly under `docs/EXPORT-FORMAT.md` (to be
  written when the feature lands), so a user can migrate away to
  another tool without lock-in.

### 4.3 Effort

2–3 weeks including UI, tests, format documentation and a
round-trip integration test. [INFERRED]

### 4.4 Risks and constraints

- **Merge rules are dangerous** and should not be shipped without
  an explicit design pass. Ship overwrite first, defer merge.
- **Schema evolution.** The exported JSON must carry a schema
  version so that a future MedReminder can read a today-exported
  file. Adopt the same additive-only discipline as the SQLite
  patches (`ANALYSIS.md` §2.8).
- **Do not include the SMTP password** in the export unless the
  user explicitly checks a box for it. If included, it must be
  re-encrypted with the export passphrase, not DPAPI.

### 4.5 Verdict

Ship early. It is the smallest, safest and most portable move on
the multi-device track, and GDPR portability is a compliance win
regardless of what happens after.

---

## 5. C.3+ — Backup to user-controlled cloud folder + explicit restore

### 5.1 Motivation

An honest, low-cost approximation of multi-device operation without
building a backend. The automatic-backup mechanism already exists
(`backup.settings.json`, `backup.state.json` — see `CLAUDE.md` §6);
this evolution reuses it.

### 5.2 Design sketch

- Extend the automatic-backup destination to accept **any local
  folder**, including one that is synchronized by the user's cloud
  provider (OneDrive, iCloud Drive, Dropbox, Google Drive Desktop).
- The app writes atomic snapshots (never the live DB — see §7 on
  why file-sync of the live DB corrupts SQLite).
- On a second device, an explicit **Restore from backup** action
  reads the most recent snapshot and replaces the local profile.
- User model: **single-writer, multiple-reader-on-demand**. There
  is no concurrent editing — the user consciously switches which
  device is "active".

### 5.3 Effort

1–2 weeks on top of C.3 and the existing backup mechanism. [INFERRED]

### 5.4 Risks and constraints

- **Not sync.** The UX has to make that explicit — this is
  assisted migration, not real-time synchronization. Two devices
  editing between two syncs diverge silently, and the last restore
  wins.
- **Cloud folder detection is fragile.** The app must not assume
  where OneDrive/iCloud is installed; let the user pick a folder.
- **The snapshot must be encrypted** with the same mechanism as
  C.3. Never write a plaintext DB to a cloud-synced folder.

### 5.5 Verdict

Best-value compromise for the ~90% of users who want "reminder on
my phone that reflects yesterday's changes on my PC". Ship after
C.3.

---

## 6. B.1 — Mobile companion client

### 6.1 Portable code already available

The projects `MedReminder.Domain` and `MedReminder.Application`
target `net10.0` with no Windows-specific dependencies. They are
reusable from a mobile host without modification. [VERIFIED against
`CLAUDE.md` §3]

### 6.2 What must be re-implemented per platform

`MedReminder.Infrastructure` targets `net10.0-windows` and hosts
platform-bound adapters:

| Current desktop adapter | Mobile substitute |
|---|---|
| DPAPI (`smtp.protected`) | iOS Keychain / Android Keystore via MAUI `SecureStorage` |
| Toast (WinRT) | Local notifications (MAUI `LocalNotification`) or push (APNs/FCM) |
| Tray icon | Not applicable |
| Backup on filesystem | Sandboxed app storage (`FileSystem.AppDataDirectory`) |
| Single-instance mutex | Not applicable — the OS enforces this |

EF Core with `Microsoft.EntityFrameworkCore.Sqlite` runs natively on
iOS and Android with no changes. MailKit runs on both.

### 6.3 UI framework choice

- **.NET MAUI** — Microsoft-official, XAML, iOS/Android/Windows/
  macOS. Best integration with the .NET ecosystem. Stability
  history through 2024 was mixed [INFERRED]; 2026 status not
  independently verified [UNCERTAIN].
- **Avalonia** — mobile production-ready from 11.x, strong desktop
  tradition. Smaller mobile ecosystem than MAUI.
- **Uno Platform** — WinUI 3 XAML across platforms, including web.
  Less widespread.

Recommended default: **MAUI**, on ecosystem grounds (documentation,
Visual Studio integration, sample density). Avalonia becomes the
better choice only if desktop Linux is also a target.

### 6.4 Effort

- Rough MVP (browse medicines, add/edit, low-stock notification,
  settings) with no sync: **2–4 developer-months**. [INFERRED —
  strongly dependent on developer experience with MAUI]
- Mobile UI is a **redesign**, not a port of the WinForms UI.
  Every screen must be re-thought for touch, scroll, stack
  navigation and system notifications.

### 6.5 Distribution

- **iOS**: Apple Developer Program ~99 USD/year; App Store review
  required; TestFlight for beta.
- **Android**: Google Play 25 USD one-off; more permissive review;
  APK sideload allowed.

Neither store rejects "medication reminder" apps as long as no
clinical claims are made — the existing disclaimer wording
(`CLAUDE.md` §1) is sufficient.

### 6.6 Localization

Reuses `assets/localization/strings.<lang>.json` unchanged if the
loader is a service exposed via a port from
`MedReminder.Application`. If it currently lives in
`MedReminder.UI` (WinForms-side), promote it to Application first.
This move should be part of the B.1 preparation, not a duplicate
translation effort.

### 6.7 Precondition

Do **not** ship B.1 without at least C.3+ in place. A mobile
client whose data does not connect to the desktop's data is a new
app, not a companion.

### 6.8 Verdict

Meaningful only when paired with C.3+ or C.1. As a standalone
effort, it is a large investment (months) for uncertain reward.

---

## 7. C.1 — End-to-end encrypted sync with dedicated backend

### 7.1 Rejection of C.2 (raw file-sync of the live SQLite DB)

Documented here rather than as a separate item because C.2 is a
tempting-looking non-option: it should be ruled out before C.1 is
considered.

- SQLite explicitly recommends **not** placing a live database on
  file-sync services (Dropbox, OneDrive, iCloud). The database
  file, its WAL sidecar and its SHM sidecar must land at the same
  logical time; sync services do not guarantee that ordering.
  [VERIFIED — SQLite FAQ, standing recommendation]
- Locking is filesystem-local. Two devices holding the same synced
  file cannot see each other's locks — concurrent writes produce
  data loss or corruption.
- File-sync conflict resolution produces `medreminder (conflicted
  copy).db`, which is meaningless to the average user.

C.2 is therefore rejected. Its only defensible variant is
**backup-of-a-snapshot on a cloud folder**, which is C.3+ (§5) —
not sync.

### 7.2 C.1 motivation

The only option that both scales to real multi-device operation
and respects the sensitivity of medical data. Its cost is the
switch from "distribute a binary" to "operate a service".

### 7.3 Architecture

Zero-knowledge model:

1. Master key derived from the user's passphrase via **Argon2id**
   (OWASP current recommendation as of writing). [VERIFIED at
   publication time]
2. Per-record symmetric key: **ChaCha20-Poly1305** or AES-GCM,
   sourced via **libsodium** (`NSec` is a mature .NET binding).
3. Server stores only: `user_id`, `record_id`,
   `encrypted_blob`, `timestamp`, `operation_type`. No content
   in cleartext.
4. Sync model: **append-only operation log** rather than snapshot
   merge. Each device publishes its own operations, pulls the
   others, replays locally. Simpler and less conflict-prone.

### 7.4 Multi-device bootstrap

Since MedReminder is single-user per account, no Signal-style
device pairing is required. The user re-enters the same passphrase
on the second device → same derived key → the server-stored blobs
decrypt. Force a printable **recovery kit** (mnemonic phrase) at
first-device setup to mitigate passphrase loss.

### 7.5 Backend

- **Stack**: ASP.NET Core (coherent with the rest), Postgres for
  the blob store, email+password auth **separate** from the E2E
  passphrase (the passphrase must never reach the server).
- **Hosting**: EU-based (Hetzner, Fly.io eu-region, DigitalOcean
  Frankfurt) for GDPR default. Raw cost order-of-magnitude
  10–30 EUR/month for a small user base. [INFERRED from 2024–2025
  price lists]
- **Backups**: encrypted blobs only, so off-site backups carry no
  additional privacy risk.

### 7.6 Non-technical cost

The reason C.1 is rated last:

1. **Perpetual operation** — TLS renewals, dependency patches,
   monitoring, incident response, on-call. A running service
   cannot be "shipped and forgotten".
2. **Recoverability** — passphrase loss = data loss. Intrinsic
   to zero-knowledge, not a bug. Requires prominent
   onboarding UX.
3. **Business model change** — from free local app to service
   with recurring cost. Consider **self-host + optional hosted
   service** (the Bitwarden/Vaultwarden and Nextcloud pattern);
   it is the pattern that best preserves the local-first DNA of
   MedReminder.
4. **GDPR posture** — ciphertext blobs tied to identifiable
   `user_id` are still personal data under art. 4 GDPR. If the
   underlying (encrypted) content is health-related, art. 9 still
   applies to the processing regardless of encryption.
   [INFERRED — the cautious legal reading; obtain qualified
   advice before production]

### 7.7 Prior art

- **Bitwarden** — open source, documented key-derivation and
  blob-storage architecture. Recommended primary reference.
- **Standard Notes** — similar zero-knowledge model, well
  documented.
- **Peer-to-cloud** synchronization via OneDrive/GoogleDrive

Study these before designing anything from scratch.

### 7.8 Verdict

Warranted only if the product owner accepts the shift from
"desktop tool" to "small service" and the perpetual cost that
comes with it. Do not enter unless that decision has been made
explicitly.

---

## 8. Explicitly excluded — and why

### 8.1 Group D — national health-system integrations

Kept out of this document because access to Fascicolo Sanitario
Elettronico, electronic prescription systems and pharmacy
reservation APIs by third-party consumer software is today
uncertain, region-fragmented in Italy, and typically mediated by
credentialed professional roles. [UNCERTAIN — status as of writing]
When the picture stabilizes, this becomes its own analysis
document rather than a section here.

### 8.2 Medical-device functions

Adherence tracking ("did you take the 08:00 dose?"), clinical
alerts, drug-interaction checks and dose-safety warnings all fall
under EU MDR 2017/745 depending on their stated intended use.
[INFERRED — MDR classification depends on the declared intended
use]. Adding them turns MedReminder into a medical-device software
requiring CE marking. Not a coding decision — a product-strategy
decision that must precede any implementation attempt.

---

## 9. Change log for this document

- 2026-09-19 — initial draft. Group A, C.3, C.3+, B.1, C.1
  documented. C.2 rejected with rationale. Group D and
  medical-device features explicitly excluded.
- 2026-09-20 — added A5 (dose-time "remind me to take it"
  notification). §2 priority ordering updated with the A1 → A5
  precondition.
- 2026-09-20 — added A6 (donation / support UI) from the draft in
  `docs/DONATION-SUPPORT-FEATURE.md`. Classified as a Group A
  item (low-friction extension, UI + configuration only, no
  backend, no schema patch). §2 priority ordering rewritten with
  an explicit inside-Group-A cost/benefit sequence:
  A6 → A2 → A3 → A1 → A5, with the A1 → A5 dependency preserved.
