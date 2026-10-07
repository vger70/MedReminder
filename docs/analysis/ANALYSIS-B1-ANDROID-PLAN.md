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
merge rules of B.1 are unchanged. The iOS implementation (B.1 Phase 6)
is out of scope here; the premium model and the repository rules of
§4.8 apply to iOS as well.

This document is public and predates the repository split of §4.8: it
stays the public summary of the mobile plan, and further mobile design
detail goes to the private repository.

Status on 2026-10-07: revision 10. Reading conventions: `[VERIFIED]`
(checked against the tree at `main` commit `64ccba9`, v2.16.0 plus
#205, #207 and #208, and the spike results of B.1 §18), `[INFERRED]`
(deduction from verified facts), `[UNCERTAIN]` (not verified).
Untagged statements are design proposals.

---

## 1. Requirements

| # | Requirement | Source |
|---|---|---|
| A1 | The app is complete on its own: first start, all daily use, notifications, backup and restore, without a PC (several profiles with premium, §4.8) | Product owner, 2026-10-06 |
| A2 | The app also works without a cloud account; sync and cloud backup are optional | Derived from A1 `[INFERRED]` |
| A3 | Every desktop feature on `main` (§3) that applies to a phone is in the plan; the rest is listed with the reason | Product owner, 2026-10-06 |
| A4 | A first release with a consistent core, then the remaining features in releasable steps | Product owner, 2026-10-06 |
| A5 | When the user also has a PC, phone and PC stay one installation (household) and one data set per profile, as B.1 and the household design define | B.1 §1.2; `ANALYSIS-HOUSEHOLD-MASTER-DEVICE.md` R2, R8 |
| A6 | The mobile apps (Android, later iOS) have a free core and a paid premium tier (subscription or lifetime purchase); the desktop stays free | Product owner, 2026-10-07 |
| A7 | The code of the Android and iOS apps lives in a private repository; the desktop app stays open source in this repository | Product owner, 2026-10-07 |

What changes against B.1:

- B.1 already lets the phone create a profile (§9.1 item 1), but it
  was approved "with mandatory sync", excludes cloud backup on the
  phone, and puts email, catalogue and PDF on the phone only in its
  last phase (§10, Phase 7). With A1 the phone runs with no sync, no
  account and no PC from its first release. The household design already
  allows a phone as first installation and as master (R8, C8, D-7)
  `[VERIFIED]`.
- B.1 §10 excludes cloud backup on the phone because "sync plus desktop
  backups cover it". Without a PC that no longer holds: the phone needs
  its own backup (§4.3).
- Email from the phone becomes necessary, since a phone without a PC
  is the master and the only device that can send it (§4.5). It stays
  a late milestone (M5), which DA4 can move earlier.
- The mobile apps get a free core and a paid premium tier (A6, §4.8);
  B.1 assumed free apps with donation links.

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
| Data | Automatic daily backup: unencrypted `.db` copies to a folder, restore from a `.db` | Not applicable: replaced by `.mrz` export (M1) and encrypted cloud backup (M2), §4.3 | — |
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
| Support | Donation links (A6) | No on mobile: the premium tier replaces them (§4.8, DA9) | — |
| Appearance | Text size, dark mode, high contrast | System font scaling and dark theme; no own setting | M1 |
| Language | Five UI languages | Yes, same dictionaries | M1 |
| Desktop only | Auto-start, tray, window placement, single-instance mutex | Not applicable: Android manages the app lifecycle | — |
| Desktop only | Update check | Not applicable: Play Store updates | — |

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
2. **Join an installation** (from M2, premium): pairing code (QR from
   the PC or another phone, or text) or household passphrase plus cloud
   account, as on the desktop (household §6, B.1 §6.1).
3. **Restore**: from a `.mrz` file (M1) or from a cloud backup (M2),
   both free (§4.8).

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
| Dose reminder | Each timed slot not yet taken, within a rolling window; as-needed slots excluded (#177) | `DoseReminderService`, `DueToday` |
| Prescription to collect | Before "valid until" | `PrescriptionReminders` |
| Administrative deadline | Before the due date, with recurrence | `DeadlineReminders` |
| Package expiry | Before the expiry date | `PackageExpiryNotices` |
| Shortage | When a feed refresh finds a new shortage for a listed medicine | `ShortageNotices`; event, not dated |

The prescription and deadline kinds are planned only with premium
(§4.8); the others for every user.

The Android adapter replaces the scheduled set with exact alarms after
every local write, every sync that changed data, every start and
resume, at `BOOT_COMPLETED` (S5), and on a time-zone or clock change
(B.1 §8.1). Android refuses more than 500 concurrent alarms per app
`[UNCERTAIN — reported by developers as "Maximum limit of concurrent
alarms 500 reached" on API 31+; not checked in the platform source]`,
so the planner
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
  and as best-effort periodic work (S8). As on the desktop, the
  scheduled backup runs on the master only (household C3): a
  standalone phone is master and backs up; a phone in a household
  whose master is a PC leaves it to the PC.
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

### 4.8 Free core and premium (A6)

**Principles.**

1. The free core is a complete app for **one person on one phone**,
   including everything that protects that person: every notification
   about the medicines themselves (doses, low stock, expiry, shortages)
   and a way to save, move and recover their data. Reminders of the
   premium tools (prescriptions, administrative deadlines) come with
   those tools.
2. Premium sells **more devices, more people and more automation**:
   sync and household, automatic cloud backup, several profiles,
   email, and the convenience tools.
3. Nothing that keeps a user safe or keeps their data reachable is
   paid: dose reminders, low-stock and expiry warnings, `.mrz` export
   and import, and restoring a cloud backup stay free; only the
   scheduled backup is premium. Export also covers the right to data portability (GDPR
   Art. 20) `[INFERRED — legal reading not verified]`.
4. **No data hostage.** When premium ends, nothing is deleted: data
   created with premium stays visible and exportable; premium actions
   (sync, scheduled backup, emails, prescription and deadline
   reminders, new extra profiles, scans) stop.
   Sync stops cleanly, and a later renewal resumes it from the group
   (B.1 §5.3 apply loop) `[INFERRED]`. A phone that is master of a
   household with a PC proposes to hand the master role to the PC,
   which keeps email and the scheduled backup running (household C3,
   C8).
5. The **desktop stays free and keeps its own sync**. Premium is
   checked on the phone only; a phone without premium cannot join or
   publish a sync group or household.

**Split** (decided by the product owner, 2026-10-07, DA6; catalogue
row amended by DA11):

| Feature (§3) | Tier | Why |
|---|---|---|
| Medicines, regimens, as-needed slots, dose times | Free | Core use |
| Stock, packages and expiry, intakes, count, history | Free | Core use |
| Low-stock (two stages), dose, expiry notifications and their actions | Free | Safety: never paid |
| Guided setup, five languages, accessibility, app lock with biometrics | Free | Core use |
| `.mrz` export and import | Free | Data reachable without paying (principle 3) |
| Main list, forecast, timeline | Free | Core use; the timeline is read-only and cheap to give |
| Sync with other devices, household, master role, pairing | Premium | The clearest added value; the request names it |
| Automatic encrypted cloud backup | Premium | Automation; manual export and restoring a cloud backup stay free |
| Several profiles, roles, PIN | Premium | Family use |
| Email: low-stock, caregiver copies, weekly digest | Premium | Automation for someone else |
| Prescriptions: request draft, lifecycle, repeatable, reminders, regional services | Premium | Convenience |
| Administrative deadlines, supply planner, calendar export, PDF report | Premium | Convenience |
| Catalogue download, search and link to a medicine | Free | Sets the national code that shortage notices and equivalents need (DA11) |
| Barcode scan, restock by scan | Premium | Convenience; search and manual entry stay free |
| Shortage notices, information links, equivalents (Italy) | Free | Safety information, from public lists at no cost to the project |

Catalogue tier (decided on 2026-10-07, DA11): shortage notices and
equivalents match a medicine by its national code (AIC), which the
desktop sets only by linking the medicine to the catalogue or by a scan
`[VERIFIED — MedicineEditDialog sets the code from a catalogue
reference; no free-text field]`. With catalogue search premium, a free
user without a PC would never get these free notices, so catalogue
search and linking are free and only the scan is premium.

**Prices** (decided on 2026-10-07, DA7):

| Offer | Price | Net per sale in Italy `[INFERRED]` |
|---|---|---|
| Monthly subscription | € 1.99 | ≈ € 1.39 |
| Yearly subscription | € 17.99 | ≈ € 12.53 |
| Lifetime (one-time purchase) | € 49.99 | ≈ € 34.83 |
| Free trial | 14 days on the subscriptions | — |

The net assumes 22% Italian VAT inside the price and a 15% store fee
(Google Play: 15% on auto-renewing subscriptions; from 2026 in the EEA
a 10% service fee plus 5% billing fee; Apple Small Business Program:
15%) `[UNCERTAIN — fees from secondary sources dated 2026; one-time
purchases may carry a higher fee on Google Play from 2026, which would
lower the lifetime net]`. The first proposal (€ 20.99 a year, € 59.99
lifetime) was replaced because € 20.99 saves only 12% against twelve
months, while
a yearly price that saves 25–35% usually moves users to the yearly
plan `[INFERRED — common practice, no market data verified]`; a
lifetime price near three years of the yearly plan keeps the yearly
plan attractive. The project has no per-user running cost (no
backend, the user's own cloud), so a lifetime offer carries no
long-term cost risk.

**Store rules and testers.**

- Digital features sold in the app go through Google Play Billing and
  Apple In-App Purchase. The lifetime offer is a one-time product, the
  others auto-renewing subscriptions.
- No donation links in the mobile apps (decided on 2026-10-07, DA9):
  the premium tier replaces them, and next to a paid tier they could
  conflict with the store payment rules `[UNCERTAIN — exact Google Play
  policy on tips not verified]`. The desktop keeps them (A6 on the
  desktop).
- Testers get premium through **Play license testing**: accounts listed
  in Play Console buy with test payment methods and are not charged;
  test subscriptions renew daily. The closed-test testers are added as
  license testers. On iOS, TestFlight purchases are free sandbox
  purchases. The app contains **no tester back door**: it would ship in
  the store build and could be found and abused.
- Closed test before production: a **personal** Play account created
  after 2023-11-13 needs at least **12** testers opted in for 14
  consecutive days; the figure was 20 until 2024-12-11. Organization
  accounts (D-U-N-S) are exempt `[VERIFIED — secondary sources dated
  2026; check Play Console at registration]`. Recruiting more than 12,
  for example 20, leaves a margin for testers who drop out.
- Selling makes the developer a **trader** under the EU Digital
  Services Act: Google Play and the App Store publish the trader's
  name, address, e-mail and phone on the product page. Income from
  sales has tax consequences in Italy that this document does not
  cover; a tax adviser is needed before the first paid release.

**Code and licence** (decided on 2026-10-07, A7, DA8). This
repository stays public and Apache-2.0 `[VERIFIED — LICENSE]`. The
mobile apps live in a **private repository** under a proprietary
licence. This is possible because the project owner holds the
copyright of the code and is not bound by the owner's own Apache licence
`[INFERRED — commit authors are the owner and tooling; the copyright
status of AI-assisted code is not assessed here]`.

| Public repository (this one, Apache-2.0) | Private repository (proprietary) |
|---|---|
| Desktop app (UI, Infrastructure), its tests and documentation | MAUI app(s) for Android and iOS, platform adapters |
| Shared libraries: Domain, Application, Infrastructure.Portable, with the M0 refactor and the notification planner | Entitlement service, store billing, premium gates |
| Formats (`EXPORT-FORMAT.md`, `SYNC-FORMAT.md`) and the shared design (B.1, household, this plan at the level of the shared core) | Mobile design details, packaging, signing configuration, store assets, mobile user guides, mobile CI |
| Throw-away spikes already published, S1–S10 (`spikes/Android`, draft PR #106) | New mobile spikes, from S11 on |

- The private repository consumes the shared libraries from this
  repository pinned to a tag, as a git submodule or as packages
  published from here; the submodule is the simpler start
  (proposal). A change to the shared core is made here first, in a
  public PR, and then picked up by the app.
- The owner's own shared code needs no Apache notice in the apps.
  Notices are due for code that others contribute to this repository
  (it arrives under Apache-2.0, §4), for the NuGet packages the apps
  ship, and for the catalogue data, whose attributions the desktop
  lists in `THIRD-PARTY-NOTICES.md` `[VERIFIED — that file covers the
  data sources; package licences are recorded by the .NET tooling; no
  NOTICE file in this repository]`. The apps show them on a licences
  screen.
- Apache-2.0 grants no right to use the name "MedReminder" (§6 of the
  licence) `[VERIFIED — LICENSE]`; how far the name is protected
  without a registered trademark is a legal question, and registering
  it is a separate decision.
- GitHub private repositories are free; GitHub Actions minutes for
  private repositories are limited per month, and macOS runners (iOS
  builds) count more than Linux ones `[UNCERTAIN — training knowledge,
  check the plan's quota]`.

**Technical design.**

- `IEntitlementService` in the private app, not in Application: current
  tier, expiry, purchase and restore. Gates live in the app's screens
  and in its sync, backup and email scheduling; the shared use cases
  stay ungated and the public code has no notion of premium.
- Android adapter on Google Play Billing (Play requires Billing
  Library 7 or later for new apps and updates `[VERIFIED — secondary
  sources, deadline 2025-08-31]`). The MAUI options are the
  `Xamarin.Android.Google.BillingClient` binding (reported
  compatibility problems with MAUI) and Microsoft's MAUI
  `BillingService` sample; `Plugin.InAppBilling` is archived
  `[UNCERTAIN — secondary sources]`. Spike S11 settles the choice
  before M2.
- No backend (B.1 §1.3): purchases are verified and acknowledged on
  the device, and the entitlement is cached so that premium works
  offline, with a grace period before it lapses. Without server-side
  verification a modified APK can unlock premium; accepted. With the
  app code private, this needs a patched binary, not a rebuild.
- One purchase covers one store account on all its phones (restore
  purchases). Store family sharing stays off (decided on 2026-10-07,
  DA10): every store account whose phone syncs or joins a household
  needs its own premium, as principle 5 states. A household with two
  phones on two accounts, for example a caregiver and the person cared
  for, buys premium twice; the PC stays free.

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

Entry: M0 merged; D11, D13, DA2, DA5 decided (DA5 fixes the
application id before the first upload); Play Console account; private
repository created (A7). D4 (which device notifies what) concerns
several devices and waits for M2; DA3 waits for M4 and DA4 for M3.

- Free features only (§4.8); no billing yet, so the first release
  needs no payments profile.
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
- In the private repository (A7): CI Android job, signing outside the
  repository, closed testing track, mobile packaging notes and mobile
  user guides in the five languages. In this repository: the M0
  refactor and any shared-core change the app needs.
- Exit: manual checklist on Android 14+ and on the D13 floor; the
  14-day closed test runs on phones without a PC or account and loses
  no data across app updates, reboots and an export/import cycle; no
  health data in logs.
- Effort: 35–50 days `[INFERRED — strongly dependent on MAUI
  experience]`.

### M2 — Cloud: backup, sync, household

Entry: M1 released; D4 decided; Android halves of S6 and S7
(Android OAuth clients, P14); spike S11 (billing); trader status and
payments profile in Play Console.

- Premium infrastructure (§4.8): entitlement service, Play Billing
  adapter, subscription and lifetime products, purchase and restore
  screens, gates, license testers on the testing tracks. Every M2
  feature below is premium except restoring a cloud backup.
- OneDrive and Google Drive sign-in on Android.
- Encrypted cloud backup and restore (§4.3); the scheduled backup on
  the master only (household C3), premium; restore free.
- Sync: create or join a group, QR pairing with the camera (the camera
  decoder chosen here is reused for barcodes in M4), conflicts to
  review, sync status with the time of the last sync (S8).
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
- Effort: 30–45 days, H6 and the premium infrastructure included
  `[INFERRED]`.

### M3 — Prescriptions, planning, views

Entry for M3, M4 and M5: M2 released, since each carries premium
features and needs its entitlement gates; DA4 decided before the first
of them. Tiers as in §4.8: the timeline (M3), catalogue search and
linking, shortage notices and equivalents (M4) are free, the rest
premium.

- Prescription request draft, lifecycle, repeatable prescriptions,
  reminders; regional service links.
- Administrative deadlines with recurrence and reminders.
- Supply planner; calendar export; timeline; therapy report as PDF.
- Effort: 15–20 days `[INFERRED]`.

### M4 — Catalogue, scan, Italian services

Entry: DA3 decided.

- Catalogue download and refresh per country (§4.4), search, link to a
  medicine.
- Barcode scan with the camera, reusing the Code 32 / DataMatrix
  parser; restock by scan.
- Shortage list and notices; information links and equivalents.
- Effort: 15–20 days `[INFERRED]`.

### M5 — Email

- SMTP settings and MailKit on the phone; low-stock email, caregiver
  copies per kind, weekly digest (§4.5).
- No donation links on mobile (§4.8, DA9).
- Exit: feature inventory of §3 fully satisfied or each gap accepted
  by the product owner.
- Effort: 10–15 days `[INFERRED]`.

### 5.1 Summary

| Milestone | Content | Entry | Effort `[INFERRED]` |
|---|---|---|---|
| M0 | Portability refactor 2, notification planner | Approval of this plan | 10–15 d |
| M1 | Standalone core, first release | M0; D11, D13, DA2, DA5; Play account; private repository | 35–50 d |
| M2 | Premium infrastructure; cloud backup, sync, household, profiles, roles and PIN | M1; D4; S6/S7 Android halves; S11; trader status | 30–45 d |
| M3 | Prescriptions, planning, views | M2; DA4 | 15–20 d |
| M4 | Catalogue, scan, Italian services | M2; DA3, DA4 | 15–20 d |
| M5 | Email | M2; DA4 | 10–15 d |

Total about 115–165 developer-days, against 60–90 for B.1 Phases 5
and 7. The difference is the standalone requirement (M0, backup,
phone-first household) and the features added to the desktop since
2026-09-26. M3, M4 and M5 are independent of each other after M2 and
can be reordered (DA4).

### 5.2 Why this order

- M1 is the free core (§4.8) and the smallest set that a person
  without a PC can use every day without losing data: medicines, stock, intakes, the notifications
  that matter daily (low stock, doses, expiry), and a way to save and
  restore the data.
- M2 comes next because it builds the premium infrastructure that
  M3–M5 need, starts the premium tier with its strongest feature,
  sync, connects the phone to a PC when there is one, and removes the
  single-device risk for premium users (scheduled cloud backup). Free
  users keep the export and the backup reminder of M1.
- Sync compatibility does not depend on the order. The phone uses the
  same persistence and apply code as the desktop, so from M2 on it
  stores and applies every operation type, including those whose
  screens come in M3–M5 `[INFERRED — shared Application and
  Infrastructure.Portable code, B.1 §7.5]`.

---

## 6. Decisions

Still open from B.1: D4 (notification defaults per device, before M2),
D11 (recommendation: reject, before M1), D13 (minimum Android version,
before M1).

New (prefix DA, to keep them apart from the B.1 and household numbering):

| # | Decision | Options | Recommendation |
|---|---|---|---|
| DA1 | The Android app works without a PC and without an account | Yes; no | **Requested by the product owner, 2026-10-06** |
| DA2 | Backup on a standalone phone | Export only; export and cloud backup; Android Auto Backup | Export in M1, cloud backup in M2; Auto Backup stays off |
| DA3 | Catalogue on the phone | Embed one country; download per country | Download per country (§4.4) |
| DA4 | Order of M3–M5 | As proposed; email (M5) before M3/M4 | As proposed, unless a caregiver relies on email without a PC |
| DA5 | Play account type and application id | Personal; organization (D-U-N-S); id such as `com.vger70.medreminder` | Product owner; the id cannot change after the first upload |
| DA6 | Free and premium split | §4.8 table; other | **Decided 2026-10-07**: §4.8 table |
| DA7 | Prices | € 1.99 / 20.99 / 59.99; € 1.99 / 17.99 / 49.99 with a 14-day trial | **Decided 2026-10-07**: € 1.99 / 17.99 / 49.99 with a 14-day trial |
| DA8 | Licence of the mobile app code | Apache-2.0 in this repository; private repository, proprietary | **Decided 2026-10-07**: private repository for the Android and iOS apps; desktop and shared core stay Apache-2.0 here (A7) |
| DA9 | Donation links on mobile | Keep; drop | **Decided 2026-10-07**: drop |
| DA10 | Premium across a family | Per store account; store family sharing | **Decided 2026-10-07**: per store account, store family sharing off (§4.8) |
| DA11 | Catalogue search and linking in the split | Premium (as decided in DA6); free, scan stays premium | **Decided 2026-10-07**: free, scan stays premium (§4.8) |

---

## 7. Risks

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| Data loss on a standalone phone (lost, reset, uninstalled) | Medium | High | Export and backup reminder in M1 for everyone; scheduled cloud backup in M2 with premium (§4.3) |
| Exact-alarm permission refused | Medium | Medium | Explanation at first start; inexact fallback stated in the UI (S5) |
| Background work late (emails, backups, feeds) | High | Low–medium | Run on app open; status shows the last run; PC as master when present |
| Scope growth beyond 165 days | Medium | Schedule | Milestones releasable on their own; M3–M5 reorderable |
| Desktop and phone notify on different days | Low | Medium | One rule set, planner parity tests (§4.2) |
| Premium unlocked by a modified app | Low (app code private, DA8) | Low–medium | Accepted without a backend |
| Shared core and private app drift apart | Medium | Medium | App pins a tag of this repository; shared changes land here first |
| Users reject paying for sync that is free on the desktop | Medium | Medium | Generous free core; clear premium value; trial |
| A household with several phones finds one premium per account too dear (DA10) | Medium | Low–medium | Lifetime offer; PC stays free; review family sharing after release data |
| MAUI billing binding immature | Medium | Schedule | Spike S11 before M2 |
| Store review of a health app with subscriptions | Low–medium | Schedule | Clear non-medical positioning, no safety feature paid (§4.8) |

---

## 8. Corrections to other documents (when approved)

- `ANALYSIS-B1-MOBILE-SYNC.md` §1.2, §9.1, §10, §13 Phases 5 and 7:
  for Android, superseded by this document.
- `ANALYSIS-HOUSEHOLD-MASTER-DEVICE.md` §10 and §13 (H6): the phone
  sends email from M5, not from B.1 Phase 7; household creation, join,
  roles and PIN on the phone are part of M2.
- `ANALYSIS-B1-MOBILE-SYNC.md` §16: D11 and D13 are needed before M1,
  D4 before M2, instead of "Phase 5".
- `docs/STATUS.md` §3.1: Phases 5 and 7 replaced by M0–M5.
- `ANALYSIS-B1-MOBILE-SYNC.md` §10 (donation links) and §16 (D14): no
  donation links on Android or iOS (DA9); D14 is settled by it.

---

## 9. Change log for this document

- 2026-10-07 — revision 10: DA10 decided (premium per store account,
  store family sharing off) and DA11 decided (catalogue search and
  linking free, scan premium), split table amended; M3–M5 enter after
  M2, since their premium features need its gates (M3 and M4 entered
  after M1 before); M1 needs DA2 and DA5 only, DA3 before M4 and DA4
  before M3–M5; M2 states that cloud restore is free; prescription and
  deadline reminders stop when premium ends; the data-loss risk and
  §5.2 say that scheduled cloud backup is premium; risk on the cost
  for households with several phones.
- 2026-10-07 — revision 9, check of revision 8: the owner is not bound
  by the owner's Apache licence, so the apps owe notices only for
  contributed code, third-party packages and catalogue data; the name protection is
  not asserted; DA11 moves from the M2 entry to the M4 entry, where the
  catalogue is built; the planner plans prescription and deadline
  reminders only with premium; A1 says that several profiles need
  premium; copyright of AI-assisted code flagged as not assessed.
- 2026-10-07 — revision 8, check after the decisions: DA9 decided (no
  donation links on mobile, D14 settled); the free core states which
  reminders are free (medicine notifications) and which come with the
  premium tools; restoring a cloud backup is free; a lapsing master
  phone hands the master role to a PC; DA11 opened (shortage notices
  need the catalogue link); iOS covered by §4.8; this public document
  as the summary of the mobile plan; M1 needs the private repository;
  price table shows the decided prices; change log in date order.
- 2026-10-07 — revision 7: product-owner decisions DA6 (split), DA7
  (€ 1.99 / 17.99 / 49.99, 14-day trial) and DA8 with requirement A7
  (Android and iOS apps in a private repository, desktop and shared
  core open); repository split, Apache-2.0 notice duty, entitlement in
  the private app; closed-test rule corrected to 12 testers (20 until
  2024-12-11, organizations exempt).
- 2026-10-07 — revision 6: free core and premium tier for the mobile
  apps (A6, §4.8): principles, feature split, prices with net
  estimates, store rules, testers through license testing, trader
  status, licence options, entitlement design; premium infrastructure
  in M2 (30–45 days), spike S11 for billing, donation links dropped on
  mobile; decisions DA6–DA10; risks.
- 2026-10-06 — revision 5, fourth check: the B.1 baseline restated
  (email, catalogue and PDF were planned for Phase 7, not left to the
  desktop); D4 moves from the M1 entry to the M2 entry (one device has
  no per-device policy); the camera decoder is chosen in M2; the
  500-alarm limit tagged as reported, not verified in the source; B.1
  §16 phase column added to the corrections.
- 2026-10-06 — revision 4, third check: B.1 already allowed a profile
  created on the phone, the change is "no sync, no account, no PC";
  the scheduled cloud backup runs on the master only (household C3);
  dose reminders skip as-needed slots; the daily `.db` backup rows
  merged; A5 cites household R2.
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
- 2026-10-06 — revision 1: standalone requirement, feature inventory of
  v2.16.0, milestones M0–M5, decisions DA1–DA5.
