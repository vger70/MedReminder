# B.1 Android UI requirements

Status: product-owner selected the **Daily overview** direction on
2026-10-08. This document defines the UX requirements for the Android
client planned in `ANALYSIS-B1-ANDROID-PLAN.md`. It does not authorize
changes to the desktop UI or shared data model.

The companion presentation is
[`../../deliverables/MedReminder-Android-UI-e-requisiti-google-drive-first-fixed.pptx`](../../deliverables/MedReminder-Android-UI-e-requisiti-google-drive-first-fixed.pptx).
The source mockups are in [`../mockups/android-ui-proposals.svg`](../mockups/android-ui-proposals.svg).

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
| **Oggi** | Daily summary, next reminder, attention items, quick actions |
| **Terapie** | Searchable active/inactive medicine list and medicine details |
| **Agenda** | Scheduled reminders grouped by local day and time |
| **Altro** | Profiles, sync, conflicts, settings, help, and app information |

The full therapy timeline/report remains in milestone M3 as set out in
the Android plan. “Agenda” in M1 is limited to scheduled reminders and
does not imply that the complete therapy timeline has moved earlier.

## 4. Functional requirements

### UI-00 — First start and setup

The M1 first-start flow shall offer standalone setup and restore from an
encrypted `.mrz` file. It shall present the required disclaimer and
Privacy Policy acknowledgment before setup, then provide the guided
setup for the first profile and medicines. Joining an installation and
restoring from a cloud backup are M2 paths.

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
entries, open details, and access the supported edit, suspension,
schedule, and slot workflows from the medicine context.

**Acceptance:** returning from a detail preserves the prior list
context and search/filter state.

### UI-04 — Stock actions

The user shall be able to add a package, add or correct stock, and
perform a guided stock count. Each action shall explain the quantity
that will be recorded and its effect before confirmation.

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
and conflict states are distinct and available from Oggi and Altro →
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
continue with the documented fallback.

**Acceptance:** denying a permission does not block access to app data;
the current reminder capability and route to Android settings remain
clear.

### UI-10 — Profiles and settings

The app shall expose app lock, language, notification preferences, sync
status, and app information in the locations defined by the Android
plan. Biometric app lock is M1; multiple profiles, roles, PIN, and
profile switching are M2.

**Acceptance:** a profile switch makes the active profile visible and
does not leave the previous profile's medicine data on screen.

## 5. Accessibility and presentation

| Requirement | Acceptance condition |
|---|---|
| System text/display scaling | Essential text and actions remain visible and operable at supported Android font and display scales; content can scroll rather than clip. |
| Screen readers | Interactive controls expose a meaningful label, role, state, and logical reading order. Decorative icons are not announced. |
| Non-color state encoding | Every warning and sync state has text; color and iconography are supplementary. |
| Touch and focus | Interactive touch targets are at least 48 × 48 dp; keyboard/accessibility focus follows the visual order. |
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

## 8. Scope and decisions

This document defines the M1/M2 experience only to the extent already
present in `ANALYSIS-B1-ANDROID-PLAN.md`. It does not pull the M3–M5
features forward. Exact notification defaults (B.1 D4) and the Android
minimum version (D13) remain subject to their existing decision gates.

The product owner changed provider priority on 2026-10-08: Google Drive
first, OneDrive second. Dropbox remains outside the current scope.

## Sources

- `ANALYSIS-B1-ANDROID-PLAN.md` §§1, 3–5, 6–7.
- `ANALYSIS-B1-MOBILE-SYNC.md` §§1, 3–5, 8–11, 13, 16.
- `ANALYSIS-UI-MODERNIZATION.md` §1 (accessibility and attention-first
  desktop goals; phone implementation remains native/touch-first).
- Android accessibility guidance for a minimum 48 dp touch target:
  <https://developer.android.com/guide/topics/ui/accessibility/apps>.
- User-selected direction and requirements review, 2026-10-08.
