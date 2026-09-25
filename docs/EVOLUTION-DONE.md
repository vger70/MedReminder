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
| §3.3 | A3 — Caregiver notifications | PR #47 |
| §3.4 | A4 — Data export/import | Merged into C.3 (§4) |
| §3.5 | A5 — Dose-time reminder | PR #37 |
| §3.6 | A6 — Donation / support UI | PR #38 |
| §4 | C.3 — Manual encrypted export/import | PR #48, PR #49, PR #52 |
| §5 | C.3+ — Backup to a cloud-synced folder + explicit restore | PR #55, PR #57, PR #58 |
| §6 (Phase 1) | C.3++ — `IArchiveStorage` abstraction | PR #56 |
| §10 | Public presentation website v1 | `vger70/medreminder-website` PRs #1–#5 |

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

## 6. C.3++ — Native cloud provider strategy: Phase 1

**Status.** Phase 1 shipped. PR #56 (2026-09-25). Phase 2 and later
(native OneDrive / Google Drive / Dropbox backends) stay open in
`EVOLUTION.md` §6 and are gated on B.1.

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
- The static version fallback (`currentVersion`) still reads
  `v1.2.0`; the app is at 2.4.1.

**Known gaps.** The site's feature list and several factual claims
no longer match the application. The corrections are listed in
`docs/prompt/PROMPT-WEBSITE-CONTENT-REFRESH.md`.

---

## Change log for this document

- 2026-09-25 — created by splitting `EVOLUTION.md`. Moved A1, A3, A4
  (pointer), A5, A6, C.3, C.3+, C.3++ Phase 1 and website v1 here and
  rewrote each item to match what shipped.
