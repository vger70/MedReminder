# EVOLUTION-DONE — Shipped evolutions

Companion to `EVOLUTION.md`. This file keeps the items of the
evolution backlog that have shipped, rewritten to describe **what
was actually implemented** rather than the original sketch. The
open backlog stays in `EVOLUTION.md`.

Section numbers are the ones the items had in `EVOLUTION.md` before
the split (2026-09-25). Analysis documents, prompts and PR
descriptions cite `EVOLUTION.md §<n>`; for any number listed below,
read this file instead.

| Former `EVOLUTION.md` section | Item | Shipped in |
|---|---|---|
| §3.1 | A1 — Complex therapy regimens | PR #31, PR #40 |
| §3.2 | A2 — AIC / barcode scan | PR #72 (v2.5.2), PR #110, PR #111 (v2.10.0) |
| §3.3 | A3 — Caregiver notifications | PR #47 |
| §3.4 | A4 — Data export/import | Merged into C.3 (§4) |
| §3.5 | A5 — Dose-time reminder | PR #37 |
| §3.6 | A6 — Donation / support UI | PR #38 |
| §4 | C.3 — Manual encrypted export/import | PR #48, PR #49, PR #52 |
| §5 | C.3+ — Backup to a cloud-synced folder + explicit restore | PR #55, PR #57, PR #58 |
| §6 (Phase 1) | C.3++ — `IArchiveStorage` abstraction | PR #56 |
| §6 (Phase 2) | C.3++ — OneDrive and Google Drive backends | Through B.1 Phase 4a/4b: PRs #91, #93–#95 (v2.8.0) |
| §10 | Public presentation website v1 | `vger70/medreminder-website` PRs #1–#5 |
| — (§12 here) | Shipped outside the backlog: multi-user G, remote catalogue feeds, household with master device, UI modernisation | v2.11.0, v2.12.0 |

Where this file and an analysis document disagree, the analysis
document and the code win; this file is a summary.

---

## 3.1 A1 — Complex therapy regimens

**Status.** Shipped. PR #31 (2026-09-19), stepped tapering in
PR #40 (2026-09-21).

**Authoritative analysis.** `docs/analysis/ANALYSIS-A1-REGIMENS.md`,
`docs/analysis/ANALYSIS-A1-STEPPED-TAPER.md`.

**As implemented.**

- `Schedule` value object in `MedReminder.Domain` with six kinds:
  `FixedDaily`, `Weekly`, `Cyclic`, `Tapering` (linear),
  `Prn` (as needed) and `SteppedTapering` (explicit list of stages,
  optional "keep the last dose as maintenance").
- Persisted through `ScheduleCodec` as `ScheduleKind` +
  `SchedulePayload` on `MedicationScheduleHistory`. Additive,
  idempotent boot patch; pre-A1 rows read back as `FixedDaily`.
  Stepped tapering required no further schema change.
- The projection engine dispatches through `Schedule.RateOn`, so the
  "days remaining" estimate follows the regimen day by day.
- UI: reusable `SchedulePanel` with a Simple / Advanced toggle in the
  new-medicine and change-schedule dialogs, as the original sketch
  suggested.

**Not done / deferred.** Slot × schedule combinations are deferred
(see `ANALYSIS-A1-REGIMENS.md`). No clinical checks of any kind, by
design.

## 3.2 A2 — AIC / barcode scan of medicine package

**Status.** Shipped in three phases: PR #72 (USB HID scanner, v2.5.2),
PR #110 (webcam) and PR #111 (restock by scan), both in v2.10.0.

**Authoritative analysis.** `docs/analysis/ANALYSIS-A2-BARCODE-SCAN.md`
(§5A, §5B, §5C, with "as implemented" notes and change log).

**As implemented.**

- **Parser** (`BarcodeParser`, `ItalianPharmacode`, Application):
  Code 32 (Italian AIC, as Code 39), GS1 DataMatrix and EAN-13, with
  check digits; never logs the payload, which may carry an FMD serial
  number.
- **Scan dialog** (`BarcodeScanDialog`): a USB HID scanner in
  keyboard-wedge mode or a code typed by hand, by default; the webcam
  on request (`WindowsCameraCaptureService` over
  Windows.Media.Capture, decoded with ZXing.Net). The camera is
  released as soon as a code is read, the user switches back, the
  30-second timeout fires or the dialog closes.
- **Flow (a), new medicine**: *Scan barcode…* in the medicine form
  looks the code up in the reference catalogue and fills the form as
  an autocomplete pick does.
- **Flow (b), restock**: *Stock → Restock from barcode…* finds the
  medicine by its AIC code (`RestockByScanQuery`) and opens the
  new-package dialog with the quantity of its last new package. A
  code no medicine carries can be added as a new medicine or linked
  to a medicine without a code, when the catalogue knows it.
- No schema change; expiry and batch are not stored.

**Deviations from the sketch.** The mobile path (phone camera) stays
with B.1 Phase 7. The webcam decode on a real pack was not confirmed
at acceptance: the test webcam's resolution was too low
(`ANALYSIS-A2-BARCODE-SCAN.md` §10.2). A GTIN-only DataMatrix does not
resolve to an AIC; revisit before Code 32 disappears from Italian
packs (§10.2 risk 3).

## 3.3 A3 — Caregiver notifications

**Status.** Shipped. PR #47.

**Authoritative analysis.**
`docs/analysis/ANALYSIS-A3-CAREGIVER-NOTIFICATIONS.md`.

**As implemented.**

- Per-profile `NotificationSettings.CaregiverAddress` in
  `notifications.settings.json`, empty by default (feature off).
- Every email sent to the primary recipient also goes to the
  caregiver, in the same message as a second `To` address. No new
  transport (MailKit), no schema change, no change to the mail body.
- Invalid caregiver address: primary-only delivery and a warning in
  the log without the address. A caregiver equal to the primary is
  rejected at save time.

**Deviation from the sketch.** The optional per-event opt-in
("low stock yes, generic reminder no") was not implemented; the
caregiver receives every email the primary receives. Recorded as
deferred in `ANALYSIS-A3` §11 item 4.

## 3.4 A4 — Data export/import

Never implemented as a Group A item. The mechanism is C.3 (§4).

## 3.5 A5 — Dose-time "remind me to take it" notification

**Status.** Shipped. PR #37 (2026-09-21).

**Authoritative analysis.**
`docs/analysis/ANALYSIS-A5-DOSE-TIME-REMINDER.md`. The two
"Correction" notes that the former `EVOLUTION.md` §3.5 carried are
resolved in the implementation: deduplication uses a dedicated
table, not `MedicationScheduleHistory` or `NotificationEvent`.

**As implemented.**

- Per-medicine opt-in `RemindOnDose` (additive boot patch, default
  off). The checkbox is enabled only when the medicine has at least
  one timed slot and stock above zero; the rule lives in
  `Medicine.CanRemindOnDose`.
- `DoseReminderService`, evaluated once a minute by
  `DoseReminderHostedService`, emits a toast and, if the medicine's
  email channel is on, an email.
- At most once per `(MedicineId, SlotKey, LocalDate)`, enforced by a
  unique index on the `DoseReminderEvents` table; 30-day retention.
- Grace window (default 30 minutes, `DoseReminder:GraceWindowMinutes`):
  a slot older than the window is dropped silently.
- No acknowledgement, no missed-dose log, no clinical wording. The
  line described in §9.2 of `EVOLUTION.md` holds.

**Not done / deferred.** Per-profile quiet hours and a per-minute
toast throttle remain optional refinements.

## 3.6 A6 — Donation / support UI

**Status.** Shipped. PR #38 (2026-09-21).

**Authoritative analysis.**
`docs/analysis/ANALYSIS-A6-DONATION-SUPPORT.md`.

**As implemented.**

- `DonationService` (Application) with `IDonationProvider` /
  `IUrlLauncher` ports; `StripeDonationProvider`,
  `PayPalDonationProvider`, `ShellUrlLauncher` and
  `JsonDonationOptionsProvider` in Infrastructure.
- Tiers €2 / €5 / €10 / €20 plus a provider-native "custom" link,
  configured in `donations.settings.json` (public Payment Link URLs
  only; HTTPS enforced).
- `DonateForm` reached from a single "Support Development" entry in
  the Help menu, hidden when the feature is disabled or
  unconfigured. The URL opens in the default browser; the app never
  claims that a payment succeeded.

**Note.** The shipped `src/MedReminder.UI/donations.settings.json`
still carries `REPLACE_*` placeholder links, so the feature stays
hidden until the maintainer populates it (procedure in
`docs/PACKAGING.md`).

---

## 4. C.3 — Manual encrypted export/import

**Status.** Shipped. PR #48, translations in PR #49, non-admin access
in PR #52 (2026-09-24).

**Authoritative analysis.** `docs/analysis/ANALYSIS-C3-EXPORT-IMPORT.md`.
Public format: `docs/EXPORT-FORMAT.md`.

**As implemented.**

- **Export all data** and **Import from export** in Settings →
  Backup, available to every profile (PR #52). The automatic-backup
  settings on the same tab stay admin-only.
- Output: a single `.mrz` archive (ZIP) with a cleartext
  `manifest.json` and an encrypted `payload.enc` covering the current
  profile, plus optional shared settings.
- Encryption: Argon2id (Konscious, t=3, m=64 MiB, p=1) + AES-GCM
  256-bit, with a user-chosen passphrase. DPAPI deliberately not
  used, so the archive moves across Windows accounts and machines.
- Import: Overwrite mode only, after a safety copy of the current
  database, inside a transaction. Newer-format and non-MedReminder
  archives are refused.
- SMTP password: opt-in only, re-encrypted with the archive key,
  DPAPI-re-encrypted on the target machine.

**Not done / deferred.** Merge mode (as the original sketch
required: overwrite first, merge only after a dedicated design
pass). The admin-only "all profiles" variant mentioned in the
original sketch is deferred (`ANALYSIS-C3` §3.5; the `AllProfiles` scope
exists in `ExportOptions` but the service writes only the current
profile).

---

## 5. C.3+ — Backup to a user-controlled cloud folder + explicit restore

**Status.** Shipped. PR #55 (2026-09-25), fixes in PR #57 and PR #58
(2026-09-25).

**Authoritative analysis.**
`docs/analysis/ANALYSIS-C3PLUS-CLOUD-BACKUP.md`.

**As implemented.**

- The automatic daily backup has two independent targets:
  - the existing **raw database copy** (`.db`) to a local folder —
    unchanged, **not encrypted**;
  - the new **cloud-folder target**: an encrypted `.mrz` snapshot
    (C.3 format) written into a user-chosen folder, which the user's
    own sync agent (OneDrive, Google Drive Desktop, Dropbox, iCloud
    Drive, …) may upload. The app never talks to a cloud provider.
- The cloud target uses a separate backup passphrase, cached with
  DPAPI in `%LOCALAPPDATA%\MedReminder\cloud-backup.protected`,
  distinct from the passphrase typed for a manual C.3 export.
- Snapshots are published atomically (temp file, then move) and
  pruned by their own retention setting, with a file-name pattern
  that never overlaps the `.db` target.
- A snapshot written by either target completes the day (PR #57);
  a missing cloud folder is detected before the export runs, so no
  Argon2id work is wasted on every tick (PR #58).
- **Restore from cloud folder…** (every profile) lists the `.mrz`
  files in a folder, shows their manifests without decrypting them,
  and restores through the C.3 import path.
- Manifest gains optional `source` (`"automatic"`) and a hashed
  `device.hostName`; older readers ignore both.
- The UI and the user guide state that this is **not** real-time
  sync: single writer, restore on demand, last restore wins.

---

## 6. C.3++ — Native cloud provider strategy: Phases 1 and 2

**Status.** Phase 1 shipped in PR #56 (2026-09-25). Phase 2 (OneDrive
and Google Drive) shipped inside B.1 Phase 4 in v2.8.0 (see below).
Phase 3 (Dropbox, enterprise REST providers) stays open and optional
in `EVOLUTION.md` §6.

**Authoritative analysis.**
`docs/analysis/ANALYSIS-C3PP-CLOUD-PROVIDERS.md` (§14 roadmap, §7.3,
§7.5 and §10.1 record the implementation-time decisions).

**As implemented.**

- `IArchiveStorage` port and `ArchiveInfo` record in
  `MedReminder.Application/Abstractions`.
- `LocalFolderArchiveStorage` in `MedReminder.Infrastructure/Backup`,
  the only implementation. It keeps the C.3+ temp-then-move
  discipline and reads the cloud folder from settings on every call.
- `AutomaticBackupHostedService` uploads through the port;
  `IBackupService.PruneCloudFolderAsync` prunes through it.
- Reusable contract test base `ArchiveStorageContractTests`, which a
  future provider backend must pass.
- No OAuth, no provider SDK, no new NuGet dependency, no change to
  the `.mrz` format. No user-visible change.

**Phase 2, as implemented** (B.1 Phase 4a, PRs #91, #93, #94; Phase 4b,
PR #95; `ANALYSIS-B1-MOBILE-SYNC.md`).

- OneDrive through Microsoft Graph REST with MSAL; Google Drive through
  Drive REST v3 with loopback sign-in and PKCE. Tokens cached with
  DPAPI (`onedrive.protected`, `googledrive.protected`).
- Both serve the cloud backup (Settings → Backup) and the sync
  transport (Tools → Sync…), next to the local or cloud-synced folder.
- Since household step H4a (v2.12.0), with a shared installation only
  the master device writes the cloud backup.

**Deviation from the roadmap.** Phase 2 was planned as an
`IArchiveStorage`-only backup feature after B.1 approval; it shipped as
part of B.1 sync, which needed the same providers.

---

## 10. Public presentation website — v1

**Status.** v1 shipped on 2026-09-25 in the separate repository
`vger70/medreminder-website` (PRs #1–#5), deployed to Cloudflare
Pages at <https://medreminder26.pages.dev/>. This repository's
`CHANGE_LOG.md` does not track those PRs (`ANALYSIS-WEBSITE.md` §14).

**Authoritative analysis.** `docs/analysis/ANALYSIS-WEBSITE.md`;
implementation prompt `docs/prompt/PROMPT-WEBSITE-IMPLEMENTATION.md`.

**As implemented.**

- Hugo (pinned 0.140.2) static site, handwritten CSS and minimal JS,
  no framework, no backend.
- Five languages (en, it, fr, es, de), all translated. Root-path
  language routing by a Cloudflare Pages Worker
  (`functions/_middleware.js`) using `Accept-Language`, with a
  client-side stored preference taking precedence.
- Single-page home (hero, features, how it works, screenshots,
  requirements, SmartScreen, download, donate, privacy) plus FAQ,
  About and Privacy pages.
- Donation block opens Stripe or PayPal hosted Payment Links.
- Cloudflare Web Analytics (no cookies) is wired in the base layout
  but renders only when `cloudflareAnalyticsToken` is set; the token
  is empty in v1, so no visits are counted yet.

**Decisions resolved at implementation start** (`ANALYSIS-WEBSITE.md`
§14): separate repository; no roadmap teaser; no dark mode in v1;
screenshots retaken only on significant UI changes. The Cloudflare
project is named `medreminder26` because `medreminder` was taken; no
custom domain yet.

**Deviations from the design.**

- The former sketch stated "the site makes no network calls". v1
  fetches the latest release from the GitHub Releases API at page
  load (`assets/js/version.js`, PR #3) to fill the version badge and
  download links.
- Screenshots ship as text placeholders; no images yet.
- The static version fallback (`currentVersion`) read `v1.2.0` at
  v1, when the app was at 2.4.1.

**Known gaps.** The site's feature list and several factual claims
no longer match the application. The corrections are listed in
`docs/prompt/PROMPT-WEBSITE-CONTENT-REFRESH.md`; releases v2.7–v2.12
are not covered by it (`EVOLUTION.md` §10).

---

## 12. Shipped outside the backlog numbering

Work that never had an `EVOLUTION.md` section but changes what the
backlog builds on. Summaries only; the analyses are authoritative.

### 12.1 Multi-user G — profile role change

**Status.** Shipped with household step H2a (PR #117 on
`feature/master-slave`, merged into `main` by PR #129, v2.12.0).

**Authoritative analysis.**
`docs/analysis/ANALYSIS-MULTI-USER-ROLES-OVERVIEW.md` (status note of
2026-09-29), `ANALYSIS-HOUSEHOLD-MASTER-DEVICE.md` §8.

**As implemented.** Manage profiles → Change role…: an administrator
makes another profile an administrator or a standard user; the open
profile's role stays fixed and one administrator always remains. The
change is a household operation, replicated with a shared
installation.

**Not done.** Item I (read-only "All profiles" view for an
administrator) is not planned.

### 12.2 Remote catalogue feeds

**Status.** Shipped in v2.11.0: Italy (PRs #130, #131), EU, Spain and
France (PRs #134, #135).

**Authoritative analysis.**
`docs/analysis/ANALYSIS-CATALOGUE-REMOTE-FEED.md`,
`docs/analysis/ANALYSIS-CATALOGUE-REMOTE-FEEDS-EU-ES-FR.md`; sources
and workflows in `docs/CATALOGUE-DATA.md`.

**As implemented.**

- Monthly GitHub workflows (`download_aifa.yaml`, `download_aemps.yaml`,
  `download_bdpm.yaml`, `download_ema.yaml`) publish a zip and
  `latest.json` per country under `data/<country>/`.
- At startup, after the update check and under the same setting,
  `RemoteCatalogueRefresher` downloads the reference country's feed
  and the EU feed when newer than the profile's catalogue (HTTPS, no
  redirects, size cap, SHA-256 when published), imports it and deletes
  the archive from `catalogue\staging\`. Data only.
- The embedded snapshots remain the offline baseline.

### 12.3 Household of devices with a master device

**Status.** Desktop steps H0–H5 shipped: H0 in PR #115 (v2.11.0),
H1–H5 in PRs #116–#128 on `feature/master-slave`, merged by PR #129 on
2026-09-30 (v2.12.0) after the manual tests
(`docs/analysis/HOUSEHOLD-MANUAL-TESTS.md`). Step H6 (mobile) belongs
to B.1 Phases 5 and 7.

**Authoritative analysis.**
`docs/analysis/ANALYSIS-HOUSEHOLD-MASTER-DEVICE.md`; storage format in
`docs/SYNC-FORMAT.md` §9.

**As implemented.**

- Profiles, roles, PINs, the email account, the cloud-backup policy
  and the reference country are household state, replicated through
  an end-to-end encrypted household group on the same storages as
  sync.
- Each device holds only the profiles an administrator grants it;
  profile keys are wrapped per device and escrowed under the household
  passphrase for recovery.
- Devices join with a short-lived `mrpair2` code or the household
  passphrase, from Tools → Installation… or the first-run wizard.
- One master device sends every email and runs the cloud backup;
  planned handover with a wizard, takeover of a lost master, and on
  the master a periodic check of every profile it holds
  (`MasterProfilesHostedService`). One low-stock email per sync group
  (`EmailNotificationSent`).
- Device removal moves the installation and the profile groups the
  device held to new keys.

**Deviations.** Tools → Sync… became admin-only. Profile operation
schemas 4 and 5: every device of a group needs v2.12.0.

### 12.4 UI modernisation

**Status.** Shipped in v2.12.0 (PRs #143–#152, #154).

**Authoritative analysis.**
`docs/analysis/ANALYSIS-UI-MODERNIZATION.md` (§6b status, known
limitations).

**As implemented.**

- Theme layer (`UiTheme`: light, dark, high-contrast palettes, WCAG AA
  checked) and a per-profile appearance setting (Same as Windows,
  Light, Dark).
- Main window with summary cards that filter the list, a navigation
  pane and a toolbar with search (Ctrl+F).
- Settings, Sync and Installation list their sections on the left
  instead of tabs.
- One dialog template (`DialogLayout`): button bar with the main
  action last, inline field errors, dialogs that grow with Large text.
- Messages, confirmations and choices in MedReminder's own themed
  dialogs, labelled in the app language.

**Not done.** Framework migration (WinUI 3, WPF, Avalonia) was out of
scope. In dark mode the date and time pickers keep a white field; the
Windows MessageBox remains on the start-up and crash paths.

### 12.5 Stock that follows the dose times; as-needed doses

**Status.** Shipped in v2.14.0 (PR #177); days-left follow-up in
PR #178.

**Authoritative analysis.**
`docs/analysis/ANALYSIS-INTRADAY-CONSUMPTION.md` (§12 status and
deviations).

**As implemented.**

- As-needed slots are never consumed automatically and get no
  reminder; a medicine whose slots are all as needed behaves as PRN;
  under a PRN schedule the slots consume nothing.
- "Extra dose as needed" in the intake dialog: the dose is deducted and
  the day's scheduled consumption stays.
- Therapy → Dose times…: editable and user-defined time-of-day presets,
  and the times of medicines without slots. Device-local.
- The main list shows the stock and the days left after today's doses
  whose time has passed; the run-out date, the forecast-based
  notifications and the recorded stock keep the start-of-day value.
- Existing data corrected once, from today: past days and recorded
  counts unchanged.

**Not done.** Retroactive restitution of past as-needed consumption
(one stock count recovers it), minimum-quantity alerts for as-needed
medicines, presets synchronized between devices, dose reminders at
preset times.

---

## Change log for this document

- 2026-09-25 — created by splitting `EVOLUTION.md`. Moved A1, A3, A4
  (pointer), A5, A6, C.3, C.3+, C.3++ Phase 1 and website v1 here and
  rewrote each item to match what shipped.
- 2026-09-28 — added A2 (§3.2), shipped in full with v2.10.0.
- 2026-10-01 — §6: C.3++ Phase 2 recorded as shipped through B.1
  Phase 4 (v2.8.0). New §12 for work shipped outside the backlog:
  multi-user G, remote catalogue feeds (v2.11.0), household with a
  master device and UI modernisation (v2.12.0). §10 notes no longer
  give the app version as current.
- 2026-10-03 — §12.5: stock that follows the dose times and as-needed
  doses (v2.14.0).
