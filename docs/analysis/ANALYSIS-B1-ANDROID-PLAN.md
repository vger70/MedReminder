# ANALYSIS — B.1 Android: standalone app and revised plan

Design document, **prior** to implementation. It revises the Android
part of B.1 (`ANALYSIS-B1-MOBILE-SYNC.md`) after two product-owner
requests of 2026-10-06:

1. The Android app must work **on its own**, without a MedReminder
   installation on a PC.
2. It must include **every feature implemented to date** that applies
   to a phone, not only the Phase 5 scope written on 2026-09-26.

Where this document and `ANALYSIS-B1-MOBILE-SYNC.md` disagree on the
Android client (§9.1 screens, §10 feature parity, §13 Phases 5 and 7),
this document wins once approved. The sync model, the formats and the
merge rules of B.1 are unchanged. iOS (B.1 Phase 6) is out of scope
here.

Status on 2026-10-06: revision 3. Reading conventions: `[VERIFIED]`
(checked against the tree at `main` commit `64ccba9`, v2.16.0 plus
#205, #207 and #208, and the spike results of B.1 §18), `[INFERRED]` (deduction from verified facts), `[UNCERTAIN]`
(not verified). Untagged statements are design proposals.

---

## 1. Requirements

| # | Requirement | Source |
|---|---|---|
| A1 | The app is complete on its own: first start, profiles, all daily use, notifications, backup and restore, without a PC | Product owner, 2026-10-06 |
| A2 | The app also works without a cloud account; sync and cloud backup are optional | Derived from A1 `[INFERRED]` |
| A3 | Every desktop feature on `main` (§3) that applies to a phone is in the plan; the rest is listed with the reason | Product owner, 2026-10-06 |
| A4 | A first release with a consistent core, then the remaining features in releasable steps | Product owner, 2026-10-06 |
| A5 | When the user also has a PC, phone and PC stay one installation (household) and one data set per profile, as B.1 and the household design define | B.1 §1.2; `ANALYSIS-HOUSEHOLD-MASTER-DEVICE.md` R8 |

What changes against B.1:

- B.1 §1.2 calls the app a "full client of a profile" whose data is
  created on the desktop and reaches the phone by sync. With A1 the
  phone can create the installation, the profiles and all the data.
  The household design already allows a phone as first installation
  and as master (R8, C8, D-7) `[VERIFIED]`.
- B.1 §10 excludes cloud backup on the phone because "sync plus desktop
  backups cover it". Without a PC that no longer holds: the phone needs
  its own backup (§4.3).
- Email from the phone becomes necessary, since a phone without a PC
  is the master and the only device that can send it (§4.5). It stays
  a late milestone (M5), which DA4 can move earlier.

---

## 2. Facts the plan builds on

From the spikes (B.1 §18) `[VERIFIED]`:

| Topic | Result | Consequence |
|---|---|---|
| Crypto (S1, S2) | AES-GCM and Argon2id produce the desktop's bytes; Argon2id default parameters 0.9–2.0 s | Archives, sync keys and household passphrases work on the phone unchanged |
| Persistence (S3) | Production EF Core SQLite, `DatabaseInitializer` and repositories run with the Release defaults (`TrimMode=partial`, profiled AOT); full trimming breaks EF Core and reflection JSON | Keep `TrimMode=partial`; no source-generator migration needed for the first releases |
| Build (S4) | `StripReleaseDebugArtifacts` unchanged works on Android | D11: reject the exclusion |
| Notifications (S5) | Exact alarms fire within 4 s, also after reboot; `SCHEDULE_EXACT_ALARM` is not granted after install on Android 16; inexact fallback up to 25 min late; force stop cancels until the next launch, which on Android 15+ delivers `BOOT_COMPLETED` | Notifications are planned ahead and set as exact alarms; the app asks for the permission; one receiver re-plans after reboot and force stop |
| Background (S8) | 15-minute WorkManager work runs every 1–4 h | Anything periodic (sync, cloud backup, email, feed refresh) is best effort on the phone |

From the code at `main` `64ccba9` `[VERIFIED]`:

- **Portable already**: Domain, Application (use cases, `MedicationMonitor`
  with low-stock stages, prescription, deadline, package-expiry and
  shortage notices, `CaregiverDigest`, `DueToday`, timeline, coverage,
  calendar, household and sync logic), and Infrastructure.Portable
  (persistence, archive cipher and reader, OneDrive and Google Drive
  REST clients and their archive storages, sync transports, household
  stores, localization, remote feed clients for the catalogue,
  shortages, equivalents and regional services). The catalogue
  refresher (`RemoteCatalogueRefresher`) and the barcode parser
  (`BarcodeParser`, `ItalianPharmacode`) are in Application.
- **Windows project, but without Windows APIs** `[VERIFIED — their
  using directives reference only System, Microsoft.Extensions, EF Core,
  SQLite, MailKit/MimeKit and MedReminder namespaces; no DPAPI,
  registry or Windows reference]`: MailKit email service, reference
  catalogue query service, catalogue importer and parsers, backup
  service, cloud archive storage (the provider router), sync setup
  service. The embedded snapshot provider is portable code too, but it
  reads the 9.2 MB of snapshots embedded in the Windows assembly; it
  stays on the desktop (§4.4).
- **Windows-specific, need an Android adapter**: DPAPI credential
  stores (`ICredentialProtector` and the stores built on it), profile
  registry and settings stores (paths), export and import shells,
  MSAL and loopback sign-in, auto-start, tray, balloon and toast
  notifications, webcam, update checker, printing.
- **Hosted services** (UI project) that poll: monitor, dose reminders,
  catalogue refresh, automatic backup, household, master profiles,
  sync. On the phone they become a notification planner plus exact
  alarms (§4.2) and best-effort periodic work.

---

## 3. Feature inventory (desktop, `main` `64ccba9`)

Every feature in `docs/USER_GUIDE.en.md` and `docs/STATUS.md` §2 at
v2.16.0, plus the guided setup merged after it (#207), with its place
in the plan. "M" refers to the milestones of §5.

| Area | Desktop feature | Android | Milestone |
|---|---|---|---|
| Start | First start, disclaimer | Yes, touch wizard (§4.1) | M1 |
| Start | Guided setup (#207): who the medicines are for, first medicines, when and how to warn | Yes; the e-mail choice of the "how" step from M5 | M1 |
| Medicines | Add, edit, deactivate, delete; administration times | Yes | M1 |
| Medicines | Complex regimens, stepped taper (A1) | Yes | M1 |
| Medicines | As-needed slots and extra dose as needed (#177) | Yes | M1 |
| Medicines | Dose-time presets and defaults | Yes; device-local as on the desktop, not replicated (#177) | M1 |
| Medicines | Main list, forecast, warnings, search, inactive filter | Yes | M1 |
| Stock | Add a package, correct stock, count stock (guided) | Yes | M1 |
| Stock | Packages and expiry, expiry notices | Yes | M1 |
| Stock | Register an intake; stock that follows the dose times | Yes | M1 |
| Stock | History and retraction of mistaken entries | Yes | M1 |
| Notifications | Low-stock warning, second stage at half threshold | Yes, planned locally | M1 |
| Notifications | Dose-time reminder (A5) | Yes, exact alarm | M1 |
| Notifications | Notification actions: open the medicine or the list, prepare the prescription request, snooze a dose reminder 15 min | Yes, Android notification actions (prescription action from M3) | M1 |
| Data | Encrypted `.mrz` export and import | Yes, through the system file picker and share sheet | M1 |
| Data | Automatic daily backup to a folder | Replaced by encrypted cloud backup (§4.3) | M2 |
| Data | Cloud backup (OneDrive, Google Drive) and restore | Yes | M2 |
| Sync | Profile sync between devices, conflicts, pairing codes and QR, key rotation, device removal | Yes; QR with the camera | M2 |
| Sync | Household: create or join, master device, handover, lost device | Yes; the phone can be master | M2 |
| Sync | Folder transport | No: no shared folder on a phone (B.1 §5.8) | — |
| Prescriptions | Prescription request draft | Yes, share sheet / `mailto:` | M3 |
| Prescriptions | Lifecycle (requested, issued, collected), repeatable prescriptions, reminder before "valid until" | Yes | M3 |
| Prescriptions | Regional prescription service links (Italy) | Yes, browser | M3 |
| Planning | Administrative deadlines with recurrence and reminders | Yes | M3 |
| Planning | Plan supply for a period | Yes | M3 |
| Planning | Export dates to a calendar (`.ics`) | Yes, share the file | M3 |
| Views | Therapy timeline | Yes | M3 |
| Views | Therapy report / card, print and PDF | PDF generated on the phone and shared (§4.6) | M3 |
| Catalogue | Reference catalogue search per country, remote monthly feeds | Yes, data downloaded per country (§4.4) | M4 |
| Catalogue | Barcode scan (webcam) and restock by scan | Yes, phone camera | M4 |
| Catalogue | Barcode scan with a USB HID scanner | No: desktop accessory `[INFERRED]` | — |
| Italy | Shortage list, notice once per shortage | Yes | M4 |
| Italy | Information links and equivalent medicines | Yes | M4 |
| Email | SMTP account, recipients, low-stock email, caregiver copies per kind, weekly digest, run-out date | Yes, MailKit; timing best effort (S8) | M5 |
| People | Several profiles, roles (administrator, user), PIN, switch profile | Yes; app lock with device biometrics as well | M2 (one administrator profile in M1) |
| Support | Donation links (A6) | Yes, browser | M5 |
| Appearance | Text size, dark mode, high contrast | System font scaling and dark theme; no own setting | M1 |
| Language | Five UI languages | Yes, same dictionaries | M1 |
| Desktop only | Auto-start, tray, window placement, single-instance mutex | Not applicable: Android manages the app lifecycle | — |
| Desktop only | Update check | Not applicable: Play Store updates | — |
| Desktop only | Raw database backup to a folder | Replaced by cloud backup and `.mrz` export | — |

No desktop feature is dropped except the desktop-only rows. The
device-side state-hash check of B.1 §12 and Phase 7 is not implemented
on the desktop either; it stays a B.1 item for every device, outside
M0–M5. The feature parity table of B.1 §10 is replaced by this one for
Android.

---

## 4. Design points for a standalone phone

### 4.1 First start

The first-start wizard offers three paths:

1. **Start here**: create the installation and the first profile, then
   the guided setup (#207). No account needed: a device with no storage
   configured is a household of one (household §4.1) `[VERIFIED]`,
   master of itself. This is the standalone path (A1, A2).
2. **Join an installation** (from M2): pairing code (QR from the PC or
   another phone, or text) or household passphrase plus cloud account,
   as on the desktop (household §6, B.1 §6.1).
3. **Restore**: from a `.mrz` file or from a cloud backup.

A standalone phone can later publish its household to a cloud account,
which needs an account because a phone has no folder transport, and
let a PC join (household §10) `[VERIFIED]`. The PC joins with the text
pairing code: its webcam decoder does not read QR codes (household §10)
`[VERIFIED]`.

### 4.2 Notification planner

The desktop polls (`MedicationMonitor`, `DoseReminderService`). The
phone cannot (S8). A `NotificationPlanner` in Application computes,
from the same rules, the dated notifications of the next days:

| Notification | Date | Source rule |
|---|---|---|
| Low stock, stage 1 and 2 | Day the forecast crosses the threshold and half of it | `NotificationCycle` |
| Dose reminder | Each timed slot not yet taken, within a rolling window | `DoseReminderService`, `DueToday` |
| Prescription to collect | Before "valid until" | `PrescriptionReminders` |
| Administrative deadline | Before the due date, with recurrence | `DeadlineReminders` |
| Package expiry | Before the expiry date | `PackageExpiryNotices` |
| Shortage | When a feed refresh finds a new shortage for a listed medicine | `ShortageNotices`; event, not dated |

The Android adapter replaces the scheduled set with exact alarms after
every local write, every sync that changed data, every start and
resume, at `BOOT_COMPLETED` (S5), and on a time-zone or clock change
(B.1 §8.1). Android 12+ refuses more than 500 concurrent alarms per
app `[VERIFIED — "Maximum limit of concurrent alarms 500 reached",
AOSP AlarmManagerService and developer reports]`, so the planner
schedules a bounded window (for example the next 48 hours of dose
reminders) and re-plans when an alarm fires. The planner and the monitor share the
rules, so desktop and phone notify on the same days; a test runs both
over the same scenarios. The desktop keeps its polling; moving it to
the planner is not needed.

### 4.3 Backup without a PC

Android Auto Backup stays off (B.1 §9.2): it would copy health data to
Google outside the app's encryption. The phone offers instead:

- **Encrypted export** (`.mrz`, same format as the desktop) to a file
  the user picks or shares; import replaces the profile, as on the
  desktop. With sync on (M2), an import starts a new generation of the
  group (B.1 §5.7).
- **Encrypted cloud backup** (C.3+ on the phone) to OneDrive or Google
  Drive with the existing `IArchiveStorage` providers, same passphrase
  rules, run when the app opens if the last backup is older than a day,
  and as best-effort periodic work (S8).
- A **reminder** when no backup or export has been made for 30 days
  and sync is off: without a PC, a lost phone is lost data.

### 4.4 Catalogue on the phone

The desktop embeds a snapshot per country (0.5–4.6 MB each, B.1 §10)
and refreshes it from monthly feeds `[VERIFIED]`. The phone downloads
the reference country's catalogue and the EU catalogue from the same
feeds (`GitHubRawCatalogueFeedClient`, `RemoteCatalogueRefresher`,
both portable `[VERIFIED]`), and refreshes it when the app opens and as periodic work.
No country is embedded in the APK, which keeps it near the 40 MB of the
spike instead of adding the 9.2 MB of the four embedded snapshots
`[INFERRED]`. Without network the catalogue is simply not
available; manual entry always works.

### 4.5 Email from the phone

On a standalone phone the phone is the master (household R1, R5), so
it sends the low-stock emails, the caregiver copies and the weekly
digest. MailKit runs on Android `[INFERRED — managed library, no native
dependency]`. The send happens when the app is opened and as periodic
work, so it can be hours late (S8); the settings say so. When a PC joins,
the app proposes to make the PC master (household C8).

### 4.6 PDF and sharing

The therapy report becomes a PDF built on the phone (Android
`PdfDocument` or a managed library, decision in M3) and shared with the
share sheet. The calendar export, the prescription request and the
`.mrz` export use the same share sheet.

### 4.7 Security on the phone

- Secrets (SMTP password, sync and household keys, cloud tokens,
  backup passphrase) in an Android Keystore-backed store, behind the
  existing ports (`ICredentialProtector` and the stores built on it).
- App lock: device biometrics in M1; the profile PIN as well from M2,
  when several profiles and roles arrive.
- Database and logs in the app sandbox; same log rules as the desktop
  (`CLAUDE.md` §7).

---

## 5. Revised plan

Replaces B.1 §13 Phases 5 and 7 for Android. Every milestone ends with
a release on a Play testing track; M1 is the first one users can rely
on alone.

### M0 — Portability refactor 2 (desktop only, no behavior change)

- Move to `MedReminder.Infrastructure.Portable` the services of §2
  that have no Windows dependency: MailKit email, catalogue query,
  importer and parsers, backup service, cloud archive storage, sync
  setup service, export core (payload build without the Windows
  settings files). The embedded snapshots and their provider stay in
  the Windows project.
- Split the profile registry and settings stores into a portable core
  and a Windows path/DPAPI shell, as Phase 1 did for the archive
  reader.
- `NotificationPlanner` in Application, with parity tests against
  `MedicationMonitor` and `DoseReminderService` (§4.2).
- Exit: desktop tests green, no behavior change, release.
- Effort: 10–15 days `[INFERRED]`.

### M1 — Standalone core (first release)

Entry: M0 merged; D4, D11, D13, DA1–DA5 decided (DA5 fixes the
application id before the first upload); Play Console account.

- MAUI app (`TrimMode=partial`), composition root, Android adapters
  (paths, Keystore credential protector, notifications, exact alarms,
  boot receiver, share sheet, file picker).
- First start: "Start here" and "Restore from file" paths of §4.1,
  disclaimer, language, notification and exact-alarm permissions;
  guided setup without its e-mail choice.
- Medicines, regimens, as-needed slots, dose times and presets; main
  list with forecast, warnings and search; stock, packages and expiry, intakes, count,
  history and retraction.
- Notification planner with exact alarms: low stock (two stages), dose
  reminders with actions, package expiry.
- Encrypted export and import, backup reminder (§4.3).
- One profile, administrator role; app lock with device biometrics;
  system font scaling and dark theme; accessibility of B.1 §9.1
  (screen-reader labels, no meaning by color alone); five languages;
  sandbox and log rules.
- CI Android job, signing outside the repository, closed testing track;
  `docs/PACKAGING.md` mobile section; user guide sections in the five
  languages.
- Exit: manual checklist on Android 14+ and on the D13 floor; the
  14-day closed test runs on phones without a PC or account and loses
  no data across app updates, reboots and an export/import cycle; no
  health data in logs.
- Effort: 35–50 days `[INFERRED — strongly dependent on MAUI
  experience]`.

### M2 — Cloud: backup, sync, household

Entry: M1 released; Android halves of S6 and S7 (Android OAuth clients,
P14).

- OneDrive and Google Drive sign-in on Android.
- Encrypted cloud backup and restore (§4.3).
- Sync: create or join a group, QR pairing with the camera, conflicts
  to review, sync status with the time of the last sync (S8).
- Household: create on the phone, join, master role on the phone or
  hand it to a PC, device removal; "Join an installation" path of
  §4.1. A phone that is master holds every profile (household C4, C7).
- Several profiles, switch profile, roles and PIN, with the permission
  matrix of household §4.3 and §8 (household step H6). Needed here
  because a joined phone can hold several profiles and must not let a
  user change administrator settings.
- Exit: phone and desktop converge in the offline and conflict
  scenarios of the B.1 checklist; a phone-first installation is joined
  by a PC and the PC becomes master.
- Effort: 25–38 days, H6 included `[INFERRED]`.

### M3 — Prescriptions, planning, views

- Prescription request draft, lifecycle, repeatable prescriptions,
  reminders; regional service links.
- Administrative deadlines with recurrence and reminders.
- Supply planner; calendar export; timeline; therapy report as PDF.
- Effort: 15–20 days `[INFERRED]`.

### M4 — Catalogue, scan, Italian services

- Catalogue download and refresh per country (§4.4), search, link to a
  medicine.
- Barcode scan with the camera, reusing the Code 32 / DataMatrix
  parser; restock by scan.
- Shortage list and notices; information links and equivalents.
- Effort: 15–20 days `[INFERRED]`.

### M5 — Email and support

- SMTP settings and MailKit on the phone; low-stock email, caregiver
  copies per kind, weekly digest (§4.5).
- Donation links.
- Exit: feature inventory of §3 fully satisfied or each gap accepted
  by the product owner.
- Effort: 10–15 days `[INFERRED]`.

### 5.1 Summary

| Milestone | Content | Entry | Effort `[INFERRED]` |
|---|---|---|---|
| M0 | Portability refactor 2, notification planner | Approval of this plan | 10–15 d |
| M1 | Standalone core, first release | M0; D4, D11, D13, DA1–DA5; Play account | 35–50 d |
| M2 | Cloud backup, sync, household, profiles, roles and PIN | M1; S6/S7 Android halves | 25–38 d |
| M3 | Prescriptions, planning, views | M1 | 15–20 d |
| M4 | Catalogue, scan, Italian services | M1 | 15–20 d |
| M5 | Email, support | M1 | 10–15 d |

Total about 110–160 developer-days, against 60–90 for B.1 Phases 5
and 7. The difference is the standalone requirement (M0, backup,
phone-first household) and the features added to the desktop since
2026-09-26. M3, M4 and M5 are independent of each other after M1 and
can be reordered.

### 5.2 Why this order

- M1 is the smallest set that a person without a PC can use every day
  without losing data: medicines, stock, intakes, the notifications
  that matter daily (low stock, doses, expiry), and a way to save and
  restore the data.
- M2 comes next because it removes the single-device risk (backup) and
  connects the phone to a PC when there is one.
- Sync compatibility does not depend on the order. The phone uses the
  same persistence and apply code as the desktop, so from M2 on it
  stores and applies every operation type, including those whose
  screens come in M3–M5 `[INFERRED — shared Application and
  Infrastructure.Portable code, B.1 §7.5]`.

---

## 6. Decisions

Still open from B.1: D4 (notification defaults per device), D11
(recommendation: reject), D13 (minimum Android version).

New (prefix DA, to keep them apart from the B.1 and household numbering):

| # | Decision | Options | Recommendation |
|---|---|---|---|
| DA1 | The Android app works without a PC and without an account | Yes; no | **Requested by the product owner, 2026-10-06** |
| DA2 | Backup on a standalone phone | Export only; export and cloud backup; Android Auto Backup | Export in M1, cloud backup in M2; Auto Backup stays off |
| DA3 | Catalogue on the phone | Embed one country; download per country | Download per country (§4.4) |
| DA4 | Order of M3–M5 | As proposed; email (M5) before M3/M4 | As proposed, unless a caregiver relies on email without a PC |
| DA5 | Play account type and application id | Personal; organization (D-U-N-S); id such as `com.vger70.medreminder` | Product owner; the id cannot change after the first upload |

---

## 7. Risks

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| Data loss on a standalone phone (lost, reset, uninstalled) | Medium | High | Backup reminder in M1, cloud backup in M2 (§4.3) |
| Exact-alarm permission refused | Medium | Medium | Explanation at first start; inexact fallback stated in the UI (S5) |
| Background work late (emails, backups, feeds) | High | Low–medium | Run on app open; status shows the last run; PC as master when present |
| Scope growth beyond 160 days | Medium | Schedule | Milestones releasable on their own; M3–M5 reorderable |
| Desktop and phone notify on different days | Low | Medium | One rule set, planner parity tests (§4.2) |

---

## 8. Corrections to other documents (when approved)

- `ANALYSIS-B1-MOBILE-SYNC.md` §1.2, §9.1, §10, §13 Phases 5 and 7:
  for Android, superseded by this document.
- `ANALYSIS-HOUSEHOLD-MASTER-DEVICE.md` §10 and §13 (H6): the phone
  sends email from M5, not from B.1 Phase 7; household creation, join,
  roles and PIN on the phone are part of M2.
- `docs/STATUS.md` §3.1: Phases 5 and 7 replaced by M0–M5.

---

## 9. Change log for this document

- 2026-10-06 — revision 1: standalone requirement, feature inventory of
  v2.16.0, milestones M0–M5, decisions DA1–DA5.
- 2026-10-06 — revision 3, second check: inventory taken at `main`
  `64ccba9` and extended with the guided setup (#207) and as-needed
  slots (#177); the embedded catalogue snapshots stay on the desktop
  (moving their provider would put 9.2 MB in the APK); planner re-plans
  on time-zone and clock change and stays under the 500-alarm limit;
  import with sync on starts a new generation; DA5 is needed before M1
  (application id); B.1 §1.2 pointer added.
- 2026-10-06 — revision 2, after a check against the tree and the
  household design: profiles, roles and PIN move from M5 to M2 (a
  joined phone holds several profiles and needs the permission
  matrix); household references corrected (§4.1 and §6, not §8);
  household without storage cited; PC joins a phone with the text
  code; catalogue feed client and refresher already portable, EU feed
  added; M1 gains accessibility, packaging and user guides, exit aligned
  with the 14-day closed test; state-hash check stated as outside the
  plan; "forwards" corrected to "applies"; totals 110–160 days.
