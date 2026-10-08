# B.1 Android development team plan

This plan turns the approved Android milestones and implementation backlog
into a team workflow. It is for staffing, sequencing and readiness decisions;
it does not authorize implementation or the deferred technical spikes.

## 1. Planning basis

- Product scope, milestone estimates and technical constraints:
  `ANALYSIS-B1-ANDROID-PLAN.md`.
- Assignable implementation slices and acceptance criteria:
  `ANALYSIS-B1-ANDROID-IMPLEMENTATION-BACKLOG.md`.
- Screen behavior and UX acceptance:
  `ANALYSIS-B1-UI-REQUIREMENTS.md`.
- Sync protocol and household rules:
  `ANALYSIS-B1-MOBILE-SYNC.md` and the household design it references.
- Milestones M1–M5 are pre-release builds on the Play internal and
  closed testing tracks (plan §5.0). The public Play Store release is
  after M5 (DA4). M3 precedes M4, which precedes M5.
- The team plans **Android release 1** (M0–M5). Vital tracking (D.1) is
  evolution V, evaluated after the store release of the Android and iOS
  apps (plan §4.3a); it needs its own staffing and estimate.
- The `ANALYSIS-B1-*` documents take precedence over `docs/STATUS.md` and
  `docs/EVOLUTION.md`.
- Google Drive precedes OneDrive in M2. Family Premium remains deferred.

## 2. Team roles and ownership

Use role ownership rather than assigning names before team capacity is
known. One person may cover several roles, but every work item needs one
directly responsible owner and a reviewer.

| Role | Owns |
|---|---|
| Product owner | Scope and acceptance decisions (D11, DA2, DA5 and DA12 decided on 2026-10-08); milestone exits; public release approval |
| Android lead | Private app architecture, Android composition root, navigation, platform APIs, integration and technical decisions |
| Shared-core lead | Portable domain/application/storage changes in this repository; desktop compatibility and shared API versioning |
| Feature developers | Vertical slices across shared core and Android UI/adapters, coordinated with the relevant lead |
| QA/accessibility owner | Requirement-to-check traceability, device matrix, TalkBack, localization, regression and closed-test evidence |
| Release/security owner | CI and signing controls, dependency/licence review, privacy-safe diagnostics, Play tracks and release checklist |

If staffing is limited, the Android lead can own platform and release
coordination, while the product owner performs acceptance. QA ownership
must still be explicit for each milestone.

## 3. Work sequence and gates

| Stage | Work | Gate to proceed |
|---|---|---|
| P0 — Team readiness | Confirm private Android repository access, named role owners, available capacity, device/test access, and shared-core contribution/release workflow. Review the backlog against the approved UX requirements. | Team and repository ownership are clear; no code work is started from an unassigned or ambiguous slice. |
| P1 — Prepare M1 entry inputs | D11, DA2, DA5 and DA12 are decided (2026-10-08). Create the personal Play account and reserve the application ID `com.vger70.medreminder`; add the strip target to the private repository (D11). Separately schedule the deferred API 26 spike and state its result is required before claiming API 26 support. | Play account and private-repository build rules ready; spike has an owner and a future slot, but is not run by this plan. |
| M0 — Shared foundations | Deliver B0-01–04 in dependency order: profile/settings extraction, notification planner, Android consumption proof, then shared-core CI/release discipline. | Existing desktop behavior remains unchanged; Android builds against a pinned shared-core revision; portable CI is green. |
| M1 — Standalone core | Deliver B1-01–09. Establish the shell and local data path first; develop medicine/regimen and package/intake flows alongside permission/reminder work after shared APIs are stable; complete export/import, security and recovery; then run the closed test on the closed testing track. | 14-day closed test passes on a phone without PC/account; no data loss across update, reboot, export/import; API 26 claim only after compatibility validation. |
| M2 — Premium, cloud and household | Before entry, finish S11 billing spike, Android OAuth client work for S6/S7, Play merchant/trader setup and API 26 validation. Deliver individual Premium and entitlement states, then Google Drive backup/restore, OneDrive, pairing/sync, household and local deletion in that order as dependencies permit. | Offline, conflict, purchase/restore, entitlement lapse, provider recovery and device-removal scenarios preserve data and report accurate state. |
| M3 — Prescriptions and planning | Deliver B3-01–05 after M2 exit; build the PDF report with SkiaSharp (DA14; no iText) and verify public regional-service links as data inputs. | Milestone acceptance of prescription lifecycle, reminders, planning, calendar/report handoff and Premium gates. |
| M4 — Catalogue and scan | Deliver B4-01–05 after M3 exit. Verify data attribution/redistribution for each feed; implement the DA3 defaults: reference country from the device region, national catalogue if one exists (IT, ES, FR, US), otherwise EMA for an EU/EEA country, otherwise no catalogue; the UI language stays independent of the country. | Catalogue/manual-entry behavior, offline states, safety information, scan and free/Premium split pass milestone acceptance. |
| M5 — Email and release preparation | Deliver B5-01–04 after M4 exit; complete B5-05 store, privacy/legal, security and operational checks, and a closed test that meets the Play production-access rule. | M5 exit and final release checks pass; product owner authorizes public Play release. |

The existing plan estimates M0–M5 at 120–170 developer-days in total:
M0 10–15, M1 35–50, M2 35–50, M3 15–20, M4 15–20, M5 10–15.
These are effort ranges, not calendar dates. Convert them to a dated
schedule only after the team, capacity, repository access and spike slots
are known. Evolution V (vital tracking) is excluded.

## 4. Parallel work and integration

- M0 is dependency-led; do not parallelize work that changes shared
  profile/settings or notification semantics before the portable contracts
  are agreed.
- In M1, Android shell/localization and shared-core domain work can proceed
  in parallel once B0-03 defines the app/core boundary. Integrate in small
  slices against pinned core revisions.
- In M2, billing and provider adapters can be developed in parallel after
  their prerequisites, but entitlement gates must be integrated with each
  feature before M2 acceptance. Implement and validate Google Drive before
  starting OneDrive.
- M3 feature slices can proceed in parallel when they do not compete for
  the notification planner or shared persistence contracts. Keep M4 and M5
  behind their approved milestone gates even if implementation capacity is
  available earlier.
- Every pull request has one author, one reviewer from the other relevant
  ownership area for cross-boundary work, linked backlog/UX IDs, and a
  pinned shared-core revision when applicable.
- Integrate continuously into builds on the internal testing track.
  Keep test data synthetic; do not put health data, credentials, signing material or
  provider tokens into source control, CI logs or issue attachments.

## 5. Team workflow and quality checks

1. Refine each backlog slice before it enters development: confirm owner,
   dependencies, user-visible behavior, data migration implications,
   accessibility/localization states and acceptance checks.
2. Implement as a vertical slice. Keep Android-only code in the private
   app repository and portable shared behavior in this repository.
3. Review changes for UX requirement coverage, data safety, provider
   boundaries, licensing, secret handling and desktop regression risk.
4. Run automated domain/protocol checks and relevant CI for each change;
   perform focused device checks for native permissions, alarms, storage,
   camera, billing and provider flows.
5. The QA/accessibility owner maintains a matrix from backlog IDs to
   automated checks, manual scenarios, supported devices/OS versions,
   localization and TalkBack evidence.
6. At each milestone exit, review the evidence against the backlog exit
   gate, record known limitations and product-owner acceptance, then
   publish that milestone to the closed testing track only.

Cross-cutting completion means all relevant loading, empty, offline,
denied-permission, error, retry, cancellation and recovery states are
covered; five languages and scalable text are checked; touch targets and
TalkBack semantics are verified; no health data or secrets enter logs;
and destructive operations show scope and require confirmation. Detailed
criteria remain in backlog §4.

## 6. Immediate planning actions

Before implementation starts, the team should:

1. Name the owners in §2 and confirm repository access for the private
   Android app and shared-core contribution path.
2. Create the personal Play account with the application ID
   `com.vger70.medreminder` (DA5) and recruit at least 20 closed-test
   testers, so that 12 remain opted in for 14 consecutive days.
3. Assign an owner and proposed date to the deferred API 26 spike without
   starting it; schedule S11, S6/S7 Android OAuth work and Play merchant
   setup before M2.
4. Confirm available devices and OS versions for closed testing, including
   low-end hardware and permission/reboot scenarios from the existing B.1
   spike findings.
5. Turn B0-01–04 into the first development-ready work queue and agree
   shared-core version pinning, PR review, CI and internal-build cadence.
6. Convert developer-day estimates to calendar dates after capacity is
   confirmed.

This plan completes team-level sequencing and readiness planning. It does
not begin implementation, run a technical spike, or authorize public
release.

## Sources

- `ANALYSIS-B1-ANDROID-PLAN.md` §§4–6.
- `ANALYSIS-B1-MOBILE-SYNC.md` §18 (spike results).
- `ANALYSIS-B1-ANDROID-IMPLEMENTATION-BACKLOG.md` §§2–5.
- `ANALYSIS-B1-UI-REQUIREMENTS.md` §§1–8.
- `ANALYSIS-B1-MOBILE-SYNC.md` and linked household design.
