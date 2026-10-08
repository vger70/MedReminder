# B.1 Android UI requirements

Status: product-owner selected the **Daily overview** direction on
2026-10-08. This document defines the UX requirements for the Android
client planned in `ANALYSIS-B1-ANDROID-PLAN.md`. It does not authorize
changes to the desktop UI or shared data model. M1–M5 builds are
pre-release builds on the Play internal and closed testing tracks
(`ANALYSIS-B1-ANDROID-PLAN.md` §5.0); public Play Store launch is after
M5. For the Android UI, this document is authoritative over the UI notes
of other analyses.

The companion presentation is
[`../../deliverables/MedReminder-Android-UI-e-requisiti-google-drive-first-fixed.pptx`](../../deliverables/MedReminder-Android-UI-e-requisiti-google-drive-first-fixed.pptx).
The source mockups are in [`../mockups/android-ui-proposals.svg`](../mockups/android-ui-proposals.svg).
Implementation slices and dependencies are tracked in
[`ANALYSIS-B1-ANDROID-IMPLEMENTATION-BACKLOG.md`](ANALYSIS-B1-ANDROID-IMPLEMENTATION-BACKLOG.md).

## 1. Product boundary

MedReminder is an organizational reminder, not a medical device. The
Android app records user-entered therapy and stock information and
shows estimates derived from that information. It must not diagnose,
recommend therapy changes, score adherence, or suggest substitute
medicines.

The Android app must work on its own, offline, and without a cloud
account. When sync is configured, its profile data converges with the
other devices through the existing encrypted sync model. The app does
not run a project-operated backend.

This document specifies **Android release 1** (M1–M5), which has no
vital-parameter tracking: no vital screens, menu entries, placeholders
or premium offers. Vital tracking (D.1,
[`ANALYSIS-D1-VITAL-TRACKING.md`](ANALYSIS-D1-VITAL-TRACKING.md)) is
**evolution V**, evaluated after the store release of the Android and
iOS apps (`ANALYSIS-B1-ANDROID-PLAN.md` §4.3a); its Android UX will be
added to this document, as a separate section, when it is scheduled.

## 2. Selected direction

The home screen is a **Daily overview**. It answers “What needs my
attention today?” before presenting the full therapy inventory.

The home screen presents, in this order:

1. Active profile and current local date.
2. A concise summary of today's scheduled reminders and attention
   items.
3. The next scheduled reminder, with its time, medicine, and
   user-entered dose description.
4. Medicines that need attention, ordered by their current status and
   estimated days remaining.
5. Shortcuts to record an intake and add a stock package.

The three concepts in the mockup are Daily overview, Agenda, and
Medicine stock. Daily overview is the selected home structure. Agenda
and the stock list remain destinations inside the app, not alternate
home concepts.

## 3. Information architecture

Use four persistent primary destinations:

| Destination | Responsibility |
|---|---|
| **Today** | Daily summary, next reminder, attention items, quick actions |
| **Medicines** | Searchable active/inactive medicine list and medicine details |
| **Agenda** | Scheduled reminders grouped by local day and time |
| **More** | Profiles, sync, conflicts, backup, Premium, settings (including privacy and data), help, and app information |

The labels are the English reference names; the displayed labels come
from the localization dictionaries of the five languages.

The full therapy timeline remains in milestone M3 as set out in the
Android plan. “Agenda” in M1 is limited to scheduled reminders and does
not imply that the complete therapy timeline has moved earlier.

## 4. Functional requirements

### UI-00 — First start and setup

The M1 first-start flow shall offer standalone setup and restore from an
encrypted `.mrz` file. It shall present the required disclaimer and
Privacy Policy acknowledgment before setup, then provide the guided
setup for the first profile and medicines. Joining an installation and
restoring from a cloud backup are M2 paths. Privacy copy shall distinguish
local-only use from optional encrypted transfer to the user's selected
cloud provider; it shall not imply that cloud sync sends no data off the
device.

**Acceptance:** a new user can complete the standalone path without a
cloud account; restore and join options identify the source and
milestone available to the user.

### UI-01 — Daily overview

On opening the selected profile, the app shall show the active profile,
local date, next reminder, and medicines requiring attention without
requiring the user to open a menu.

**Acceptance:** the screen remains understandable when there are no
medicines, no reminders today, no attention items, or no cloud account.

### UI-02 — Attention status

The app shall show medicine stock, forecast days remaining, estimated
run-out date, and warning state from the existing application/domain
results. Status shall be conveyed by text and may also use color or an
icon.

**Acceptance:** each warning remains identifiable in monochrome and
with color-vision deficiencies; no forecast is invented when required
source data is unavailable.

### UI-03 — Medicine list and detail

The user shall be able to search medicines, filter active/inactive
entries, open details, and access the supported add, edit, deactivate,
delete, regimen, suspension, schedule, as-needed slot, and extra-dose
workflows from the medicine context. Dose-time presets shall be clearly
identified as device-local and shall not appear as synchronized profile
data.

**Acceptance:** returning from a detail preserves the prior list
context and search/filter state.

### UI-04 — Stock actions

The user shall be able to add a package, add or correct stock, and
perform a guided stock count. Package details shall include supported
expiry information and its current warning state. Each stock action
shall explain the quantity that will be recorded and its effect before
confirmation.

**Acceptance:** cancellation leaves stored data unchanged; completion
shows the result and the updated stock forecast.

### UI-05 — Intake actions

The user shall be able to record or update an intake from the medicine
detail and today's scheduled reminder. Notification actions shall
provide only the actions defined by the Android plan, such as opening
the medicine or list and snoozing a dose reminder; they do not record
an intake directly.

**Acceptance:** the selected medicine, slot, time, and status are
reviewable before save; the UI describes intake records as
organizational records, not adherence or medical guidance.

The medicine context shall also expose intake history and the supported
retraction flow for a mistaken entry. Retraction shall be presented as a
new corrective action, with the affected entry and resulting stock
change reviewable before confirmation.

### UI-06 — Local-first behavior

All M1 daily tasks shall work without a network connection or cloud
account. M1 shall label data as local to this device where that fact
affects portability or recovery.

**Acceptance:** airplane mode does not block reading, editing, stock
updates, intake recording, or local reminder scheduling.

### UI-07 — Sync and provider status

When M2 sync is configured, the app shall show the last successful sync
time, whether local operations are pending, and whether the provider
needs sign-in. “Saved on this device” and “synchronized” shall never be
used interchangeably.

Cloud provider delivery order is **Google Drive first, then OneDrive**.
Both providers remain supported. Provider-specific authentication
shall not change the shared sync model or expose plaintext profile
data to cloud storage.

**Acceptance:** offline, pending, synchronized, authentication-needed,
and conflict states are distinct and available from Today and More →
Sync.

### UI-08 — Conflicts

The app shall present conflicts needing user judgment with the affected
field, local and remote values, and the available resolution actions.

**Acceptance:** the user can review and resolve a listed conflict
without losing unrelated pending local changes; dismissing a conflict
does not imply that a value was restored.

### UI-09 — Notifications and permissions

The app shall explain the purpose and effect of notification and exact
alarm permissions before requesting them. If exact alarms are denied,
the app shall explain that reminder timing may be approximate and
continue with the documented fallback. Dose reminders, two-stage
low-stock warnings, package-expiry notifications, and shortage notices
shall be distinct from premium prescription and administrative-deadline
reminders. Dose reminder actions shall match the plan: open the
medicine/list or snooze for 15 minutes; from M3, the prescription action
may prepare a request. These actions shall not silently register an
intake or submit a prescription request.

From M2 (several devices), dose reminders are delivered by default by
the phone on which they are scheduled and low-stock alerts on every
paired device; users may override these defaults per device and
notification kind. Medicine details on the lock screen are hidden by
default from M1; users may change this per device. Settings shall explain that Android may apply
its own lock-screen visibility rules.

**Acceptance:** denying a permission does not block access to app data;
the current reminder capability and route to Android settings remain
clear.

### UI-10 — Profiles and settings

The app shall expose app lock, language, notification preferences, sync
status, backup status, privacy and data controls, Premium status, help,
licences, and app information in a clearly grouped settings area.
Biometric app lock is M1; multiple profiles, roles, PIN, and profile
switching are M2. The app shall not imply that users need a MedReminder
account; provider sign-in and Google Play purchases are separate
concepts.

**Acceptance:** a profile switch makes the active profile visible and
does not leave the previous profile's medicine data on screen.

### UI-11 — Export, import, and backup

The user shall be able to export and import an encrypted `.mrz` file
through Android's document/share flows in M1. Explain that import
replaces the selected profile and show the profile identity before the
user confirms. Cancellation or a failed file operation shall preserve
the current profile. When sync is enabled in M2, explain that importing
starts a new sync generation and requires explicit confirmation.

Cloud backup setup shall be separate from profile sync. It shall show
the selected provider, last backup time, scheduled-backup state, and
restore action. Restoring a cloud backup and manual `.mrz` export/import
remain free; scheduled automatic cloud backup is Premium. The M1 backup
reminder shall appear after 30 days without an export/backup while sync
is off, say that data is local, and offer the free export action.

**Acceptance:** the user can distinguish export, cloud backup, restore,
and sync; each operation identifies its source/destination and whether
it replaces a profile; a cancelled or failed operation does not report
success.

### UI-12 — Sync, household, and devices

M2 shall provide guided create/join flows for an installation, including
the supported pairing code/QR and passphrase paths, provider
authentication, profile selection and role/PIN checks. The provider
order is Google Drive first, then OneDrive. Show a clear progress,
offline, pending, synchronized, sign-in-required, conflict, and
recoverable-error state. The app shall explain that sync uses the user's
cloud storage and encrypted data, and shall distinguish provider sign-in
from a MedReminder account.

The household area shall expose the current master device, paired
devices, and supported device removal/key-rotation flow. Before removal
or a generation reset, identify the affected device/data and the
consequence for remaining devices; require explicit confirmation.
When a phone is master and a PC joins, present the planned proposal to
hand the master role to the PC with the implications for scheduled
backup and email. Do not change master role without user confirmation.

**Acceptance:** users can work offline with local changes preserved;
every displayed sync state corresponds to the sync service result;
device removal, conflict resolution, or pairing failure does not
silently discard unrelated pending changes.

### UI-13 — Free and Premium access

The UI shall follow the tier split in `ANALYSIS-B1-ANDROID-PLAN.md`
§4.8. M1 is free and has no billing flow. In M2, premium offers and
entitlements shall use Google Play Billing and the in-app entitlement
service; product and entitlement status shall remain understandable
offline from the cached entitlement state.

| Free, always available | Premium-gated |
|---|---|
| One person on one phone; medicines, regimens, stock, intakes, history, and medicine reminders | Sync, household, pairing, and additional devices |
| Accessibility, five languages, biometric app lock, medicine list, forecasts, and read-only therapy timeline | Scheduled automatic cloud backup; extra profiles, roles, and PIN |
| Encrypted `.mrz` export/import; restore from cloud backup | Email automation; prescriptions and their reminders; administrative deadlines and supply planning |
| Catalogue search/link, shortage notices, equivalents and information links | Calendar export and PDF report; barcode scan/restock by scan |

The UI shall not gate medicine reminders or other safety-related
medicine notifications, accessibility, data export/import, cloud backup
restore, catalogue search/link, or public medicine information behind
payment. Manual entry shall remain available when catalogue access is
unavailable. Timeline is read-only and free; reports and the other M3
planning tools follow the plan's Premium split.

Premium explanations shall be contextual: show them when a user chooses
a gated feature, preserve the free path, and avoid interrupting an
unrelated medicine task. Before purchase, show the included features,
price, billing period, trial length where offered, renewal/cancellation
terms supplied by Play, and the action to start or restore a purchase.
Do not invent or hard-code store terms. Provide a visible Premium status
page with product, trial/renewal or lifetime state, and the store
management/restore routes.

If a purchase is pending, unavailable, or cannot be refreshed, explain
the state and keep non-premium tasks usable. A cached valid entitlement
shall continue to work offline according to the plan's grace policy. If
Premium expires, retain all existing data and keep it visible and
exportable; stop only gated actions/reminders/automation, identify what
stopped, and explain how to renew or continue with the free features. If
the phone is household master and a PC is present when Premium expires,
offer the planned master-transfer path before sync-dependent automation
stops. Handing the master role to the PC stays possible after Premium
has ended, since it is what keeps email and scheduled backup running on
the free desktop (plan §4.8 principle 4). Do not delete extra-profile data or create a data hostage. Test
accounts shall use Play license testing, not an in-app tester bypass.

M2 shall offer individual Play products only. One individual purchase
shall be restorable on phones using the same Google Play account, as
specified in the Android plan. A family tier, household grants, and
family prices are deferred until after M2 and shall not be shown as
available offers or implemented as M2 entitlements.

**Acceptance:** every gate has an accessible explanation and a safe
back/cancel path; entitlement refresh, purchase, restore, offline,
grace, and expiry states are distinguishable; no free feature in the
left column becomes unavailable when billing fails or Premium ends.

### UI-14 — Prescriptions, planning, and reports (M3)

M3 shall add the premium prescription draft/share flow, prescription
lifecycle (requested, issued, collected), repeat entries, regional
prescription-service links, and "valid-until" reminder; recurring
administrative deadlines and their reminders; supply planning; calendar
`.ics` export; and report/PDF generation and sharing. A prescription
notification action shall open the preparation flow, not submit or mark
a request as sent. These flows shall be reachable from the
medicine detail, Agenda, and the relevant planning section without
moving the M1 medicine agenda out of its existing role.

The full therapy timeline is read-only and free. The report/PDF and
other premium planning actions shall have contextual gates under UI-13.
Before external sharing, identify the file/content being handed to the
Android share sheet; do not imply that a shared file remains private to
MedReminder.

**Acceptance:** reminder and lifecycle status is derived from the
existing application/domain results; calendar/PDF cancellation is
recoverable; no screen describes the report as clinical advice or
adherence scoring.

### UI-15 — Catalogue and scan (M4)

Defaults follow DA3 (`ANALYSIS-B1-ANDROID-PLAN.md` §4.4):

- The UI language is the system language when it is one of the five
  supported languages (Italian, English, French, Spanish, German),
  English otherwise. The user can change it in Settings at any time. It
  is set from M1 and never changed by a country or catalogue choice.
- The reference country is preselected from the device region setting,
  without location permission, and the user confirms or changes it.
  When the region is unknown, nothing is preselected.
- The catalogue is preselected from the reference country: the national
  catalogue when one exists (Italy, Spain, France, US); otherwise the
  EMA (EU) catalogue for an EU/EEA country; otherwise no catalogue. The
  user may pick a catalogue explicitly or none.

The app remains usable worldwide; catalogue coverage does not restrict
installation or core use. Where a public dataset is available and
redistributable, the catalogue is an optional aid for searching and
linking medicines. With no catalogue selected, medicine entry is manual
and no catalogue is downloaded. Manual medicine entry remains available
everywhere, including offline. Android shall use the feed delivery
behavior in the Android plan, show catalogue availability and last
refresh, and explain the source. Country-specific shortage lists,
equivalents and public information are shown only where desktop data
supports them (Italy); they remain free. Barcode scan and
restock-by-scan are Premium and shall offer a clear manual alternative.

**Acceptance:** search/linking and scan are visibly different access
levels; changing country applies the catalogue default (national, EMA
for EU/EEA, or none) and never changes the UI language; with no
catalogue the medicine form works in manual mode without error. No
private profile data is sent by catalogue downloads; scan permission
denial does not block manual entry or stock correction.

### UI-16 — Email automation (M5)

The email settings flow shall cover SMTP configuration, recipients,
caregiver copies by notification type, low-stock email, and weekly
digest. It shall state that sends can be delayed by hours because work
runs when the app opens and as best-effort background work. Do not label
queued email as sent. Store credentials securely and never display
saved secrets.

Only the household master sends email, for every profile of the
installation (household R5, C4); the B.1 designated mail device is
superseded. When the phone is master, explain its responsibility for
email; on a non-master, show which device sends and keep user-initiated
`mailto:` available. If a PC joins, the planned master-transfer proposal shall explain
the effect on email before the user decides. Email and its reminders
are Premium; ordinary medicine reminders remain free.

**Acceptance:** recipient and message categories are reviewable before
save; invalid configuration and send failures have recovery guidance;
no success state claims delivery; at most it reports that the SMTP
server accepted the message.

### UI-17 — Privacy and data controls

`More → Settings → Privacy and data` (its canonical location, UI-18)
shall remain available after onboarding and provide:

- A link to the current Privacy Policy and a concise summary of what is
  stored locally, what is encrypted before cloud-provider transfer, and
  that MedReminder has no project-operated backend or analytics/crash
  reporting SDKs.
- A clear distinction between local save, `.mrz` export, cloud backup,
  and profile sync, including the provider currently configured and the
  fact that Android system backup is disabled for the profile database.
- Direct routes to encrypted export/import, provider/sync settings,
  app lock, notification preferences, permissions, and Android's
  lock-screen notification visibility settings.
- Notification privacy guidance: medicine details are hidden by
  default on the lock screen, with a per-device in-app control to show
  them. Explain that Android's own visibility settings may also affect
  what appears.
- An in-app local-data deletion flow (M2; free). Let the user select the active
  profile's local data or all MedReminder profile data stored locally
  on this device, show the exact scope and affected profile/device,
  suggest exporting an encrypted `.mrz` file first, and require an
  explicit confirmation. Explain that this removes the selected local
  copy only; it does not delete provider/cloud copies or data on other
  devices. For a synced profile, explain that its cloud copy remains and
  could be downloaded again if the device reconnects or rejoins. Pause
  sync for that profile on this device after deletion; require an
  explicit restore/reconnect action before downloading it again.
  This is distinct from removing a device or profile from a household.
  Do not claim secure erasure or remote deletion unless the underlying
  operation guarantees it.
- An explanation before handing data to another app through the share
  sheet. The operating system's destination app controls the shared
  copy after handoff.

Onboarding acknowledgment shall be recorded locally with the policy
version and date, without treating acknowledgment as cloud consent.
Connecting a provider shall be a separate, explicit opt-in and shall
state that encrypted data is uploaded to that provider. A disconnect
confirmation shall describe which operations stop and confirm that
local data remains on the device. Do not promise remote deletion unless
the provider operation confirms it.

**Acceptance:** users can reopen the policy and understand where their
data is stored, whether sync is enabled, what is sent to a provider,
and how to export data without buying Premium.

Deletion shall respect the active profile and household role
permissions. If the user cannot delete the selected scope, explain who
can do so. The confirmation and completion states shall distinguish
local removal from remote/provider deletion; disconnecting sync alone
shall not be described as deleting either copy.

### UI-18 — Settings map and support

Use stable, named groups under **More → Settings**:

| Group | Entries and milestone |
|---|---|
| Profile and security | Active profile and switch (M2); roles/PIN (M2); biometric app lock (M1) |
| Reminders | Per-device, per-kind notification preferences (M2; dose on phone, low-stock on every device by default); lock-screen detail visibility (hidden by default); notification and exact-alarm permission status; Android settings route; contextual battery-restriction guidance |
| Appearance and language | Follow system light/dark appearance and text scaling; language selector (M1, five supported languages; default system language, English otherwise) |
| Backup and sync | `.mrz` export/import (M1); provider, sync, conflicts and devices (M2); cloud backup and restore (M2) |
| Privacy and data | UI-17 privacy summary, data routes and local data deletion (M2) |
| Premium | Entitlement, offer, purchase/restore and Play management (M2 onward) |
| Email | SMTP and recipient settings (M5) |
| Reference country and catalogue | Country, catalogue or none (M4, UI-15) |
| Help and about | Help, version, privacy policy (link to the same policy as UI-17), open-source/third-party licences |

When Android battery restrictions may delay reminders or sync, the app
shall show a contextual, non-blocking invitation to review the relevant
system setting. The user may decline and continue using the app; explain
the possible delay and provide a route back to the setting without
repeatedly interrupting unrelated tasks.

Do not put Premium controls in place of general settings or make privacy
information conditional on purchase. Settings reachable on a shared
device shall respect the active profile and household role permissions.

**Acceptance:** every setting has one canonical location; direct links
from a blocked feature, permission explanation, or error state return
to the relevant setting; unavailable future-milestone entries are not
shown as active controls.

### UI-19 — Shared workflow behavior

- Forms shall distinguish required from optional fields, validate near
  the relevant input, preserve entered values after validation or
  recoverable network failure, and identify the saved result.
- Back/cancel shall return to the prior context. If a form has unsaved
  changes, ask before discarding them. Do not require confirmation for
  routine reversible navigation.
- Confirm destructive or externally consequential actions immediately
  before they happen, name the affected profile/device/data, and state
  the expected consequence. Cancel leaves stored data unchanged.
- Loading and stale states shall not be presented as empty/zero data.
  Long-running sync, import, restore, scan, and export work shall show
  progress or a clear ongoing state; errors shall retain a recovery
  route.
- Every action shall give a result appropriate to its outcome: saved
  locally, queued for sync, synchronized, exported, restored, failed,
  or cancelled. Never show a success state for a queued or unconfirmed
  operation.
- Lists and navigation shall retain the active profile context. Returning
  from detail shall preserve the list position and search/filter state;
  switching profile clears the previous profile's visible content
  before rendering the new one.
- Dates and times use the device locale and local time zone. Schedule
  changes and time-zone/clock changes shall be reflected from the
  existing notification planner, not calculated in presentation code.

**Acceptance:** a user can cancel or recover each supported workflow
without accidental data loss; the visible state distinguishes local
save, pending work, and confirmed completion.

## 5. Accessibility and presentation

| Requirement | Acceptance condition |
|---|---|
| System text/display scaling | Essential text and actions remain visible and operable at supported Android font and display scales; content can scroll rather than clip. |
| Screen readers | Interactive controls expose a meaningful label, role, state, and logical reading order. Decorative icons are not announced; status changes and validation errors are announced without moving focus unexpectedly. |
| Non-color state encoding | Every warning and sync state has text; color and iconography are supplementary. |
| Touch and focus | Interactive touch targets are at least 48 × 48 dp; every action is available without a swipe-only gesture; keyboard/accessibility focus follows the visual order. |
| Localization | UI strings ship in Italian, English, French, Spanish, and German; layouts tolerate expansion and longer translations. |
| Motion | No state or task depends on animation; respect Android reduced-motion settings where applicable. |
| Theme | Follow system light/dark appearance; keep readable contrast in both. |

## 6. Error, empty, and recovery states

- Empty profile: explain that no therapy is recorded and provide the
  add-medicine action.
- No cloud configured: state that data is local and keep all M1 actions
  available.
- Offline with pending operations: state that edits are saved locally
  and will sync after reconnection.
- Provider sign-in expired: retain local data and identify the provider
  sign-in action.
- Conflict: explain what needs review and keep unrelated local changes.
- Missing forecast inputs: state which data is missing; do not show a
  fabricated run-out date.
- Permission denied: explain the feature affected and how to change the
  permission later.
- Purchase pending or billing unavailable: explain that Premium has not
  yet been confirmed (or show the cached entitlement state); preserve
  free access and offer retry/restore.
- Backup stale: show the date and local/device status, and offer free
  export or the configured backup action without claiming a backup ran.
- Catalogue unavailable: explain that online search is unavailable and
  provide manual entry.
- Email delayed or failed: distinguish queued, attempted, failed and
  accepted-by-server states; explain that background delivery may be late.

Errors shall be stated in plain language with a recovery action where
one exists. Notification contents shall follow the product's
notification privacy behavior. Logs shall not contain identifying
health data.

## 7. Implementation and verification constraints

- Implement the production UI in the private Android repository under
  the MAUI decision in `ANALYSIS-B1-ANDROID-PLAN.md`; this repository
  contains the public plan and shared portable core.
- UI writes go through Application use cases. The UI does not write
  repositories or database rows directly.
- Respect the existing sync, ledger, and notification planner rules;
  do not duplicate stock or schedule calculations in view code.
- Store profile data in the app sandbox and exclude the database from
  Android system backup. Do not add analytics or crash-reporting SDKs.
- Keep logs free of medicine names, notes, quantities tied to a person,
  email addresses, tokens, and passphrases.
- Verify the M1 UI on Android 14+ and the supported minimum version;
  verify sync/provider and conflict behavior in M2 on devices with
  intermittent connectivity.

## 8. Scope, milestone acceptance, and decisions

This document specifies the UX for M1–M5 without changing the Android
plan's milestone order or making a future-milestone feature available
early.

### Feature-to-requirement coverage

This mapping traces every user-facing area in the Android plan's §3
feature inventory to the requirement that owns its UI behavior.

| Android plan area | Milestone | UX requirements |
|---|---|---|
| First start, guided setup, join, restore | M1–M2 | UI-00, UI-11, UI-12 |
| Medicines, complex regimens, taper, as-needed slots, dose presets, search/filter | M1 | UI-03 |
| Packages, stock correction/count, intakes, history/retraction, expiry | M1 | UI-04, UI-05 |
| Dose, low-stock, expiry, shortage, prescription, and deadline notices/actions | M1–M5 | UI-09, UI-14, UI-15, UI-16 |
| Encrypted file export/import, backup, restore | M1–M2 | UI-11, UI-17 |
| Provider sign-in, sync, pairing, household, master role, devices, conflicts | M2 | UI-07, UI-08, UI-12 |
| Billing, entitlement, profile count/roles/PIN | M2 | UI-10, UI-12, UI-13, UI-18 |
| Prescriptions, administrative deadlines, supply planning, calendar/PDF, timeline | M3 | UI-02, UI-09, UI-13, UI-14 |
| Catalogue, barcode scan/restock, shortages, equivalents, information links | M4 | UI-13, UI-15 |
| SMTP, recipients, caregiver copies, low-stock email, weekly digest | M5 | UI-13, UI-16, UI-18 |
| App lock, permissions, appearance, localization, accessibility, licences/help | M1–M5 | UI-09, UI-10, UI-17, UI-18, §5 |
| Local data deletion | M2 | UI-17 |

| Milestone | UX exit condition |
|---|---|
| M1 — Standalone core | A user can set up or restore from `.mrz`, manage therapies and stock, record/retract intakes, receive local medicine reminders, change core settings, export data, and continue offline without a cloud account or purchase. After 30 days without export/backup while sync is off, the reminder offers export without blocking use. |
| M2 — Cloud and Premium | A user can buy/restore individual Premium, create/join an installation, use Google Drive before OneDrive, review sync/conflict/device state, use multiple profiles/roles, delete a selected local copy with accurate cloud-copy warnings, and back up/restore without losing local data when billing or network is unavailable. |
| M3 — Prescriptions and planning | Premium prescription and planning flows work with correct reminder states; free read-only timeline remains available; PDF/share and calendar actions explain the handoff. |
| M4 — Catalogue and scan | DA3 country and catalogue defaults apply, including the no-catalogue case; catalogue search/link and safety information remain free; scan is Premium with manual alternatives; offline catalogue limitations are clear. |
| M5 — Email | SMTP and recipient setup is understandable, delivery timing is described as best effort, and Premium expiry does not affect ordinary medicine reminders. M1–M5 builds remain pre-release builds; public Play Store launch is gated on M5 exit, the closed test required for production access, and final release checks. |

Decisions recorded on 2026-10-08: D4 notification distribution defaults
and lock-screen privacy; Android API 26 as the provisional D13 minimum,
subject to the M1 technical spike; DA13 contextual, non-blocking battery
guidance; deferral of DA10's family tier until after M2; DA3 (UI language
from the system, English otherwise; catalogue from the reference country:
national, else EMA in the EU/EEA, else none; app use worldwide); DA4 order
M3 → M4 → M5 with pre-release milestone builds and public launch after
M5; vital tracking (D.1) moved to evolution V, after the store release;
D11, DA2, DA5 (personal account, `com.vger70.medreminder`) and DA12
(onboarding acknowledgment) decided. The local data deletion behavior is specified
in UI-17 and affects only the selected device's local copy. Before public
release, complete the privacy/legal review and verify API 26 support.
Exact prices and Play-provided renewal/cancellation terms must come from
current store product data at runtime, not static UI copy.

The product owner changed provider priority on 2026-10-08: Google Drive
first, OneDrive second. Dropbox remains outside the current scope.

## Sources

- `ANALYSIS-B1-ANDROID-PLAN.md` §§1, 3–5, 6–7, especially §4.8.
- `ANALYSIS-B1-MOBILE-SYNC.md` §§1, 3–5, 8–11, 13, 16.
- `ANALYSIS-UI-MODERNIZATION.md` §1 (accessibility and attention-first
  desktop goals; phone implementation remains native/touch-first).
- Android accessibility guidance for a minimum 48 dp touch target:
  <https://developer.android.com/guide/topics/ui/accessibility/apps>.
- User-selected direction and requirements review, 2026-10-08.
