# B.1 Android implementation backlog

This backlog turns the Android plan and UX requirements into ordered,
reviewable implementation slices. It is a planning artifact for the
private Android repository and this repository's shared-core changes; it
does not start implementation or technical spikes.

## 1. Source of truth and delivery rules

- Product scope and milestone dependencies: `ANALYSIS-B1-ANDROID-PLAN.md`.
- Screen behavior and UX acceptance: `ANALYSIS-B1-UI-REQUIREMENTS.md`.
- Vital-tracking scope for M1b: `ANALYSIS-D1-VITAL-TRACKING.md`.
- Sync behavior and protocol constraints: `ANALYSIS-B1-MOBILE-SYNC.md` and
  the household design it references.
- Implement each item as a separately reviewable vertical slice where
  practical. Keep shared domain/application/portable changes in this
  repository and Android-only UI/adapters in the private app repository,
  as required by A7.
- Each slice includes localized strings in all five supported languages,
  screen-reader semantics, scalable text/layout, loading/empty/error/offline
  states, and accessibility verification where applicable. Do not make a
  later-milestone control appear active early.
- Spike S11 for billing and the API 26 compatibility spike remain explicit
  prerequisites at their plan gates. They are listed here as deferred work;
  this backlog does not launch them.

## 2. Decisions and gates

| Key | Backlog decision | Current recommendation/status | Blocks |
|---|---|---|---|
| DA3 | Catalogue, language and country parity with desktop | **Decided 2026-10-08**: follow the desktop's five languages and supported catalogues (IT, EU, ES, FR and US); country selects the matching national catalogue and default language where available; otherwise default to EMA and English. App use remains worldwide; catalogues are optional entry aids and public/redistributable only. Keep Android's §4.4 delivery approach. | M4 implements the supported catalogue set; no country catalogue is required to use the app |
| DA4 | Order of M3–M5 and public launch | **Decided 2026-10-08**: M3 → M4 → M5; intermediate builds are for internal testing; public Play Store launch is only after M5. | M3/M4/M5 exit gates and the single public launch gate |
| D13 | Android minimum OS | API 26 selected provisionally; verify MAUI, alarms, billing/store compatibility in the M1 spike before claiming support. Spike is deferred until separately authorized/scheduled. | M1 release claim and M2 entry |
| DA5 | Play account and application ID | Decide before first Play upload; application ID is immutable after first upload. | M1 release |
| S11 | Google Play Billing integration | Plan requires the native billing spike before M2. | M2 entitlement/purchase implementation |
| M2 accounts | Google Drive and OneDrive Android OAuth clients; Play trader/payment profile | Provision and verify before their respective M2 integration/release gates. | M2 provider sign-in and paid release |

### DA3: what “country/catalogue” means

DA3 is closed. Android follows the desktop's five supported UI languages
(Italian, English, French, Spanish and German) and supported catalogue
sources (IT, EU, ES, FR and US; US support is recorded in
`ANALYSIS-CATALOGUE-US-GB-SOURCES.md`). Country selects the corresponding
national catalogue and its default language where available (for example,
Italy → IT/Italian, France → FR/French, Spain → ES/Spanish, USA →
US/English). If the country has no available national catalogue, the
initial selection is the EMA (EU) catalogue with English. The user can
explicitly change the UI language afterwards.
Country matching is a default, not a restriction: the app remains usable
worldwide and core features do not depend on a catalogue. Catalogues are
optional medicine-entry aids only where public datasets can legally be
redistributed; manual entry always works. Android keeps plan §4.4's feed
delivery approach without embedding a national catalogue in the APK.
Future country/source additions follow the desktop's source and
redistribution review.

## 3. Backlog by milestone

### B0 — Shared foundations (M0)

**Entry:** approval of the Android plan. **Exit:** shared components build
and behave unchanged on desktop; Android can consume the portable APIs.

| ID | Slice | Acceptance / done when | Depends on |
|---|---|---|---|
| B0-01 | Extract portable profile registry and settings stores | Existing desktop profile/settings behavior is unchanged; Windows path and DPAPI concerns remain in the Windows shell. | Plan M0 |
| B0-02 | Add the shared notification planner | Planner results match existing desktop medication monitor/reminder behavior for dose, stock, expiry, time-zone and schedule edge cases. | B0-01; plan §4.2 |
| B0-03 | Prove the Android app can consume shared core | Private app builds against a pinned shared-core revision; no Windows-only dependency crosses into the Android composition root. | B0-01; private repository A7 |
| B0-04 | Establish shared-core CI and release discipline | CI builds and runs relevant portable tests; shared changes are tagged/referenced by the mobile app. | B0-03 |

### B1 — Standalone Android core (M1)

**Entry:** M0 merged; D11, the provisional API 26 selection for D13, DA2,
and DA5 resolved as the Android plan requires; Play account and private
repository ready. M1 implementation may proceed, but claiming API 26
support and release readiness are gated on the deferred D13 technical
spike and compatibility check, which are not started in this task.
**Exit:** the 14-day closed test completes on a phone without PC/account,
with no data loss across update, reboot, and export/import.

| ID | Slice | Acceptance / done when | Depends on / UX |
|---|---|---|---|
| B1-01 | Android shell, navigation, theme and localization | Daily overview is the landing screen; stable navigation, light/dark/system appearance, five languages, text scaling, and accessible touch/semantic labels work. | B0-03; UI-01, UI-18, §6 |
| B1-02 | First-run and standalone setup | Start-new and restore-from-`.mrz` are distinct; explain local storage and policy, record acknowledgment locally, do not require MedReminder/provider account, and handle notification permission denial without blocking data. | UI-00, UI-06, UI-11, UI-17 |
| B1-03 | Medicine and regimen workflows | Create/edit/deactivate medicines and schedules, including complex regimens, tapering, as-needed slots and presets; search/filter and forecasts remain usable offline. | Shared domain/use cases; UI-02, UI-03 |
| B1-04 | Packages, stock and intake history | Add packages, correct/count stock, record/retract intake and inspect history/expiry; confirm destructive or consequential edits and refresh affected screens. | Shared domain/use cases; UI-04, UI-05 |
| B1-05 | Local reminders and OS permissions | Schedule dose, two-stage low-stock and expiry notifications; explain exact-alarm/notification permissions and degraded timing; hide medicine details on the lock screen by default; dose actions open medicine/list or snooze, never silently record intake. Multi-device delivery overrides apply in M2. | B0-02; UI-09 |
| B1-06 | Free export/import and backup reminder | Export/import encrypted `.mrz` using Android document flows; preview selected profile and replacement effect; failure/cancel preserves data; offer free export after 30 days without backup when sync is off. | UI-11, UI-17 |
| B1-07 | Security, profile and app lock | One local profile with administrator role; biometric app lock; app data in private storage and secrets protected by the Android Keystore adapter. | D13 technical validation; UI-10, UI-17; plan §4.7 |
| B1-08 | Reminder settings and recovery states | Expose permission status and routes to Android settings; when battery restrictions may delay reminders, offer contextual optional guidance and explain the limitation. | DA13; UI-09, UI-18 |
| B1-09 | Release readiness | Private-repository CI, signing outside source control, closed testing, five-language user guides, upgrade/reboot/export-import checklist, and no health data in logs. | B1-01–08; D13 validation |

### B1b — Local vital tracking (M1b / D.1)

**Entry:** M1 is released and its baseline stable. **Exit:** D.1 Android
acceptance and privacy/local-storage criteria pass. Follow the dedicated
D.1 backlog and acceptance criteria; do not merge vital data into B.1
sync, cloud backup, or `.mrz`.

| ID | Slice | Acceptance / done when | Depends on / UX |
|---|---|---|---|
| B1b-01 | Encrypted local vital database | Separate `vitals.db`, Keystore-protected key, no automatic backup or background upload. | M1; D.1 §6 |
| B1b-02 | Reading entry, history and charts | Manual values are validated and displayed descriptively, without diagnosis, treatment advice or adherence scoring. | B1b-01; D.1 §6 |
| B1b-03 | CSV import/export and PDF output | CSV import merges/deduplicates by `VitalReading.Id`; export always includes full local history; PDF is output-only and user initiated. | B1b-01; D.1 §6 |
| B1b-04 | Premium/free vital-data presentation | Any free-tier history limit is a presentation/access limit only; existing values remain accessible and export remains complete. | DA1 in D.1; D.1 §5–6 |

### B2 — Individual Premium, cloud and household (M2)

**Entry:** M1 internal-test exit passed, API 26 support validated, D4 recorded, S6/S7 Android
OAuth clients ready, S11 billing spike complete, Play merchant/trader setup
ready. Google Drive integration precedes OneDrive. **Exit:** offline,
conflict, purchase, restore, entitlement lapse and device-removal flows
preserve user data and accurately report state.

| ID | Slice | Acceptance / done when | Depends on / UX |
|---|---|---|---|
| B2-01 | Individual Play products and entitlement state | Buy/restore individual monthly, yearly and lifetime products; show pending/unavailable/offline/grace/expired states; cached valid entitlement works under the documented grace policy. No family product/grant in M2. | S11, DA7, DA10; UI-13 |
| B2-02 | Contextual Premium gates | Explain gated feature, value and runtime Play terms; preserve free path and unrelated medicine work; no safety reminders, export or restore are gated. | B2-01; UI-13 |
| B2-03 | Google Drive sign-in and encrypted backup | Explicit provider opt-in; configure, schedule and restore backup; show provider and last-success status; restore remains free and does not silently replace a profile. | S6/S7, P14; UI-07, UI-11, UI-12 |
| B2-04 | OneDrive provider | Add after Google Drive; same encrypted archive, provider-state and recovery semantics. | B2-03; UI-07, UI-12 |
| B2-05 | Installation create/join and pairing | QR/code/passphrase paths explain provider authentication, key handling and profile replacement; cancellation and failure preserve current local data. | Sync protocol, B2-03; UI-12 |
| B2-06 | Multi-device sync and conflict review | Show pending/synced/offline/error/conflict state; resolve listed conflicts without discarding unrelated local edits; low-stock notifications default all paired devices while dose reminders default scheduled phone, with per-device overrides. | B2-05, D4; UI-07–09 |
| B2-07 | Household, profiles, roles, PIN and device removal | Show master and paired devices; enforce permissions; explain and confirm device removal/key rotation and master transfer; no silent data loss. | B2-05; household design; UI-10, UI-12 |
| B2-08 | Local data deletion | Export suggestion, exact local scope and confirmation; delete current profile or all local profile data on this device; preserve remote copies, pause that profile's sync on this device, and require explicit restore/reconnect before redownload. | B2-06/07; UI-17 |
| B2-09 | Lock-screen privacy and notification distribution | Hide medicine details by default; allow per-device opt-in; expose per-kind/per-device delivery preferences; explain Android visibility controls. | D4; UI-09, UI-17, UI-18 |
| B2-10 | Battery restriction guidance for sync | Contextual optional route to battery settings when OS restriction threatens sync; declining remains safe and shows expected limitation. | DA13; UI-18 |
| B2-11 | Billing and sync recovery verification | Exercise license-test purchase, restore on same Play account, offline grace, expiry, provider failures, interrupted restore, conflict and removal. | B2-01–10; M2 exit |

### B3 — Prescriptions, planning and reports (M3)

**Entry:** M2 internal-test exit passed; implement first in the approved
M3 → M4 → M5 order. **Exit:** each feature has
correct reminder states, accessible Premium gate, and safe external handoff.

| ID | Slice | Acceptance / done when | Depends on / UX |
|---|---|---|---|
| B3-01 | Prescription drafts and lifecycle | Prepare/share a request, record requested/issued/collected and repeats; notifications open preparation only and never claim submission. | M2; UI-09, UI-14 |
| B3-02 | Regional prescription links and validity | Show supported public service link and valid-until reminders without implying submission or clinical approval. | Regional service data; UI-14 |
| B3-03 | Administrative deadlines and reminders | Create recurring deadlines, edit/pause them, and resolve notifications; keep distinct from free medicine-dose reminders. | Planner parity; UI-14 |
| B3-04 | Supply planning and calendar export | Show estimates and inputs; export `.ics` via Android document/share flow with cancellation recovery. | UI-14 |
| B3-05 | Therapy timeline and PDF report | Timeline remains read-only/free; report uses approved library/licence, previews scope and explains share-sheet handoff. | PDF implementation decision; UI-14 |

### B4 — Reference catalogue, scan and safety information (M4)

**Entry:** M3 internal-test exit passed. Use the desktop's supported
country/catalogue and language options; verify public redistribution and
source attribution for each dataset. **Exit:** manual entry works
regardless of catalogue state; search/link, safety information and scan
obey the Free/Premium split.

| ID | Slice | Acceptance / done when | Depends on / UX |
|---|---|---|---|
| B4-01 | Reference-country setup and setting | Match desktop country options; default to the matching national catalogue and language, or EMA and English when no national catalogue exists; allow language override and explain worldwide use without a catalogue. | DA3; UI-15, UI-18 |
| B4-02 | National + EMA catalogue refresh and offline cache | Follow §4.4 for delivery; refresh the selected national catalogue when available, otherwise EMA; show public/redistributable sources, version and last refresh; preserve downloaded data for offline search; isolate failed feeds and never send profile data. | Desktop feed contracts; UI-15 |
| B4-03 | Search and link catalogue entries | Search by supported fields, preview selected medicine and identifiers, link/unlink with confirmation; manual entry remains free if no match. | B4-02; DA11; UI-03, UI-15 |
| B4-04 | Barcode scan and scan-to-restock | Request camera only in context; decode supported formats; confirm resolved medicine/package and quantity before writing stock; provide manual fallback. | B4-03; UI-15 |
| B4-05 | Shortages, equivalents and information links | Show source/update date and distinguish public information from medical advice; provide country-specific items only where the desktop has supported public data. | UI-15 |

### B5 — Email automation (M5)

**Entry:** M4 internal-test exit passed; master/mail-device behavior
agreed. **Exit:** configuration and failures are transparent; no UI calls
queued work “sent”; no public store release occurs before M5 exits.

| ID | Slice | Acceptance / done when | Depends on / UX |
|---|---|---|---|
| B5-01 | SMTP credentials and connection setup | Validate configuration, protect secrets, support test/recovery states and never display saved credentials. | UI-16, UI-18 |
| B5-02 | Recipients and message categories | Configure caregiver copies, low-stock emails and weekly digest separately; review recipients/content before save. | UI-16 |
| B5-03 | Designated mail device and master transfer | Identify sender device and last-seen state; explain handoff to PC and effect if sender is unavailable. | Household design; UI-16 |
| B5-04 | Best-effort delivery and Premium expiry | State sends may be delayed; show queued/attempted/failed/confirmed states; ordinary medicine reminders continue when Premium expires. | B2-01, planner; UI-13, UI-16 |
| B5-05 | Public Play Store launch | Publish only after M5 exit and final store/privacy/legal/release checks; M1–M5 artifacts before then are internal-test builds only. | M5 exit; D13 validation; privacy/legal and Play release gates |

## 4. Cross-cutting definition of done

Every user-facing item is complete only when:

1. The happy path and loading, empty, offline, denied-permission, failure,
   retry, cancel and recovery states are implemented where relevant.
2. TalkBack labels/order, non-color cues, at least 48 dp touch targets,
   system text scaling, dark/light appearance and all five localizations
   are checked.
3. Safety-related medicine reminders, manual data entry and data export
   stay available on the free path; no screen suggests diagnosis or
   treatment changes.
4. Destructive/replacement operations preview scope, identify local versus
   provider/remote copies and require confirmation.
5. Logs contain no secrets or health data; remote/network operations state
   what leaves the device and which provider receives it.
6. Acceptance checks map to the requirement IDs linked in each backlog
   row. Add automated tests for domain/protocol behavior and focused manual
   accessibility/device checks for native UI and Android OS interactions.

## 5. Recommended execution order

1. Resolve administrative M1 prerequisites (D11, DA2, DA5) and schedule the
   API 26 technical spike as a separate later task; do not claim API 26
   support before its result.
2. Deliver B0, then B1 and its closed-test exit criteria.
3. Once the M1 baseline is stable, schedule B1b (D.1) separately.
4. Complete M2 provider, household, billing and recovery work; keep Google
   Drive ahead of OneDrive and family Premium out of scope.
5. Deliver M3, then M4, then M5 in internal-test builds; publish to Play
   only after M5 exits. Use the desktop's catalogue/country/language
   behavior in M4; public-data source additions require the same
   redistribution review as desktop.

This ordering refines the product plan into work slices; it does not
change the approved M0–M5 scope or authorize the deferred API 26 spike.
