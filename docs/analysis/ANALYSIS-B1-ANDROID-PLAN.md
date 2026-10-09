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
this document wins. The `ANALYSIS-B1-*` documents also take precedence
over `docs/STATUS.md` and `docs/EVOLUTION.md`, which are snapshots and
are not kept up to date with the Android plan. The sync model, the formats and the
merge rules of B.1 are unchanged. The iOS implementation (B.1 Phase 6)
is out of scope here; the premium model and the repository rules of
§4.8 apply to iOS as well.

This document is public and predates the repository split of §4.8: it
stays the public summary of the mobile plan, and further mobile design
detail goes to the private repository.

Status on 2026-10-08: revision 22, approved by the product owner on
2026-10-08 (M3 → M4 → M5 sequence included). M1–M5 builds are
pre-release builds on the Play testing tracks (§5.0); the public Play
Store launch follows M5.
Reading conventions: `[VERIFIED]` (checked against the tree at `main` commit `64ccba9`, v2.16.0 plus #205, #207 and #208, and the spike results of B.1 §18), `[INFERRED]` (deduction from verified facts), `[UNCERTAIN]` (not verified), **`[ANDROID EXPERT NOTE]`** (Technical integration/correction from senior Android platform review).
Untagged statements are design proposals.

### Scope boundary: Android release 1 and evolution V

| | Android release 1 (this plan) | Evolution V (vital tracking) |
|---|---|---|
| Content | Milestones M0–M5: every applicable desktop feature on `main`, without vital-parameter tracking | D.1 (`ANALYSIS-D1-VITAL-TRACKING.md`): vital diary on the desktop, Android and iOS |
| When | Now; public Play Store launch after M5 | Evaluated only after the store release of the Android and iOS apps (product owner, 2026-10-08) |
| Vital data | None: no `vitals.db`, no vital screens, no vital transfer, no vital entries in the Privacy Policy or the Play data-safety form | Separate local `vitals.db`, outside `.mrz`, cloud backup and sync |
| Effort | 120–170 developer-days (§5.1) | Not estimated; estimated when evolution V is scheduled |

Every requirement, milestone, backlog item, UX requirement and estimate in
the `ANALYSIS-B1-*` documents belongs to release 1 unless it names
evolution V. §4.3a lists what release 1 must leave in place for it.

---

## 1. Requirements

| # | Requirement | Source |
|---|---|---|
| A1 | The app is complete on its own: first start, all daily use, notifications, backup and restore, without a PC (several profiles with premium, §4.8) | Product owner, 2026-10-06 |
| A2 | The app also works without a cloud account; sync and cloud backup are optional | Derived from A1 `[INFERRED]` |
| A3 | Every desktop feature on `main` (§3) that applies to a phone is in the plan; the rest is listed with the reason | Product owner, 2026-10-06 |
| A4 | Deliver a consistent Android core and remaining features in milestones M0–M5 (M1–M5 as pre-release builds on the Play testing tracks, §5.0); publish to the Play Store production track only after M5 | Product owner, 2026-10-06; launch timing confirmed 2026-10-08 |
| A5 | When the user also has a PC, phone and PC stay one installation (household) and one data set per profile, as B.1 and the household design define | B.1 §1.2; `ANALYSIS-HOUSEHOLD-MASTER-DEVICE.md` R2, R8 |
| A6 | The mobile apps (Android, later iOS) have a free core and a paid premium tier (subscription or lifetime purchase); the desktop stays free | Product owner, 2026-10-07 |
| A7 | The code of the Android and iOS apps lives in a private repository; the desktop app stays open source in this repository | Product owner, 2026-10-07 |
| A8 | Privacy by design: local-first storage, transparent onboarding privacy acknowledgment, and user-controlled data export/import | Legal Assessment, 2026-10-07 |
| A9 | Release 1 contains no vital-parameter tracking; it is evolution V, evaluated after the store release on Android and iOS | Product owner, 2026-10-08 |

What changes against B.1:

- B.1 already lets the phone create a profile (§9.1 item 1), but it
was approved "with mandatory sync", excludes cloud backup on the
phone, and puts email, catalogue and PDF on the phone only in its
last phase (§10, Phase 7). With A1 the phone runs with no sync, no
account and no PC from its first pre-release milestone (M1). The household design already
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
| Notifications (S5) | Exact alarms fire within 4 s, also after reboot; `SCHEDULE_EXACT_ALARM` is not granted after install on Android 16; inexact fallback up to 25 min late; force stop cancels until the next launch, which on Android 15+ delivers `BOOT_COMPLETED` | Notifications are planned ahead and set as exact alarms; the app asks for the permission; one receiver re-plans after reboot and force stop. **`[ANDROID EXPERT NOTE]`**: We must gracefully degrade to `WorkManager` for non-critical alerts if permission is permanently denied. |
| Background (S8) | 15-minute WorkManager work runs every 1–4 h | Anything periodic (sync, cloud backup, email, feed refresh) is best effort on the phone. **`[ANDROID EXPERT NOTE]`**: OEM Battery Managers (Xiaomi, Samsung) often kill WorkManager tasks. We will need an in-app prompt directing users to disable battery optimization (`REQUEST_IGNORE_BATTERY_OPTIMIZATIONS`) for the app to ensure reliable background sync. |

From the code at `main` `64ccba9` `[VERIFIED]`:

- **Portable already**: Domain, Application (use cases, `MedicationMonitor`
with low-stock stages, prescription, deadline, package-expiry and
shortage notices, `CaregiverDigest`, `DueToday`, timeline, coverage,
calendar, household and sync logic), and Infrastructure.Portable
(persistence, archive cipher and reader, OneDrive and Google Drive
REST clients and their archive storages, sync transports, household
stores, localization, remote feed clients for the catalogue,
shortages, equivalents and regional services).
- **Windows project, but without Windows APIs**: MailKit email service, reference
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

*(Parity list remains identical to original for desktop tracking)*
| Area | Desktop feature | Android | Milestone |
|---|---|---|---|
| Start | First start, disclaimer & Privacy Policy | Yes, touch wizard (§4.1) | M1 |
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
| Vitals | Not a desktop feature on `main`: D.1 (`ANALYSIS-D1-VITAL-TRACKING.md`) is designed, not implemented | Not in release 1; evolution V, on the desktop and the phones, after the store release on Android and iOS (§4.3a) | — |
| Catalogue | Reference catalogue search per country, remote monthly feeds | Yes, data downloaded per country (§4.4) | M4 |
| Catalogue | Barcode scan (webcam) and restock by scan | Yes, phone camera | M4 |
| Catalogue | Barcode scan with a USB HID scanner | No: desktop accessory `[INFERRED]` | — |
| Italy | Shortage list, notice once per shortage | Yes | M4 |
| Italy | Information links and equivalent medicines | Yes | M4 |
| Email | SMTP account, recipients, low-stock email, caregiver copies per kind, weekly digest, run-out date | Yes, MailKit; timing best effort (S8) | M5 |
| People | Several profiles, roles, PIN, switch profile | Yes | M2 |
| People | App lock | Device biometrics instead of the desktop PIN for the single M1 profile. **`[ANDROID EXPERT NOTE]`**: Implemented via AndroidX BiometricPrompt API. | M1 |
| Support | Donation links (A6) | No on mobile: the premium tier replaces them (§4.8, DA9) | — |
| Appearance | Text size, dark mode, high contrast | System font scaling and dark theme; no own setting | M1 |
| Language | Five UI languages | Yes, same dictionaries | M1 |
| Desktop only | Auto-start, tray, window placement, single-instance mutex | Not applicable: Android manages the app lifecycle | — |
| Desktop only | Update check | Not applicable: Play Store updates | — |

---

## 4. Design points for a standalone phone

### 4.1 First start & Legal Onboarding

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

**Privacy / Store onboarding note:**
Before entering the setup path, the onboarding UI presents a concise privacy acknowledgment explaining that MedReminder is local-first, that data leaves the device only through actions the user configures (export, share, encrypted cloud backup or sync with the user's own provider), and where the current Privacy Policy is (UI-00, UI-17 of `ANALYSIS-B1-UI-REQUIREMENTS.md`).

This screen is a product/privacy safeguard, not a claim that a GDPR consent is always the developer's legal basis. Whether the household exemption in GDPR Article 2(2)(c) applies depends on the concrete activity and context; the app must not present the exemption as an automatic legal conclusion.

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

Implemented in M0 (backlog B0-02) for low stock, dose reminders and
package expiry: `NotificationPlanner` and `NotificationPlanLoader` in
`MedReminder.Application/Notifications`, checked against the desktop
passes by `NotificationPlannerParityTests`. The prescription and
deadline kinds are added with premium in M2.

The Android adapter replaces the scheduled set with exact alarms after
every local write, every sync that changed data, every start and
resume, at `BOOT_COMPLETED` (S5), and on a time-zone or clock change
(B.1 §8.1). 

**`[ANDROID EXPERT NOTE]`**: A bounded scheduling window (for example, the next 48 hours of dose reminders) limits pending work and allows the planner to re-plan when an alarm fires. Android caps the alarms one app can register: at 500 alarms `AlarmManager` throws `SecurityException` ("Too many alarms (500) registered") `[VERIFIED — AOSP and public crash reports from Android 8/9 onward; not specific to API 31]`. The planner stays well below that limit and the adapter catches the exception. Because Play restricts `USE_EXACT_ALARM` to narrow core use cases, the current proposal is `SCHEDULE_EXACT_ALARM`, subject to user approval; gracefully degrade if the permission is denied. The UI must explain the request.

### 4.3 Backup without a PC

Android Auto Backup stays off (B.1 §9.2): it would copy health data to
Google outside the app's encryption. The phone offers instead:

- **Encrypted export** (`.mrz`, same format as the desktop) to a file
the user picks or shares; import replaces the profile data, as on the
desktop. With sync on (M2), an import starts a new generation of the
group (B.1 §5.7).
**`[ANDROID EXPERT NOTE]`**: This will be implemented using the Android Storage Access Framework (SAF) (`ACTION_CREATE_DOCUMENT` / `ACTION_OPEN_DOCUMENT`) to ensure full compatibility with Scoped Storage restrictions introduced in Android 11+.
- **Encrypted cloud backup** (C.3+ on the phone) to OneDrive or Google
Drive with the existing `IArchiveStorage` providers.
- A **reminder** when no backup or export has been made for 30 days
  and sync is off: without a PC, a lost phone is lost data.

### 4.3a Vital tracking (D.1) is evolution V, outside release 1

Vital-parameter tracking (`ANALYSIS-D1-VITAL-TRACKING.md`) is not part
of release 1 (M0–M5). It is evolution V, for the desktop, Android and
iOS together, evaluated only after the store release of the Android and
iOS apps (product owner, 2026-10-08). Release 1 has no `vitals.db`, no
vital screens, no vital CSV/PDF transfer and no reference to vital data
in its store listing, data-safety form or Privacy Policy.

Release 1 keeps evolution V possible without designing for it now:

- `.mrz`, cloud backup and the sync model stay as they are; evolution V
  adds a separate database and does not change these formats.
- The onboarding acknowledgment records the policy version (UI-17), so
  evolution V can ask for a new acknowledgment when the Privacy Policy
  changes.
- No vital placeholder, menu entry or premium offer appears in the
  release 1 UI.

When evolution V is scheduled, its entry includes: both apps on the
store; the D.1 design re-checked against the shipped apps (MAUI UI per
`ANALYSIS-B1-UI-REQUIREMENTS.md`, encrypted SQLite on Android and iOS,
which no spike has validated yet); updated Privacy Policy, DPIA
(`docs/DPIA-VITAL-TRACKING.md`) and store data-safety declarations; its
own estimate and decisions (DV1–DV10 of D.1).

### 4.4 Catalogue on the phone

The desktop embeds a snapshot for IT, EU, ES and FR (0.5–4.6 MB each,
B.1 §10), refreshes them from monthly feeds and reads US from its feed
only `[VERIFIED — CatalogueFeedDescriptor]`. The phone downloads the
selected catalogue from the same feeds (`GitHubRawCatalogueFeedClient`,
`RemoteCatalogueRefresher`, both portable `[VERIFIED]`), and refreshes
it when the app opens and as periodic work. No catalogue is embedded in
the APK, which keeps it near the 40 MB of the spike instead of adding
the 9.2 MB of the four embedded snapshots `[INFERRED]`. Without network
the catalogue is simply not available; manual entry always works. No
personal data is transmitted during catalogue downloads.

**Country, catalogue and language defaults (DA3).**

- **UI language**: the system language when it is one of the five
  supported languages (it, en, fr, es, de), English otherwise. The user
  can change it at any time. The language does not depend on the
  country. This matches the desktop first run
  (`Program.ApplySystemLanguageOnFirstRun`) `[VERIFIED]`.
- **Reference country**: preselected from the device region setting
  (no location permission) and confirmed or changed by the user. When
  the region cannot be determined, nothing is preselected.
- **Reference catalogue**, from the reference country:
  1. a national catalogue exists (IT, ES, FR, US): preselect it. The
     search scope follows the desktop: the national catalogue plus EU
     where EMA centralised authorisations are valid (IT, ES, FR), the
     national catalogue only for US (`StaticCountryProfileProvider`)
     `[VERIFIED]`;
  2. otherwise, the country is in the EU/EEA, where EMA centralised
     authorisations are valid: preselect the EMA (EU) catalogue;
  3. otherwise, or when no country is set: no reference catalogue. The
     user enters medicines manually and may still pick a catalogue
     explicitly in the settings.
- The app is usable worldwide; a catalogue is an optional data-entry
  aid. Shortage notices, equivalents and regional services stay tied
  to Italy as reference country, as on the desktop.

The desktop differs on the country and catalogue part (default `IT`,
no "no catalogue" state, any unlisted country treated as EMA-covered).
`ANALYSIS-DESKTOP-COUNTRY-CATALOGUE-DEFAULTS.md` specifies the desktop
integration.

### 4.5 Email from the phone

On a standalone phone the phone is the master (household R1, R5), so
it sends the low-stock emails, the caregiver copies and the weekly
digest. MailKit runs on Android `[INFERRED — managed library, no native
dependency]`. The send happens when the app is opened and as periodic
work, so it can be hours late (S8); the settings say so. When a PC joins,
the app proposes to make the PC master (household C8). Email is sent
only after the user has configured SMTP and recipients on the master.
The B.1 "designated mail device" (B.1 §8.4) is superseded by the
household master.

### 4.6 PDF and sharing

The therapy report becomes a PDF built on the phone with SkiaSharp
(DA14, decided 2026-10-08) and shared with the
share sheet. **`[ANDROID EXPERT NOTE]`**: Android's native `PdfDocument` requires manual canvas drawing coordinates. SkiaSharp (MIT; an opt-in package for MAUI, not a default dependency; PDF output through `SKDocument.CreatePdf`) is the selected managed library for complex layouts; it avoids the AGPL-3.0 / commercial licence cost of iText7, which must not be used without a paid licence in a proprietary app. SkiaSharp ships a native library in the APK, whose size impact is measured in M3 `[UNCERTAIN — not measured]`; its MIT notice goes on the licences screen.

### 4.7 Security on the phone

- Secrets (SMTP password, sync and household keys, cloud tokens,
backup passphrase) use an
Android Keystore-backed secure-storage abstraction behind the existing
ports. The architecture must not depend on a deprecated storage helper;
the concrete Android implementation is selected during the spike and
keeps the key material outside the database itself.
- App lock: device biometrics in M1 (via `BiometricPrompt`).
- Database and logs in the app sandbox (internal storage). 

### 4.8 Free core and premium (A6) & Legal Compliance

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
3. **User-controlled data portability:** Nothing that keeps a user safe or keeps their data reachable is
paid: dose reminders, low-stock and expiry warnings, `.mrz` export
and import, and restoring a cloud backup stay free; only the
scheduled backup is premium. Export and import provide user-controlled portability; they do not by themselves establish GDPR Article 20 compliance, which depends on the regulation's stated conditions, including legal basis and the scope of data provided by the data subject.
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
checked on the phone only; a phone without its own individual premium
cannot join or publish a sync group or household. A family tier is
deferred until after M2 (DA10).

**Split** (decided by the product owner, 2026-10-07, DA6; catalogue
row amended by DA11):

| Feature (§3) | Tier | Why |
|---|---|---|
| Medicines, regimens, as-needed slots, dose times | Free | Core use |
| Stock, packages and expiry, intakes, count, history | Free | Core use |
| Low-stock (two stages), dose, expiry notifications and their actions | Free | Safety: never paid |
| Guided setup, five languages, accessibility, app lock with biometrics | Free | Core use |
| `.mrz` export and import | Free | Data remains reachable without paying |
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

DA10 was decided on 2026-10-08: defer the family tier until after M2.
Family products, grant mechanics, and the following historical price
proposal are out of M2 scope and are not offers: monthly € 2.99, yearly
€ 26.99, lifetime € 74.99. The prior 1.5× estimate was inferred and
has not been validated against market data.

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
for example 20, leaves a margin for testers who drop out. How the
plan uses the tracks is in §5.0.
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
Library 8 or later for new apps and updates since 2026-08-31, with an
extension available on request until 2026-11-01; version 8 removed
APIs of version 7 `[VERIFIED — secondary sources dated 2026; check the
Play Console policy status at S11]`). The MAUI options are the
`Xamarin.Android.Google.BillingClient` binding (reported
compatibility problems with MAUI) and Microsoft's MAUI
`BillingService` sample; `Plugin.InAppBilling` is archived
`[UNCERTAIN — secondary sources]`. Spike S11 settles the choice
before M2. **`[ANDROID EXPERT NOTE]`**: Because third-party plugins like `Plugin.InAppBilling` are deprecated/unmaintained, Spike S11 will implement a direct C# wrapper over Android's native `com.android.billingclient:billing` library, version 8 or later (the version Play requires at the time of S11). Device-side receipt validation and `PurchasesUpdatedListener` handling pending purchases will be implemented in M2.
- No backend (B.1 §1.3): purchases are verified and acknowledged on
the device, and the entitlement is cached so that premium works
offline, with a grace period before it lapses. Without server-side
verification a modified APK can unlock premium; accepted. With the
app code private, this needs a patched binary, not a rebuild.
- One purchase covers one store account on all its phones (restore
purchases).

**Family premium (deferred beyond M2 by DA10 on 2026-10-08).** M2
implements only individual Play products. The following is retained as
historical design exploration, not an accepted requirement or committed
implementation. Revisit family products, entitlement sharing, prices,
and store-policy implications after M2 using product evidence.

- **The stores do not do it on Android.** Google Play Family Library
shares paid apps but not in-app products or subscriptions `[VERIFIED
— secondary sources; no change found since its launch]`. Apple
Family Sharing does share subscriptions and non-consumables with up
to five family members, is turned on per product and cannot be
turned off `[VERIFIED — App Store Connect Help]`. One mechanism on
both platforms means doing it in the app.
- **Products.** Three more products, family monthly, yearly and
lifetime (prices above). An individual subscription moves to the
family one through the store's upgrade with proration; an individual
lifetime owner gets a one-time "family upgrade" product priced at
the difference `[INFERRED — Play Billing replacement modes cover
subscriptions; a separate one-time product for the lifetime case]`.
- **Grant through the household.** The buyer's phone verifies its
purchase on the device, as for individual premium, and writes a
family grant into the household log: product, valid-until date
(renewal date plus a grace period for subscriptions, none for
lifetime), buyer device and the time of the last check. The grant is
sealed with the household key like every household operation (§5 of
the household design). The other phones read it and are premium
while it is valid; the buyer's phone refreshes it at each check.
Household settings are administrator state (household §4.3), so the
buyer acts with an administrator profile, or an administrator
accepts the grant on first purchase `[INFERRED — permission check on
apply to be designed in M2]`.
- **Shared format, no premium logic in public code.** The grant travels
as a `HouseholdSettingChanged` entry under an app-reserved setting
name, which the desktop stores and ignores, so the household format
and the desktop need no premium concept `[INFERRED — settings are a
name and value map; that the desktop keeps unknown names without
error is to be verified in M2]`. A new operation type would instead
stop older desktops (household design §12).
- **Limits.** At most six phones in the household, as the store family
groups `[VERIFIED — Google Play and Apple family groups have six
members]`; PCs do not count and stay free. Joining as a family
member needs a free place; when the plan ends, members fall back to
their own entitlement, or to the free core with no data lost
(principle 4). A member's own individual subscription is not
cancelled by the app; the app tells the member it can be cancelled.
- **What a family member can do.** Everything premium, including a
second household of their own only with their own premium: the grant
covers the household that holds it.
- **Abuse.** Without a server the grant cannot be checked against the
store from another phone; a member with a modified app could forge
one, the same accepted risk as a modified APK. The six-phone limit
caps sharing with strangers.
- **Store policy.** Selling through store billing an access that the
app extends to other users is common (family plans of password
managers and media services) `[UNCERTAIN — no Google Play or App
Store rule on it verified for third-party apps; to check before
M2]`. On iOS, Apple Family Sharing stays off so that a purchase is
shared through one mechanism only `[INFERRED]`.
- Cost: 5–8 more days in M2 `[INFERRED]`.

### 4.9 Android UI requirements

The product owner selected the Daily overview direction on 2026-10-08.
The screen architecture, M1–M5 functional requirements, accessibility
conditions, offline and sync states, and acceptance criteria are in
[`ANALYSIS-B1-UI-REQUIREMENTS.md`](ANALYSIS-B1-UI-REQUIREMENTS.md).
The production UI belongs to the private Android repository (A7); this
repository holds the public requirements and shared-core work.

The product owner also changed the provider priority: integrate Google
Drive before OneDrive in the Android client. Both remain supported. The
desktop transports shipped in their existing order; that history and
the sync format do not change.

---

## 5. Revised plan

Replaces B.1 §13 Phases 5 and 7 for Android. Every milestone from M1
ends with a pre-release build on the Play testing tracks (§5.0); M1 is
the first build testers can use on its own, without a PC or account.

### 5.0 Play tracks

| Track | Use in this plan |
|---|---|
| Internal testing | Development builds for the team, at any time; no milestone exit runs here |
| Closed testing | The milestone builds M1–M5 for the recruited testers, who are also license testers (§4.8). Each milestone exit runs on this track. The M1 exit is a 14-day closed test; before applying for production access after M5, the closed test must again meet the Play rule for personal accounts (at least 12 testers opted in for 14 consecutive days) |
| Open testing | Not used |
| Production | Public launch, only after the M5 exit (DA4) |

"Pre-release build" in the B.1 Android documents means a build on the
internal or closed testing track; no milestone build goes to
production before M5.

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

### M1 — Standalone core (first pre-release milestone)

Entry: M0 merged (D11, DA2, DA5 and DA12 were decided on 2026-10-08;
DA5 fixes the application id `com.vger70.medreminder`); D13 selected provisionally
(API 26), with its validation due before support is claimed and before
M2; Play Console account; private repository created (A7). M1 and
later milestones are pre-release builds (§5.0). D4 multi-device
delivery preferences are implemented in M2.

- Free features only (§4.8); no billing in M1, so its pre-release
  build needs no payments profile.
- MAUI app (`TrimMode=partial`), composition root, Android adapters
(paths, Keystore credential protector, notifications, exact alarms,
boot receiver, share sheet, file picker).
- First start: "Start here" and "Restore from file" paths of §4.1,
  disclaimer & Privacy Policy acknowledgment (§4.1), language (system
  language if supported, English otherwise; DA3),
  Android 13+ `POST_NOTIFICATIONS` runtime permission and exact-alarm
  permission; denied permissions have an explained degraded mode;
  guided setup without its e-mail choice.
- Medicines, regimens, as-needed slots, dose times and presets; main
list with forecast, warnings and search; stock, packages and expiry, intakes, count,
history and retraction.
- Notification planner with exact alarms: low stock (two stages), dose
reminders with actions, package expiry.
- Encrypted export and import; backup reminder (§4.3). These support user-controlled portability without asserting blanket GDPR Art. 20 compliance.
- One profile, administrator role; app lock with device biometrics;
system font scaling and dark theme; accessibility of B.1 §9.1
(screen-reader labels, no meaning by color alone); five languages;
sandbox and log rules.
- In the private repository (A7): CI Android job, signing outside the
repository, internal and closed testing tracks (§5.0), mobile packaging notes and mobile
user guides in the five languages. In this repository: the M0
refactor and any shared-core change the app needs.
- Exit: manual checklist on Android 14+ and on the D13 floor; the
14-day closed test runs on phones without a PC or account and loses
no data across app updates, reboots and an export/import cycle; no
health data in logs.
- Effort: 35–50 days `[INFERRED — strongly dependent on MAUI
experience]`.

### M2 — Cloud: backup, sync, household

Entry: M1 exit passed; D4 decided; Android API 26 validated by the M1
technical spike; DA10 family tier deferred beyond M2; Android halves of
S6 and S7 (Android OAuth clients, P14); spike S11 (billing); trader
status and payments profile in Play Console. Google Drive is the first
Android provider, followed by OneDrive.

- Premium infrastructure (§4.8): entitlement service, Play Billing
adapter, subscription and lifetime products, purchase and restore
screens, gates, license testers on the testing tracks. M2 offers
individual products only; purchases restore on phones using the same
Google Play account. Every M2 feature below is premium except restoring
a cloud backup.
- Google Drive sign-in on Android, then OneDrive sign-in; both use the
  existing provider transports and the shared sync model.
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
- Local data deletion (UI-17): remove the active profile's local copy
or all local profile data on this device, without deleting provider
copies or other devices' data. Free, like export.
- Exit: phone and desktop converge in the offline and conflict
scenarios of the B.1 checklist; a phone-first installation is joined
by a PC and the PC becomes master.
- Effort: 35–50 days, H6 and individual premium infrastructure
  included `[INFERRED]`; no family grant is included.

### M3 — Prescriptions, planning, views

Entry for M3, M4 and M5: M2 exit passed, since each carries premium
features and needs its entitlement gates. The confirmed order is M3,
then M4, then M5; public Play Store launch follows M5. Tiers as in §4.8:
the timeline (M3), catalogue search and
linking, shortage notices and equivalents (M4) are free, the rest
premium.

- Prescription request draft, lifecycle, repeatable prescriptions,
reminders; regional service links.
- Administrative deadlines with recurrence and reminders.
- Supply planner; calendar export; timeline; therapy report as PDF.
- Effort: 15–20 days `[INFERRED]`.

### M4 — Catalogue, scan, Italian services

Entry: M3 exit passed.

- Reference country, catalogue defaults and catalogue download and
refresh as in §4.4 (DA3), search, link to a medicine. Only public,
redistributable catalogues; the app remains usable worldwide without
one.
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
| M1 | Standalone core, first pre-release milestone | M0; D13/API 26 selected provisionally (D11, DA2, DA5, DA12 decided); Play account; private repository | 35–50 d |
| M2 | Individual Premium infrastructure; cloud backup, sync, household, profiles, roles and PIN, local data deletion | M1; D4; API 26 validated; S6/S7 Android halves; S11; trader status | 35–50 d |
| M3 | Prescriptions, planning, views | M2 exit | 15–20 d |
| M4 | Catalogue (DA3 defaults), scan, Italian services | M3 exit | 15–20 d |
| M5 | Email; last pre-release milestone | M4 exit | 10–15 d |

Total for M0–M5 about 120–170 developer-days, against 60–90 for B.1 Phases 5
and 7. The difference is the standalone requirement (M0, backup,
phone-first household) and the features added to the desktop since
2026-09-26. Evolution V (vital tracking) is outside the total (§4.3a). The
confirmed delivery order is M3 → M4 → M5 and the public Play Store
launch follows M5 (DA4, 2026-10-08).

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

From B.1: D11 was decided on 2026-10-08 (reject; the private app
repository adopts an equivalent strip target, see B.1 §16). D4 was
decided on 2026-10-08. D13 selects Android API 26 provisionally; validate
MAUI, alarm and Play compatibility in the M1 technical spike. iOS 15
remains an inferred proposal and is outside this Android decision.

New (prefix DA, to keep them apart from the B.1 and household
numbering; the D.1 vital-tracking decisions use the prefix DV):

| # | Decision | Options | Recommendation |
|---|---|---|---|
| DA1 | The Android app works without a PC and without an account | Yes; no | **Requested by the product owner, 2026-10-06** |
| DA2 | Backup on a standalone phone | Export only; export and cloud backup; Android Auto Backup | **Decided 2026-10-08**: export in M1, cloud backup in M2; Auto Backup stays off |
| DA3 | Catalogue, language and country parity with desktop | Country/catalogue/language support differs from desktop; same support as desktop | **Decided 2026-10-08; revised 2026-10-08**: same sources (IT, EU, ES, FR, US) and languages (it, en, fr, es, de) as the desktop. UI language: system language if supported, English otherwise; user-changeable; independent of the country. Catalogue from the reference country: national catalogue if one exists; otherwise EMA for an EU/EEA country; otherwise no catalogue and manual entry. Android remains usable worldwide; catalogues are optional, public and redistributable. Details in §4.4; desktop integration in `ANALYSIS-DESKTOP-COUNTRY-CATALOGUE-DEFAULTS.md`. |
| DA4 | Order of M3–M5 and public launch | M3 → M4 → M5; M5 before M3/M4 | **Decided 2026-10-08**: M3 → M4 → M5; M1–M5 are pre-release builds on the testing tracks (§5.0); public Play Store launch only after M5. |
| DA5 | Play account type and application id | Personal; organization (D-U-N-S); id such as `com.vger70.medreminder` | **Decided 2026-10-08**: personal account; application id `com.vger70.medreminder` (cannot change after the first upload). A personal account needs the closed test of §5.0 (at least 12 testers for 14 consecutive days) before production access |
| DA6 | Free and premium split | §4.8 table; other | **Decided 2026-10-07**: §4.8 table |
| DA7 | Prices | € 1.99 / 20.99 / 59.99; € 1.99 / 17.99 / 49.99 with a 14-day trial | **Decided 2026-10-07**: € 1.99 / 17.99 / 49.99 with a 14-day trial |
| DA8 | Licence of the mobile app code | Apache-2.0 in this repository; private repository, proprietary | **Decided 2026-10-07**: private repository for the Android and iOS apps; desktop and shared core stay Apache-2.0 here (A7) |
| DA9 | Donation links on mobile | Keep; drop | **Decided 2026-10-07**: drop |
| DA10 | Premium across a family | Per store account; family tier granted through the household; any premium phone covers its household | **Decided 2026-10-08**: defer family products and grants until after M2; M2 offers individual products, restorable on phones using the same Play account |
| DA11 | Catalogue search and linking in the split | Premium (as decided in DA6); free, scan stays premium | **Decided 2026-10-07**: free, scan stays premium (§4.8) |
| DA12 | GDPR / Store Onboarding | Explicit privacy acknowledgment banner; hidden in settings | **Decided 2026-10-08**: explicit acknowledgment at first start, without claiming it is always GDPR consent (§4.1, UI-00, UI-17) |
| **DA13** | **Battery Optimization Handling** | No prompt; contextual optional guidance to battery settings | **Decided 2026-10-08**: show a contextual, non-blocking invitation to Android battery settings when OS restrictions threaten reminders or sync; declining keeps the app usable and explains possible delays. This guidance cannot guarantee timely background work. |
| DA14 | PDF library for the mobile report | Android `PdfDocument`; SkiaSharp; iText7 | **Decided 2026-10-08**: SkiaSharp (MIT); iText7 excluded (AGPL-3.0 or paid licence) (§4.6) |

---

## 7. Risks

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| Data loss on a standalone phone (lost, reset, uninstalled) | Medium | High | Export and backup reminder in M1 for everyone; scheduled cloud backup in M2 with premium (§4.3) |
| Exact-alarm permission refused | Medium | Medium | Explanation at first start; inexact fallback stated in the UI (S5) |
| **App process killed by aggressive OEM Battery Managers (Xiaomi/Samsung)** | **High** | **High** | Show a contextual, non-blocking explanation and route to Android battery settings when restrictions may delay reminders or sync; declining remains possible and the app explains that this guidance cannot guarantee timely background work. |
| Background work late (emails, backups, feeds) | High | Low–medium | Run on app open; status shows the last run; PC as master when present |
| Scope growth beyond 170 days | Medium | Schedule | M0–M5 are milestones with separate exits; public launch follows M5, and the M3 → M4 → M5 order is fixed by DA4 |
| Desktop and phone notify on different days | Low | Medium | One rule set, planner parity tests (§4.2) |
| Premium unlocked by a modified app | Low (app code private, DA8) | Low–medium | Accepted without a backend |
| Shared core and private app drift apart | Medium | Medium | App pins a tag of this repository; shared changes land here first |
| Users reject paying for sync that is free on the desktop | Medium | Medium | Generous free core; clear premium value; trial |
| A household with several phones finds one premium per account too dear (DA10) | Medium | Low–medium | Family tier is deferred beyond M2; revisit after observing individual-tier use |
| Family grant shared outside the family or forged | Low–medium | Low | Deferred with DA10; no family grant in M2 |
| Store review objects to access extended by the app | Low `[UNCERTAIN]` | Schedule | Reassess store policy only if a family tier is reconsidered after M2 |
| Store review or rejection over health-data handling | Medium | High | Explicit privacy acknowledgment at first start (§4.1), accurate Privacy Policy and Play data-safety form, no unsupported GDPR claims |
| Data lock-in / portability concern (including GDPR Art. 20 questions) | Low | High | `.mrz` export and import stay free (§4.8 principle 3); the plan does not claim that export alone establishes Art. 20 compliance |
| DSA / Tax compliance oversight | Low | Medium | Consult tax adviser prior to publishing paid Tier |
| MAUI billing binding immature | Medium | Schedule | Spike S11 before M2 |
| Store review of a health app with subscriptions | Low–medium | Schedule | Clear non-medical positioning, no safety feature paid (§4.8) |

---

## 8. Corrections to other documents

`docs/STATUS.md` and `docs/EVOLUTION.md` are not updated for these
corrections; the `ANALYSIS-B1-*` documents take precedence over them.

- `ANALYSIS-B1-MOBILE-SYNC.md` §1.2, §9.1, §10, §13 Phases 5 and 7:
for Android, superseded by this document.
- `ANALYSIS-HOUSEHOLD-MASTER-DEVICE.md` §10 and §13 (H6): the phone
sends email from M5, not from B.1 Phase 7; household creation, join,
roles and PIN on the phone are part of M2.
- `ANALYSIS-B1-MOBILE-SYNC.md` §16: D11 and D4 decided; validate the
  provisional API 26 floor during the M1 spike.
- `ANALYSIS-B1-MOBILE-SYNC.md` §10 (donation links) and §16 (D14): no
donation links on Android or iOS (DA9); D14 is settled by it.
- `ANALYSIS-B1-UI-REQUIREMENTS.md`: selected Daily overview,
  M1–M5 screen behavior, settings/privacy, Free/Premium flows,
  accessibility, offline/sync states, and acceptance criteria for the
  private Android UI.
- `ANALYSIS-B1-MOBILE-SYNC.md` §8.4: the designated mail device is
  superseded by the household master (household R5, C4); backlog B5-03.
- `ANALYSIS-D1-VITAL-TRACKING.md` and `docs/DPIA-VITAL-TRACKING.md`:
  D.1 as a whole (desktop and mobile) is evolution V, after the store
  release; decisions renumbered DV1–DV10.
- `ANALYSIS-DESKTOP-COUNTRY-CATALOGUE-DEFAULTS.md`: desktop integration
  of the DA3 country and catalogue defaults.
- `ANALYSIS-B1-MOBILE-SYNC.md` §16: provider
  priority updated to Google Drive first, then OneDrive for Android;
  the desktop implementation history remains OneDrive first.

---

## 9. Change log for this document

Numbering note: two entries carry revision 14 and none carries
revision 17; the numbers are kept as recorded.

- **2026-10-08 — revision 22:** DA14 decided: SkiaSharp for the mobile
  PDF report, iText7 excluded. B.1 D12 (iCloud transport) decided: no.
- **2026-10-08 — revision 21:** Decisions D11 (reject; equivalent strip
  target in the private repository), DA2, DA5 (personal account,
  `com.vger70.medreminder`) and DA12 recorded. Scope boundary between
  release 1 (M0–M5, no vital data) and evolution V (D.1 on desktop,
  Android and iOS, after the store release) added; requirement A9;
  §4.3a rewritten with the release 1 constraints and the evolution V
  entry.
- **2026-10-08 — revision 20:** Consistency review. DA3 revised: UI
  language from the system (English fallback), catalogue from the
  reference country (national, else EMA in the EU/EEA, else none), desktop
  integration in a new analysis. Vital tracking (M1b) removed from
  M0–M5 and deferred to after the store release. Play track usage
  defined (§5.0) and "internal-test" replaced by "pre-release". Billing
  Library 8 required since 2026-08-31. Email through the household
  master (B.1 §8.4 superseded). App lock in M1 in §3; local data
  deletion in M2; DA2 and DA12 adopted as baseline pending confirmation;
  M1 entry states D13 as provisional; M4 entry corrected; duplicate
  store rules and risks merged; SkiaSharp and 500-alarm statements
  corrected; plan marked approved; STATUS and EVOLUTION declared
  superseded by the B.1 documents.
- **2026-10-08 — revision 19:** Clarified DA3: included the available US catalogue and defined country-based catalogue/language defaults (including Spain → ES/Spanish), with EMA and English fallback where a national catalogue is unavailable; retained worldwide use and user language choice.
- **2026-10-08 — revision 18:** Recorded DA3 desktop parity for supported countries/catalogues/languages without restricting worldwide app use; confirmed M3 → M4 → M5, internal-only intermediate builds, and public Play Store launch after M5.
- **2026-10-08 — revision 16:** Recorded D4 notification distribution and lock-screen defaults; selected Android API 26 provisionally pending M1 validation; selected contextual non-blocking battery guidance (DA13); deferred family products and grants until after M2 (DA10); specified local-only data deletion.
- **2026-10-08 — revision 15:** Expanded the linked UX requirements across M1–M5, including the Free/Premium lifecycle, settings/privacy, and full feature-flow coverage; corrected first-start privacy copy to account for optional encrypted cloud transfer and require privacy/legal review.
- **2026-10-08 — revision 14:** Selected the Daily overview UI; added linked implementation requirements and acceptance criteria; set Google Drive first and OneDrive second for Android without changing the historical desktop provider rollout.
- **2026-10-07 — revision 14:** Review corrections: D.1b is an explicit
  post-M1 milestone with separate estimation; removed the duplicate
  backup reminder; limited GDPR portability wording; marked the
  500-alarm figure as unverified; clarified notification permissions,
  battery-optimization guidance and onboarding acknowledgment.
- **2026-10-07 — revision 13:** Integrated Android Expert Review. Added specific platform implementations (SAF for Storage, Jetpack Security for Keystore, `BiometricPrompt` for App Lock). Clarified `SCHEDULE_EXACT_ALARM` vs Play Policy restrictions. Added DA13 and battery management risk mitigation. Confirmed 500 alarm limit and Family Sharing store policy.
- 2026-10-07 — revision 12: Integrated Legal & GDPR Privacy Assessment. Added Requirement A8, local-only vital-data boundary, separate vital database, manual CSV/PDF transfer, and revised privacy onboarding language. Removed unsupported claims that local processing automatically requires or avoids GDPR consent and that every export automatically satisfies Article 20.
- 2026-10-07 — revision 11: DA10 reopened at the product owner's
request; family tier designed (three products, grant through the
household log under an app-reserved setting, six phones, PCs free,
Apple Family Sharing off); family price proposal; Google Play
Family Library does not share subscriptions; M2 35–50 days, total
120–170; risks on grant abuse and store review.
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
