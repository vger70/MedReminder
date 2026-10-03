# Change Log

All notable changes to MedReminder are recorded here, grouped by pull
request. Each PR gets a single entry, added when the PR is opened and
updated only if the PR's scope changes materially before it merges.

## How this file is maintained

- Every time a new pull request is created for this repository, an
  entry is prepended below in reverse-chronological order (newest
  first).
- The entry title is `## PR #<number> — <one-line summary>` and links
  back to the PR on GitHub.
- The body lists the observable changes as terse bullet points,
  focused on **what changed** and **why**, not on implementation
  detail. Reference the affected paths when it helps a future reader
  locate the change.
- If a PR is later closed without merging, mark the entry as
  `**Status:** closed (not merged)` — do not delete it.
- Once a PR merges, mark it as `**Status:** merged (<merge-date>)`
  under the title.
- Do not squash entries across releases: this log tracks pull
  requests, not versions. Release-level history belongs in the GitHub
  Releases page.

Format loosely inspired by [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
with the classification adapted to per-PR granularity: **Added**,
**Changed**, **Deprecated**, **Removed**, **Fixed**, **Security**,
**Docs**, **Build**.

---

## PR #177 — Add analysis for intraday stock that follows dose times

Link: [vger70/MedReminder#177](https://github.com/vger70/MedReminder/pull/177)
Branch: `feature/intraday-consumption` → `main`

### Docs

- New `docs/analysis/ANALYSIS-INTRADAY-CONSUMPTION.md`: the stock shown
  in the main window is the start-of-day value, so a dose already taken
  still appears in it. The analysis recommends a read-side projection
  that subtracts the doses already due today, resolved from slot times
  or user-editable time-of-day presets, and leaves the ledger, sync,
  counts and forecasts unchanged.

---

## PR #176 — Show all About data sources, stop text box border flicker, remember the main window placement

Link: [vger70/MedReminder#176](https://github.com/vger70/MedReminder/pull/176)
Branch: `claude/fonti-dati-textbox-issues-c699a3` → `main`

### Added

- The main window reopens at the size, position and maximized state
  it had when last closed, per profile. The placement is saved in
  `profiles\<id>\ui.settings.json` on every close, the hide to the tray
  included; one whose title bar is on no current screen is ignored
  (`src/MedReminder.UI/Forms/MainForm.cs`).
- `ProfileUiSettingsFile` now changes only the properties it writes
  and keeps the others, so the placement, the text size and the
  appearance never overwrite one another.

### Fixed

- The About dialog's data-source attributions could not be read in
  full: the dialog is taller and the last attribution keeps a bottom
  gap, which a scrolling `FlowLayoutPanel` drops
  (`src/MedReminder.UI/Forms/AboutDialog.cs`).
- In dark mode the text box border flickered on mouse hover: Windows
  repainted the frame in the hot state before the palette border was
  drawn over it. Without visible scroll bars the frame is now drawn
  once, by the palette painter
  (`src/MedReminder.UI/UiExtensions/ThemedBorder.cs`).

## PR #175 — Hide a maximized main window to the tray on the first close

Link: [vger70/MedReminder#175](https://github.com/vger70/MedReminder/pull/175)
Branch: `claude/fix-maximized-close-to-tray` → `main`

### Fixed

- Closing a maximized main window hid it and showed it again at once;
  only a second close sent it to the tray. Setting `ShowInTaskbar` on
  close recreated the window handle, which re-showed a maximized
  window; the assignment is removed (`src/MedReminder.UI/Forms/MainForm.cs`).
- Restoring from the tray keeps a maximized window maximized instead
  of resetting it to Normal.

## PR #174 — Run the AIFA shortage workflow daily

Link: [vger70/MedReminder#174](https://github.com/vger70/MedReminder/pull/174)
Branch: `claude/vigilant-hypatia-yby7iu` → `main`

### Changed

- `download_aifa_shortages.yaml` runs daily at 04:27 UTC instead of
  on days 2, 9, 16 and 23: AIFA updates the list on no fixed day, so a
  new list now reaches clients within a day. Runs on a list already
  published commit nothing.

### Docs

- `docs/CATALOGUE-DATA.md` §8.
## PR #173 — Publish the catalogue feeds on a single-commit feeds branch

Link: [vger70/MedReminder#173](https://github.com/vger70/MedReminder/pull/173)
Branch: `claude/feeds-branch` → `main`

### Changed

- Feed files are published on the `feeds` branch, which holds a single
  parentless commit replaced at each publish, instead of accumulating
  about 9.5 MB of archives a month in `main`'s history.
  `scripts/feeds/feeds_branch.sh` (`load`, `publish`) is called by the
  five `download_*.yaml` workflows; only runs on the default branch
  publish.
- The client reads
  `https://raw.githubusercontent.com/vger70/MedReminder/feeds/data/`
  (`CatalogueFeedOptions.DefaultBaseUrl`, `appsettings.json`).

### Build

- "Mirror to main" step keeps `main/data/` current for releases up to
  v2.12.1 until the repository variable `FEEDS_MAIN_MIRROR` is `false`.
- `scripts/feeds/tests/test_feeds_branch.py`.

### Docs

- `docs/CATALOGUE-DATA.md` §1.1, `docs/ANALYSIS.md`.

---

## PR #172 — Move feed workflow schedules off the top of the hour

Link: [vger70/MedReminder#172](https://github.com/vger70/MedReminder/pull/172)
Branch: `claude/vigilant-hypatia-yby7iu` → `main`

### Fixed

- No feed workflow started on 2 October 2026, the first scheduled day:
  GitHub delays or drops `schedule` runs at minute 0 under load. Same
  days and stagger, 7 minutes later: `download_aifa.yaml` 03:07,
  `download_aemps.yaml` 03:27, `download_bdpm.yaml` 03:47,
  `download_ema.yaml` 04:07, `download_aifa_shortages.yaml` 04:27 UTC.

### Docs

- `download_aifa_shortages.yaml` comment no longer says "twice a
  week"; `docs/CATALOGUE-DATA.md` lists the new times.
## PR #171 — Record the second round of proposals in the status snapshot

Link: [vger70/MedReminder#171](https://github.com/vger70/MedReminder/pull/171)
Branch: `claude/status-proposals-2` → `main`

### Docs

- `docs/STATUS.md` §2.9: the eight proposals of
  `EVOLUTION-PROPOSALS-2.md` with their PRs and sync / format impact;
  §3.3 and §4 updated (release and the update of every synced device).

---

## PR #170 — Let the caregiver receive chosen emails and a weekly summary

**Status:** merged (2026-10-02)

Link: [vger70/MedReminder#170](https://github.com/vger70/MedReminder/pull/170)
Branch: `claude/caregiver-digest` → `main`

### Added

- Caregiver per-kind copies (`docs/notes/EVOLUTION-PROPOSALS-2.md`
  §3.8; A3 §11 item 4): `EmailKind` on every automated email and the
  `CaregiverEmails` setting; the MailKit adapter copies only the chosen
  kinds. Default: every kind, as before.
- Weekly stock summary to the caregiver only (`CaregiverDigest`): stock,
  status and run-out date of each active medicine, no dose data; sent
  from a device that sends email, in the periodic check.
- Settings → Notifications: a box per kind and the weekly summary.
- Strings `Ui.SettingsDialog.Notifications.Caregiver.*`,
  `Notifications.Digest.*` in all five dictionaries; caregiver help text
  updated; user guides in five languages.

### Changed

- New replicated profile settings `CaregiverEmails`, `CaregiverDigest`,
  `CaregiverDigestSentOn` (no operation schema bump: an older app keeps
  their versions without projecting them).
- Export: additive `caregiverEmails`, `caregiverDigest` in
  `notificationSettings`.

### Docs

- `docs/SYNC-FORMAT.md`, `docs/EXPORT-FORMAT.md`, `docs/ANALYSIS.md`
  §9.2, A3 analysis §11, `README.md`, proposal status.

---

## PR #169 — Export coming dates to a calendar file

**Status:** merged (2026-10-02)

Link: [vger70/MedReminder#169](https://github.com/vger70/MedReminder/pull/169)
Branch: `claude/calendar-export` → `main`

### Added

- Calendar export (`docs/notes/EVOLUTION-PROPOSALS-2.md` §3.7):
  Therapy → Export to calendar… saves an `.ics` file with the day to
  request each prescription, the run-out dates, the last day to collect
  each prescription and the open administrative deadlines. Generic
  titles by default; names only when ticked at export time. Stable
  UIDs, so a new export updates the same events.
- `IcsWriter` (RFC 5545), `CalendarEntries`, `CalendarExportQuery` in
  `MedReminder.Application/Calendar`.
- Low-stock emails carry the run-out date as `medreminder.ics`, with a
  generic title (`EmailMessage.CalendarEvent`, multipart MIME in
  `MailKitEmailNotificationService`).
- Strings `Ui.CalendarExportDialog.*`, `Calendar.*`,
  `Ui.MainForm.Menu.Therapy.ExportCalendar` in all five dictionaries;
  section in the five user guides.

### Docs

- `docs/ANALYSIS.md` §9.2, `README.md`, proposal status.

---

## PR #168 — Add recurring administrative deadlines with reminders

**Status:** merged (2026-10-02)

Link: [vger70/MedReminder#168](https://github.com/vger70/MedReminder/pull/168)
Branch: `claude/admin-deadlines` → `main`

### Added

- Administrative deadlines (`docs/notes/EVOLUTION-PROPOSALS-2.md` §3.6):
  therapeutic plan, exemption renewal, check-up or other, optionally
  tied to a medicine, with a notice period, an optional recurrence in
  months and their own channels. No regulatory default: every date is
  entered by the user.
- Therapy → Administrative deadlines… (`DeadlinesDialog`,
  `DeadlineEditDialog`); "Done" closes a one-off deadline and moves a
  recurring one to its next date, counted from the previous date.
- `DeadlineReminders` in the periodic check: one reminder per deadline
  and date on this device (`DeadlineReminderEvents`, device-local),
  email only where this device sends email; the toast opens the list.
- Strings `Ui.DeadlinesDialog.*`, `Ui.DeadlineEditDialog.*`,
  `Deadlines.*`, `Notifications.Deadline.*` in all five dictionaries;
  section in the five user guides.

### Changed

- Sync: `DeadlineChanged`, one last-writer-wins register per deadline
  (operation schema 8, image schema 6). Every device of a sync group
  must run this version before deadlines are used.
- Boot patch for `Deadlines` and `DeadlineReminderEvents`; deadlines of
  a medicine are removed with it; reminders left out of sync images.
- Export: additive `deadlines` field (schema version stays 2).

### Fixed

- The export retries the deletion of its temporary snapshot folder:
  Windows can keep the just-closed snapshot locked for a moment, which
  left the folder behind (`ExportServiceTests.Export_deletes_its_temporary_snapshot`).
- Carries the `.gitattributes` of PR #167 (feed JSON files kept byte for
  byte), so the shortage feed test passes on this branch too.

### Docs

- `docs/SYNC-FORMAT.md`, `docs/EXPORT-FORMAT.md`, `docs/ANALYSIS.md`,
  `README.md`, proposal status in `EVOLUTION-PROPOSALS-2.md`.

---

## PR #167 — Keep feed JSON files byte for byte in every checkout

**Status:** merged (2026-10-02)

Link: [vger70/MedReminder#167](https://github.com/vger70/MedReminder/pull/167)
Branch: `claude/shortage-feed-line-endings` → `main`

### Fixed

- `.gitattributes` marks `data/**/*.json` as `-text` and `data/**/*.zip`
  as binary: with `core.autocrlf=true` Git added a CR to the published
  shortage list, which then failed the size and SHA-256 check against
  `latest.json` (`ShortageFeedTests.The_published_feed_parses`).

### Docs

- `docs/CATALOGUE-DATA.md` §8 documents the line-ending rule.

---

## PR #166 — Mark Italian medicines in shortage from the AIFA list

**Status:** merged (2026-10-01)

Link: [vger70/MedReminder#166](https://github.com/vger70/MedReminder/pull/166)
Branch: `claude/shortage-notice` → `main`

### Added

- Shortage feed (`docs/notes/EVOLUTION-PROPOSALS-2.md` §3.3):
  `scripts/feeds/aifa_shortages.py` and `download_aifa_shortages.yaml`
  publish `data/it/shortages/` (list date, start, expected end,
  equivalent flag, reason category per AIC code); first list of
  29/09/2026 published.
- Client: `ShortageRefresher`, `GitHubRawShortageFeedClient`,
  `JsonFileShortageListStore` (`catalogue\shortages\shortages-it.json`,
  shared by every profile), run with the catalogue feeds when Italy is
  the reference country.
- Supply column in the medicine list with the detail as tooltip;
  `ShortageNotices` notifies once per shortage start
  (`ShortageNoticeEvents`, device-local).
- Strings `Ui.MainForm.Column.Supply`, `Shortage.*`,
  `Notifications.Shortage.*` in all five dictionaries.

### Changed

- Update-check tooltip mentions the shortage list.
- Boot patch for `ShortageNoticeEvents`; left out of sync images and
  removed with the medicine.

### Docs

- `docs/CATALOGUE-DATA.md` §8, `docs/ANALYSIS.md`, `docs/SYNC-FORMAT.md`,
  `CLAUDE.md` §5, user guides (en, it, fr, es, de), `README.md`,
  proposals note.

---

## PR #165 — Add actions to the Windows notifications

**Status:** merged (2026-10-01)

Link: [vger70/MedReminder#165](https://github.com/vger70/MedReminder/pull/165)
Branch: `claude/toast-actions` → `main`

### Added

- Toast actions (`docs/notes/EVOLUTION-PROPOSALS-2.md` §3.4): body click
  opens the app on the medicine (or the prescriptions); "Prepare
  request" on low-stock warnings; "Remind me in 15 minutes" on dose
  reminders, scheduled with Windows. No intake is recorded from a
  toast: intakes are per day in the ledger.
- Application `NotificationTarget`, `NotificationActionArguments`
  (identifiers only); UI `ToastActivationRouter`.
- Strings `Notifications.Action.Snooze`,
  `Notifications.Action.RequestPrescription` in all five dictionaries.

### Changed

- `IWindowsNotificationService`: target-aware `ShowAsync` overload with
  a default body; the low-stock, dose and prescription notifiers pass
  their target.
- Startup: the toast activation is subscribed first; a toast launch
  opens the last used profile minimized and exits silently when another
  instance runs.

### Docs

- User guides (en, it, fr, es, de), `README.md`, proposals note.

---

## PR #164 — Follow prescriptions from the request to the pharmacy

**Status:** merged (2026-10-01)

Link: [vger70/MedReminder#164](https://github.com/vger70/MedReminder/pull/164)
Branch: `claude/prescription-lifecycle` → `main`

### Added

- Prescription lifecycle (`docs/notes/EVOLUTION-PROPOSALS-2.md` §3.2):
  Therapy → Prescriptions… (and navigation pane) with requested, issued
  (code, packages, valid until) and collected dates, all optional;
  "Mark as requested" in the request window; after a new package, a
  question whether an open prescription was collected.
- Reminder to collect an issued prescription from 3 days before it
  lapses, once per date and device, on the medicine's channels; the
  code is never in the reminder or the logs.
- Domain `Prescription`, `PrescriptionRules`,
  `PrescriptionReminderEvent`; Application `SavePrescription`,
  `CollectPrescription`, `DeletePrescription`, `PrescriptionListQuery`,
  `PrescriptionReminders`; UI `PrescriptionsDialog`,
  `PrescriptionEditDialog`.
- Strings `Ui.PrescriptionsDialog.*`, `Ui.PrescriptionEditDialog.*`,
  `Prescriptions.Status.*`, `Notifications.Prescription.*` and the
  menu, request-window and main-window keys in all five dictionaries.

### Changed

- Sync: `PrescriptionChanged` (operation schema 7), one last-writer-wins
  register per prescription; image schema 5. Devices on older versions
  stop at the first such operation until updated.
- Persistence: boot patch for `Prescriptions` and
  `PrescriptionReminderEvents`; medicine deletion removes both.
- Export: additive `prescriptions[]`.

### Fixed

- Prescription request window: wider (820 px), the hint wraps to the
  field column, and the window is never narrower than its button bar
  (`DialogLayout.KeepButtonsVisible`, also applied to the Prescriptions
  and Plan supply windows); the buttons were cut on the left.

### Docs

- `docs/SYNC-FORMAT.md`, `docs/EXPORT-FORMAT.md`, `docs/ANALYSIS.md`,
  user guides (en, it, fr, es, de), `README.md`, proposals note.

---

## PR #163 — Add a supply planner for a trip or the next pharmacy visit

**Status:** merged (2026-10-01)

Link: [vger70/MedReminder#163](https://github.com/vger70/MedReminder/pull/163)
Branch: `claude/coverage-planner` → `main`

### Added

- Therapy → Plan supply… (and navigation pane): for a period chosen by
  the user, each active medicine's need in the period, stock left at
  its start, what is missing and the packages to get, sized like the
  last new package (`docs/notes/EVOLUTION-PROPOSALS-2.md` §3.5).
  Print, PDF and clipboard. Read-only.
- `Application/Coverage`: `CoveragePlanner`, `CoveragePlanQuery`,
  `CoveragePlanText`; `Application/Reporting`: `PrintableTable`,
  `PrintableTableText`.
- UI: `CoveragePlannerDialog`, `TablePrintDocument`, `PrintOutput`.
- Strings `Ui.CoveragePlannerDialog.*`, `Reports.Coverage.*`,
  `Ui.MainForm.Menu.Therapy.PlanSupply`,
  `Ui.MainForm.Error.OpenCoveragePlanner` in all five dictionaries.

### Docs

- User guides (en, it, fr, es, de), `README.md`, status in
  `docs/notes/EVOLUTION-PROPOSALS-2.md`.

---

## PR #162 — Send a second low-stock warning at half of the threshold

**Status:** merged (2026-10-01)

Link: [vger70/MedReminder#162](https://github.com/vger70/MedReminder/pull/162)
Branch: `claude/escalation-warning` → `main`

### Added

- Second low-stock warning (`docs/notes/EVOLUTION-PROPOSALS-2.md`
  §3.1): when the days left reach half of the medicine's warning
  threshold and no new package has been added since the first warning,
  a second toast and/or email follows ("Second reminder"). A medicine
  already below half when first checked gets only the second one. No
  new setting: the second threshold is derived from `ThresholdDays`.
- Strings `Notifications.Email.SubjectSecond`, `.HeaderSecond`,
  `Notifications.Toast.TitleSecond`, `.BodySecond` in all five
  dictionaries.

### Changed

- `NotificationCycle`: `StageToNotify`, `StageFor`,
  `SecondWarningDays`; `EmailAlreadySent` takes the stage.
- `NotificationEvents.Stage` and `SentEmailNotifications.Stage`, boot
  patch with default 1; the latest row of an instant is the later stage.
- Sync: `EmailNotificationSent.stage`; a second-stage email is written
  with operation schema 6 (first-stage emails keep 4); image schema 4.
  Devices still on v2.12.x stop at the first second-stage email until
  updated.
- Export: additive `stage` on `notificationEvents[]`.

### Docs

- `docs/SYNC-FORMAT.md`, `docs/EXPORT-FORMAT.md`, `docs/ANALYSIS.md`,
  `README.md`, user guides (en, it, fr, es, de).

---

## PR #160 — Add a second round of evolution proposals

**Status:** merged (2026-10-01)

Link: [vger70/MedReminder#160](https://github.com/vger70/MedReminder/pull/160)
Branch: `claude/evolution-proposals-2` → `main`

### Docs

- `docs/notes/EVOLUTION-PROPOSALS-2.md`: eight new proposals ranked by
  user benefit after a survey of similar medication apps (escalation
  warning before run-out, prescription lifecycle, AIFA shortage notice,
  toast actions, coverage planner, administrative deadlines, `.ics`
  export, caregiver opt-in and digest), each with an implementation
  plan; re-assessment of expiry tracking and pill-organizer preparation.
- Shortage notice (§3.3) based on the reviewed AIFA shortage CSV of
  29/09/2026: format, anomalies and matching on the 9-digit AIC.
- `docs/notes/EVOLUTION-PROPOSALS.md`: pointer to the second round.

---

## PR #159 — Check the remote catalogue feeds once a day during the session

Link: [vger70/MedReminder#159](https://github.com/vger70/MedReminder/pull/159)
Branch: `claude/catalogue-daily-refresh` → `main`

### Changed

- `CatalogueRefreshHostedService`: after the boot import, an hourly tick
  runs the remote feed step again once 24 hours have passed since its
  last run, so an app left open for days picks up a newly published
  catalogue. Gates are re-read on every tick.
- Setting relabelled "Check for updates automatically (GitHub)" in the
  five dictionaries; `CheckForUpdatesOnStartup` keeps its name.

### Fixed

- Backup restore, archive import and sync join / rebuild / rekey now
  wait for a running remote catalogue import (new port
  `IDatabaseExclusiveAccess` over `WriteGate`) instead of failing to
  move a database file that is still open.
- `CsvReferenceCatalogueImporter` closes the connection it opened when
  each call ends; the remote refresh no longer holds the database open
  during the download.

### Docs

- `ANALYSIS-CATALOGUE-REMOTE-FEED.md` §4.1, §4.4, §4.5, D2, new §11.5;
  user guides (5 languages) and README.

## PR #158 — Add publish-signed-release.ps1 to automate signed releases

Link: [vger70/MedReminder#158](https://github.com/vger70/MedReminder/pull/158)
Branch: `claude/signed-release-script`

### Build

- `publish-signed-release.ps1 <version>` chains the Git release, the
  wait for the CI run, the tag checkout, the Certum-signed local build
  with `signtool verify`, and `gh release upload --clobber` of the
  signed assets, then returns to `main`. Prerequisites are checked
  before tagging; `-SkipGitRelease` resumes on an existing release.

### Docs

- `docs/PACKAGING.md` §25 documents the script.

---

## PR #137 — Add local Certum-signed release build to release.ps1

Link: [vger70/MedReminder#137](https://github.com/vger70/MedReminder/pull/137)
Branch: `claude/certum-code-signing`

### Build

- `release.ps1 -LocalBuild` mirrors the CI packaging into
  `dist\<version>\` (ZIPs, MSI, `SHA256SUMS.txt`) and signs
  MedReminder's own binaries and the MSI with the Certum SimplySign
  certificate, SHA-256 with an RFC 3161 timestamp
  (`http://time.certum.pl`). The Git release flow is unchanged.
- `.gitignore` ignores `dist/`.

### Fixed

- `packaging/scripts/sign-artifact.ps1` no longer shadows the `$args`
  and `$matches` automatic variables.

### Docs

- `docs/PACKAGING.md` §25 documents the signed local build.
## PR #157 — Align STATUS, EVOLUTION and the proposals note with v2.12.0

Link: [vger70/MedReminder#157](https://github.com/vger70/MedReminder/pull/157)
Branch: `claude/docs-status-evolution-v2-12` → `main`

### Docs

- `docs/STATUS.md`: snapshot at v2.12.0; remote catalogue feeds,
  household H0–H5 and UI modernisation recorded as shipped; open work
  adds household step H6, PR #137 and the UI known limitations.
- `docs/EVOLUTION.md`: multi-user G shipped, I not planned; household
  impact on the mobile phases; website gap extended to v2.12.
- `docs/EVOLUTION-DONE.md`: C.3++ Phase 2 shipped through B.1 Phase 4;
  new §12 for work shipped outside the backlog numbering.
- `docs/notes/EVOLUTION-PROPOSALS.md`: status marks and a status table
  at v2.12.0; ranking unchanged.

## PR #156 — Say in the update-check tooltip that the catalogue is downloaded

Link: [vger70/MedReminder#156](https://github.com/vger70/MedReminder/pull/156)
Branch: `claude/update-check-tooltip-catalogue` → `main`

**Status:** merged (2026-10-01)

### Fixed

- Settings → General → Check for updates on startup: the tooltip, in
  all five languages, no longer says nothing is downloaded. Since
  v2.11.0 the setting also downloads the catalogue of the reference
  country and the EU one when a newer edition is published; a new app
  version is still never downloaded or installed. The README feature
  line said the same and is corrected.

## PR #155 — Bring README, ANALYSIS and the user guides in line with v2.12.0

Link: [vger70/MedReminder#155](https://github.com/vger70/MedReminder/pull/155)
Branch: `claude/docs-refresh-v2-12` → `main`

**Status:** merged (2026-10-01)

### Docs

- README: SQL commands reach the log only while the query-logging
  option is on; features list the redesigned main window, section
  lists, appearance, remote catalogue refresh and query logging;
  repository layout adds `MedReminder.Infrastructure.Portable`;
  data tables list the appearance and query-logging settings.
- `docs/ANALYSIS.md`: release line 2.12.x; seven hosted services
  (adds `HouseholdHostedService`, `MasterProfilesHostedService`);
  `UiMessageBox`, `ChoiceDialog` and `SectionView` described;
  query-logging filter in §10; feature-analysis index adds the remote
  feeds and UI modernisation, household marked shipped.
- The five user guides list appearance and query logging in the
  data-folder tree.

## PR #154 — Close the remaining dark-mode gaps

Link: [vger70/MedReminder#154](https://github.com/vger70/MedReminder/pull/154)
Branch: `claude/ui-dark-mode-gaps` → `main`

### Changed

- Messages, confirmations and multiple-choice questions use
  MedReminder's own dialogs: they follow the dark theme and show OK,
  Cancel, Yes and No in the language chosen in MedReminder.
- The Sync and Installation windows list their sections on the left
  instead of tabs (Ctrl+Tab moves to the next one).

### Fixed

- Settings check box captions were cut at the minimum window size with
  Large text; they wrap.

## PR #153 — Add an admin-only option to log database queries

Link: [vger70/MedReminder#153](https://github.com/vger70/MedReminder/pull/153)
Branch: `claude/db-query-logging` → `main`

### Added

- Settings → General → Log database queries (diagnostics), administrators
  only: writes each SQL command EF Core runs, with its duration, to the
  log file. Parameter values stay hidden. Applies without a restart.
  Device setting `UI:LogDatabaseQueries` in `user.settings.json`, not
  replicated in the household.

### Fixed

- EF Core command entries ("Executed DbCommand", category
  `Microsoft.EntityFrameworkCore.Database.Command`) reached the log on
  every run. They are now written only while the option is on. The
  filter names `SerilogLoggerProvider`: `AddSerilog` registers a rule for
  that provider (any category, Trace), which wins over any rule without
  a provider (`src/MedReminder.UI/Program.cs`).

### Changed

- `UpdateGeneralSettings` requires an administrator to change the option;
  `HouseholdProjection` keeps it when it rewrites `user.settings.json`.

### Docs

- The five user guides describe the option.
## PR #152 — Draw text and number box borders in the dark palette

Link: [vger70/MedReminder#152](https://github.com/vger70/MedReminder/pull/152)
Branch: `claude/ui-dark-textbox-border` → `main`

### Fixed

- In dark mode text boxes and number boxes had a light border; it is
  now dark grey, and a text box shows the accent colour while it has
  the focus.

## PR #151 — Document the appearance setting and the UI building blocks

Link: [vger70/MedReminder#151](https://github.com/vger70/MedReminder/pull/151)
Branch: `claude/ui-phase6-docs` → `main`

### Changed

- The five user guides describe Settings → General → Appearance.
- `docs/ANALYSIS.md` names the shared dialog layout, the confirmation
  dialog, the navigation pane and the medicine list filter; the UI
  modernisation analysis records the final status and the known
  dark-mode limitations.

## PR #150 — Ask confirmations in the app language and give the wizards the template buttons

Link: [vger70/MedReminder#150](https://github.com/vger70/MedReminder/pull/150)
Branch: `claude/ui-phase5c-wizards-confirmations` → `main`

### Changed

- Confirmation questions show Yes and No in the language chosen in
  MedReminder instead of the Windows language.
- The first-run and handover wizards use the same buttons as the other
  dialogs, with the main action last; the first-run wizard uses the
  10 pt base font and shows its errors under the fields.

## PR #149 — Apply the dialog template to the large dialogs

Link: [vger70/MedReminder#149](https://github.com/vger70/MedReminder/pull/149)
Branch: `claude/ui-phase5b-large-dialogs` → `main`

### Changed

- The export, import, restore from cloud, about, donate, fact history,
  prescription request, therapy report, barcode scan, sync,
  installation, profiles (with the new, rename and delete profile
  dialogs) and restore-into-profile windows use the same button bar
  as the other dialogs: bottom right, main action last, buttons at
  least 32 px high that grow with their caption.
- The profile dialogs no longer use fixed positions: their content
  stacks and the window grows with Large text; the new-profile errors
  appear under the fields.
- The medicine editor, import, restore and profile dialogs use the
  10 pt base font.

## PR #148 — Add the dialog template and apply it to the small dialogs

Link: [vger70/MedReminder#148](https://github.com/vger70/MedReminder/pull/148)
Branch: `claude/ui-phase5-dialogs` → `main`

### Changed

- One dialog layout: labels sized to the longest caption, buttons at
  the bottom right with the main button last, buttons at least 32 px
  high that grow with their caption, dialogs that grow to their content
  with Large text. Applied to the stock, stock count, intake, schedule
  change, administration time, PIN, cloud passphrase, sync passphrase,
  pairing code, sync group, household profiles and medicine picker
  dialogs, which also use the 10 pt base font.
- Validation errors in these dialogs appear under the fields instead
  of in a message box.

### Fixed

- Name and active ingredient rows in the medicine editor were about
  twice the field height with Large text at 150 %.
- The PIN prompt and the first-run and handover wizards were scaled
  twice above 100 % display scaling.
- Settings sections taller than the window (General, Notifications)
  showed no scroll bar, so the Save buttons could not be reached; every
  section now scrolls as a whole, as Backup does. The
  Email port and timeout fields were a few pixels wide.
- The My PIN group in Settings → Notifications was cut at the bottom
  with Large text.
- The therapy timeline was sized twice above 100 % display scaling,
  leaving the chart too short for its first row; date fields no longer
  cut the first digit.

## PR #147 — Replace the Settings tabs with a section list

Link: [vger70/MedReminder#147](https://github.com/vger70/MedReminder/pull/147)
Branch: `claude/ui-phase4-settings` → `main`

### Changed

- Settings lists its sections on the left and shows one at a time
  under a heading; Ctrl+Tab and Ctrl+PageDown move between sections.
- The Settings window can be resized (minimum 760×520) and uses the
  10 pt base font.
- Each Settings section lives in its own source file; no behaviour
  change.

### Fixed

- Settings tab headers stayed light in dark mode.
- With Large text at 150 % the Settings Close button was about twice
  its size; long German section names are no longer cut.
- Auto-sized buttons in every window are no longer scaled twice at
  display scaling above 100 %.
- Settings → Backup: the folder fields take the section's width, so
  the Browse buttons stay visible, and the passphrase and account rows
  wrap instead of cutting their text; the storage-location list is
  wider.

## PR #146 — Redesign the main window around a summary and a navigation pane

Link: [vger70/MedReminder#146](https://github.com/vger70/MedReminder/pull/146)
Branch: `claude/ui-phase3-main-window` → `main`

### Added

- Summary cards above the medicine list (Empty, Running low, Suspended,
  All medicines) that filter the list on click.
- Search box in the toolbar (Ctrl+F) filtering the list by name.
- Navigation pane on the left for the timeline, therapy report,
  prescription request, installation and settings windows; icons only
  in a narrow window.
- Context menu on the medicine list with the per-medicine commands.

### Changed

- Toolbar reduced to New medicine, Register intake and search; every
  other command stays in the menus with its shortcut.
- Status shown as a coloured label; list rows 36 px high.
- Main-window section of the five user guides rewritten.

### Fixed

- With Large text at 150 % the main window no longer extends past its
  right edge: the search box was scaled twice and the grid's full
  column width sized the page. The list columns share the width by
  weight down to a minimum, so the list no longer scrolls sideways
  there.
- Profile picker buttons grow with their captions instead of being cut.

## PR #145 — Fix the dark-mode and layout defects found in the UI baseline

Link: [vger70/MedReminder#145](https://github.com/vger70/MedReminder/pull/145)
Branch: `claude/ui-phase2-controls` → `main`

### Fixed

- Dark mode: drop-down lists, text box borders and list view grid
  lines no longer stay light; the autocomplete drop-down selection
  reads at AA contrast.
- Wrapped German column headers in the main window are no longer cut;
  medicine editor labels no longer split mid-word; Settings → General
  groups are spaced and, with Large text at 150 %, scroll instead of
  wrapping into a second column; form labels align with the top of
  their field.
- The profile picker uses the appearance of the profile used last, and
  the PIN prompt that of the profile being opened, instead of always
  following Windows.

### Changed

- One 10 pt base font for every window (was 9 or 9.75 pt).
- Toolbar and menu icons grow with the text size and display scaling;
  Segoe Fluent Icons on Windows 11.

### Docs

- `ANALYSIS-UI-MODERNIZATION.md` revision 4: screenshot baseline.

## PR #144 — Add a theme layer with a per-profile light/dark appearance

Link: [vger70/MedReminder#144](https://github.com/vger70/MedReminder/pull/144)
Branch: `claude/ui-theme-foundation` → `feature/master-slave`

### Added

- Appearance setting (Same as Windows, Light, Dark) in Settings →
  General, stored per profile in `ui.settings.json` next to the text
  size and applied at restart; keys in all five dictionaries.
- `UiTheme`: light, dark and high-contrast palettes (WCAG AA checked
  by `UiThemeTests`), type and spacing scales.
- `UiThemeApplier` (buttons, grids) and `UiToolStripRenderer` (menus,
  toolbars, status bar, tray menu).

### Changed

- Main grid: status shown only in the Status cell, no whole-row tint;
  error banner, timeline and autocomplete badge use theme colours.
- `UiColors` is a facade over `UiTheme`.

### Docs

- `ANALYSIS-UI-MODERNIZATION.md` revision 3 (implementation status,
  dark-mode behaviour verified against .NET 10.0.12); `ANALYSIS.md`
  boot sequence and runtime data updated.

## PR #143 — Add phase 0 review of the WinForms UI modernization

Link: [vger70/MedReminder#143](https://github.com/vger70/MedReminder/pull/143)
Branch: `claude/ui-modernization-review` → `feature/master-slave`

### Docs

- New `docs/analysis/ANALYSIS-UI-MODERNIZATION.md`: inventory of the
  WinForms UI, findings with file references (two base fonts, colours
  hard-coded in the forms, double status encoding in the main grid, no
  inline validation), target tokens for light, dark and high contrast
  with WCAG AA contrast checked, layout proposals for the main window,
  Settings and dialogs, phased plan with estimates, open decisions.
- No code changes.

## PR #142 — Rewrite the user guides around tasks and cover the household features

Link: [vger70/MedReminder#142](https://github.com/vger70/MedReminder/pull/142)
Branch: `claude/household-user-guides` → `feature/master-slave`

### Docs

- User guides (en, it, fr, es, de) rewritten with one structure: contents
  with stable anchors, "where to find what", a "Several computers"
  chapter (sync, shared installation, master device, handover, device
  removal), intake and administration-time sections, and problems and
  answers keyed on the app's messages.
- Menu paths aligned with the UI dictionaries; removed the outdated
  "does not sync between devices" and duplicate low-stock email
  statements; data layout lists `household\`, `sync.*` and
  `cloud-backup.protected`.
- `README.md`: several-devices features, cloud backup of every profile,
  sync and household runtime files, updated known limitations.

---

## PR #141 — Merge main into feature/master-slave

Link: [vger70/MedReminder#141](https://github.com/vger70/MedReminder/pull/141)
Branch: `claude/merge-main-into-master-slave` → `feature/master-slave`

### Changed

- The household integration branch takes `main` up to v2.11.0 (remote
  catalogue feeds #130–#135, database updates), so the manual tests
  run on the code the final merge (#129) ships.

### Docs

- `CLAUDE.md`: `main`'s wording, with the household integration-branch
  rule and the `household/` and `setup/` runtime folders.
- User guides: the admin-only reference country and the catalogue
  self-update paragraph, both kept.

---

## PR #138 — Record unchanged recipients a legacy profile group has no version of

Link: [vger70/MedReminder#138](https://github.com/vger70/MedReminder/pull/138)
Branch: `claude/replicate-unchanged-recipients` → `feature/master-slave`

### Fixed

- Settings → Notifications: saving unchanged addresses now records the
  addresses the profile group has no version of (a group created before
  P8). Before, the save returned early, so a device that joined the
  installation kept empty recipients even after a re-save and a sync.
  Found by manual test H3c/H3d-1
  (`src/MedReminder.Application/UseCases/ProfileSettingsUseCases.cs`).

### Docs

- `docs/analysis/HOUSEHOLD-MANUAL-TESTS.md`: H3c/H3d-1 checks the
  recipients on the joined device, and the re-save for a legacy group.

---

## PR #136 — Show the pairing code on request instead of hiding its window from capture

Link: [vger70/MedReminder#136](https://github.com/vger70/MedReminder/pull/136)
Branch: `claude/pairing-code-show-on-request` → `feature/master-slave`

### Fixed

- The pairing code window (Pair a device, Add a device, the new key
  after a removal) was invisible in remote-control sessions such as
  RustDesk or Remote Desktop, and the application looked frozen. It now
  opens normally, with the QR and text code hidden until **Show the
  code**; the window is no longer excluded from screen capture.

### Docs

- User guides (5 languages), `docs/analysis/ANALYSIS-B1-MOBILE-SYNC.md`,
  `docs/analysis/HOUSEHOLD-MANUAL-TESTS.md`.

---

## PR #135 — Refresh the EU, ES and FR catalogues from remote feeds

Link: [vger70/MedReminder#135](https://github.com/vger70/MedReminder/pull/135)
Branch: `claude/catalogue-feeds-eu-es-fr`

### Added

- Monthly remote feeds for the EU, Spanish and French catalogues, on
  the AIFA days: `download_aemps.yaml` (03:20 UTC,
  `data/es/aemps-<yyyymm>.zip`), `download_bdpm.yaml` (03:40,
  `data/fr/bdpm-<yyyymm>.zip`) and `download_ema.yaml` (04:00,
  `data/eu/ema-epar-<yyyymm>.zip`, EMA XLSX converted to the parser's
  CSV). Each script checks format, header or columns and row floors
  before anything under `data/` changes.
- `scripts/feeds/common.py`, shared by the four feed scripts, with
  pytest tests in `scripts/feeds/tests/` run by `scripts_tests.yaml`.
- Fixtures `ema-epar-sample.xlsx` and `ema-epar-from-xlsx.csv`, and a
  parser test proving the converted CSV reads like the manual export.

### Changed

- The remote catalogue feed is no longer AIFA-only: each country is a
  `CatalogueFeedDescriptor` (archive prefix, required entries,
  uncompressed cap) and its files live under `data/<country>/`
  (`Catalogue:RemoteFeed:BaseUrl`, per-feed `Feeds:<country>` with
  `Enabled` and `MaxDownloadBytes`; all four enabled). `ManifestUrl`
  and `SnapshotUrlTemplate` remain as Italy-only overrides.
- The manifest parser rejects a manifest whose `file` or `country`
  belongs to another feed.
- At startup the app refreshes the reference country's feed plus EU
  (`CatalogueFeedSelection`), one scope per feed; a failure in one feed
  does not stop the next. Log lines read `Remote catalogue feed <country>: …`.
- `scripts/download_aifa.py` moved to `scripts/feeds/aifa.py` on the
  shared module, with the same output; its manifest writes `file_count`
  instead of `csv_count`.
- All feed workflows, AIFA included, share the concurrency group
  `catalogue-feeds-publish` and rebase before pushing.

### Docs

- `CATALOGUE-DATA.md` §1, §3, §5, §6: remote feed first, embedded
  refresh optional, with the runner measurements; §2.1 describes the
  client for every feed.
- `THIRD-PARTY-NOTICES.md`: the four datasets are also redistributed
  from `data/<country>/`.
- `ANALYSIS.md`: outbound calls, staging folder and hosted service list
  all feeds.
- User guides (en, it, fr, es, de): the reference country's catalogue
  and the EU one update themselves.
- `ANALYSIS-CATALOGUE-REMOTE-FEEDS-EU-ES-FR.md` §11: implementation
  notes and verification.

---

## PR #134 — Add the implementation prompt for the EU, ES and FR catalogue feeds

Link: [vger70/MedReminder#134](https://github.com/vger70/MedReminder/pull/134)
Branch: `claude/aifa-catalog-auto-update-jrkles`

### Docs

- `docs/prompt/PROMPT-CATALOGUE-REMOTE-FEEDS-EU-ES-FR.md`: step-by-step
  briefing to implement the EMA, AEMPS and ANSM feeds from
  `ANALYSIS-CATALOGUE-REMOTE-FEEDS-EU-ES-FR.md` (client generalisation,
  shared script module, one workflow per source, docs).
- The AIFA workflow comment, `download_aifa.py`, `CATALOGUE-DATA.md` and
  both feed analyses describe the current schedule (days 2, 9, 16, 23)
  instead of the former days 2–7.

---

## PR #133 — Remove the EMA/BDPM probe workflow and record #131 as merged

Link: [vger70/MedReminder#133](https://github.com/vger70/MedReminder/pull/133)
Branch: `claude/aifa-catalog-auto-update-jrkles`

**Status:** merged (2026-09-29)

### Build

- Removed `.github/workflows/ema_bdpm_probe.yaml`, a manual probe whose
  results are recorded in `ANALYSIS-CATALOGUE-REMOTE-FEEDS-EU-ES-FR.md`.

### Docs

- PR #131 marked merged; the AIFA analysis records the empty staging
  folder after the field run.

---

## PR #131 — Refresh the Italian catalogue from the remote AIFA feed at startup

Link: [vger70/MedReminder#131](https://github.com/vger70/MedReminder/pull/131)
Branch: `claude/aifa-catalog-auto-update-jrkles`

**Status:** merged (2026-09-29)

Implements `docs/analysis/ANALYSIS-CATALOGUE-REMOTE-FEED.md`.

### Added

- At startup, after the passive update check, the app reads
  `data/latest.json` from the repository and, when its version is newer
  than the open profile's Italian catalogue, downloads
  `aifa-<yyyymm>.zip` into `%LOCALAPPDATA%\MedReminder\catalogue\staging\`,
  verifies it, imports it and deletes it. Gated by
  `Catalogue:RemoteFeed:Enabled` and *Check for updates on startup*
  (`RemoteCatalogueRefresher`, `GitHubRawCatalogueFeedClient`,
  `StartupUpdateCheckSignal`).

### Fixed

- The catalogue importer replaced a country on any version change, so
  an older embedded snapshot would overwrite a newer one; it now
  imports only newer versions.
- A snapshot that parsed to zero rows emptied the country; the
  importer now rejects snapshots below a minimum row count before
  deleting anything.
- Review follow-up: the startup catalogue refresh no longer keeps a
  SQLite connection open while it waits and downloads (it could break
  backup restore, archive import and sync join); the remote import
  runs under `WriteGate`; manifest read errors and staging leftovers
  are handled on every start.

### Build

- `scripts/download_aifa.py` fails before touching `data/` when a CSV
  is missing, lacks a required column, is below 100 000 / 200 000
  rows or below 90% of the previous run, and publishes `sha256`,
  `size` and `rows` in `latest.json`. One timestamp drives the archive
  name and the manifest version.
- The AIFA download retries transient HTTP errors (a 502 from the AIFA
  site used to fail the month's only run), and the workflow now runs
  daily from day 2 to day 7, skipping once the month is published;
  `workflow_dispatch` gains a `force` input.
- The AIFA feed publishes under `data/it/` (manifest gains
  `"country": "IT"`) so each catalogue feed owns `data/<country>/`;
  the client defaults follow and a manifest for another country is
  ignored.
- A month republished with `force` is re-imported by clients: remote
  imports store `yyyymm+<generated, UTC>` as the snapshot version when
  the manifest has a SHA-256, and a later build of the same month is
  newer.

### Docs

- `CATALOGUE-DATA.md` §2 rewritten around the remote feed; the
  embedded refresh becomes optional. `ANALYSIS.md`, `CLAUDE.md` §5,
  `ANALYSIS-DRUG-CATALOGUE.md` §3.6 and the five user guides updated.
  Prompt moved to `docs/prompt/Completed/`.
- `docs/analysis/ANALYSIS-CATALOGUE-REMOTE-FEEDS-EU-ES-FR.md`: design
  for extending the remote feed to EMA EPAR, AEMPS CIMA and ANSM BDPM
  (not implemented; waits for the AIFA feed to be validated).

---

## PR #130 — Add analysis and implementation prompt for the remote AIFA feed

Link: [vger70/MedReminder#130](https://github.com/vger70/MedReminder/pull/130)
Branch: `claude/aifa-catalog-auto-update-jrkles`

**Status:** merged (2026-09-29)

### Docs

- `docs/analysis/ANALYSIS-CATALOGUE-REMOTE-FEED.md`: startup download
  and import of the monthly AIFA snapshot published in `data/` by
  `download_aifa.yaml`, for the open profile only, staged under
  `%LOCALAPPDATA%\MedReminder\catalogue\staging\` and deleted after
  use. Records two importer defects the feature would trigger (downgrade
  by the embedded snapshot, catalogue wipe on a header-only snapshot).
- `docs/prompt/PROMPT-CATALOGUE-REMOTE-FEED.md`: implementation
  briefing.

---

## PR #129 — Household of devices with a master device (H1–H5)

Link: [vger70/MedReminder#129](https://github.com/vger70/MedReminder/pull/129)
Branch: `feature/master-slave` → `main`

**Status:** merged (2026-09-30), after the manual tests
(`docs/analysis/HOUSEHOLD-MANUAL-TESTS.md`).

### Added

- The final merge of the household feature: PRs #116–#128 listed
  below. An installation is shared between devices (profiles, roles,
  PINs, installation settings), each device holds only the profiles an
  administrator gives it, one master device sends email and runs the
  cloud backup, and a device can be removed.

### Changed

- Tools → Sync… is shown to administrators only.
- Profile operation schemas 4 and 5: every device of a sync group needs
  this version.

---

## PR #128 — Rotate the profile keys of a removed device (household H5b)

Link: [vger70/MedReminder#128](https://github.com/vger70/MedReminder/pull/128)
Branch: `claude/household-h5b-profile-rotation` → `feature/master-slave`

### Added

- Removing a device also changes the keys of the profiles it held; the
  other devices take the new keys from the installation without typing
  anything (at start, or in the master's background checks; an open
  profile asks to restart).
- A device that does not hold every profile of the device to remove
  refuses the removal and names the missing profiles.

### Docs

- User guides (5 languages), `docs/ANALYSIS.md`, analysis revision 15,
  `docs/analysis/HOUSEHOLD-MANUAL-TESTS.md` (H5b section).

---

## PR #127 — Remove a device from the installation (household H5a)

Link: [vger70/MedReminder#127](https://github.com/vger70/MedReminder/pull/127)
Branch: `claude/household-h5-device-removal` → `feature/master-slave`

### Added

- Tools → Installation → Devices → Remove device…: the removed device
  keeps what it has and receives nothing new. The installation moves to
  a new key, passphrase and recovery key; every other device takes the
  new key once, with the new passphrase or a code (D-15 option A), and
  keeps its changes made meanwhile. Removing the master makes the
  removing device the master.

### Docs

- User guides (5 languages), `docs/SYNC-FORMAT.md` §9.3,
  `docs/ANALYSIS.md`, analysis revision 14 (D-15 decided),
  `docs/analysis/HOUSEHOLD-MANUAL-TESTS.md` (manual tests H1–H5).

---

## PR #126 — Check every profile on the master device (household H4c)

Link: [vger70/MedReminder#126](https://github.com/vger70/MedReminder/pull/126)
Branch: `claude/household-h4c-master-profiles` → `feature/master-slave`

### Added

- The master device checks every profile it holds, also those not open:
  it syncs them, and sends their low-stock and dose emails to each
  profile's recipients.

### Changed

- On a device that is not the master, a prescription request opens in
  the mail client only, and the email connection test is refused with an
  explanation.

### Docs

- User guides (5 languages), `docs/ANALYSIS.md`, analysis revision 13.

---

## PR #125 — Confirm the master handover with a wizard on the elected device (household H4b)

Link: [vger70/MedReminder#125](https://github.com/vger70/MedReminder/pull/125)
Branch: `claude/household-h4b-handover` → `feature/master-slave`

### Added

- Handover wizard on the elected device: settings of the installation,
  email connection test, cloud backup sign-in and passphrase (typed
  again, never copied), download of the profiles the master needs, and
  recovery of the others with the installation passphrase. The device
  becomes master only after an administrator confirms it.
- An election grants the elected device the profiles the electing
  device holds.
- Takeover: an election made while the master is not seen for more than
  24 hours.

### Docs

- User guides (5 languages), `docs/ANALYSIS.md`, analysis revision 12.

---

## PR #124 — Send email and run the cloud backup from the master device only (household H4a)

Link: [vger70/MedReminder#124](https://github.com/vger70/MedReminder/pull/124)
Branch: `claude/household-h4a-master` → `feature/master-slave`

### Added

- Master device: the device that publishes the installation is the
  master; an administrator moves the role from Tools → Installation →
  Devices → Make master…. The outgoing master releases at its next sync;
  a master not seen for 25 hours is taken over.
- Only the master sends email reminders (low stock, dose) and runs the
  cloud backup, and only while it has synced the installation in the
  last 24 hours. Without a master, every device sends as before.

### Docs

- User guides (5 languages), `docs/SYNC-FORMAT.md` §9.3,
  `docs/ANALYSIS.md`, analysis revision 11.

---

## PR #123 — Join an existing installation from the first-run wizard (household H3d-2)

Link: [vger70/MedReminder#123](https://github.com/vger70/MedReminder/pull/123)
Branch: `claude/household-h3d2-first-run-join` → `feature/master-slave`

### Added

- The first-run wizard offers "Join an existing installation…": a new
  PC joins with a code or with the installation passphrase and an
  administrator's PIN, and starts with the profiles it received.

### Docs

- User guides (5 languages), `docs/ANALYSIS.md`, `CLAUDE.md` §5
  (`setup\` folder), analysis revision 10.

---

## PR #122 — Manage the installation from Tools → Installation (household H3d-1)

Link: [vger70/MedReminder#122](https://github.com/vger70/MedReminder/pull/122)
Branch: `claude/household-h3d-ui` → `feature/master-slave`

### Added

- Tools → Installation… (administrators only): publish the
  installation, list its devices, add a device with a code for the
  selected profiles, join an existing installation with a code or with
  the installation passphrase and an administrator's PIN.
- The household syncs in the background every 15 minutes.

### Changed

- Tools → Sync… is shown to administrators only.
- The storage choice and the pairing dialogs are shared by the sync and
  installation windows.

### Docs

- User guides (5 languages): the installation section; email settings
  are shared by every device until the master device arrives.
- `docs/ANALYSIS.md`; analysis revision 9.

---

## PR #121 — Join an existing installation with a household pairing code (household H3c)

Link: [vger70/MedReminder#121](https://github.com/vger70/MedReminder/pull/121)
Branch: `claude/household-h3c-join-installation` → `feature/master-slave`

### Added

- Household pairing codes (`mrpair2`): an admin offers the household
  and selected profiles; the joining device grants those profiles to
  itself and keeps them after the offer ends.
- Join with the household passphrase: an admin of the household
  approves on the new device with an admin PIN; the escrow grants the
  selected profiles.
- `JoinInstallation`: each granted profile is built from its sync group
  in a staging folder, moved to `profiles\<id>\` and added to
  `profiles.json` under its household id.
- `HouseholdLinked` (profile operation schema 5): an adopted profile
  group records the household that claims it; the earliest claim wins.

### Fixed

- The start-up reconciliation no longer records a local role that
  differs from the household's, which could undo a demotion made on
  another device.

### Docs

- `docs/SYNC-FORMAT.md` §6, §9.1, §9.5; `docs/ANALYSIS.md`; analysis
  revision 8.

---

## PR #120 — Grant profile keys per device and escrow them for recovery (household H3b)

Link: [vger70/MedReminder#120](https://github.com/vger70/MedReminder/pull/120)
Branch: `claude/household-h3b-profile-keys` → `feature/master-slave`

### Added

- Each device has a household key pair (`household/device.protected`,
  DPAPI); its public key is published in the household.
- The household has a recovery key pair: the private key is stored on
  the storage wrapped with the household passphrase
  (`recovery.<v>.wrap`), so any device can escrow a profile key
  without knowing the passphrase.
- Profile group keys are granted per device and escrowed for recovery
  (`ProfileKeyGranted`, `ProfileKeyRevoked`, `ProfileKeyEscrowed`);
  publishing and every household run adopt the installation's synced
  profiles.

### Docs

- `docs/SYNC-FORMAT.md` §9.4, `docs/ANALYSIS.md`, `CLAUDE.md` §5,
  analysis revision 7.

---

## PR #119 — Replicate the household through a household group (household H3a)

Link: [vger70/MedReminder#119](https://github.com/vger70/MedReminder/pull/119)
Branch: `claude/household-h3a-household-sync` → `feature/master-slave`

### Added

- The household can be published on a sync storage as a household group
  and joined with a household passphrase; segments carry profile and
  settings changes both ways, and a projection writes them into
  `profiles.json` and the settings files. The SMTP password is in clear
  only inside the encrypted segments. Not yet reachable from the UI.

### Docs

- `SYNC-FORMAT.md` §9 (household group), `ANALYSIS.md` §5, `CLAUDE.md` §5.

## PR #114 — Add analysis of a household of devices with a master device

Link: [vger70/MedReminder#114](https://github.com/vger70/MedReminder/pull/114)
Branch: `claude/medreminder-sync-analysis-fquf7h`

### Docs

- `docs/analysis/ANALYSIS-HOUSEHOLD-MASTER-DEVICE.md`: analysis of one
  installation spread over several devices (PC, later phone). The
  first device is master; later devices join through a household sync
  group that replicates profiles, roles, PIN hashes and installation
  settings; an admin can elect another master, confirmed by a handover
  wizard; only the master sends email and runs the scheduled cloud
  backup. A setup wizard lets a new device join an existing
  installation; each device holds only the profiles an admin grants
  it, enforced by per-device key wrapping. Records the duplicate-email
  defect of today's two-PC setups,
  proposes changes to the requirements, and splits the work into steps
  H0 to H6 with open decisions.
- `docs/ANALYSIS.md` §12: index entry for the new analysis.
- `CLAUDE.md` §4: PRs of this feature target the integration branch
  `feature/master-slave`; only the final PR merges it into main.
## PR #118 — Record installation settings in the household (household H2b)

Link: [vger70/MedReminder#118](https://github.com/vger70/MedReminder/pull/118)
Branch: `claude/household-h2b-installation-settings` → `feature/master-slave`

### Added

- The SMTP transport and password, the scheduled cloud backup policy and
  the reference country are recorded in the household
  (`HouseholdSettingChanged`); the SMTP password only protected with
  DPAPI. The start-up reconciliation records settings restored by an
  import or edited by hand.

### Changed

- Settings → Email SMTP, Backup and General save through use cases with
  the admin checks in the Application layer.
- The reference country can be changed by an administrator only.

### Docs

- User guides (5 languages), `ANALYSIS.md` §5.2.

## PR #117 — Record profile administration in a local household and allow role changes (household H2a)

Link: [vger70/MedReminder#117](https://github.com/vger70/MedReminder/pull/117)
Branch: `claude/household-h2a-profile-registry` → `feature/master-slave`

### Added

- Local household store under `%LOCALAPPDATA%\MedReminder\household\`:
  every profile creation, rename, PIN, role change and deletion is
  recorded as an HLC-stamped operation with last-writer-wins registers,
  ready for replication in step H3.
- Manage profiles → Change role…: an admin makes another profile an
  administrator or a standard user; the open profile's role stays fixed
  and one admin always remains.
- Start-up reconciliation of the household with `profiles.json`.

### Changed

- Profile administration goes through use cases with the admin checks
  in the Application layer; the UI no longer writes the registry,
  except the first-run wizard.

### Docs

- User guides (5 languages), `ANALYSIS.md` §5, `CLAUDE.md` §5,
  `ANALYSIS-MULTI-USER-ROLES-OVERVIEW.md`.

## PR #116 — Send each low-stock email once per sync group (household H1)

Link: [vger70/MedReminder#116](https://github.com/vger70/MedReminder/pull/116)
Branch: `claude/household-h1-email-dedup` → `feature/master-slave`

### Fixed

- Every synced device with email configured sent the same low-stock
  email. A successful email is now a replicated fact
  (`SentEmailNotification`, operation `EmailNotificationSent`); a device
  about to email for an epoch another device already emailed for leaves
  the email out and still shows its toast. Two devices that notify
  before either has synced still both send (removed by the master, H4).

### Changed

- Operation schema 4, image schema 3: apps up to v2.10 must be updated
  on every device of a group.

### Docs

- `SYNC-FORMAT.md`, `ANALYSIS.md`, `ANALYSIS-B1-MOBILE-SYNC.md` §20.

## PR #115 — Close the join-during-listing-lag limit and ask which group a passphrase opens

Link: [vger70/MedReminder#115](https://github.com/vger70/MedReminder/pull/115)
Branch: `claude/household-h0-join-fixes`

Step H0 of the household / master device feature; targets `main`
(decision D-17).

### Fixed

- A device joining through OneDrive or Google Drive while the listing
  lagged could end in `RebuildRequired`. The join now waits until the
  provider itself lists its record (`IProviderListing`), rechecks that
  the chosen image still covers the remaining segments and picks again
  if not.
- A failed first join removes its device record, so it does not hold
  back compaction on the other devices.

### Changed

- A join by passphrase that opens several groups asks which one, showing
  each group's devices (`SyncGroupChoiceDialog`), instead of joining the
  first.

### Docs

- User guides (5 languages): configure email on one PC of a synced group
  only; group choice; join time with a cloud account.
- `STATUS.md`, `ANALYSIS.md`, `ANALYSIS-B1-MOBILE-SYNC.md` §20: the known
  limit is closed.

## PR #113 — Align STATUS, EVOLUTION and ANALYSIS with v2.10.0

Link: [vger70/MedReminder#113](https://github.com/vger70/MedReminder/pull/113)
Branch: `claude/docs-status-v2.10`

### Docs

- `docs/STATUS.md`: snapshot at v2.10.0; A2 phases 2 and 3 shipped
  (#110, #111), webcam decode on a real pack still unconfirmed;
  multi-user G/I design ready, gated on decisions D1–D7; next steps.
- `docs/EVOLUTION.md` / `docs/EVOLUTION-DONE.md`: A2 moved to the
  shipped items (§3.2); sequence and multi-user note updated.
- `docs/ANALYSIS.md`: release line 2.10.x;
  `ANALYSIS-MULTI-USER-ROLES-OVERVIEW.md` listed.

## PR #112 — Design role change and all-profiles view (multi-user G/I)

Link: [vger70/MedReminder#112](https://github.com/vger70/MedReminder/pull/112)
Branch: `claude/sweet-ritchie-ft5498`

### Docs

- `docs/analysis/ANALYSIS-MULTI-USER-ROLES-OVERVIEW.md`: design for
  changing a profile's role (admin only, never the open profile or the
  last admin) and a read-only all-profiles view for the admin, built on
  a snapshot copy of each profile with schema patches and consumption
  catch-up applied to the copy. Decisions D1–D7 still open.
- `docs/prompt/PROMPT-MULTI-USER-ROLES-OVERVIEW.md`: implementation
  brief (steps G1, I1, I2, D).
- `docs/STATUS.md` §3.5: pointer to the new design.

---

## PR #111 — Restock a medicine by scanning its package (A2 phase 3)

Link: [vger70/MedReminder#111](https://github.com/vger70/MedReminder/pull/111)
Branch: `claude/a2-restock-by-scan`

### Added

- Stock → **Restock from barcode…**: the scanned national code
  identifies the medicine, with no row selected; the new-package
  dialog opens with the quantity of its last new package. Several
  matches are resolved with a pick list (inactive medicines marked).
- A code no medicine carries, when the catalogue knows it, can be
  added as a new medicine (form filled in from the catalogue) or
  linked to a medicine without a code, which is then restocked.
- `RestockByScanQuery` (`src/MedReminder.Application/Catalogue/`),
  `MedicinePickerDialog`; `RestockByScanQueryTests`.

### Changed

- `StockAdjustmentDialog` accepts an initial quantity;
  `MedicineEditDialog` accepts an initial catalogue row in Create
  mode.

### Docs

- 16 new UI keys in the five dictionaries; "Restock by scanning"
  section in the five user guides; `ANALYSIS-A2-BARCODE-SCAN.md`
  (§5C.7 as implemented, §12.1 settled), `ANALYSIS.md`, `STATUS.md`,
  `EVOLUTION.md`.

## PR #110 — Scan medicine barcodes with the webcam (A2 phase 2)

Link: [vger70/MedReminder#110](https://github.com/vger70/MedReminder/pull/110)
Branch: `claude/a2-webcam-scan`

### Added

- Scan barcode window: **Use webcam** reads the package with the PC
  camera (preview with a framing guide), through the same parser and
  catalogue lookup as the USB scanner. The camera starts only on
  request and is released when a code is read, on **Use scanner**, on
  close, or after 30 s without a code (`Capture:ScanTimeoutSeconds`).
- Camera error states in the window: no camera, blocked by the Windows
  privacy settings (opens `ms-settings:privacy-webcam`), start-up
  failure (copies the log folder path); **Try again** in each.
- `ICameraCaptureService` (Application), `WindowsCameraCaptureService`
  and `FrameBarcodeDecoder` (`src/MedReminder.UI/Camera/`); new
  `Capture` keys `MaxDecodeFps` and `CameraMaxWidthPixels`.
- `FrameBarcodeDecoderTests`: Code 32, EAN-13 and GS1 DataMatrix
  rendered with ZXing and decoded from BGRA pixels.

### Changed

- The scan window is resizable and grows for the webcam preview; the
  scan button tooltip mentions the webcam.

### Security

- Camera frames stay in memory; frames and barcode payloads are never
  saved or logged.

### Build

- `ZXing.Net` 0.16.11 (Apache 2.0) in the UI project; no Bitmap
  binding package.

### Docs

- 12 new UI keys in the five dictionaries; "With the webcam" section
  in the five user guides; `ANALYSIS-A2-BARCODE-SCAN.md` (design as
  built, acceptance result), `ANALYSIS.md`, `STATUS.md`,
  `EVOLUTION.md`, `README.md`.

## PR #109 — Align STATUS, EVOLUTION and ANALYSIS with v2.9.1

Link: [vger70/MedReminder#109](https://github.com/vger70/MedReminder/pull/109)
Branch: `claude/docs-review-v2.9`

### Docs

- `docs/STATUS.md`: snapshot at v2.9.1; Phase 4c and P8 released in
  v2.9.0; #107 and #108 shipped; draft spike PR #106; next steps A2
  phase 2 then phase 3, and the Android spikes.
- `docs/EVOLUTION.md`: shipped desktop side of B.1, OneDrive and
  Google Drive; A2 phase 2 then phase 3 planned as the next desktop
  work; multi-user non-goals left unplanned until a need emerges.
- `docs/ANALYSIS.md`: release line, network calls, sync UI, Phase 4c
  pairing and key rotation, P8, toast language, secrets, therapy card
  (§9.6), hosted-service and project counts, known sync join limit.
- `ANALYSIS-A2-BARCODE-SCAN.md` §1.6 and `EVOLUTION-PROPOSALS.md`
  aligned.

## PR #108 — Add a per-profile text size and honour display scaling and high contrast

Link: [vger70/MedReminder#108](https://github.com/vger70/MedReminder/pull/108)
Branch: `claude/large-text-mode-prompt-us0vm4`

### Added

- Settings → General: **Text size** (Normal / Large / Extra large),
  stored per profile in `profiles\<id>\ui.settings.json`
  (`ProfileUiSettingsFile`). Applied at startup to every window of the
  profile, including grid rows, list columns and the help viewer.

### Changed

- `MedReminderFormBase` scales forms by display DPI / 96 on load, so
  fixed-pixel dialogs no longer clip text above 100 % display scaling.
- Hint text uses `SystemColors.GrayText` instead of `DarkGray` (about
  2:1 contrast); status colours and the medicine list palette yield to
  Windows contrast themes (`UiColors`).
- The General tab button is now **Save**, since it saves the whole tab.

### Docs

- Text-size section in the five user guides; `ANALYSIS.md` and
  `CLAUDE.md` list `ui.settings.json`.

## PR #107 — Print the therapy card as a table and save it as PDF

Link: [vger70/MedReminder#107](https://github.com/vger70/MedReminder/pull/107)
Branch: `claude/medication-card-pdf-prompt-jxu0ft`

### Added

- Therapy report dialog: **Save as PDF…** through the built-in
  "Microsoft Print to PDF" printer, to a path chosen in a save dialog;
  a clear message when that printer is not installed. No new package.
- **Include notes** option (off by default: notes may be private) and
  **Paper** choice (A4 / Letter, preselected from the Windows region).
- `TherapyCardBuilder` / `TherapyCard`: structured, localized content
  of the card, with the active profile's name
  (`src/MedReminder.Application/Reporting/`).
- `TherapyCardPagination` and tests on the model, the text renderer
  and pagination (`tests/MedReminder.Application.Tests/Reporting/`).

### Changed

- **Print…** draws a paginated table (header and column titles on
  every page, disclaimer and page number in the footer) instead of
  monospace text (`src/MedReminder.UI/Printing/TherapyCardPrintDocument.cs`).
- `TherapyReport` renders the plain text from the card model; the
  output of `TherapyReport.Build` is unchanged.

### Docs

- New UI and report keys in the five dictionaries; new "Therapy
  report (print and PDF)" section in the five user guides.
- `EVOLUTION-PROPOSALS.md` §4.4 and `STATUS.md` updated.

## PR #104 — Align the B.1 Phase 0 exit with the S6 result

Link: [vger70/MedReminder#104](https://github.com/vger70/MedReminder/pull/104)
Branch: `claude/nifty-galileo-vfepsw`

### Docs

- `ANALYSIS-B1-MOBILE-SYNC.md` §13: the Phase 0 exit requires S6 for
  Windows only, since §18 defers its Android half to Phase 5; Phase 5
  runs the Android halves of S6 and S7 before provider sign-in.
## PR #105 — Replicate the profile name and notification recipients (P8)

Link: [vger70/MedReminder#105](https://github.com/vger70/MedReminder/pull/105)
Branch: `claude/b1-phase-4c-t44dis`

### Added

- Sync operation `ProfileSettingChanged` (operation schema version 3):
  the profile's display name and its notification recipients (to,
  caregiver, doctor) reach every device of the profile's sync group,
  last writer wins per setting; a device that joins takes the group's
  values (`UpdateNotificationSettings`, `RenameProfile`,
  `ProfileSettingsProjection`, `ProfileSettingsStore`).

### Changed

- Settings → Notifications and Manage profiles → Rename go through use
  cases instead of writing the files directly. Renaming another profile
  that is synced asks to open that profile first.

### Docs

- `docs/SYNC-FORMAT.md` §6 and current versions in its header;
  `docs/SYNC-TWO-PC-CHECKLIST.md` P1–P4; user guides (5 languages);
  `ANALYSIS-B1-MOBILE-SYNC.md` (P8 closed); `docs/STATUS.md` updated
  for Phase 4c, P8 and the published Google Cloud project.

### Fixed

- `CHANGE_LOG.md`: the #97 entry was inside the maintenance notes; #99
  and #100 were out of order.

## PR #103 — Open key rotation and device removal to every profile

Link: [vger70/MedReminder#103](https://github.com/vger70/MedReminder/pull/103)
Branch: `claude/b1-phase-4c-t44dis`

### Changed

- Tools → Sync…: Change key and passphrase… and Remove device… are
  available to every profile. A sync group belongs to one profile, so
  they reach only that profile's devices and data; no sync action
  depends on the profile role any more
  (`src/MedReminder.UI/Forms/SyncDialog.cs`; product owner,
  2026-09-28).

### Docs

- User guides (5 languages), `docs/SYNC-TWO-PC-CHECKLIST.md` step 14,
  `ANALYSIS-B1-MOBILE-SYNC.md` change log.

## PR #102 — Let every profile manage the sync of its own data

Link: [vger70/MedReminder#102](https://github.com/vger70/MedReminder/pull/102)
Branch: `claude/b1-phase-4c-t44dis`

### Changed

- Tools → Sync…: every profile can enable, join (passphrase or pairing
  code), pair a device, rebuild, enter the new key and disable sync
  for its own data. Before, only the administrator could, which left
  profiles with the user role with no way to sync. Changing the key
  and removing a device stay with the administrator
  (`src/MedReminder.UI/Forms/SyncDialog.cs`; product owner,
  2026-09-28).

### Docs

- User guides (5 languages), `docs/SYNC-TWO-PC-CHECKLIST.md` step 14,
  `ANALYSIS-B1-MOBILE-SYNC.md` change log.

## PR #101 — Keep the sync window open while one of its actions runs

Link: [vger70/MedReminder#101](https://github.com/vger70/MedReminder/pull/101)
Branch: `claude/b1-phase-4c-t44dis`

### Fixed

- Closing Tools → Sync… while an action was running (for example
  Enable sync…) reported "Cannot access a disposed object" although the
  action succeeded. While an action runs, the window now stays open,
  Close is disabled and the wait cursor shows; if the application
  closes the window anyway, the action's messages are dropped
  (`src/MedReminder.UI/Forms/SyncDialog.cs`).

## PR #100 — Show toasts in the app language instead of the Windows language

Link: [vger70/MedReminder#100](https://github.com/vger70/MedReminder/pull/100)
Branch: `claude/stoic-ride-3st4kz`

### Fixed

- Dose-reminder and low-stock toasts use the language chosen in the
  app instead of the Windows UI language. The dose-reminder email,
  built by the same composer, now also follows the user's language
  (`NotificationTexts`).

### Docs

- User guides (5 languages): toasts follow the language chosen in the
  app.

## PR #99 — B.1 Phase 4c: key rotation, device removal and pairing codes

Link: [vger70/MedReminder#99](https://github.com/vger70/MedReminder/pull/99)
Branch: `claude/b1-phase-4c-t44dis`

### Added

- Tools → Sync… → Pair a device…: a QR code and the same code as text,
  valid 10 minutes and only while the window is open; the window is
  excluded from screen capture. Join with a pairing code… joins a
  group with it instead of the passphrase (`SyncPairingOffers`,
  `SyncPairingCode`, `SyncPairingDialog`).
- Change key and passphrase… and Devices → Remove device…: a new group
  key under a new passphrase; a removed device never gets it and reads
  nothing written afterwards (`RotateSyncKey`).
- Enter the new key…: after a rotation elsewhere, with the new
  passphrase or a pairing code; the profile is rebuilt and this
  device's own changes are carried over (`JoinSyncGroup.RekeyAsync`,
  `ApplyRemoteOperations.ApplyCarriedAsync`,
  `ISyncSetupService.RekeyAsync`).

### Changed

- A rotation starts a new sync generation sealed with the new key.
  Devices that see it publish nothing more until they have the new key
  (`SyncRunResult.NewKeyRequired`), so none of their changes is sealed
  with a key the removed device holds.

### Security

- The pairing code carries a secret that opens an ephemeral pairing
  file, not the group key: once the offer ends a photographed code
  opens nothing (`docs/SYNC-FORMAT.md` §4.4).
- A newer generation sealed with an older key is ignored; device
  records and checkpoints sealed with another key are skipped, so they
  no longer block compaction or a join.

### Build

- `QRCoder` 1.8.0 (MIT) in the UI project for the QR code.

### Docs

- `docs/SYNC-FORMAT.md` (§2, §4.4, §7), `ANALYSIS-B1-MOBILE-SYNC.md`
  (§6.1, §6.2 as implemented, Phase 4 split table),
  `docs/SYNC-TWO-PC-CHECKLIST.md` (K1–K11), `docs/STATUS.md`,
  `docs/ANALYSIS.md` (dependencies), user guides (5 languages).
## PR #98 — Add a development status report

Link: [vger70/MedReminder#98](https://github.com/vger70/MedReminder/pull/98)
Branch: `claude/report-stato-arte-sviluppi-h9s7ks`

### Docs

- `docs/STATUS.md`: snapshot at v2.8.1 of what has shipped (MVP,
  evolution items, B.1 phases 1–4b) and what remains open (B.1 phases
  4c–7 with spikes and decisions, A2 webcam, proposals, automatic
  update, issue #11), with suggested next steps.

## PR #97 — Allow deleting a medicine without history and hide inactive medicines

Link: [vger70/MedReminder#97](https://github.com/vger70/MedReminder/pull/97)
Branch: `claude/medicine-delete-hide-inactive`

### Added

- Therapy → Delete… removes a medicine entered by mistake, with its
  schedule, while no stock entry, intake, count or suspension was
  recorded for it; otherwise the user is told to deactivate it or
  retract the entries first (`DeleteMedicine`,
  `MedicineDeletionRepository`).
- Sync operation `MedicineDeleted` (operation schema version 2): it
  wins over every operation for the medicine, and later ones are
  logged but not applied. Other operations keep version 1; sync
  images move to schema version 2 (`docs/SYNC-FORMAT.md`).
- Therapy → Show inactive medicines, and an *Inactive* status in the
  list.

### Changed

- The main list hides deactivated medicines by default; the status
  bar counts the hidden ones.

### Docs

- User guides (5 languages), `docs/ANALYSIS.md`,
  `docs/SYNC-FORMAT.md`, `ANALYSIS-B1-MOBILE-SYNC.md` (P11, §4.2).

## PR #96 — Align the repeat-passphrase label in the sync passphrase dialog

Link: [vger70/MedReminder#96](https://github.com/vger70/MedReminder/pull/96)
Branch: `claude/bold-lamport-eru3jp`

### Fixed

- Sync passphrase dialog: the "Repeat passphrase" label sat below its
  text box because the layout gave the leftover height to the last
  row. Content rows now size to fit and a filler row takes the rest
  (`src/MedReminder.UI/Forms/SyncPassphraseDialog.cs`).

## PR #95 — B.1 Phase 4b: Google Drive sync transport and cloud backups

Link: [vger70/MedReminder#95](https://github.com/vger70/MedReminder/pull/95)
Branch: `claude/b1-phase4b-google-drive`

### Added

- Sync through Google Drive: Tools → Sync… offers Google Drive next to
  OneDrive and a shared folder. Sync files go to the hidden app data
  folder of the Google account, encrypted as before
  (`docs/SYNC-FORMAT.md`).
- Cloud backup to Google Drive: Settings → Backup → storage Google
  Drive; encrypted snapshots in a visible `MedReminder/backups` folder
  of My Drive; restore lists them and downloads only the chosen one.
- `GoogleDriveClient`, `GoogleDriveSyncTransport`,
  `GoogleDriveArchiveStorage`, `GoogleOAuthClient`
  (`src/MedReminder.Infrastructure.Portable/Cloud/GoogleDrive/`): Drive
  REST v3 and OAuth for installed apps over `HttpClient`, no Google
  library. Duplicate names allowed by Drive are resolved by an
  oldest-wins rule; retried creates are recognised by a pre-generated
  id.
- `GoogleCloudAccountService`: sign-in in the browser with a loopback
  redirect and PKCE; refresh tokens in
  `%LOCALAPPDATA%\MedReminder\googledrive.protected` (DPAPI).
  `CloudAccountService` routes between OneDrive and Google Drive.

### Changed

- `CloudProvider` gains `GoogleDrive`. `CloudStorageException` names
  its provider.

### Build

- The Google OAuth client id and secret are not in the repository:
  `MEDREMINDER_GOOGLE_CLIENT_ID` / `MEDREMINDER_GOOGLE_CLIENT_SECRET`
  at build time, or `GoogleDrive:ClientId` / `ClientSecret` in
  configuration (`docs/PACKAGING.md` §24). Without them Google Drive is
  not offered.
- `.github/workflows/dotnet-desktop.yml`: both `dotnet publish` steps
  receive the two values from repository secrets of the same names.

### Fixed

- `docs/PACKAGING.md` §12–§14 described a `release.yml` workflow with a
  test step and a single ZIP; they now describe `dotnet-desktop.yml` as
  it is (no test step, two ZIPs and an MSI).

### Docs

- Spike S7 results (`ANALYSIS-B1-MOBILE-SYNC.md` §18.7) and a known
  limit in §20 (a device joining during a listing lag can be told to
  rebuild); `SYNC-FORMAT.md` Google Drive layout; Google Drive steps in
  `SYNC-TWO-PC-CHECKLIST.md`; `ANALYSIS.md`; `PACKAGING.md` §24;
  `ANALYSIS-C3PP-CLOUD-PROVIDERS.md`; user guides (5 languages).

---

## PR #94 — Stream OneDrive downloads, fix hidden-provider validation, share snapshot naming

Link: [vger70/MedReminder#94](https://github.com/vger70/MedReminder/pull/94)
Branch: `claude/code-review-followup`

**Status:** merged (2026-09-28)

### Fixed

- OneDrive archive downloads are streamed to the restore file instead
  of buffered in memory, so a large snapshot is no longer cut off by the
  2-minute HTTP timeout on a slow link.
- Settings no longer asks for (and creates) an unused cloud folder when
  the provider choice is hidden and the saved target is OneDrive.

### Changed

- One definition of the C.3+ snapshot name (`CloudSnapshotName` in
  `MedReminder.Application/Export/`) for the backup host, retention and
  the restore list; names outside it (manual exports, renamed files)
  are listed without a profile and never pruned.
- `OneDriveClientFactory` (portable) keeps one OneDrive client per
  account for both sync and the cloud backup; `CloudArchiveStorage` no
  longer builds its own clients over a hard-coded HTTP client and clock.
- `OneDriveSyncTransport` resolves the files under `sync/` once per
  change of its change-feed index, with memoized folder paths, instead
  of walking every node's parent chain on each listing.

---

## PR #93 — Surface ended OneDrive sessions and verify restored archive profiles

**Status:** merged (2026-09-28)

Link: [vger70/MedReminder#93](https://github.com/vger70/MedReminder/pull/93)
Branch: `claude/code-review-rjy7oe`

### Fixed

- Automatic cloud backups no longer stop silently when the OneDrive
  session ends: a silent token check runs before the export, and a
  needed sign-in is recorded as the run's error, shown in Settings.
- `OneDriveArchiveStorage` lets `CloudSignInRequiredException` reach the
  caller instead of reporting a missing folder; the restore dialog asks
  for a new sign-in instead of listing no snapshots.
- OneDrive snapshot restore checks the other-profile confirmation
  against the downloaded manifest, not the file name.
- An `onedrive.protected` cache that decrypts but cannot be read starts
  empty instead of breaking every MSAL call.
- Snapshot file names use an invariant-culture timestamp, so dates
  parse correctly on non-Gregorian calendars.

## PR #91 — B.1 Phase 4a: OneDrive sync transport and cloud backups

Link: [vger70/MedReminder#91](https://github.com/vger70/MedReminder/pull/91)
Branch: `claude/b1-phase4a-onedrive`

**Status:** merged (2026-09-27)

### Added

- Sync through OneDrive: Tools → Sync… offers OneDrive (Microsoft
  sign-in in the browser) or a shared folder when enabling or joining.
  Files go to `sync/` in the app folder (`/Apps/MedReminder26`),
  encrypted as before (`docs/SYNC-FORMAT.md`).
- `OneDriveClient` and `OneDriveSyncTransport`
  (`src/MedReminder.Infrastructure.Portable/Cloud/OneDrive/`): Graph REST,
  create-only writes, temporary name plus rename for large files, a
  change-feed index instead of folder walks.
- `MsalCloudAccountService`: MSAL public client; token cache in
  `%LOCALAPPDATA%\MedReminder\onedrive.protected` (DPAPI).
- "Sign in to OneDrive again" in the sync window when the session ends.
- Cloud backup to OneDrive (C.3++ Phase 2): Settings → Backup → storage
  OneDrive; encrypted snapshots in `backups/` of the app folder;
  restore lists them by name and downloads only the chosen one.
- `IArchiveStorage` contract tests run against the OneDrive backend.

### Changed

- `SyncSettings` gains `Provider` and `AccountId`; existing files read
  as folder targets. `ISyncSetupService` takes a `SyncTarget`.
- `BackupSettings` gains `CloudProvider` and `CloudAccountId`; existing
  files keep the folder target. A signed-out account skips the backup
  run like a missing folder.

### Docs

- Spike S6 results (`ANALYSIS-B1-MOBILE-SYNC.md` §18.6); Phase 4 split
  into 4a/4b/4c; `SYNC-FORMAT.md` sync root in a provider app folder;
  OneDrive steps in `SYNC-TWO-PC-CHECKLIST.md`; `ANALYSIS.md`; user
  guides (5 languages); `ANALYSIS-C3PP-CLOUD-PROVIDERS.md`.

### Build

- `Microsoft.Identity.Client` 4.90.1 in `MedReminder.Infrastructure`.

---

## PR #89 — B.1 Phase 3d: desktop sync service and Tools → Sync… window

Link: [vger70/MedReminder#89](https://github.com/vger70/MedReminder/pull/89)
Branch: `claude/b1-phase3d-desktop-sync`

**Status:** merged (2026-09-27)

### Added

- Tools → Sync…: enable sync, join a group, sync now, devices list,
  conflict review (restore a lost medicine field, dismiss), rebuild and
  disable. Configuration is for the administrator only.
- `SyncHostedService`: sync at start, every 5 minutes
  (`Sync:IntervalMinutes`) and shortly after local changes.
- Import and restore on a synced profile warn, publish pending changes
  and start a new sync generation.
- User guide section "Sync between PCs" (five languages) and
  `docs/SYNC-TWO-PC-CHECKLIST.md`.

### Changed

- The database swap of the import is shared with the sync join and
  rebuild (`ProfileDatabaseSwap`).

### Docs

- `ANALYSIS-B1-MOBILE-SYNC.md` §13, §20; `ANALYSIS.md` §6 and sync.

---

## PR #88 — B.1 Phase 3c: encrypted segments, group key, checkpoints and a folder transport

Link: [vger70/MedReminder#88](https://github.com/vger70/MedReminder/pull/88)
Branch: `claude/b1-phase3c-segments-transport`

**Status:** merged (2026-09-27)

### Added

- Sync engine (`SyncEngine`): encrypted append-only segments per
  device, causal apply with dependency vectors (`SyncPeers`), device
  records, checkpoints and compaction, generation and gap detection.
- Group key wrapped with the sync passphrase (Argon2id) and the `MRS1`
  encrypted envelope with an authenticated cleartext header.
- Genesis and checkpoint images of the profile database
  (`SqliteSyncSnapshotStore`); `CreateSyncGroup`, `JoinSyncGroup`,
  `ResetSyncGeneration`.
- `ISyncTransport` with `LocalFolderSyncTransport` and contract tests;
  DPAPI key store (`sync.protected`).
- `docs/SYNC-FORMAT.md`: public contract of the sync files.

### Changed

- `IArchiveCipher` gains associated-data overloads.
- Stock-movement and peer repositories reuse instances already tracked
  in the unit of work.

### Docs

- `ANALYSIS-B1-MOBILE-SYNC.md` §7.3, §13, §20; `ANALYSIS.md` runtime
  files and persistence.

---

## PR #87 — B.1 Phase 3b-2: re-evaluate stock counts on the facts recorded before them by HLC

Link: [vger70/MedReminder#87](https://github.com/vger70/MedReminder/pull/87)
Branch: `claude/b1-phase3b2-count-reevaluation`

**Status:** merged (2026-09-27)

### Added

- `CountReevaluation`: with sync enabled, each synced stock count is
  evaluated again on the facts recorded before it by HLC, with end
  dates as of that instant; a retracted fact is in no snapshot.
- `SyncGenesis`: genesis versions of every register, for the
  enable-sync flow.
- `SyncOperations.EntityId` (boot patch) and the per-medicine index.

### Changed

- `LedgerFactsLoader` applies the re-evaluation when sync is enabled;
  with sync disabled the stored count outcome is used as before.

### Docs

- `ANALYSIS-B1-MOBILE-SYNC.md` §4.2, §4.3, §13, §20; `ANALYSIS.md`.

---

## PR #86 — B.1 Phase 3b-1: apply operations from other devices, with LWW registers and conflicts

Link: [vger70/MedReminder#86](https://github.com/vger70/MedReminder/pull/86)
Branch: `claude/b1-phase3b-merge-apply`

**Status:** merged (2026-09-27)

### Added

- `ApplyRemoteOperations`: merges operations recorded on other devices
  (facts by id, retraction wins, last writer wins by HLC on registers),
  idempotent on re-delivery, then derives the touched medicines again.
  Not called by the app until the transport exists (Phase 3c).
- `SyncFieldVersions` (every register version, with the version its
  writer had seen) and `SyncConflicts` (the §4.5 list, concurrent
  writes only), with their boot patch.
- Convergence harness on real SQLite databases
  (`SyncConvergenceTests`, `SYNC_CONVERGENCE_SEEDS`).

### Changed

- Register operations carry `BaseVersion`; the operation log records
  register versions of local writes.
- `LedgerFactsLoader` breaks recording-instant ties by id.

### Docs

- Phase 3b split into 3b-1 / 3b-2; retraction versus concurrent count
  and conflict detection decided (`ANALYSIS-B1-MOBILE-SYNC.md` §4.2,
  §4.5, §7.3, §13).

---

## PR #85 — B.1 Phase 3a: operation log, hybrid clock and a write gate on every use case

Link: [vger70/MedReminder#85](https://github.com/vger70/MedReminder/pull/85)
Branch: `claude/b1-phase3a-operation-log`

**Status:** merged (2026-09-27)

### Added

- Local operation log for sync (`SyncOperations`, boot patch): every
  use case records the facts it writes, in the same unit of work, with
  a hybrid logical clock timestamp (`Domain/Sync`, `Application/Sync`).
  Operation catalogue schema version 1, JSON form in `OperationCodec`.
  Nothing is recorded until sync is enabled for the profile
  (`sync.settings.json`, not created before Phase 3d).
- Guard tests: every Application writer takes `IOperationLog`; every
  use case waits on the write gate; the UI must not call
  `IOperationLog`.

### Changed

- `MonitoringGate` renamed `WriteGate` and extended to every use case
  (`docs/analysis/ANALYSIS-B1-MOBILE-SYNC.md` §7.4).
- The medicine edit dialog writes only the fields the user changed
  (`UpdateMedicineCommand.Baseline`); saving without touching the slots
  no longer records a new slot set.

### Docs

- D7 decided (conflict review shows the §4.5 list); Phase 3 split into
  3a–3d; `ANALYSIS.md` and the B.1 analysis updated.

---

## PR #84 — B.1 Phase 2d: fact retraction and epoch fact id

Link: [vger70/MedReminder#84](https://github.com/vger70/MedReminder/pull/84)
Branch: `claude/b1-phase2d-fact-retraction`

**Status:** merged (2026-09-27)

### Added

- *Stock → History…* (Ctrl+H): the stock entries, intakes, stock counts
  and suspensions of the selected medicine, newest first, with Delete
  on the entries that can be retracted. Stock and consumption are
  recalculated (`docs/analysis/ANALYSIS-B1-MOBILE-SYNC.md` §4.2, D8).
  Only entries recorded after the latest stock count and after the
  update can be deleted.

### Changed

- The low-stock warning dedup identifies the stock epoch by the fact
  that opened it, so a deleted refill does not suppress the warning of
  the next one (§4.4).

### Docs

- User guides (en, it, fr, es, de): history window. `ANALYSIS.md`
  §4.1, §4.4, §8.1; B.1 analysis (§4.2, §4.4, §13, §20).

## PR #83 — B.1 Phase 2c-2: derive the stock ledger from facts in the use cases

Link: [vger70/MedReminder#83](https://github.com/vger70/MedReminder/pull/83)
Branch: `claude/b1-phase2c2-ledger-wiring`
**Status:** merged (2026-09-27)

### Changed

- The stock ledger is derived from facts (`LedgerDeriver`) and kept up
  to date by the catch-up, intakes and stock counts
  (`docs/analysis/ANALYSIS-B1-MOBILE-SYNC.md` §4.3, P9). Stock counts
  are stored as facts with their outcome; activation changes, intake
  and schedule recording instants and the epoch baseline are recorded.
- A schedule change dated in the past recalculates automatic
  consumption from that date; the days a medicine was inactive are not
  booked after a reactivation; of two schedule changes with the same
  date the later one wins. Movements recorded before this version are
  never recalculated (re-freeze at first start and on every import).
- A backdated intake on a recalculated day replaces the automatic
  consumption instead of adding a reversal movement.

### Docs

- User guides (en, it, fr, es, de): past *Effective from* dates and
  reactivation. `ANALYSIS.md` §4.1, §4.3, §4.4, §8.1;
  `EXPORT-FORMAT.md` §5.1 (import freeze); B.1 analysis (P9, §13, §14,
  §20).

## PR #82 — B.1 Phase 2c-1: LedgerDeriver in the Domain, with parity against the use cases

Link: [vger70/MedReminder#82](https://github.com/vger70/MedReminder/pull/82)
Branch: `claude/b1-phase2c1-ledger-deriver`
**Status:** merged (2026-09-27)

### Added

- `MedReminder.Domain/Ledger`: `LedgerDeriver` derives a medicine's
  stock ledger and `StockEpoch` from its facts; `EvaluateCount` applies
  the `ReconcileStock` formula to a stock count. Not called by the
  application yet: no user-visible change
  (`docs/analysis/ANALYSIS-B1-MOBILE-SYNC.md` §4.3, §13).

### Build

- Parity tests (`tests/MedReminder.Application.Tests/Ledger`): the
  spike S9 harness, ported to the production deriver, runs random
  scenarios on the real use cases and compares stock and epoch after
  every action. The documented D6 / D15 differences are pinned.

### Docs

- B.1 analysis: 2c split, count outcome stored with the fact until
  Phase 3, re-freeze in 2c-2, rules 1b and 2 extended for mid-day
  patches. `ANALYSIS.md` §4.3.

## PR #81 — B.1 Phase 2b: ledger schema (movement origin, slot sets, stock counts, cutoff)

Link: [vger70/MedReminder#81](https://github.com/vger70/MedReminder/pull/81)
Branch: `claude/b1-phase2b-schema`
**Status:** merged (2026-09-27)

### Changed

- Stock movements record their origin (`Legacy`, `User`, `Derived`).
  The boot patch marks every existing movement `Legacy` and stores the
  ledger cutoff (the day before the patch), so the future ledger
  derivation never changes past numbers
  (`docs/analysis/ANALYSIS-B1-MOBILE-SYNC.md` §3.5, §7.3).
- Administration slot changes append a dated slot set instead of
  replacing the rows; the current slots are those of the latest set.
  The patch groups existing slots into one set per medicine. No
  user-visible change.
- New `StockCounts` table for stock-count facts; written from Phase 2c.
- Export schema version 2: movement origin, slot sets, stock counts,
  cutoff. Version 1 archives import with `Legacy` movements, one slot
  set per medicine and a cutoff at the day before the import.

### Docs

- `EXPORT-FORMAT.md` schema version 2 and version history;
  `ANALYSIS.md` entities, invariants and patch list; B.1 analysis
  (slot-set table, `FrozenAt`, Phase 2 split, constraint for 2c).

## PR #80 — B.1 Phase 2a: route every UI data write through a use case

Link: [vger70/MedReminder#80](https://github.com/vger70/MedReminder/pull/80)
Branch: `claude/b1-phase2a-write-paths`
**Status:** merged (2026-09-27)

### Changed

- The main window's Deactivate action now calls the new
  `DeactivateMedicine` use case instead of writing through
  `IMedicineRepository` and `IUnitOfWork`. Same fields changed
  (`IsActive`, `UpdatedAt`), same messages; no user-visible change.
  Needed so that sync can emit one operation per use case
  (`docs/analysis/ANALYSIS-B1-MOBILE-SYNC.md` P8, §7.2).

### Build

- New `UiWritePathGuardTests` (`MedReminder.Application.Tests`): a
  source scan that fails when a file under `src/MedReminder.UI` calls
  a repository write method, `SaveChangesAsync` or the EF Core
  context. Empty allow-list.

### Docs

- B.1 analysis: P8 met for the profile database; open items for the
  replicated values written outside it. `ANALYSIS.md`: UI layering
  rule.

## PR #79 — B.1 Phase 1: move platform-neutral infrastructure into a net10.0 project

Link: [vger70/MedReminder#79](https://github.com/vger70/MedReminder/pull/79)
Branch: `claude/b1-phase1-portability`
**Status:** merged (2026-09-27)

### Changed

- New `src/MedReminder.Infrastructure.Portable` (`net10.0`): EF Core
  persistence, `DatabaseInitializer`, archive cipher, localization
  loader, moved unchanged from the Windows Infrastructure project so a
  mobile host can reuse them (`docs/analysis/ANALYSIS-B1-MOBILE-SYNC.md`
  Phase 1). No user-visible change.
- `ArchiveReader` (`IArchiveReader`) and `ProfileDatabaseBuilder`
  extracted from `ImportService`, which keeps the Windows-only steps.
- `IAppDataLocation` port; `LocalizationService` reads its overrides
  through it.
- `MedicineOverviewLoader` moved from the UI to
  `MedReminder.Application/Overview`.

### Fixed

- `MedReminder.Infrastructure.Tests` did not compile: ambiguous
  `Should()` on MimeKit's `InternetAddressList`.

### Build

- New `tests/MedReminder.Infrastructure.Portable.Tests` (`net10.0`,
  runs on any OS) with the persistence, cipher and schema tests, plus
  new archive-reader, database-builder and localization tests.

### Docs

- `ANALYSIS.md`, `PACKAGING.md`, `CLAUDE.md` updated for the new
  project; B.1 analysis (D9, P5–P7); corrections applied to
  `EVOLUTION.md` and `ANALYSIS-C3PP-CLOUD-PROVIDERS.md`.

## PR #78 — Revert the S9 prototype merged by mistake

Link: [vger70/MedReminder#78](https://github.com/vger70/MedReminder/pull/78)
Branch: `claude/revert-s9-prototype`
**Status:** merged (2026-09-26)

### Removed

- `prototypes/` (spike S9 code from PR #77). It was not meant for
  `main`: outside `MedReminder.sln`, not built by CI, and linked to the
  test support files of `MedReminder.Application.Tests`. The code
  remains on branch `claude/b1-s9-convergence-prototype`.

## PR #77 — Spike S9: B.1 ledger derivation and sync convergence prototype

Link: [vger70/MedReminder#77](https://github.com/vger70/MedReminder/pull/77)
Branch: `claude/b1-s9-convergence-prototype`
**Status:** merged by mistake (2026-09-26), reverted in PR #78

### Added

- Throw-away prototype under `prototypes/` (outside `MedReminder.sln`)
  for spike S9 of `docs/analysis/ANALYSIS-B1-MOBILE-SYNC.md`: ledger
  derivation, merge rules, convergence simulation. Parity with the real
  use cases over 10 000 random scenarios; convergence over 10 000
  random multi-device histories.

## PR #76 — Add B.1 mobile client and synchronization analysis

Link: [vger70/MedReminder#76](https://github.com/vger70/MedReminder/pull/76)
Branch: `claude/b1-analysis-document-1r5r0v`
**Status:** merged (2026-09-26)

### Docs

- New `docs/analysis/ANALYSIS-B1-MOBILE-SYNC.md`: mobile full client
  with mandatory, end-to-end encrypted synchronization with the desktop
  through the user's cloud storage, no backend. Covers preconditions
  audited against the tree, the facts / derived split of the stock
  ledger (count anchors, derived stock epoch, genesis cutoff), HLC and
  per-class merge rules, per-device encrypted operation segments,
  pairing and key rotation, provider transports (absorbs C.3++
  Phase 2), notifications across devices, feature parity, phases 0–7
  with entry and exit criteria, decisions D1–D15.
- `docs/ANALYSIS.md` §12 and `docs/EVOLUTION.md` §7 link the new
  analysis.
- Product-owner decisions D1, D2, D3, D5, D6, D8, D10, D15 recorded;
  spike S9 (convergence prototype) added as the first Phase 0 step.
  `docs/EVOLUTION.md` §2.0 sequence updated: B.1 with sync first, A2
  phase 2 independent, C.1 reduced to an optional hosted transport.
- Spike S9 results recorded in §18.9 (prototype in PR #77): parity
  with the current use cases and multi-device convergence over 10 000
  seeds each. Design updated from its findings: count anchors
  evaluated by recording order, dated slot history, register version
  history and anchor horizon, checkpoint selection, resolved-value
  hash, wider D6 scope.

## PR #75 — Add read-only therapy timeline view

Link: [vger70/MedReminder#75](https://github.com/vger70/MedReminder/pull/75)
Branch: `claude/prompt-therapy-timeline-c51i9d`

### Added

- Therapy timeline (Therapy menu, Ctrl+T, and toolbar): one row per
  medicine over a movable date window (default 60 days back, 120
  forward) showing active periods, suspensions, dosage changes,
  stepped-taper stages, today and the estimated run-out date. Read-only;
  "Show in list" selects the medicine in the main grid
  (`src/MedReminder.UI/Forms/TherapyTimelineForm.cs`,
  `src/MedReminder.UI/Controls/TherapyTimelineChart.cs`).
- Pure `TherapyTimelineBuilder`, `TherapyTimelineQuery` and localized
  `TherapyTimelineText` in `src/MedReminder.Application/Timeline/`.
- Accessibility: elements differ by pattern and shape, keyboard
  navigation, tooltips, a textual details pane, high-contrast palette,
  sizes derived from the form font.

### Changed

- `MedicineOverviewLoader` computes the forecast through the new
  `MedicineForecast` domain helper, shared with the timeline so both
  views show the same run-out date.

### Docs

- Timeline section in the five `docs/USER_GUIDE.<lang>.md`.
## PR #74 — Add prescription request draft for the doctor

Link: [vger70/MedReminder#74](https://github.com/vger70/MedReminder/pull/74)
Branch: `claude/prompt-prescription-request-6foa34`

### Added

- "Request prescription…" action (Therapy menu and toolbar) opening a
  dialog with an editable, localized request for the selected
  medicine: name, package, national code, doctor name in the greeting,
  profile name as signature. No clinical detail
  (`src/MedReminder.Application/Prescriptions/`,
  `src/MedReminder.UI/Forms/PrescriptionRequestDialog.cs`).
- Delivery by clipboard, `mailto:` (length guard with clipboard
  fallback) or SMTP send after explicit confirmation.
- Optional per-profile doctor e-mail on Settings → Notifications,
  included in export/import as the additive field
  `notificationSettings.doctorAddress`.

### Changed

- `EmailMessage.ExplicitRecipient`: the MailKit adapter sends such a
  message to that address only, the retry decorator does not back off,
  and neither the address nor the message content is logged.

### Docs

- `docs/EXPORT-FORMAT.md` §3.9, `docs/ANALYSIS.md` §9.2, and a new
  section in all five `docs/USER_GUIDE.<lang>.md`.
## PR #73 — Add guided stock count with gap display

Link: [vger70/MedReminder#73](https://github.com/vger70/MedReminder/pull/73)
Branch: `claude/hopeful-curie-fpi6ej`

### Added

- "Stock > Count stock..." dialog: the user enters the counted
  quantity and sees the expected stock, the signed stock discrepancy
  and the run-out date before and after; one positive or negative
  correction is recorded (`StockCountDialog`).
- `ReconcileStock` use case: materializes pending automatic
  consumption, computes the gap and writes the correction in one unit
  of work under `MonitoringGate`. Zero gap writes no correction;
  negative counts are rejected. "Already taken today" (suggested from
  slot times) avoids decrementing today twice: the whole day taken
  materializes today's consumption, a partial day keeps the
  start-of-day stock for the next catch-up.
- A positive count correction advances `StockEpoch` only when it lifts
  the forecast above `ThresholdDays`, reopening the low-stock warning
  cycle without an immediate duplicate warning.
- When consumption has pushed the ledger below zero, the dialog states
  the alignment included in the recorded correction.
- Localized strings in all five dictionaries.

### Changed

- `ConsumptionCatchUp`: per-medicine planning extracted into
  `PlanMissingAsync`, shared with `ReconcileStock`; behavior unchanged.

### Docs

- "Count stock" section in the five user guides.

## PR #71 — Extend A2 barcode analysis to the USB HID-scanner variant

Link: [vger70/MedReminder#71](https://github.com/vger70/MedReminder/pull/71)
Branch: `claude/barcode-webcam-hid-scanner-vlcsjo`

### Added

- "Scan barcode…" button next to the commercial name in the medicine
  form (shown when the reference catalogue is on). It opens a scan
  dialog that accepts a USB barcode scanner in keyboard mode or a code
  typed by hand, looks the code up in the catalogue and fills the form
  as an autocomplete pick does; a code not in the catalogue changes
  nothing. `src/MedReminder.UI/Forms/BarcodeScanDialog.cs`,
  `src/MedReminder.UI/Controls/ScannerInputBox.cs`.
- Barcode parser for Code 32 / AIC (raw, `A` + 9 digits, 9 digits),
  GS1 DataMatrix (GTIN, and batch / expiry / serial only when
  unambiguous) and EAN-13, with check-digit validation. Never logs the
  payload. `src/MedReminder.Application/Catalogue/BarcodeParser.cs`,
  `ItalianPharmacode.cs`.
- `Capture` section in `appsettings.json` for the scanner timings and
  the optional GS1 separator substitute.
- New UI strings in all five dictionaries; "Scan the package barcode"
  section in all five user guides.

### Docs

- `docs/analysis/ANALYSIS-A2-BARCODE-WEBCAM.md` renamed to
  `ANALYSIS-A2-BARCODE-SCAN.md` and extended to the USB HID-scanner
  (keyboard wedge) variant alongside the webcam one: shared parser and
  catalogue lookup, scanner as default input mode, two-PR delivery
  proposal.
- Corrected first-draft facts: Italian AIC barcode is Code 32, not
  EAN-13; FMD DataMatrix applies in Italy from February 2025; the
  dialog has no national-code field and the link use case cannot
  serve Create mode.
- `docs/EVOLUTION.md` §2.0 and §3.2 updated to the new scope;
  references in `docs/ANALYSIS.md` and
  `docs/notes/EVOLUTION-PROPOSALS.md` follow the rename.
- Recorded the decided delivery phases (HID scanner, then webcam,
  then restock-by-scan) and the restock-by-scan flow design, deferred
  until the new-medicine flow is complete and explicitly requested.
- Analysis updated with the phase 1 implementation findings (Code 32
  alphabet and AIC check digit verified, scanner-burst rule for idle
  submission).

## PR #70 — List all five interface languages in the user guides

Link: [vger70/MedReminder#70](https://github.com/vger70/MedReminder/pull/70)
Branch: `claude/website-content-refresh-qzen39`

### Docs

- `docs/USER_GUIDE.{en,it,fr,es}.md`, section "Interface language":
  list English, Italian, French, Spanish and German, matching
  `SupportedLanguages.All`. The guides previously named two or four.

---

## PR #68 — Consolidate evolution drafts into a single ranked proposal note

Link: [vger70/MedReminder#68](https://github.com/vger70/MedReminder/pull/68)
Branch: `claude/consolidate-evolution-docs-z32p4u`

### Docs

- Added `docs/notes/EVOLUTION-PROPOSALS.md`, merging the proposals from
  the two evolution drafts into one list ranked by user value, with
  shipped items separated and feasibility caveats recorded.
- Removed `docs/notes/EVOLUZIONI-1.md` and `docs/notes/EVOLUZIONI-2.md`,
  superseded by the new note.

---

## PR #61 — Rewrite ANALYSIS.md as the as-built architecture reference

Link: [vger70/MedReminder#61](https://github.com/vger70/MedReminder/pull/61)
Branch: `claude/review-analysis-md-43wfc3`
**Status:** merged (2026-09-25)

### Docs

- **`docs/ANALYSIS.md` now describes the current architecture.** It was
  still the pre-implementation plan while being referenced as the
  architecture document. The new version covers projects and layering,
  dependencies, domain model, runtime data and profiles, configuration,
  hosted services, boot flow, schema patching, backup and export,
  notifications, security, the status of the original decisions, and
  known limitations. It reflects PRs #62 to #67.
- **Original plan preserved** unchanged at
  `docs/analysis/ANALYSIS-MVP.md` with a historical banner, so code
  comments citing `ANALYSIS §x.y` keep their numbering.

---

## PR #67 — Document that real profile separation needs separate Windows accounts

Link: [vger70/MedReminder#67](https://github.com/vger70/MedReminder/pull/67)
Branch: `claude/document-pin-role-limits`
**Status:** merged (2026-09-25)

### Docs

- User guides (5 languages): profiles inside one Windows account are
  not a privacy boundary; separate Windows accounts are needed for real
  separation, and the administrator's automatic backups include every
  profile. PIN/role enforcement on disk stays an accepted limitation.

---

## PR #66 — Let admin profiles export every profile in one step

Link: [vger70/MedReminder#66](https://github.com/vger70/MedReminder/pull/66)
Branch: `claude/export-all-profiles-admin`
**Status:** merged (2026-09-25)

### Added

- **Export every profile (admin).** With more than one profile, the
  Export dialog of an admin profile writes one encrypted single-profile
  `.mrz` per profile into a chosen folder, all under the same
  passphrase (`src/MedReminder.UI/Forms/ExportDialog.cs`). Archive
  format unchanged; the profile registry is never exported. New
  localization keys in all five languages.

### Docs

- User guides (5 languages) and `ANALYSIS-C3-EXPORT-IMPORT.md` §3.5.

---

## PR #65 — Back up every profile to the cloud folder and guard cross-profile restores

Link: [vger70/MedReminder#65](https://github.com/vger70/MedReminder/pull/65)
Branch: `claude/export-every-profile`
**Status:** merged (2026-09-25)

### Changed

- **Cloud-folder backup covers every profile.** The automatic cloud
  target writes one encrypted `.mrz` per registered profile, like the
  local target, all under the same backup passphrase
  (`src/MedReminder.UI/Hosting/AutomaticBackupHostedService.cs`).
- **Export of another profile.** `ExportOptions.ProfileId`; allowed to
  admin profiles and to the automatic backup
  (`src/MedReminder.Infrastructure/Export/ExportService.cs`).

### Added

- Restore preselects the active profile's newest snapshot; restore and
  import ask for confirmation before applying another profile's
  archive (`src/MedReminder.UI/Forms/OtherProfileArchivePrompt.cs`).
  New localization keys in all five languages.

### Docs

- User guides (5 languages) and `ANALYSIS-C3PLUS-CLOUD-BACKUP.md`.

---

## PR #64 — Stop manual intakes from hiding or doubling automatic consumption

Link: [vger70/MedReminder#64](https://github.com/vger70/MedReminder/pull/64)
Branch: `claude/consumption-day-coverage`
**Status:** merged (2026-09-25)

### Fixed

- **Backdated intakes counted twice.** Recording an intake for a past
  day that already had the automatic consumption now reverses that
  consumption (`PositiveCorrection`) before booking the intake
  (`src/MedReminder.Application/UseCases/RegisterIntake.cs`).
- **Manual intakes could hide unmaterialized days.** The catch-up now
  starts after the last automatic consumption day instead of the last
  consumption of any kind, and skips days that already have a
  consumption or an intake
  (`src/MedReminder.Application/Monitoring/ConsumptionCatchUp.cs`).

### Added

- Application tests for both cases and a SQLite integration test with
  a non-UTC local zone.

---

## PR #62 — Serialize consumption catch-up and monitor passes

Link: [vger70/MedReminder#62](https://github.com/vger70/MedReminder/pull/62)
Branch: `claude/serialize-consumption-catch-up`
**Status:** merged (2026-09-25)

### Fixed

- **Automatic consumption could be written twice.** The monitor tick
  and "Check now" ran the consumption catch-up concurrently, each in
  its own scope; overlapping runs wrote the same days twice and
  understated stock. Overlapping monitor passes could also send the
  same low-stock warning twice. Both passes now run under a
  process-wide gate
  (`src/MedReminder.Application/Monitoring/MonitoringGate.cs`).

### Changed

- Corrected the `ConsumptionCatchUp` comment that cited a database
  unique constraint that does not exist.

### Added

- Regression test for concurrent catch-ups
  (`tests/MedReminder.Application.Tests/Monitoring/ConsumptionCatchUpTests.cs`).

---

## PR #63 — Correct stale donation-config comments and CLAUDE.md frameworks

Link: [vger70/MedReminder#63](https://github.com/vger70/MedReminder/pull/63)
Branch: `claude/fix-stale-donation-and-tfm-docs`
**Status:** merged (2026-09-25)

### Docs

- `JsonDonationOptionsProvider` and `MedReminder.UI.csproj` comments now
  state that the donation configuration comes from the embedded
  `assets/donations.settings.json`, not from `%LOCALAPPDATA%`.
- `CLAUDE.md`: Infrastructure targets `net10.0-windows`.

---

## PR #59 — Fix clipped first-run/PIN dialogs, Backup tab scroll, system language on first run

Link: [vger70/MedReminder#59](https://github.com/vger70/MedReminder/pull/59)
Branch: `claude/ui-issues-first-run-backup-ssh7hd`
**Status:** merged (2026-09-25)

### Fixed

- **Clipped buttons in the first-run wizard and the PIN prompt.** Both
  dialogs used absolute coordinates and a fixed size. They now use
  auto-sizing layout panels with DPI-scaled widths and minimum button
  sizes (`src/MedReminder.UI/Forms/FirstRunWizardForm.cs`,
  `src/MedReminder.UI/Forms/PinPromptForm.cs`).
- **Backup tab cut off.** The tab container now scrolls vertically when
  its content is taller than the tab
  (`src/MedReminder.UI/Forms/SettingsDialog.cs`).

### Changed

- **First-run language.** With no profile and no `user.settings.json`,
  the UI language follows the Windows UI culture (English fallback) and
  is persisted (`src/MedReminder.UI/Program.cs`).

---

## PR #58 — Skip the cloud export early when the cloud folder is missing

Link: [vger70/MedReminder#58](https://github.com/vger70/MedReminder/pull/58)
Branch: `claude/skip-cloud-export-when-folder-missing`
**Status:** merged (2026-09-25)

### Fixed

- **Wasted exports with a missing cloud folder.** Since PR #56, a
  missing folder was only detected at upload time, after the Argon2id
  export. The day stays open so a later tick retries, so every 15-minute
  tick after the preferred time ran a full export for nothing. The host
  checks the folder again before exporting, as C.3+ did
  (`src/MedReminder.UI/Hosting/AutomaticBackupHostedService.cs`).
  No user-visible change.

### Docs

- `ANALYSIS-C3PLUS-CLOUD-BACKUP.md` §4.7 describes the pre-export check.

---

## PR #57 — Fix cloud-only automatic backup re-exporting on every tick

Link: [vger70/MedReminder#57](https://github.com/vger70/MedReminder/pull/57)
Branch: `claude/fix-cloud-only-backup-state` (stacked on PR #56)
**Status:** merged (2026-09-25)

### Fixed

- **Cloud-only automatic backup ran every 15 minutes.** When only the
  cloud-folder target was enabled, a written `.mrz` did not mark the day
  as backed up. Every tick after the preferred time exported another
  snapshot into the synced folder, until the end of the day. A snapshot
  written by either target now completes the day. Skips (missing
  passphrase or folder) and failed uploads still leave the day open, so
  a later tick retries
  (`src/MedReminder.UI/Hosting/AutomaticBackupHostedService.cs`).

### Added

- Regression tests for the cloud-only tick
  (`tests/MedReminder.UI.Tests/Hosting/AutomaticBackupHostedServiceTests.cs`).

---

## PR #56 — C.3++ Phase 1: IArchiveStorage + LocalFolderArchiveStorage

Link: [vger70/MedReminder#56](https://github.com/vger70/MedReminder/pull/56)
Branch: `feature/archive-storage-abstraction`
**Status:** merged (2026-09-25)

Internal refactor with no user-visible change. It puts delivery of the
C.3+ cloud-folder snapshots behind a storage port, so native cloud
backends (Phase 2, `docs/analysis/ANALYSIS-C3PP-CLOUD-PROVIDERS.md` §14)
can be added without touching the export service, the `.mrz` format or
the backup host.

### Added

- **`IArchiveStorage` port and `ArchiveInfo` record** in
  `src/MedReminder.Application/Abstractions/`.
- **`LocalFolderArchiveStorage`**
  (`src/MedReminder.Infrastructure/Backup/`), the only implementation in
  this phase. It keeps the C.3+ temp-then-move discipline and reads the
  cloud folder from settings on every call.
- **Contract test base** `ArchiveStorageContractTests` and
  `LocalFolderArchiveStorageContractTests`
  (`tests/MedReminder.Infrastructure.Tests/Backup/`).

### Changed

- **`AutomaticBackupHostedService`** uploads the cloud snapshot through
  `IArchiveStorage` instead of calling `File.Move` itself.
- **`IBackupService.PruneCloudFolderAsync`** takes the storage and
  prunes through it. It keeps the same name pattern and the same
  last-write-time age rule.

### Docs

- `ANALYSIS-C3PLUS-CLOUD-BACKUP.md` §4.7 records that delivery is now
  behind `IArchiveStorage`. `ANALYSIS-C3PP-CLOUD-PROVIDERS.md` §7.3,
  §7.5 and §10.1 record the implementation-time decisions.

---

## PR #55 — C.3+ backup to a user-controlled cloud folder + explicit restore

Link: [vger70/MedReminder#55](https://github.com/vger70/MedReminder/pull/55)
Branch: `feature/cloud-folder-backup`
**Status:** merged (2026-09-25)

Implements `docs/analysis/ANALYSIS-C3PLUS-CLOUD-BACKUP.md`. The automatic
daily backup gains a second, independent target that writes encrypted
`.mrz` snapshots (C.3's archive format) into a user-chosen local folder,
which the user's OS-level sync agent (OneDrive, iCloud Drive, Dropbox,
Google Drive Desktop, …) is free to upload. A new **Restore from cloud
folder** dialog reads the most recent snapshot on a second device and
applies it through the existing C.3 `IImportService`.

The user model is single-writer / multiple-reader-on-demand: this is
explicitly not real-time sync. The UI copy states it and the user guide
restates it.

### Added

- **Cloud-folder target on the automatic backup.**
  `BackupSettings.CloudFolderEnabled` / `CloudFolderDirectory` /
  `CloudFolderRetention` (`src/MedReminder.Application/Abstractions/BackupSettings.cs`).
  Independent from the existing raw-DB `Directory` — a user may run
  either target, both, or neither.
- **Backup passphrase, DPAPI-cached.** New
  `ICloudBackupPassphraseStore` port with a DPAPI-`CurrentUser`
  Infrastructure adapter backed by
  `%LOCALAPPDATA%\MedReminder\cloud-backup.protected`. Distinct from the
  user-typed C.3 export passphrase so a compromise of one does not
  compromise the other.
- **`ICloudRestoreService`.** Lists the `.mrz` archives in a folder,
  reads each manifest without decrypting, and delegates the actual
  restore to `IImportService`.
- **Manifest additions.** Optional `source` (`"automatic"` for a
  scheduled snapshot, absent for a user export) and hashed
  `device.hostName` block (SHA-256 hex of the plain host name) —
  additive, ignored by older readers.

### Changed

- **`AutomaticBackupHostedService`.** Same daily schedule, now runs
  both targets with independent try/catch so a failure on one target
  never skips the other. The cloud target uses temp-then-move to
  publish an atomic `.mrz` into the user's folder; sync agents watching
  the folder only see the finished file.
- **`BackupService.PruneCloudFolderAsync`.** Separate regex for
  `medreminder-<profileId>-YYYYMMDD-HHmmss.mrz`, distinct from the
  `.db` regex, so the two retention windows never cross-prune when the
  user points both targets at the same folder.
- **Settings dialog.** Backup tab gains a *Backup to a cloud-synced
  folder (encrypted)* subsection (admin-only writer): enable checkbox,
  folder picker, retention counter, backup-passphrase set/change
  button, and two disclaimers ("this is not real-time sync", "losing
  the passphrase means losing the ability to restore"). A new
  **Restore from cloud folder…** button opens the C.3+ restore dialog
  and is available to every profile.

### Added (UI)

- **`ChangeCloudPassphraseDialog`.** Small modal to set or rotate the
  DPAPI-cached backup passphrase; enforces the same minimum length as
  C.3 exports.
- **`RestoreFromCloudDialog`.** Lists the `.mrz` snapshots in the
  chosen folder with date, profile, source and hashed device columns;
  accepts the DPAPI-cached passphrase or a user-typed one; requires
  the explicit *"I understand this will overwrite"* confirmation;
  prompts for restart on success.

### Localization

- All five dictionaries (`en`, `it`, `fr`, `es`, `de`) gain the
  `Ui.SettingsDialog.CloudBackup.*`, `Ui.RestoreCloudDialog.*`,
  `Ui.CloudBackup.*` and `Ui.SettingsDialog.File.RestoreFromCloud`
  key families. `DictionaryParityTests` passes.

### Tests

- `MedReminder.Application.Tests`: `BackupSettingsBindingTests` covers
  the additive JSON deserialization contract (old file → defaults,
  new file → round-trip).
- `MedReminder.Infrastructure.Tests`:
  `DpapiCloudBackupPassphraseStoreTests` (DPAPI round-trip, tampering
  rejection, empty-passphrase refusal) and
  `BackupServicePruneCloudTests` (regex isolation from `.db` files,
  per-profile retention, retention-of-zero no-op).
- `ExportImportRoundTripTests`: two new tests assert that
  `AutomaticSource=true` writes `manifest.source = "automatic"` plus
  a valid 64-hex-char device hash, and that a user export leaves both
  fields null.

### Docs

- `docs/USER_GUIDE.en.md` gains a "Cloud folder backup" section
  covering setup on device #1, setup + restore on device #2, and the
  passphrase-loss / provider-recycle-bin caveats.
- `docs/EXPORT-FORMAT.md` documents the optional `source` and
  `device.{hostNameSha256, profileId}` manifest fields.
- The four non-English user guides may follow in a separate PR,
  mirroring A1 / A5.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>

## PR #54 — Document implementation decisions confirmed on 2026-09-25

Link: [vger70/MedReminder#54](https://github.com/vger70/MedReminder/pull/54)
Branch: `claude/festive-meitner-ppf0wb`
**Status:** merged (2026-09-25)

### Docs

- **Website decisions recorded before the first implementation commit.**
  New §14 in `docs/analysis/ANALYSIS-WEBSITE.md` closes the four items
  that were still open: the site lives in the separate repository
  `vger70/medreminder-website` (so this file does not track its PRs),
  no roadmap teaser on the About page in v1, no dark mode in v1, and
  screenshots are retaken only when a UI change materially alters what
  a shot depicts.

---

## PR #53 — Fix and expand user guides (6 corrections + A3/C3/stepped tapering)

Link: [vger70/MedReminder#53](https://github.com/vger70/MedReminder/pull/53)
**Status:** merged (2026-09-24)

Branch: `claude/negli-user-guide-fixes-461ae2`

Corrects six inconsistencies in all five shipped user guides and adds the
three recently shipped features (stepped tapering, A3 caregiver notifications,
C3 export/import) that were missing from the non-English guides.

### Fixed

- **`docs/USER_GUIDE.it.md`** — duplicate section title: the standalone
  "Avvio automatico con Windows" (configure Settings → Automatic startup)
  renamed to "Configurare l'avvio automatico" so it no longer collides with
  the same-named sub-section inside "Profili multipli" (which describes
  which profile opens at login).
- **`docs/USER_GUIDE.it.md`** — stale MVP note in "Modificare o disattivare":
  replaced "funzione da linea di comando o edit DB per l'MVP" with the correct
  reference to `Toolbar → Cambia schedulazione`.
- **`docs/USER_GUIDE.it.md`** — contradiction with A5: the "Cosa NON fa"
  bullet that stated the app does not remind you to take a specific dose is
  replaced with an accurate statement about what MedReminder does not track
  (adherence, missed doses, clinical advice).

### Added

- **`docs/USER_GUIDE.it.md`** — "Scalare" entry in "Regimi complessi"
  expanded to document the Linear / Stepped sub-selector and stage editor
  added by PR #40, which were missing from the Italian guide.
- **`docs/USER_GUIDE.it.md`** — "Notifiche al caregiver" section (A3,
  PR #47): optional second e-mail recipient, consistent with the English guide.
- **`docs/USER_GUIDE.it.md`** — "Esportazione e importazione" section (C3,
  PR #48): encrypted `.mrz` archive, passphrase requirements, and import
  overwrite semantics, consistent with the English guide.
- **`docs/USER_GUIDE.en.md`** — same corrections 2 and 3 (MVP text and
  "What does NOT do") applied; the other four were already present.
- **`docs/USER_GUIDE.fr.md`**, **`docs/USER_GUIDE.es.md`**,
  **`docs/USER_GUIDE.de.md`** — all six corrections applied in the
  respective languages (fr: Linéaire/Par paliers, Notifications au soignant,
  Export et importation; es: Lineal/Por etapas, Notificaciones al cuidador,
  Exportación e importación; de: Linear/Stufenweise,
  Benachrichtigungen für Pflegepersonen, Export und Import).

---

## PR #52 — Allow non-admin profiles to use manual export and import

Link: [vger70/MedReminder#52](https://github.com/vger70/MedReminder/pull/52)
**Status:** merged (2026-09-24)

Branch: `claude/non-admin-export-import-181083`

Non-admin profiles were inadvertently locked out of the four manual
export/import commands introduced in PR #48, because the entire Backup
tab was admin-gated. This change exposes the Backup tab to all profiles
while keeping the automatic-backup settings (directory, schedule,
retention, Save, Run-now) visible only to admins.

### Changed

- **`SettingsDialog`**: `BuildBackupTab()` is now called unconditionally;
  the automatic-backup `TableLayoutPanel` and the Save / Run-now buttons
  are conditionally hidden when `_currentProfile.IsAdmin` is false.

---

## PR #48 — C.3: Manual encrypted export / import

Link: [vger70/MedReminder#48](https://github.com/vger70/MedReminder/pull/48)
**Status:** merged (2026-09-24)

Branch: `feature/export-import`

Adds two user-triggered commands under Settings → Backup: **Export all
data** and **Import from export**. The export writes a single encrypted
`.mrz` archive (a ZIP with a cleartext `manifest.json` and an
AES-GCM-encrypted `payload.enc`) covering the current profile's data
plus optional shared settings. The archive is portable across Windows
accounts and machines, so it doubles as the recommended device-migration
path. Import applies an archive to the current profile in Overwrite mode
after a safety backup. Encryption is Argon2id (KDF) + AES-GCM (cipher)
with a user-chosen passphrase; DPAPI is deliberately not used for the
archive so it is not bound to the Windows account. Implements
`docs/analysis/ANALYSIS-C3-EXPORT-IMPORT.md`.

### Added

- **`MedReminder.Application.Export` namespace**: `IExportService`,
  `IImportService`, `IArchiveCipher` ports, the `ExportManifest` /
  `ExportPayload` shapes, `ExportOptions` / `ImportOptions`, and the
  typed `ExportValidationException` / `ImportFailedException` failure
  surfaces.
- **`ArchiveCipher`** (Infrastructure): Argon2id
  (`Konscious.Security.Cryptography.Argon2`, first cut t=3, m=64 MiB,
  p=1) + in-box `AesGcm` (256-bit key, 96-bit nonce, 128-bit tag).
- **`ExportService` / `ImportService`** (Infrastructure): DB snapshot
  via `BackupService`, entity round-trip through a temporary read-only
  EF Core context, encrypted ZIP write, manifest / hash / version
  validation, transactional overwrite with a pre-import safety copy.
- **Export / Import dialogs** in `SettingsDialog` (Backup tab): passphrase
  entry with confirmation, opt-in shared-settings checkboxes, manifest
  info panel, mandatory overwrite confirmation, progress and cancel.
- **Localization keys** for the new UI in all five dictionaries
  (`assets/localization/`). English is final; `it`/`fr`/`es`/`de` ship
  as `TODO(<lang>)` placeholders pending maintainer sign-off.
- **`docs/EXPORT-FORMAT.md`**: the public archive contract (ZIP layout,
  manifest and payload schemas, KDF / cipher parameters, and an
  off-the-shelf decryption recipe).
- **"Export and import" section** in `docs/USER_GUIDE.en.md`.

### Tests

- **`ArchiveCipher`**: deterministic KDF, salt / passphrase key
  separation, AES-GCM round-trip, wrong-key and tampered-input rejection.
- **`ExportService`**: ZIP layout, manifest fields, payload decrypts and
  matches the hash, short-passphrase refusal, SMTP opt-in / opt-out with
  password re-encryption, scratch-snapshot cleanup.
- **`ImportService`**: manifest read, newer-version and non-MedReminder
  refusal, missing-payload / non-zip / missing-file corruption surfaces.
- **End-to-end round-trip**: export → wipe → import restores every entity
  (row counts and field-for-field on a rich and a bare medicine); wrong
  passphrase, tampered payload, truncated archive, newer format / schema
  version, older-schema defaulting, and SMTP-password round-trip each
  behave as specified, leaving the target untouched on failure.

### Security

- The archive is always encrypted; there is no plaintext export path.
  An empty or too-short passphrase refuses the export before any file is
  written. The passphrase, the derived key and the payload plaintext are
  never logged.
- The SMTP password is opt-in only, DPAPI-decrypted and re-encrypted with
  the archive key on export, and DPAPI-re-encrypted on the target machine
  on import — never carried across accounts as a raw DPAPI blob.

### Build

- New dependency `Konscious.Security.Cryptography.Argon2` (MIT, managed).

## PR #47 — A3: Caregiver notifications

Link: [vger70/MedReminder#47](https://github.com/vger70/MedReminder/pull/47)
**Status:** merged (2026-09-24)

Branch: `feature/caregiver-notifications`

Adds an optional per-profile secondary email recipient. When set, every
email delivered to the primary recipient is also delivered to the
caregiver in the same message. No new transport, no schema change, no
change to the email body — the recipient list widens by one address.
Implements `docs/analysis/ANALYSIS-A3-CAREGIVER-NOTIFICATIONS.md`.

### Added

- **`NotificationSettings.CaregiverAddress`** (default empty). Pre-A3
  `notifications.settings.json` files load unchanged (empty = no
  caregiver).
- **MailKit fan-out** in
  `MailKitEmailNotificationService.BuildMimeMessage`: the caregiver is
  appended as a second `To` recipient (primary first) when configured.
- **`SettingsDialog` Notifications tab**: a "Caregiver e-mail
  (optional)" field with helper copy and save-time validation.
- **Four localization keys** in all five dictionaries
  (`assets/localization/`). English is final; `it`/`fr`/`es`/`de` ship
  as `TODO(<lang>)` placeholders pending maintainer sign-off.

### Changed

- Caregiver address validation rejects addresses without a domain
  (`AllowAddressesWithoutDomain = false`), applied consistently in the
  adapter and in the settings dialog.

### Security

- A malformed caregiver address falls back to primary-only delivery and
  logs a warning without writing the address to the log (`CLAUDE.md`
  §9). A self-copy (caregiver equal to primary) is deduplicated to a
  single recipient and rejected at save time.

### Docs

- **`docs/USER_GUIDE.en.md`** — new "Caregiver notifications" section
  (how to enable, same-email semantics, mutual visibility, empty =
  disabled). The four localized guides may follow.

---

## PR #46 — Add implementation prompt and EVOLUTION entry for public website

Link: [vger70/MedReminder#46](https://github.com/vger70/MedReminder/pull/46)
**Status:** merged (2026-09-23)

Branch: `claude/website-implementation-prompt-821c66`

Adds the authoring artifacts needed to gate the MedReminder public
presentation website implementation. No source code, no schema, no
packaging change — prompt and evolution-document update only.

### Docs

- **`docs/prompt/PROMPT-WEBSITE-IMPLEMENTATION.md`** (new). Self-contained
  implementation briefing for the website Claude Code session. Structure
  mirrors `PROMPT-C3PLUS-IMPLEMENTATION.md`: hard boundaries (no backend,
  no framework, no third-party analytics, no dark mode in v1), seven open
  decisions to confirm before coding, three resolved decisions (Cloudflare
  Pages, Cloudflare Web Analytics, GitHub Actions + Wrangler), technical
  stack table (Hugo, system-font CSS, < 10 KB JS), full repository layout,
  ten-step implementation order, content governance rules, risk mitigations,
  and fourteen acceptance criteria.
- **`docs/EVOLUTION.md`** — new §9.5 (Public presentation website). Records
  motivation, design sketch, effort estimate, open decisions, and pointers to
  `ANALYSIS-WEBSITE.md` and the new prompt. Change-log entry appended.

---

## PR #45 — Add analysis and implementation prompts for A3, C.3, C.3+

Link: [vger70/MedReminder#45](https://github.com/vger70/MedReminder/pull/45)
**Status:** merged (2026-09-22)

Branch: `claude/gracious-ptolemy-bf39iz`

Adds six documentation artifacts under `docs/` covering the next
three items in `EVOLUTION.md` §2.0 (after the shipped A6 and A5).
No source code, no schema, no packaging change — analysis and
implementation-prompt files only, ready to gate the three
follow-up implementation PRs.

### Docs

- **A3 — Caregiver notifications.**
  `docs/ANALYSIS-A3-CAREGIVER-NOTIFICATIONS.md` and
  `docs/PROMPT-A3-IMPLEMENTATION.md`. Per-profile
  `CaregiverAddress` added to `notifications.settings.json`; the
  MailKit adapter fans out to a second `To` recipient. No schema
  change; no per-event opt-in in the first cut; toasts
  unaffected (local channel). One open decision: whether A5
  dose-time emails should also fan out to the caregiver.
- **C.3 — Manual export / import.**
  `docs/ANALYSIS-C3-EXPORT-IMPORT.md` and
  `docs/PROMPT-C3-IMPLEMENTATION.md`. Encrypted `.mrz` archive =
  ZIP (cleartext `manifest.json` + AES-GCM `payload.enc` +
  nonce); Argon2id KDF (matches C.1's later choice). DPAPI is
  deliberately not used for the archive — DPAPI ties data to the
  Windows account and defeats migration. SMTP password inclusion
  is opt-in and re-encrypted with the archive key across
  accounts. Overwrite-only import in the first cut; merge is
  deferred. Public format contract to ship as
  `docs/EXPORT-FORMAT.md`.
- **C.3+ — Backup to cloud folder + explicit restore.**
  `docs/ANALYSIS-C3PLUS-CLOUD-BACKUP.md` and
  `docs/PROMPT-C3PLUS-IMPLEMENTATION.md`. Hard precondition: C.3
  shipped. Reuses C.3's `.mrz` format; extends
  `AutomaticBackupHostedService` to also write snapshots into a
  user-picked local folder synchronized by the user's own cloud
  agent. No cloud API usage; no live-DB file-sync
  (`EVOLUTION.md` §7.1 rejection stands). Model C selected for
  the unattended-passphrase problem: a separate backup
  passphrase, DPAPI-cached in `cloud-backup.protected`, distinct
  from the C.3 export passphrase.

Each analysis follows the pattern established by
`ANALYSIS-A5-DOSE-TIME-REMINDER.md` and
`ANALYSIS-A6-DONATION-SUPPORT.md` (Scope · Preconditions · Data
model · Runtime · UI · Localization · Tests · Retro-compatibility ·
Risks · Decisions still to confirm · Implementation plan · Change
log). Each implementation prompt mirrors
`PROMPT-A5-IMPLEMENTATION.md`.

## PR #44 — Reinstate Edit-medicine schedule seed; default Effettiva-dal to therapy start

Link: [vger70/MedReminder#44](https://github.com/vger70/MedReminder/pull/44)
**Status:** merged (2026-09-21)

Branch: `claude/relaxed-shannon-4unkbv`

### Fixed

- The *Modifica medicina* dialog (F2 / double-click / Modifica menu)
  now re-opens on the therapy's saved schedule again. Commit `0dbd0a4`
  (post-merge of PR #42) had removed the
  `_schedulePanel.ApplySchedule(_seedSchedule)` call from
  `MedicineEditDialog.OnLoad`, re-opening exactly the regression PR
  #42 was supposed to close: on any advanced regime (stepped taper,
  weekly, cyclic, linear taper, PRN) the `SchedulePanel` reset to
  Simple defaults, `Advanced` unchecked, kind combo back to
  `FixedDaily`, every stage / dose / duration input lost. The
  regression test `SchedulePanelTests.Edit_medicine_dialog_reopens_
  on_the_saved_stepped_schedule` — which stayed in the tree — has
  been failing since that commit. Reinstated the seeding call with
  `SyncSimpleControlsEnabled` after it, same deferred-to-OnLoad
  discipline `ChangeScheduleDialog` already uses since bda16f5.
- The *Cambia dose/frequenza* dialog now defaults its "Effettiva dal"
  picker to the therapy's start date rather than to today. Common
  case: the user creates a medicine and immediately opens the dialog
  to attach an advanced schedule to it — the intended semantics is
  "the new schedule applies from the beginning of the therapy", not
  "from now onwards". Users can still backdate or forward-date freely;
  `MinDate` still enforces the lower bound.

No change to `ScheduleCodec`, to any domain / application code, to
persistence, or to release packaging.

## PR #37 — A5: dose-time reminder ("remind me to take it")

Link: [vger70/MedReminder#37](https://github.com/vger70/MedReminder/pull/37)
**Status:** merged (2026-09-21)

Branch: `feature/dose-time-reminder`

Implements evolution A5 (`docs/ANALYSIS-A5-DOSE-TIME-REMINDER.md`):
an opt-in, per-medicine reminder that fires at each scheduled dose
slot's wall-clock time. Strictly a convenience prompt — it does not
acknowledge, log, or infer a missed dose, does not touch stock, and
gives no clinical advice, so the app stays on the non-device side of
the EU MDR line.

### Added

- **Per-medicine opt-in** *Remind me at dose time* on the New /
  Edit medicine form (`MedicineEditDialog`). Enabled only when the
  medicine has at least one timed slot and non-zero stock; the rule
  lives in `Medicine.CanRemindOnDose` so UI and domain agree.
- **DoseReminderService** (Application) evaluated once a minute by
  `DoseReminderHostedService` (UI). At each due slot it dispatches a
  toast and, when the email channel is selected, an email, using
  `NotificationTexts.BuildDoseReminder` (system-language, English
  fallback).
- **At-most-once-per-day dedup** via a dedicated `DoseReminderEvents`
  table keyed on `(MedicineId, SlotKey, LocalDate)` with a unique
  index; survives restarts. 30-day retention prune on each tick.
- **Grace window** (default 30 min, `DoseReminder:GraceWindowMinutes`
  in `appsettings.json`): a slot older than the window is treated as
  missed and silently dropped, with no dedup row so a later in-window
  tick can still fire.
- Six localization keys added and translated in all five dictionaries
  (`en`, `it`, `fr`, `es`, `de`).

### Changed

- `Medicines.RemindOnDose` column added additively and idempotently
  by `DatabaseInitializer` (INTEGER NOT NULL DEFAULT 0); no
  `EnsureCreated`. Pre-A5 databases upgrade with the flag off.

### Docs

- New "Dose-time reminder" section in `docs/USER_GUIDE.en.md` and
  in all four localized guides (`it`, `fr`, `es`, `de`) — opt-in,
  toast/email, grace window, DST behavior.

---

## PR #42 — Seed the Edit-medicine schedule panel in OnLoad

Link: [vger70/MedReminder#42](https://github.com/vger70/MedReminder/pull/42)
**Status:** merged (2026-09-21)

Branch: `claude/relaxed-shannon-4unkbv`

### Fixed

- The *Modifica medicina* (Edit medicine) dialog now opens
  pre-populated with the therapy's current schedule. PR #40 had
  wired the `SchedulePanel` into Edit mode too and the constructor
  captured `_seedSchedule = seed?.InitialSchedule`, but the same PR
  left the `_schedulePanel.ApplySchedule(_seedSchedule)` call inside
  a commented-out `OnLoad` draft. Reopening the dialog on any
  advanced regime (stepped taper, weekly, cyclic, linear taper, PRN)
  therefore showed the panel in Simple defaults: `Advanced` stayed
  unchecked, the kind combo fell back to `FixedDaily`, and every
  stage / dose / duration input was lost. `MedicineEditDialog.OnLoad`
  now calls `ApplySchedule` after `base.OnLoad` — same OnLoad-not-
  constructor discipline `ChangeScheduleDialog` already uses since
  bda16f5.
- The *Cambia dose/frequenza* dialog now defaults its "Effettiva dal"
  picker to the therapy's start date rather than to today. When the
  user opens the dialog shortly after creating a medicine to attach
  an advanced schedule to it, the intent is almost always to make
  the new schedule effective from the beginning of the therapy, not
  from the current day. Users who want to backdate or forward-date a
  change to a different day can still edit the picker; `MinDate`
  still holds the value at or above the therapy's start date.

### Tests

- New `SchedulePanelTests.Edit_medicine_dialog_reopens_on_the_saved_stepped_schedule`
  round-trips a three-stage `SteppedTaperingSchedule` seed through
  `MedicineEditDialog` in Edit mode, driving it through `OnLoad` the
  way `ShowDialog` would, and asserts the panel rebuilds the exact
  seed. Guards against the same regression coming back.

No change to `ChangeScheduleDialog` (already correct via bda16f5), to
domain / application code, to `ScheduleCodec`, to the database schema,
or to release packaging.

## PR #40 — Implement multi-stage (stepped) tapering regimens

Link: [vger70/MedReminder#40](https://github.com/vger70/MedReminder/pull/40)
**Status:** merged (2026-09-21)

Branch: `feature/stepped-tapering`

Implements the multi-stage tapering regime designed in
`docs/ANALYSIS-A1-STEPPED-TAPER.md` (PR #39). Tapering therapies can
now step the dose down (or up) through an explicit list of stages,
each with its own dose and its own duration — for example 4/day for 7
days, then 2/day for 7 days, then 1/day for 14 days — which the linear
tapering shipped with A1 could not express.

### Added

- New domain value objects `TaperStage` and `SteppedTaperingSchedule`
  (`ScheduleKind.SteppedTapering = 5`) in `MedReminder.Domain`, with a
  `MaintainLastDose` flag: by default the course ends after the last
  stage, or the last dose is held indefinitely as a maintenance
  regime when the flag is set. Serialized through the existing
  `ScheduleCodec` into A1's `SchedulePayload` column — **no database
  schema change**.
- In the medicine and change-schedule dialogs, the Tapering panel now
  offers a **Linear / Stepped** choice. Stepped mode has a dynamic
  add/remove stage editor, a "keep the last dose as maintenance"
  checkbox, and a live preview of the whole breakdown (per-stage
  totals, day ranges and grand total) before saving.
- 14 localization keys added to every dictionary (`en`, `it`, `fr`,
  `es`, `de`).

### Changed

- `docs/USER_GUIDE.en.md` — the *Complex regimens* section documents
  the Linear / Stepped split and the maintenance option.

### Fixed

- The *Change dose/frequency* dialog now opens pre-populated with the
  therapy's current schedule. Previously it always reset to Simple
  mode, so an existing advanced regime (stepped, but also weekly,
  cyclic, tapering or PRN) looked as if it had never been saved.
  `MainForm` now loads the latest `MedicationScheduleHistory` entry,
  rebuilds the `Schedule` via `ScheduleCodec` and seeds the dialog's
  `SchedulePanel` through the existing `ApplySchedule`.

No change to the projection engine (the `Schedule.RateOn` contract and
the day-by-day materializer already handle a varying rate), to the
application command signatures, or to existing linear tapers.
## PR #38 — Implement A6 donation / Support Development feature

Link: [vger70/MedReminder#38](https://github.com/vger70/MedReminder/pull/38)
**Status:** merged (2026-09-21)

Branch: `feature/donation-support`

Implements feature A6: an unobtrusive "Support Development" surface.
A dialog lets the user pick a fixed donation tier (€2/€5/€10/€20) or a
provider-native custom amount, choose a provider (Stripe or PayPal),
and open the provider's public hosted payment page in the default
browser. The app never handles money, holds no secrets, and never
claims a payment succeeded. Hosted Payment Links only; no backend, no
webhooks, no card data, no false confirmation, no nagware. Zero new
NuGet packages, no schema change, no per-profile data. The feature is
off unless a `donations.settings.json` with `Enabled: true` and valid
HTTPS links is present, in which case the menu entry stays hidden.

### Added

- Application `Donations`: `DonationProvider` enum (Stripe/PayPal live,
  KoFi/BuyMeACoffee reserved), `IDonationProvider` / `IUrlLauncher`
  ports, `DonationOptions` / `ProviderOptions`, `DonationLaunchResult`,
  `DonationFailureReason`, and `DonationService` — the single
  orchestrator owning the ordered validation pipeline.
- Infrastructure adapters: `StripeDonationProvider`,
  `PayPalDonationProvider`, `ShellUrlLauncher` (the only place
  `Process.Start` is called), `JsonDonationOptionsProvider`.
- UI `DonateForm` and a "Support Development" entry under the Help menu,
  hidden when the feature is disabled or unconfigured.
- `donations.settings.json` template with placeholder links (custom
  "choose your amount" key included).
- Localization keys in all five dictionaries.

### Docs

- "Support Development" section in the user guides.
- Maintainer section in `docs/PACKAGING.md` on creating Stripe / PayPal
  Payment Links (including the custom-amount link) and populating
  `donations.settings.json`.

### Tests

- `DonationService` tests (fixed tiers, amount validation,
  feature/provider gates, malformed/non-HTTPS links, launch success and
  failure, custom-amount verbatim launch and failure modes).
- Infrastructure tests for the provider adapters and the JSON options
  loader.
## PR #39 — Add analysis for multi-stage (stepped) tapering regimens

Link: [vger70/MedReminder#39](https://github.com/vger70/MedReminder/pull/39)
**Status:** merged (2026-09-21)

Branch: `feature/stepped-tapering-analysis`

Docs-only change. Adds `docs/ANALYSIS-A1-STEPPED-TAPER.md`, a
pre-implementation design for tapering regimes that require
intermediate step-down stages (dose D for X days, D/2 for Y days,
D_final for Z days) — a shape the linear `TaperingSchedule` shipped
with A1 cannot express. Proposes a new `SteppedTaperingSchedule` value
object (`ScheduleKind = 5`) holding an ordered list of
`(dose, durationDays)` stages, serialized into A1's existing
`SchedulePayload` column so **no SQLite schema patch is required**.
Records the two confirmed product decisions: both end-of-course
behaviors via a `MaintainLastDose` flag (the course ends by default,
with an opt-in indefinite maintenance dose), and a Linear / Stepped
sub-choice inside the existing "Tapering" regime, with a dynamic stage
editor and a pre-save preview. No code change; implementation is a
separate PR pending sign-off.

### Docs

- New `docs/ANALYSIS-A1-STEPPED-TAPER.md` — data model, codec payload,
  projection-engine impact (none structural), UI, localization keys,
  tests, retro-compatibility, risks, implementation plan, and the
  confirmed / open decisions.

---

## PR #35 — Add A6 donation/support UI evolution to EVOLUTION.md

Link: [vger70/MedReminder#35](https://github.com/vger70/MedReminder/pull/35)
**Status:** merged (2026-09-20)

Branch: `claude/stoic-mendel-p9jlaf`

Docs-only change. Classifies the donation/support feature drafted
in `docs/DONATION-SUPPORT-FEATURE.md` as a Group A item (A6) —
pure UI + configuration, no backend, no schema patch, no change
to the app's local-first, non-clinical posture. Records the
design constraints: hosted payment pages only (Stripe Payment
Links, PayPal hosted donate URL), no secrets in the client,
provider abstraction (`IDonationProvider`) shaped so a future
backend can swap the "open a hosted URL" adapter for a
"call our checkout API + verify via webhook" adapter without
touching the UI, no false payment-completion claims, single
Help menu entry with no launch nagware. Rewrites the
inside-Group-A priority ordering in §2 by ascending cost with
dependencies respected: A6 (3–5 days, zero deps) → A2 → A3 →
A1 → A5, preserving the A1 → A5 precondition introduced in
PR #34.

### Docs

- New §3.6 in `docs/EVOLUTION.md` — A6 evolution with
  motivation, preconditions, design sketch (options model,
  storage path, provider abstraction, amount tiers,
  browser-launch UX, validation, logging), effort estimate,
  risks (false confirmation, secrets, nagware, store policy,
  regional payment failure) and verdict.
- §2 priority ordering rewritten with an explicit
  inside-Group-A cost/benefit sequence.
- Change log entry appended to `docs/EVOLUTION.md` §9.
- `CLAUDE.md` §5 tightened: branch naming and "open PR after
  first commit of a work session" are now marked **mandatory**
  explicitly. Both rules are also mirrored at the top of §8
  "What to always do" so they surface in the non-negotiable
  checklist.

---

## PR #34 — Add A5 dose-time "remind me to take it" evolution to EVOLUTION.md

Link: [vger70/MedReminder#34](https://github.com/vger70/MedReminder/pull/34)
**Status:** merged (2026-09-20)
Branch: `claude/sleepy-bardeen-f5y5ir`

Docs-only change. Adds a new candidate evolution (A5) to
`docs/EVOLUTION.md` describing a per-medicine dose-time reminder
that fires a toast (and optional email) at the scheduled time of
each dose, gated by a "remind me to take it" checkbox in
`MedicineEditDialog` that is enabled only when the medicine is
active in the therapy and on-hand stock is greater than zero.
The analysis records that the existing groundwork
(`AdministrationSlotEntry.Time`, `SchedulePanel`, `Schedule`,
per-medicine channel checkboxes, `MedicationMonitor`
deduplication) is sufficient, and explicitly bounds the feature
away from adherence tracking / EU MDR 2017/745 scope: no
acknowledgement UI, no missed-dose logging, no clinical alerts.
The Group A ordering note is updated with the hard A1 → A5
precondition.

### Docs

- New §3.5 in `docs/EVOLUTION.md` — A5 evolution with
  motivation, preconditions (verified against the current tree),
  design sketch, effort estimate, risks and verdict.
- §2 priority ordering note updated with the A1 → A5
  dependency.
- §9 change log entry appended for 2026-09-20.

No source, build or runtime behavior is changed.

---

## PR #33 — Expose AIFA leaflet / SPC links in the medicine edit dialog

Link: [vger70/MedReminder#33](https://github.com/vger70/MedReminder/pull/33)
**Status:** merged (2026-09-20)
Branch: `claude/vigilant-thompson-0colfk`

The reference-catalogue SQLite table already stores `link_leaflet`
(AIFA `LINK_FI`, the package leaflet) and `link_spc` (AIFA
`LINK_RCP`, the summary of product characteristics) for every
Italian row, but no UI surface exposed them. This PR adds a
"Documenti AIFA" row to `MedicineEditDialog`, immediately below
the active-ingredient field, that surfaces both documents as
`LinkLabel`s that open in the user's default browser.

### Added

- New "Documenti AIFA" row in `MedicineEditDialog` with two
  `LinkLabel`s — one for the package leaflet, one for the SPC —
  present only when the user's reference country is Italy (the
  other supported catalogues do not carry these fields). Each
  link is shown only if the corresponding URL is available on the
  linked reference row; the whole row hides when neither is.
- `ReferenceMedicineLookupAsync` delegate on
  `CatalogueAutocompleteContext`, implemented in
  `MainForm.LookupReferenceByNationalCodeAsync` through
  `IReferenceCatalogueQueryService.GetByNationalCodeAsync`
  (already existed). Used by the dialog on `OnLoad` in Edit mode
  to re-hydrate the two URLs from the seeded `NationalCode`.
- `IsSafeAifaUrl` guard: `Process.Start(UseShellExecute = true)`
  only fires for `https` URLs whose host equals or ends with
  `aifa.gov.it` or `agenziafarmaco.gov.it`. A corrupted catalogue
  snapshot cannot turn either label into an open-redirect vector.
- Four localization keys — `Ui.MedicineEditDialog.Field.Documents`,
  `Ui.MedicineEditDialog.Documents.Leaflet`,
  `Ui.MedicineEditDialog.Documents.Spc`,
  `Ui.MedicineEditDialog.Documents.OpenError` — added to every
  shipped dictionary (`en`, `it`, `fr`, `es`, `de`).

### Changed

- `CatalogueAutocompleteContext` gains a fourth field
  `LookupByNationalCode` for the exact-match hydration path. The
  three search / country fields keep their meaning.
- `OnReferenceSelected` also refreshes the two links from the
  freshly picked `ReferenceMedicine`, and `ClearReferenceLinkage`
  hides them when the user diverges from the linked record —
  matching the existing NationalCode / AtcCode / LinkedReferenceMedicineId
  bookkeeping.

---

## PR #31 — A1: Complex therapy regimens (Schedule value object, Simple/Advanced UI)

Link: [vger70/MedReminder#31](https://github.com/vger70/MedReminder/pull/31)
**Status:** merged (2026-09-19)
Branch: `feature/complex-regimens`

Implements Group A item **A1** from `docs/EVOLUTION.md` §3.1 per
the design locked in `docs/ANALYSIS-A1-REGIMENS.md`. Extends the
linear `dose × administrations/day` consumption model with four
non-constant schedule shapes so cyclic, weekly, tapering and
as-needed therapies produce the correct daily rate for the
projection engine. Pre-A1 databases upgrade transparently: an
additive `ALTER TABLE … ADD COLUMN` patch guarded by
`PRAGMA table_info` runs on first boot, and the existing rows read
back as `FixedDaily` via SQLite's `DEFAULT 0` — the projection
stays byte-for-byte identical without a data-fix pass.

### Added

- New `Schedule` value object in `MedReminder.Domain` with five
  discriminated shapes: `FixedDailySchedule` (existing behavior),
  `WeeklySchedule` (per-day-of-week quantities),
  `CyclicSchedule` (N on / M off with a per-on-day quantity),
  `TaperingSchedule` (start dose → end dose in fixed steps every
  fixed number of days) and `PrnSchedule` (as-needed; no
  scheduled consumption).
- `ScheduleCodec` — `System.Text.Json` (de)serializer that bridges
  the domain value object to `ScheduleKind` + optional payload
  columns on `MedicationScheduleHistory`. Unknown enum values fall
  back to `FixedDaily` (fail-safe); malformed payloads throw
  `InvalidOperationException` naming the offending kind.
- Reusable `SchedulePanel` control hosting the Simple / Advanced
  toggle, the Regime-type dropdown and one sub-panel per kind.
  Embedded in the "New medicine" dialog (Create mode) and in the
  "Change schedule" dialog; the Simple flow stays one click and
  the Advanced flow is validated against each `Schedule` subtype's
  invariants (weekly requires 7 non-negative days with at least
  one > 0; cyclic requires `on ≥ 1`, `off ≥ 0`, `quantity > 0`;
  tapering rejects `start == end`; PRN takes no inputs).
- "Complex regimens" section in `docs/USER_GUIDE.en.md` explaining
  the Simple / Advanced selector, each kind's semantics, and an
  explicit reminder that MedReminder performs no clinical checks
  (no maximum-daily-dose, no interaction warnings).
- 29 new `Ui.Schedule.*` localization keys covering the toggle,
  the regime types, the weekly weekday headers, the cyclic /
  tapering summary strings, the PRN help text and the validation
  fallback. Italian ships with `TODO(it): <english fallback>`
  placeholders per the maintainer's preference to finalize the
  wording on the form itself.

### Changed

- `MedicationScheduleHistory` gains two nullable / defaulted
  fields: `ScheduleKind` (defaults to `FixedDaily`) and
  `SchedulePayload` (`null` for `FixedDaily`, JSON otherwise).
  Existing rows keep their meaning without a data-fix pass.
- `DailyConsumption.RateOn` now picks the latest applicable
  `MedicationScheduleHistory` entry and dispatches through
  `ScheduleCodec.Deserialize(...).RateOn(day, anchor)`. Slot
  behavior is unchanged: slots keep taking precedence when
  present.
- `AddMedicineCommand.InitialSchedule` and
  `ChangeMedicationScheduleCommand.NewSchedule` are optional and
  default to `null` (every existing caller keeps compiling).
  When set they are persisted verbatim; when `null` the use case
  builds a `FixedDailySchedule` from the legacy Dose /
  Administrations parameters, so the pre-A1 path is preserved.
  Validation on those two parameters is relaxed to `>= 0` when
  a `Schedule` is supplied (so PRN's display `Dose = 0` is
  accepted) and stays strict `> 0` otherwise.
- `MedicineEditResult.InitialSchedule` and
  `ChangeScheduleResult.NewSchedule` thread the value object from
  the dialogs down to the use cases. In Advanced mode the outer
  Dose / Administrations / Slots inputs are disabled so the
  source of truth is unambiguous.

### Docs

- New `docs/ANALYSIS-A1-REGIMENS.md`: full pre-implementation
  design, confirmed decisions (§12), deferred slot × schedule
  follow-up path (§13) and implementation-status footer listing
  every shipped and deferred item.

### Build

- No new NuGet dependencies. `System.Text.Json` used by the codec
  ships with the target runtime.

### Deferred (deliberately out of scope for this PR)

- `MainForm` grid badges for non-FixedDaily therapies — the
  Consumption/day cell shows today's numeric rate as before.
- Italian final translations of the new UI strings.
- "Complex regimens" section translated into the four non-English
  user guides (`it`, `fr`, `es`, `de`).
- Slot × non-FixedDaily combinations — the design's future path is
  captured in `docs/ANALYSIS-A1-REGIMENS.md` §13.
- Forward-integrating run-out ETA (the forecast stays a
  scalar-snapshot using today's rate).

---

## PR #30 — Add EVOLUTION.md, prospective work beyond Increment 15

Link: [vger70/MedReminder#30](https://github.com/vger70/MedReminder/pull/30)
**Status:** merged (2026-09-19)
Branch: `claude/practical-maxwell-uzn54q`

Docs-only change. Adds a new prospective-analysis document that
records candidate evolutions past the Increment 15 baseline so
future sessions and maintainers do not re-derive them. No source
code, tests, build scripts, CI workflows or localization
dictionaries are touched; runtime behavior is unchanged.

### Docs

- New `docs/EVOLUTION.md` covering:
  - **Group A** — low-friction extensions: complex therapy regimens
    (cycles, tapering, PRN — extending the `Schedule` value object
    in `MedReminder.Domain`), AIC/barcode scan of the medicine
    package leveraging the existing reference catalogue, and
    caregiver email notifications reusing the MailKit transport.
  - **C.3** — manual export/import as encrypted zip (Argon2id
    passphrase key derivation, AES-GCM payload, deliberately not
    DPAPI so the export survives device migration); public JSON
    format to be documented in a follow-up `docs/EXPORT-FORMAT.md`
    once the feature lands.
  - **C.3+** — automatic backup targeting a user-controlled cloud
    folder (OneDrive, iCloud Drive, Dropbox…) with explicit
    *Restore from backup* on a second device. Reuses the existing
    `backup.settings.json` mechanism from `CLAUDE.md` §6. Model is
    single-writer, multiple-reader-on-demand — not real-time sync.
  - **B.1** — mobile companion client. Portable `MedReminder.Domain`
    and `MedReminder.Application` reuse table; per-platform
    replacement of the `MedReminder.Infrastructure` adapters
    (DPAPI → Keychain/Keystore; WinRT toast → local notifications;
    tray → n/a; single-instance mutex → n/a). MAUI recommended as
    default UI framework, with Avalonia as the fallback when
    desktop Linux is also a target. Precondition: do not ship B.1
    without C.3+ in place.
  - **C.1** — end-to-end encrypted sync with a dedicated backend
    (zero-knowledge; Argon2id-derived master key; ChaCha20-Poly1305
    per-record; append-only operation log; ASP.NET Core + Postgres
    hosted in the EU). Documents the non-technical cost of running
    a service (perpetual operation, recovery UX for lost passphrases,
    business-model shift), and the GDPR posture on ciphertext blobs.
    Recommends Bitwarden and Standard Notes as prior art.
- **C.2** (raw file-sync of the live SQLite database via OneDrive /
  iCloud / Dropbox) explicitly rejected in §7.1 on technical
  grounds (SQLite FAQ; WAL/SHM ordering; no distributed locking;
  meaningless conflict files).
- **Group D** (national health-system integrations) and
  medical-device functions (adherence tracking, clinical alerts,
  drug-interaction checks) explicitly excluded from the document
  with rationale — the first for regulatory / API-access
  uncertainty, the second for EU MDR 2017/745 scope.
- Priority order set out in §2: Group A → C.3 → C.3+ → B.1 → C.1.
- Non-commitment posture: the document records options, not work
  planned. Items become work only once explicitly approved and
  turned into a dedicated analysis document or GitHub issue.

---

## PR #29 — About dialog with credits + passive GitHub update check

Link: [vger70/MedReminder#29](https://github.com/vger70/MedReminder/pull/29)
**Status:** merged (2026-09-19)
Branch: `claude/stoic-bohr-dker85`

Two small user-facing additions and their supporting plumbing. No
changes to the domain, persistence or notification pipelines.

### Added

- New `MedReminder.UI.Forms.AboutDialog` — clickable About window
  reachable from Help → *About MedReminder…*. Shows the running
  version (from `Assembly.GetExecutingAssembly().GetName().Version`),
  the author handle (`vger70`), the author email
  (`m.mosti@gmail.com`), a link to the GitHub repository, a link to
  report an issue, the Apache-2.0 license note, the "not a medical device"
  disclaimer, and the existing per-country reference-catalogue
  attributions (AIFA / EMA / AEMPS / BDPM). It also embeds a
  *Check for updates now* button that hits the same endpoint as the
  passive startup check.
- Passive update check against the public GitHub Releases API. New
  ports and adapter live under `MedReminder.Application.UpdateChecking`
  (`IUpdateChecker`, `UpdateCheckResult`, `GitHubReleaseParser`) and
  `MedReminder.Infrastructure.UpdateChecking.GitHubUpdateChecker`.
  On main-window load the app queries
  `https://api.github.com/repos/vger70/MedReminder/releases/latest`,
  compares the tag with the assembly version and pops a non-modal
  prompt if a newer stable release exists — otherwise it stays
  silent. The check downloads and installs nothing; the user still
  opens the release page in their browser to upgrade manually.
  Timeouts, rate limits and network errors all collapse into a
  no-op on startup (logged at Information).
- New Help → *Check for updates…* menu entry that always runs the
  check on demand and always reports the outcome (up to date, new
  version, or error), regardless of the opt-in flag.
- New checkbox on the Settings → General tab:
  *Check for updates on startup (GitHub)*, backed by
  `UserSettings.CheckForUpdatesOnStartup` (default `true`, persisted
  in `%LOCALAPPDATA%\MedReminder\user.settings.json`).
- Assembly and package metadata in `Directory.Build.props`
  (`Authors`, `Company`, `Product`, `Copyright`, `PackageProjectUrl`,
  `RepositoryUrl`, `RepositoryType`, `PackageLicenseExpression`) so
  the shipped `.exe` carries proper file properties in Windows
  Explorer and the About dialog can read them via
  `Assembly.GetCustomAttribute`.

### Localisation

- 21 new keys added to every shipped dictionary
  (`en`, `it`, `fr`, `es`, `de`):
  - `Ui.MainForm.Menu.Help.CheckUpdates`
  - `Ui.AboutDialog.Title`, `.AppName`, `.Version`, `.Author`,
    `.Email`, `.Repository`, `.ReportIssue`, `.License`,
    `.Disclaimer`, `.DataSources`, `.CheckForUpdates`,
    `.CheckingForUpdates`
  - `Ui.UpdateCheck.Title`, `.UpToDate`, `.NewVersion`, `.Error`,
    `.NewVersionTitle`, `.NewVersionPrompt`
  - `Ui.SettingsDialog.General.CheckUpdates`
  - `Ui.SettingsDialog.Tooltip.CheckUpdates`
- The obsolete `Ui.MainForm.About.Body` / `Ui.MainForm.About.Title`
  keys are removed from all five dictionaries — they were the copy
  of the previous `MessageBox`-based About that the new dialog
  replaces.

### Tests

- `MedReminder.Application.Tests.UpdateChecking.GitHubReleaseParserTests`
  covers tag parsing (`v2.0.1`, `V2.0.1`, `2.0.1`, `v2.0.1-rc1`,
  garbage), version comparison (ahead / equal / behind), the
  revision-component normalisation, prerelease / draft flags, and
  malformed / incomplete JSON payloads.

### Version

- `Directory.Build.props`: `VersionPrefix` set to `2.0.1` to match
  the released tag; the release workflow will bump it further on the
  next release.

---

## PR #28 — Profile UX polish (Increment 15 follow-up)

Link: [vger70/MedReminder#28](https://github.com/vger70/MedReminder/pull/28)
**Status:** merged (2026-09-19)
Branch: `claude/profile-ux-polish`

Three small follow-ups on the multi-user feature that shipped in
Increment 15. No new capability — cleanup only.

### Added

- `MedReminder.UI.Forms.ChangePinDialog` — the PIN dialog is now a
  top-level `public sealed class` under `MedReminder.UI.Forms`,
  reused by `ProfilesManagerForm` (admin action on any profile)
  and by a new "My PIN" section on the Notifications tab of
  `SettingsDialog` (self-service action on the caller's own
  profile). A non-admin profile no longer has to ask the
  administrator to set or clear its own PIN
  (`docs/ANALYSIS-MULTI-USER.md` §8). Five new localisation
  keys, added to every shipped language:
  - `Ui.SettingsDialog.Notifications.MyPin`
  - `Ui.SettingsDialog.Notifications.PinStateSet`
  - `Ui.SettingsDialog.Notifications.PinStateNone`
  - `Ui.SettingsDialog.Notifications.SetPin`
  - `Ui.SettingsDialog.Notifications.PinChanged`

### Changed

- `MedReminder.UI.Forms.SettingsDialog` — the Startup tab is now
  admin-only, alongside Email and Backup. The Windows Run entry
  is a per-Windows-account setting, so a non-admin profile must
  not toggle it (would change auto-start behaviour for every
  profile of the same Windows account). Coherent with the §7.4
  gating already applied to Email and Backup.
- `MedReminder.Application.Abstractions.IApplicationRestarter` —
  new overload `RestartAndExit(IReadOnlyList<string>? extraArgs)`;
  the no-argument overload is preserved and delegates to it. The
  UI implementation forwards each argument via
  `ProcessStartInfo.ArgumentList` so the CLR handles escaping.

### Fixed

- `MedReminder.UI.Forms.MainForm.ChangeProfile` — switching
  profile from *File → Change profile…* no longer opens the
  profile picker twice. The restarted process is now given
  `--profile <id>` on the command line, which `Program.Main`
  already honours as the highest-priority profile selector
  (`ANALYSIS-MULTI-USER.md` §6.1). The
  `ActiveProfileIdHint` is still set as a fallback but is no
  longer relied upon to skip the picker.
- `MedReminder.UI.Forms.SettingsDialog.ImportBackupAsync` —
  when the imported backup targets the active profile, the
  restart now also passes `--profile <id>` for the same reason.
  Redundant with the hint but symmetric with the profile-switch
  restart.

---

## PR #27 — Increment 15: merge multi-user support into main

Link: [vger70/MedReminder#27](https://github.com/vger70/MedReminder/pull/27)
**Status:** merged (2026-09-19)
Branch: `claude/incremento-15b`

Rollup merge of the four Increment 15 sub-increments (15b + 15c +
15d + 15e — PRs #23, #24, #25, #26) into `main`. No code changes
of its own; the observable outcome is that
`docs/ANALYSIS-MULTI-USER.md` is now fully implemented on `main`
and Increment 15 has been cleared from `CLAUDE.md` §7 pending
list.

---

## PR #26 — Increment 15e: PIN polish, user guide, feature marked implemented

Link: [vger70/MedReminder#26](https://github.com/vger70/MedReminder/pull/26)
**Status:** merged (2026-09-19)
Branch: `claude/incremento-15e`

Fifth and final sub-increment of the multi-user work
(`docs/ANALYSIS-MULTI-USER.md` §8, §12, §15e). Wraps up the
feature: polishes the PIN prompt, documents the multi-profile
experience in every shipped language, marks the design as
implemented, and clears Increment 15 from the CLAUDE.md pending
list. No behavior change beyond the PinPromptForm touches.

### Changed

- `MedReminder.UI.Forms.PinPromptForm` — polish pass:
  - The "friction, not security" wording is now shown as an
    always-visible label under the PIN box, not only as a
    tooltip (§8.2). New localisation key
    `Ui.PinPromptForm.FrictionNote`.
  - On the third wrong attempt the dialog now shows a modal
    "locked out" `MessageBox` before closing, so the user sees
    what happened instead of watching the window vanish.
  - Layout widened to 420×240 to accommodate the note without
    reflow.

### Docs

- `docs/USER_GUIDE.{en,it,fr,es,de}.md` — new
  **"Multiple profiles and admin/user roles"** section, added to
  each shipped language between "Edit or deactivate a medicine"
  and "Configure email sending". Covers: roles, creating and
  switching profiles, rename / PIN / delete, on-disk layout,
  multi-profile automatic backup, restore-into-profile,
  Windows auto-start behaviour, and the V1 → V2 upgrade with the
  manual pre-migration backup cleanup note (§14 F).
  "First start" was also updated in each language to describe the
  first-run wizard and the new per-profile database path.
- `docs/ANALYSIS-MULTI-USER.md` — added an **Implementation
  status** footer that maps every sub-increment to its PR and
  reiterates the two non-goals (promote/demote, consolidated
  admin view) that remain deferred (§16).
- `CLAUDE.md` §5 — no standing working branch after Increment 15;
  new features start from `main`.
- `CLAUDE.md` §6 — "Data locations at runtime" table split into
  shared/admin-managed and per-profile files, matching the V2
  layout that shipped in 15b + 15c.
- `CLAUDE.md` §7 — Increment 15 removed from the pending list;
  only the two documented non-goals remain deferred.

### Localisation

- **1 new key** added to every dictionary
  (`Ui.PinPromptForm.FrictionNote`). All 5 dictionaries stay at
  parity at **439 keys each** — `DictionaryParityTests` remain
  green.

### Increment 15 complete

Increment 15 shipped in **five sequential pull requests**
(#22 → #26). All the confirmed decisions in
`docs/ANALYSIS-MULTI-USER.md` §14 / §14a are honored in the
shipped code. The two explicit non-goals — profile promote /
demote and the consolidated admin view — remain deferred.

---

## PR #25 — Increment 15d: ProfilesManagerForm, admin/user gating, restore-into-profile

Link: [vger70/MedReminder#25](https://github.com/vger70/MedReminder/pull/25)
**Status:** merged (2026-09-19)
Branch: `claude/incremento-15d`

Fourth sub-increment of the multi-user work
(`docs/ANALYSIS-MULTI-USER.md` §7.4, §11.3, §12, §15d). Adds the
admin-only profile-management UI, gates the SettingsDialog by role,
surfaces the active profile in the main window and lets the admin
restore any profile from the Backup tab. PIN prompt polish and the
user-guide entries remain for 15e.

### Added

- `MedReminder.UI.Forms.ProfilesManagerForm` — admin-only CRUD for
  profiles (§12.4). Columns: name, role, PIN status, last-used
  (`dd/MM HH:mm`), active-indicator. Inline dialogs for New / Rename
  / Change PIN and a **type-name-to-confirm** delete dialog with an
  "also delete data on disk" checkbox that defaults to OFF (§13).
  Delete button is disabled for the currently active profile
  (§14a H) and for the last remaining admin (§2.2). Defense-in-depth
  guards inside the form back up the button-disable logic in case
  the form is opened by a non-admin caller.
- `File → Change profile…` menu entry (everyone): opens the
  `ProfilePickerForm`, sets the hint on confirm and restarts through
  `IApplicationRestarter` (§6.1).
- `Tools → Manage profiles…` menu entry (admin only, hidden for
  non-admins — §12.2).
- New **Notifications** tab in `SettingsDialog` (§7.4). Visible to
  every profile; contains only the per-profile `ToAddress`. Persists
  to `<DataDirectory>\notifications.settings.json`.
- **Restore-into-profile** dropdown in the Backup tab (§11.3).
  Extracts the `profileId` from the backup filename
  (`medreminder-<profileId>-YYYYMMDD-HHmmss.db`) as the default
  selection, falling back to the active profile when the filename
  does not follow the convention. `RestartAndExit` is called only
  when the target profile is the active one — restoring into an
  inactive profile does not touch the live `DbContext` connection.

### Changed

- `SettingsDialog` — Email and Backup tabs are hidden for non-admin
  profiles. The Email tab no longer contains the recipient field;
  it lives in the new Notifications tab (visible to everyone). The
  Save button on the Email tab writes only SMTP; the Notifications
  tab has its own Save button that writes only the per-profile
  file. `IProfileRegistry` was added to the constructor so the
  Backup dropdown can enumerate profiles.
- `MainForm` — title bar shows the profile name
  (`MedReminder — Grandma`, §12.1). StatusStrip carries a
  `Profile: <name>` label on the left, bold + dark-blue with the
  `(admin)` suffix when the current profile is an admin
  (distinctive badge, §12.1). The constructor now injects
  `ICurrentProfile`, `IProfileRegistry` and `IApplicationRestarter`.

### Localisation

- **60 new keys** added to every dictionary
  (`assets/localization/strings.{en,it,fr,es,de}.json`) — menus,
  StatusStrip, ProfilesManagerForm dialogs, Notifications tab,
  restore-into-profile chooser. All 5 dictionaries stay at
  **parity at 438 keys each** — `DictionaryParityTests` remain
  green.

### Invariants (unchanged, enforced twice)

- **At least one admin** — Delete refuses in `ProfileRegistry` and
  the Delete button is disabled for the last admin.
- **Active profile not deletable** — Delete button is disabled;
  the form also shows an explanatory warning if a script triggers
  the click (§14a H).
- **Immutable role** — no promote/demote path exists in the UI or
  in the registry API (§14a G). Explicit hint label at the bottom
  of the form.
- **Restore into inactive profile skips restart** — only the
  active-profile restore triggers `RestartAndExit` (§11.2).

### Out of scope (still)

- PIN prompt polish, tooltip wording pass, user-guide entries —
  15e.
- Promote/demote flow, cross-profile consolidated view — non-goals
  for Increment 15 (§16).

---

## PR #24 — Increment 15c: multi-profile boot flow and per-profile services

Link: [vger70/MedReminder#24](https://github.com/vger70/MedReminder/pull/24)
**Status:** merged (2026-09-18)
Branch: `claude/incremento-15c` (stacked on `claude/incremento-15b` from PR #23)

Third and largest sub-increment of the multi-user work
(`docs/ANALYSIS-MULTI-USER.md` §4, §7, §11, §15c). Wires the
15a / 15b groundwork into the boot flow. After this PR the app
opens with a picker when more than one profile exists, runs the
first-run wizard on a clean install, gates the DB and per-profile
recipient behind `ICurrentProfile`, backs up every profile on each
successful automatic-backup tick, and enforces the profile PIN
when one is set. Admin/user UI gating remains for 15d; PIN prompt
polish and user-guide entries remain for 15e.

### Added

- `MedReminder.UI.Forms.FirstRunWizardForm` — mandatory wizard
  shown when the registry is empty. Collects the admin name and
  an optional PIN, then creates the profile through
  `IProfileRegistry.Create` (which forces `Role = Admin` on an
  empty registry). Cannot be dismissed with the window `X`;
  Exit closes the app (§12.3).
- `MedReminder.UI.Forms.ProfilePickerForm` — boot picker shown
  when more than one profile exists. `ListView` with name / role
  badge / `dd/MM HH:mm` last-used (decision §14 D), sorted by
  `LastUsedAt` descending, `ActiveProfileIdHint` pre-selected.
- `MedReminder.UI.Forms.PinPromptForm` — three in-memory attempts
  (§8.3). Returns `DialogResult.Abort` on lockout so the caller
  bails out of the boot flow. Tooltip already carries the
  "friction, not security" note; the polish pass lands in 15e.

### Changed

- `IBackupService` — replaced `ExportAsync(dir, ct)` /
  `ImportAsync(src, ct)` with per-profile
  `ExportProfileAsync(profileId, dir, ct)` /
  `ImportProfileAsync(profileId, src, ct)` (§11.2). File name
  becomes `medreminder-<profileId>-YYYYMMDD-HHmmss.db` so
  different profiles can share a folder.
- `BackupService` — implements the new API. The retention regex
  captures the `profileId` group so
  `PruneOldBackupsAsync` applies retention per-profile: the most
  recent backup of profile A does not shield old backups of
  profile B (§11.1). `ImportProfileAsync` only closes the
  currently-active `DbContext` connection when the target matches
  the DB path — an import of an inactive profile no longer
  touches the live connection.
- `AutomaticBackupHostedService` — each tick now enumerates
  `IProfileRegistry.ListProfiles()` and calls `ExportProfileAsync`
  for every profile. A failure on one profile is logged but does
  not stop the others. Retention runs once on the shared folder.
  The tick is marked successful when at least one profile
  exported, so a partial failure never masks days without any
  backup (§11.1).
- `SmtpSettings` — dropped `ToAddress`. The recipient moved to
  `NotificationSettings.ToAddress` in
  `<DataDirectory>\notifications.settings.json` (§7.1).
  `IsConfigured` no longer checks the recipient.
- `MailKitEmailNotificationService` — now takes
  `IOptionsMonitor<SmtpSettings>` **and**
  `IOptionsMonitor<NotificationSettings>`. Throws a specific
  `InvalidOperationException` when the per-profile recipient is
  missing (§7.1).
- `AddMedReminderInfrastructure` — takes `ICurrentProfile` in
  place of a raw `databasePath`. Registers the current profile
  as a singleton, registers `IProfileRegistry` (built from
  `AppDataPaths`), and binds `NotificationSettings` from the
  configuration chain.
- `Program.Main` — new multi-profile boot flow (§4.1): run the
  V1 → V2 migrator, list profiles, pick one (first-run wizard /
  hint / `--profile` / picker), prompt for the PIN if the profile
  has one, then build the host. `--minimized` skips the picker
  and uses the hint (§4.2). `--profile <id>` bypasses the picker
  (§4.3). The single-instance mutex stays per Windows account,
  independent of the profile (§10.1).
- `Program.BuildHost` — adds
  `notifications.settings.json` (per-profile path from
  `ICurrentProfile.NotificationSettingsPath`) to the configuration
  chain with `reloadOnChange: true`.
- `SettingsDialog` — reads / writes `NotificationSettings` for
  the recipient (per-profile file). Export / import buttons now
  call the new per-profile backup APIs against
  `_currentProfile.Id`. The Backup tab still shows every existing
  option to the current user; the admin/user gating and the
  "Restore into profile…" dropdown land in 15d.
- `MainForm.ShowSettings` — passes the new
  `IOptionsMonitor<NotificationSettings>` and `ICurrentProfile`
  dependencies through the DI scope.
- `MedReminder.Infrastructure.Profiles.ProfileRegistry`,
  `CurrentProfile`, `MedReminder.Infrastructure.Migration.MigrationV1toV2`
  are now `public sealed class` so `Program.Main` (in
  `MedReminder.UI`) can build them at boot without expanding
  `InternalsVisibleTo`.

### Tests

- `MailKitEmailNotificationServiceTests` updated to the new
  two-monitor constructor. New test:
  `Send_throws_when_recipient_is_missing`.
- Existing `MigrationV1toV2Tests` and `ProfileRegistryTests`
  unchanged and still green: the migrator is now called at boot
  but its API is untouched.

### Localisation

- 27 new keys added to every dictionary
  (`assets/localization/strings.{en,it,fr,es,de}.json`) —
  `Common.Exit`, migration-failure banner, PIN prompt,
  profile picker, first-run wizard. All 5 dictionaries stay at
  parity (378 keys each) — `DictionaryParityTests` remain green.

### Out of scope (still)

- `ProfilesManagerForm`, admin/user UI gating, `File → Change
  profile…` menu entry, restore-into-profile dropdown — 15d.
- PIN prompt polish, tooltips wording pass, user-guide entries —
  15e.
- Promote/demote flow and consolidated admin view — non-goals
  for Increment 15 (§16).

---

## PR #23 — Increment 15b: V1 → V2 on-disk migration

Link: [vger70/MedReminder#23](https://github.com/vger70/MedReminder/pull/23)
**Status:** merged (2026-09-18)
Branch: `claude/incremento-15b` (stacked on `claude/incremento-15` from PR #22)

Second sub-increment of the multi-user work
(`docs/ANALYSIS-MULTI-USER.md` §5, §15b). Adds the data-lossless
migrator that moves an existing V1 installation to the V2 on-disk
layout under `%LOCALAPPDATA%\MedReminder\`. **The migrator is
dormant**: `Program.Main` does not call it in this PR. Wiring lands
in 15c together with the boot flow, so the app still boots as
single-user and no user-visible behavior changes.

### Added

- `MedReminder.Infrastructure.Migration.MigrationV1toV2` — one-shot
  idempotent V1 → V2 migrator with mandatory pre-migration backup
  and full rollback on any post-backup failure (§5.2).
  - Idempotence guard: runs only when `profiles.json` is missing
    AND a legacy `medreminder.db` sits at the app-data root (§5.1).
  - Step 1 copies `medreminder.db` (+ `-wal` / `-shm`) and
    `smtp.settings.json` into
    `backups\pre-migration-YYYYMMDD-HHmmss\`. The folder is
    self-describing and never overwritten — user is responsible
    for manual cleanup (§14 F).
  - Steps 2-3 move the DB files into `profiles\default\`.
  - Step 5 extracts `Smtp.ToAddress` from the legacy
    `smtp.settings.json` into
    `profiles\default\notifications.settings.json` and rewrites
    the source with the key removed. Empty / missing `ToAddress`
    is handled gracefully.
  - Step 6 seeds `profiles.json` via a new internal
    `ProfileRegistry.SeedFromV1Migration(id, displayName)` — the
    migrated profile is always `Role = admin`, `Id = "default"`,
    `DisplayName = "User"` (§5.2 step 6). The registry refuses to
    seed a non-empty file.
  - Any exception between steps 2 and 6 triggers
    `RollbackFromPreBackup`: `profiles.json` and
    `profiles\default\` are dropped, the DB files are restored
    from the pre-backup, and `smtp.settings.json` is restored
    verbatim. The pre-backup itself is preserved.
- `MigrationOutcome` public enum (`NotNeeded`, `Migrated`) — returned
  by `MigrationV1toV2.Run()` so the future boot flow can log the
  outcome.
- `tests/MedReminder.Infrastructure.Tests/Migration/MigrationV1toV2Tests.cs`
  — 6 integration tests exercising the migrator against a fake V1
  tree under `Path.GetTempPath()`:
  - fresh install (no legacy DB) → `NotNeeded`
  - already migrated (`profiles.json` present) → `NotNeeded`
  - full V1 tree with `ToAddress` → V2 layout, per-profile
    notifications file, `ToAddress` stripped from source
  - V1 without `smtp.settings.json` → DB migrated, no
    notifications file
  - V1 with empty `ToAddress` → key stripped, no notifications file
  - forced mid-migration failure → rollback restores the V1 state
    and the pre-migration backup is preserved
- Two extra `ProfileRegistryTests` covering the new
  `SeedFromV1Migration` internal (default admin seeding + refusal on
  a populated registry).

### Changed

- `MedReminder.Infrastructure.Profiles.ProfileRegistry`: added the
  internal `SeedFromV1Migration(string id, string displayName)`
  hook. It writes the initial `profiles.json` with a caller-chosen
  `Id` (the migrator uses the literal `"default"`) and forces
  `Role = Admin`. Throws if the registry is already populated so
  the migrator cannot silently be re-run.

### Docs / rollback semantics

- No changes to `docs/ANALYSIS-MULTI-USER.md`: the design is
  authoritative and will be marked as implemented at the bottom
  by 15e.
- No new localisation keys — the migrator is silent, log-only in
  15b. UI wiring for the outcome banner (if any) can be added in
  15c when the boot flow calls the migrator.

---

## PR #22 — Increment 15a: profile registry and ICurrentProfile abstraction

Link: [vger70/MedReminder#22](https://github.com/vger70/MedReminder/pull/22)
**Status:** merged (2026-09-18)
Branch: `claude/incremento-15`

First sub-increment of the multi-user support work designed in
[`docs/ANALYSIS-MULTI-USER.md`](docs/ANALYSIS-MULTI-USER.md) §15a. The
app still boots as single-user: this PR introduces the abstractions
and the on-disk registry (`profiles.json`) but does not yet wire
them into the boot flow — that lands in 15c. Zero user-visible
behavior change.

### Added

- `MedReminder.Application.Abstractions.ProfileRole` (User / Admin,
  `docs/ANALYSIS-MULTI-USER.md` §1.1a).
- `MedReminder.Application.Abstractions.Profile` — immutable record
  exposed by the registry (`Id`, `DisplayName`, `Role`, `CreatedAt`,
  `LastUsedAt`, `HasPin`).
- `MedReminder.Application.Abstractions.IProfileRegistry` — port
  owning `%LOCALAPPDATA%\MedReminder\profiles.json` with atomic
  writes, tolerant deserialization and the "at least one admin"
  invariant enforced on every mutation (§2.2, §2.3).
- `MedReminder.Application.Abstractions.ICurrentProfile` — read-only
  view of the profile the running process opened; `IsAdmin` is
  exposed here (§2.4).
- `MedReminder.Application.Abstractions.NotificationSettings` —
  per-profile POCO holding only `ToAddress`. Not yet consumed by
  `MailKitEmailNotificationService`; wiring lands in 15c (§7.1).
- `MedReminder.Infrastructure.Profiles.ProfileRegistry` — JSON
  persistence with PBKDF2-HMAC-SHA256 (100_000 iterations, 16-byte
  salt) for the optional PIN (§8.3), tmp + `File.Move` atomic
  writes (same pattern as `BackupStateStore`), and fail-safe
  "unknown role → user" deserialization.
- `MedReminder.Infrastructure.Profiles.CurrentProfile` — the
  concrete `ICurrentProfile` assembled at boot in 15c.
- `MedReminder.Infrastructure.Storage.DatabasePathProvider` —
  internal singleton fed by the composition root so
  `BackupService` shares the same DB path as EF Core without
  reaching back to `AppDataPaths`.
- `tests/MedReminder.Infrastructure.Tests/Profiles/ProfileRegistryTests.cs`
  — 19 unit tests covering CRUD, atomic save, PBKDF2 PIN roundtrip,
  last-admin refusal, tolerant `Role` deserialization, and the
  first-profile-forced-to-admin rule.

### Changed

- `MedReminder.Infrastructure.Storage.AppDataPaths` — removed the
  implicit `GetDatabasePath()` (moved to `ICurrentProfile.DatabasePath`
  in 15c); added `GetProfilesRootDirectory()`,
  `GetProfilesRegistryPath()` and `GetProfileDataDirectory(id)`
  (§2.5, §3). `BuildSqliteConnectionString` now requires an
  explicit path — no per-machine default.
- `MedReminder.Infrastructure.InfrastructureServiceCollectionExtensions.AddMedReminderInfrastructure`
  — new required `string databasePath` parameter. Registers the
  new `DatabasePathProvider` singleton.
- `MedReminder.Infrastructure.Backup.BackupService` — takes
  `DatabasePathProvider` from DI; `DatabasePath` now flows through
  the provider so the export target matches the EF Core
  connection string. Signature change to `ExportProfileAsync` /
  `ImportProfileAsync` is postponed to 15c (§11.2).
- `MedReminder.Infrastructure.Persistence.MedReminderDbContextFactory`
  — design-time factory passes an explicit legacy path (only used
  by `dotnet ef` tooling for schema generation).
- `MedReminder.UI.Program.BuildHost` — passes the legacy
  single-user path
  (`%LOCALAPPDATA%\MedReminder\medreminder.db`) to
  `AddMedReminderInfrastructure`. The multi-profile boot flow
  arrives in 15c; upgrades continue to open the historical file
  until then.
- `CLAUDE.md` §5 — updated the "current working branch" to
  `claude/incremento-15`.

### Docs

- No changes to `docs/ANALYSIS-MULTI-USER.md`: it stays the
  authoritative design document and will be marked as implemented
  at the bottom by 15e.
- No new localisation keys — the registry has no UI surface in
  15a. The 15-30 keys mentioned in the plan land in 15c / 15d /
  15e.

---

## PR #21 — Reference catalogue: suspend M4b (UK / DE) — sources not readily obtainable

Link: [vger70/MedReminder#21](https://github.com/vger70/MedReminder/pull/21)
**Status:** merged (2026-09-18)
Branch: `M4b_UK_DE_national_catalogues`

Documents the decision to **suspend M4b** (the UK MHRA and Germany
BfArM national catalogues described in
[`docs/ANALYSIS-DRUG-CATALOGUE.md`](docs/ANALYSIS-DRUG-CATALOGUE.md)
§3.5) for lack of an easily obtainable, licence-clear bulk source.
No code, snapshots, tests or localisation keys change: the shipped
country set stays at IT + EU + ES + FR (M4 baseline).

### Docs

- `docs/ANALYSIS-DRUG-CATALOGUE.md` §3.5 — M4 status table extended
  with a "Suspended" row for UK and DE, spelling out why:
  - **UK / MHRA:** the `products.mhra.gov.uk` portal does not offer
    a bulk structured export of the Products dictionary. The
    realistic alternative (NHS BSA dm+d) ships under a licence that
    is not compatible with redistribution inside the shipped binary
    without a separate agreement. Post-Brexit UK is also the only
    country in the confirmed target set that would need
    `IncludesEuCentralised = false` — the override is already
    encoded in `StaticCountryProfileProvider.NonEuCovered` and stays
    dormant.
  - **DE / BfArM:** the AMIS-öffentlich / AMIce distribution has
    changed shape multiple times, and the current portal does not
    surface a stable bulk export with a clearly declared open-data
    licence at the point of download. Reopening the milestone
    requires that both prerequisites are met simultaneously.
- `docs/CATALOGUE-DATA.md` §7 — rewritten from "not shipping yet"
  to "suspended", with the rationale mirroring the analysis note
  and pointers back to the criteria that would need to be met to
  reopen the milestone.
- No changes to `THIRD-PARTY-NOTICES.md`: nothing new is
  redistributed. No changes to the About dialog, localisation
  dictionaries, parsers, tests or embedded assets.

### Changed

- `CHANGE_LOG.md` — the PR #20 entry (M4 ES + FR) is transitioned
  from `open` to `merged (2026-09-18)` in the same commit that
  prepends this entry, per the "update the entry when the PR's
  scope changes materially, and mark it merged / closed once the
  PR resolves" rule at the top of this file. PR #20 landed on
  `main` on 2026-09-18 as commit `ead9f5d`.

---

## PR #20 — Reference catalogue: AEMPS (Spain) + BDPM (France) national catalogues (M4)

Link: [vger70/MedReminder#20](https://github.com/vger70/MedReminder/pull/20)
**Status:** merged (2026-09-18)
Branch: `M4_Additional_national_catalogues`

Implements **M4** of the drug reference catalogue described in
[`docs/ANALYSIS-DRUG-CATALOGUE.md`](docs/ANALYSIS-DRUG-CATALOGUE.md)
§3.5 — the first two additional national catalogues, Spain (AEMPS
CIMA) and France (ANSM BDPM). ES and FR are EU member states, so
the existing `IncludesEuCentralised = true` default in
`StaticCountryProfileProvider` covers them without any change:
searches from `userCountry = ES` now scope to `{ ES, EU }`, and
searches from `userCountry = FR` scope to `{ FR, EU }`.

### Added

- Two new snapshots embedded in the Infrastructure assembly:
  `src/MedReminder.Infrastructure/Assets/Catalogue/es/aemps-202609.zip`
  (2.7 MB, XLSX-in-ZIP wrapper) and
  `src/MedReminder.Infrastructure/Assets/Catalogue/fr/bdpm-202609.zip`
  (1.6 MB, three ISO-8859-15 TSVs at the archive root). Picked up
  automatically by two new `<EmbeddedResource>` globs and served
  under `MedReminder.Infrastructure.Assets.Catalogue.{es,fr}.<file>`
  — mirrors the existing IT / EU convention, no changes to
  `EmbeddedSnapshotProvider` needed.
- `AempsCimaParser` (`src/MedReminder.Infrastructure/Catalogue/Parsers/`):
  `IReferenceSnapshotParser` for country `ES`. Reads a single-XLSX
  ZIP entry (`aemps.xlsx` at the archive root) via
  `System.IO.Compression.ZipArchive` + `System.Xml.XmlReader`
  streaming — no new XLSX library dependency. Handles both the
  shared-string cell type (`t="s"`, the shape the real CIMA export
  emits) and the inline-string cell type (`t="inlineStr"`, the
  shape openpyxl-generated fixtures emit) via the same code path.
  Validates the fixed 15-column header at parse time, maps every
  row to `country = "ES"`, copies the row-level `Cód. ATC` onto
  every ingredient (pattern shared with `AifaSnapshotParser` and
  `EmaEparParser`), splits `Principios Activos` on `", "`, and
  populates `DispensingRegime` verbatim from the free-text
  `Observaciones` column. Registered next to `AifaSnapshotParser`
  in `InfrastructureServiceCollectionExtensions`.
- `AnsmBdpmParser`: `IReferenceSnapshotParser` for country `FR`.
  Reads `CIS_bdpm.txt` joined on CIS with `CIS_COMPO_bdpm.txt`
  from the ZIP archive. Encoding is **Windows-1252** — the ANSM
  portal documents it as ISO-8859-15 but the actual bytes contain
  cp1252-only 0x92 (curly single-quote `’`) used as apostrophe in
  French denominations; the code page provider is registered
  defensively on first use, requiring a new
  `System.Text.Encoding.CodePages` package reference on
  Infrastructure. **No header row** (columns are positional and
  hard-coded per the ANSM description); a shape-validation guard
  asserts the first non-short row's Statut column starts with
  `Autorisation` so a future column-order change in the ANSM
  export trips the parser instead of silently corrupting every
  row's MAH / MarketingStatus. Skips rows whose `Type de procédure
  AMM` starts with `Enreg homéo` — analogue of the `Omeopatico`
  filter in `AifaSnapshotParser`. `CIS_CIP_bdpm.txt` is kept in
  the shipped ZIP for symmetry with what ANSM publishes but is
  not consumed (the reference catalogue keys on CIS, and upstream
  CIP ships with a divergent UTF-8 encoding).
- `CatalogueRefreshHostedService.ImportOrder` extended from
  `{ IT, EU }` to `{ IT, EU, ES, FR }`. Each country still runs in
  its own transaction so a broken snapshot for one never blocks
  the others.
- Two new localisation keys `about.dataSources.aemps` and
  `about.dataSources.bdpm` in every dictionary
  (`en/it/fr/es/de`). Parity holds at 350 keys per dictionary;
  `DictionaryParityTests` stays green. `MainForm.ShowAboutDialog`
  appends both attributions below the existing AIFA and EMA EPAR
  lines, matching the four rows `THIRD-PARTY-NOTICES.md` carries.
- Curated fixtures:
  `tests/fixtures/catalogue/aemps-cima-sample.xlsx` (145 rows +
  header, stratified across Estado / multi-ingredient / brands,
  with three mandatory pins for the `Nº P. Activos`-aware split
  code path — REZAFUNGINA/NEVIRAPINA/TETRAKIS),
  `tests/fixtures/catalogue/bdpm-cis-sample.txt` (102 CIS in
  Windows-1252 — includes 9 homeopathic rows for the skip filter
  and two mandatory pins carrying the curly single-quote byte
  0x92, CELSIOR and CARMIN D'INDIGO),
  `bdpm-compo-sample.txt` (224 COMPO rows, 57 CIS with 2+
  ingredients), `bdpm-cip-sample.txt` (129 rows, kept only for
  fixture symmetry).
- `AempsCimaParserTests` (13 tests): row-count invariant on the
  fixture, ES-country invariant on every row, three-Estado
  coverage, multi-ingredient `", "` splitter guided by
  `Nº P. Activos` (mono-ingredient rows with intra-name commas
  like `REZAFUNGINA, ACETATO DE` stay intact), row-level ATC copied
  onto every ingredient, `Observaciones` on `DispensingRegime`,
  `PharmaceuticalForm` / `Dosage` / `LinkLeaflet` / `LinkSpc` all
  null, non-seekable stream. One dedicated test builds an XLSX in
  memory using `t="s"` + `sharedStrings.xml` so the code path
  used by the real AEMPS export is covered even though the
  openpyxl-generated fixture emits `t="inlineStr"`. Another
  dedicated test builds an XLSX with a leading blank row so the
  header-latch skip is exercised.
- `AnsmBdpmParserTests` (13 tests): 93 rows + 9 homeopathic
  skipped, FR-country invariant, Windows-1252 round-trip on
  accented denominations, curly single-quote (byte 0x92) preserved
  on CELSIOR / CARMIN D'INDIGO fixture pins, multi-ingredient
  join, ATC always null, MAH leading-space trim, status coverage,
  non-seekable stream. One dedicated test synthesises a broken
  BDPM row whose Statut column does not start with `Autorisation`
  and asserts the shape-validation `InvalidDataException`.
- `CsvReferenceCatalogueImporterTests` M4 additions: importing
  IT + EU + ES + FR in order populates each country row count as
  expected (168 + 70 + 145 + 93 = 476 rows, 4 distinct countries);
  a newer ES snapshot never touches FR rows at their older
  `snapshot_version`.
- `SqliteReferenceCatalogueQueryServiceTests` M4 additions:
  search from `userCountry = ES` returns rows scoped to
  `{ ES, EU }`; search from `userCountry = FR` returns rows
  scoped to `{ FR, EU }`; `ListAvailableCountriesAsync` surfaces
  all four countries after loading `IT + EU + ES + FR`.
- `StaticCountryProfileProviderTests` M4 additions: explicit
  `GetSearchScope("ES") = { ES, EU }` and
  `GetSearchScope("FR") = { FR, EU }` — the default "any country
  not explicitly listed" branch already covered them, but M4 gets
  an explicit assertion.

### Changed

- New `System.Text.Encoding.CodePages` package reference in
  `MedReminder.Infrastructure.csproj` (Microsoft, MIT). Only the
  BDPM parser needs it — every other parser stays on UTF-8.
- `THIRD-PARTY-NOTICES.md`: two new sections — AEMPS CIMA
  (Spain, reused under Ley 37/2007, attribution *"Fuente: AEMPS"*)
  and ANSM BDPM (France, reused under Licence Ouverte Etalab 2.0).
  Each section documents source URL, licence, applied filters,
  snapshot path and any deviation from the row schema.

### Docs

- `docs/CATALOGUE-DATA.md`: the "What ships" table gains ES and
  FR rows. New §5 documents the AEMPS refresh procedure (portal
  URL, XLSX-inside-ZIP layout, naming convention, drop path, boot
  log line). New §6 does the same for BDPM (portal URL,
  three-TSV-inside-ZIP layout, no-header positional columns,
  ISO-8859-15 encoding note for CIP-only, homeopathic-skip filter).
  §7 renames the former §5 "other countries" section and shrinks
  the pending list to UK + DE. Fixture-regen notes for the two new
  fixtures land in §4.3 (AEMPS) and §4.4 (BDPM), following the
  §4.2 template.
- `docs/USER_GUIDE.{en,it,fr,es,de}.md`: the "Reference catalogue
  (Italy + EU)" section is renamed to "Reference catalogue
  (multi-country)" and gains a "Spanish and French national
  catalogues" subsection. Each guide is written in its own
  language and points to Settings → General → Reference country
  for switching. The "Data sources and terms" paragraph now
  mentions AEMPS (Ley 37/2007) and ANSM (Licence Ouverte Etalab
  2.0) alongside AIFA and EMA.

### Deviations from `docs/ANALYSIS-DRUG-CATALOGUE.md`

- **One PR for ES + FR instead of one PR per country.** §3.5 M4
  says "One PR per country". This PR ships ES + FR together as
  requested. Ordering rationale (§3.5): ES and FR are the two
  most mature open-data offerings on the M4 shortlist and share
  the "EU member → `IncludesEuCentralised = true`" profile, so
  they can land in a single reviewable slice without any code
  divergence. UK (which needs the per-country EU-flag override
  documented in §12.7) and DE stay on the M4 backlog.
- **AEMPS XLSX instead of the XML "Prescripción" bundle.** AEMPS
  ships two alternative dumps of the same registry — a tabular
  XLSX (~2.7 MB) and a relational XML bundle (~16 MB compressed,
  ~200 MB decompressed). The XLSX is ~6× smaller, needs no new
  library dependency (`System.Xml.XmlReader` +
  `System.IO.Compression` from the BCL cover it), and covers every
  field the autocomplete uses. `PharmaceuticalForm` / `Dosage` /
  `LinkLeaflet` / `LinkSpc` land as null — form and dose are
  embedded in the CommercialName text (same as AIFA
  `DENOMINAZIONE`); a future increment can switch to the XML
  variant without changing the row schema.
- **BDPM CIP file present but not parsed.** `CIS_CIP_bdpm.txt`
  ships in the embedded ZIP for symmetry with what ANSM publishes,
  but the M4 parser only consumes CIS + COMPO. The reference
  catalogue keys on CIS, not on packaging-level CIP, and upstream
  CIP has a divergent UTF-8 encoding (mismatch with the ISO-8859-15
  CIS / COMPO files); ignoring CIP keeps the parser on a single
  encoding path.

---

## PR #19 — Reference catalogue: EU centralised authorisations (EPAR) (M3)

Link: [vger70/MedReminder#19](https://github.com/vger70/MedReminder/pull/19)
**Status:** merged (2026-09-18)
Branch: `M3-drug-reference-catalogue`

Implements **M3** of the drug reference catalogue described in
[`docs/ANALYSIS-DRUG-CATALOGUE.md`](docs/ANALYSIS-DRUG-CATALOGUE.md)
§3.4 — the supranational `EU` catalogue. Instead of the Article 57
dataset the analysis mentions, this ships the EMA EPAR (European
public assessment reports) dataset, which is the file that
concretely materialises the "EU-centralised authorisations" concept
the design targets. Article 57 (pan-EEA per-country
authorisations) stays out of scope; the deviation is documented in
the PR body and in `THIRD-PARTY-NOTICES.md`.

### Added

- First real EMA snapshot embedded in the Infrastructure assembly:
  `src/MedReminder.Infrastructure/Assets/Catalogue/eu/ema-epar-202609.zip`
  (2.1 MB uncompressed, 486 KB compressed). Picked up automatically
  by a new `<EmbeddedResource>` glob and served under
  `MedReminder.Infrastructure.Assets.Catalogue.eu.ema-epar-202609.zip`
  — mirrors the existing IT convention, no changes to
  `EmbeddedSnapshotProvider` needed.
- `EmaEparParser` (`src/MedReminder.Infrastructure/Catalogue/Parsers/`):
  `IReferenceSnapshotParser` for country `EU`, reads a single-CSV ZIP
  entry (`ema-epar.csv`) at the archive root, matches EPAR header
  columns case-insensitively, filters `Category != 'Human'` (395
  veterinary rows dropped from the full 2 734-row export). Every
  emitted row carries `country = 'EU'` via
  `CountryCode.Parse("European Union")` — the long form never
  reaches the DB. `NationalCode` = EMA product number
  (e.g. `EMEA/H/C/004556`), guaranteed unique per row so the
  `UNIQUE (country, national_code)` constraint stays trivially
  satisfied without any synthetic derivation. Active substances
  split on `;` only (comma stays inside a single substance
  description); the row-level ATC is copied onto every ingredient
  of the row, same convention as `AifaSnapshotParser`. Registered
  next to `AifaSnapshotParser` in
  `InfrastructureServiceCollectionExtensions`.
- Extended `CatalogueRefreshHostedService` to iterate over
  `{ IT, EU }` instead of importing IT only. Each country runs in
  its own transaction (owned by the importer); a per-country
  `try/catch` around the import ensures a broken snapshot for one
  country never blocks the other.
- Curated fixture `tests/fixtures/catalogue/ema-epar-sample.csv`
  (73 rows: 70 Human + 3 Veterinary) covering every non-veterinary
  `Medicine status` EMA emits (`Authorised`, `Withdrawn`, `Refused`,
  `Suspended`, `Lapsed`, `Application withdrawn`, `Expired`,
  `Revoked`, `Opinion`), plus multi-substance rows (DuoPlavin,
  Symtuza, Qdenga) to exercise the `;` splitter, Gardasil 9 for the
  "comma-inside-a-single-substance" case, three no-ATC rows
  (Camcevi, Myqorzo, Vafseo), and a handful of well-known brands so
  assertions stay readable.
- New `EmaEparParserTests` (11 tests) exercising the fixture:
  row-count invariants, EU-country invariant on every yielded row,
  `;` vs `,` splitter behaviour, `EMEA/H/...` uniqueness, status
  pass-through, no-ATC handling, `Medicine URL` mirrored onto both
  leaflet + SPC links, non-seekable-stream buffering.
- `CsvReferenceCatalogueImporterTests` M3 additions:
  IT-then-EU import populates both countries with no dedup
  (168 + 70 = 238 rows visible with `country IN ('IT','EU')`),
  EU-only import leaves IT rows alone, a newer EU snapshot never
  touches IT rows at their older `snapshot_version`, no row ever
  lands with the `'European Union'` long form in `country`.
- `SqliteReferenceCatalogueQueryServiceTests` M3 additions:
  cross-country search after loading the EU fixture on top of the
  Italian one — scope `{ IT, EU }` returns Symtuza (an EU-only
  medicine that never appears in the Italian fixture); scope
  `{ EU }` returns only EU rows; `ListAvailableCountriesAsync`
  surfaces both `IT` and `EU`.

### Changed

- `about.dataSources.emaArticle57` value updated in all 5
  dictionaries (`en/it/fr/es/de`) from the M2 "reserved for a
  future release" placeholder to the real EPAR attribution line.
  The **key name** is left unchanged for parity stability — it
  is an internal identifier, the user-visible text is what
  changed. Parity holds at 348 keys per dictionary;
  `DictionaryParityTests` stays green.
- `THIRD-PARTY-NOTICES.md`: replaced the EMA placeholder section
  with the real attribution — EPAR dataset, EMA legal notice
  (Commission decision 2011/833/EU on reuse of Commission
  documents), snapshot path, applied filter (`Category = 'Human'`),
  and an explicit note that EPAR ≠ Article 57.

### Docs

- `docs/CATALOGUE-DATA.md`: new §3 "Refresh procedure (EU — EMA
  EPAR)" covering the XLSX-to-CSV conversion, the ZIP layout
  (single `ema-epar.csv` at archive root), the naming convention
  (`ema-epar-<yyyymm>.zip`), the drop path and the boot
  verification. §1 table gains the EU row; §5 renames from "other
  countries" and drops EMA from the pending list. Fixture-regen
  notes for AIFA move to §4.1, EPAR gets §4.2 with the exact
  status buckets the fixture must cover.
- `docs/USER_GUIDE.{en,it,fr,es,de}.md`: "Reference catalogue
  (Italy)" section becomes "Reference catalogue (Italy + EU)"
  with a new "EU centrally authorised medicines" subsection
  explaining what EPAR is, that the autocomplete shows EU rows
  automatically when the reference country is any EU member
  (default IT), and that setting reference country to `EU`
  narrows the list to EU-only. Each guide in its own language.

### Deviations from `docs/ANALYSIS-DRUG-CATALOGUE.md`

- **EPAR instead of Article 57.** The design doc §1.3 lists "EMA
  Article 57" as the source for the `EU` country. The public
  Article 57 dump is actually the pan-EEA per-country
  authorisation register (~160 000 rows, one per (product ×
  authorisation country)) and has no explicit "centralised" flag —
  importing all of it as `country = 'EU'` would mis-label 160k
  national authorisations as supranational. EPAR is the concrete
  dataset that matches "EU-centralised medicines valid across the
  EU/EEA" (~2 700 rows, 1 568 currently Authorised), and it also
  ships ATC, MAH and the EMA product number as structured columns.
  File names, parser class and doc language use "EPAR" throughout;
  the localisation key `about.dataSources.emaArticle57` is kept
  as-is for compatibility with the M2 dictionaries.
- **Category = 'Human' filter.** Veterinary rows (395 of 2 734)
  are skipped at parse time — MedReminder targets human medicine
  reminders, and mixing veterinary products into the autocomplete
  would confuse users. Recorded as `Skipped` in `ImportReport`.

---

## PR #18 — Reference catalogue: foundations + AIFA autocomplete (Italy) (M1 + M2)

Link: [vger70/MedReminder#18](https://github.com/vger70/MedReminder/pull/18)
**Status:** merged (2026-09-18)
Branch: `claude/sleepy-turing-s6fwzy`

### M2 — Autocomplete Italy (`src/MedReminder.UI` + snapshot embedded)

#### Added

- First real AIFA snapshot embedded in the Infrastructure assembly:
  `src/MedReminder.Infrastructure/Assets/Catalogue/it/aifa-202609.zip`
  (94 MB uncompressed, ~5 MB compressed), picked up automatically by
  a `<EmbeddedResource>` glob and served under
  `MedReminder.Infrastructure.Assets.Catalogue.it.aifa-202609.zip`.
- `MedicineAutocompleteBox` WinForms control
  (`src/MedReminder.UI/Controls/`): text input + owner-drawn ListBox
  with 150 ms debounce on keystrokes, hard 20-row limit per query,
  row template `commercial_name — active_ingredient — dosage`, red
  circle badge on rows whose AIFA `STATO_AMMINISTRATIVO` contains
  `sospesa`, `ritirat` or `revocata` (case-insensitive substring —
  free-text column, no enum assumed), horizontal scrollbar with
  measured `HorizontalExtent`, dropdown anchored at the form's left
  edge and spanning the full dialog width.
- `CatalogueRefreshHostedService` (`src/MedReminder.UI/Hosting/`):
  boot-time importer that runs on a background thread only when
  `Catalogue:Enabled == true`, opens the embedded snapshot via
  `EmbeddedSnapshotProvider` and short-circuits when the recorded
  `snapshot_version` matches. Registered conditionally in
  `Program.BuildHost`.
- `MedicineEditDialog` hosts two `MedicineAutocompleteBox` instances
  — one on "Commercial name" and one on "Active ingredient".
  Picking a catalogue row on either side fills the sibling text
  field plus `Package` (from AIFA `DESCRIZIONE`), `Unit` (mapped
  from AIFA `FORMA` to a localised label via a 13-entry stem table,
  falling back to raw FORMA when nothing matches), and an extended
  commercial name of the shape `<CommercialName> <strength> <form>`
  where strength is extracted from `DESCRIZIONE` with a
  conservative regex (`mg / g / mcg / µg / ug / ml / l / ui / iu / %`).
  Free-text edits clear the reference linkage so a manually-typed
  entry saves unlinked.
- Settings dialog — General tab: new "Reference country" dropdown
  populated from `IReferenceCatalogueQueryService.ListAvailableCountriesAsync`
  plus the fixed `IT` and `EU` options. Save writes Language +
  ReferenceCountry atomically to `user.settings.json`.
- Root `THIRD-PARTY-NOTICES.md` listing the AIFA attribution
  (CC BY 4.0) and a placeholder for EMA Article 57 (M3). The two
  `about.dataSources.*` strings are surfaced in the About dialog.
- `docs/CATALOGUE-DATA.md`: monthly AIFA-refresh procedure — where
  to download, how to build the ZIP, where to drop it, how to
  regenerate the reduced fixture.
- Seven new unit keys in all five dictionaries (`Granules`,
  `Drops`, `Suppositories`, `Patches`, `Grams`, `Puffs`,
  `Ampoules`) — the medicine form's unit combo grew from 6 to 13
  options and picks up AIFA FORMA values automatically. Ten new
  M2 keys (`medicine.field.*`, `medicine.autocomplete.*`,
  `settings.referenceCountry.*`, `about.dataSources.*`). Parity
  verified across the 5 dictionaries (348 keys each).

#### Changed

- `UserSettings` gains `ReferenceCountry` (default `"IT"`).
  `user.settings.json` is now loaded with `reloadOnChange: true`
  so a country change takes effect at the next medicine-form open
  without a restart.
- `IReferenceCatalogueQueryService` gains
  `ListAvailableCountriesAsync` — one method, needed by the
  Settings dropdown.
- `AddMedicineCommand` and `UpdateMedicineCommand` gain trailing
  optional catalogue-linkage parameters (`NationalCode`,
  `AtcCode`, `LinkedReferenceMedicineId` on Add; a `CatalogueLink`
  block on Update). Existing named-arg callers stay
  source-compatible; the two use-case implementations copy the
  linkage into the Medicine entity when set.
- `CatalogueFeatureOptions.Enabled` defaults to `true` in
  `appsettings.json`, activating the feature end-to-end.
- `MedicineEditDialog` width bumped from 620 to 880 px so the
  autocomplete dropdown has enough room for long AIFA rows without
  heavy horizontal scrolling; German `Unit.Vials` translation fixed
  from "Ampullen" (semantically ambiguous) to "Fläschchen", freeing
  "Ampullen" for the new `Ampoules` key that maps AIFA "fiale".

#### Docs

- `USER_GUIDE.{en,it,fr,es,de}.md`: new "Reference catalogue
  (Italy)" section covering autocomplete behaviour, the free-text
  fallback, the reference-country setting, and the AIFA source with
  its CC BY 4.0 licence — each guide in its own language.

---

### M1 — Foundations (`src/MedReminder.Domain` + `Application` + `Infrastructure`)

#### Added

- Domain (`net10.0`): `CountryCode` value object (normalises
  `European Union` → `EU`, validates ISO 3166-1 alpha-2) and
  `AtcCode` value object (7-character WHO ATC pattern), plus the
  `ReferenceMedicine` and `ReferenceActiveIngredient` read models
  under `src/MedReminder.Domain/Catalogue/`.
- Application (`net10.0`): `IReferenceCatalogueQueryService`,
  `IReferenceCatalogueImporter`, `ImportReport`,
  `SearchCatalogueUseCase`, `LinkMedicineToReferenceUseCase`,
  `ICountryProfileProvider` / `StaticCountryProfileProvider` (the
  sole owner of the "national ∪ EU" filter — default `true`;
  explicitly `false` for `GB` / `UK` per §12 point 7) and
  `CatalogueFeatureOptions` (off by default).
- Infrastructure (`net10.0-windows`): `AifaSnapshotParser` reads
  `confezioni_fornitura.csv` joined on `CODICE_AIC` with
  `PA_confezioni.csv` inside a ZIP archive, skipping
  `TIPO_PROCEDURA = 'Omeopatico'` and `PRINCIPIO_ATTIVO = 'N.D.'`;
  every row lands with `country = 'IT'`; `dispensing_regime` /
  `link_leaflet` / `link_spc` are mapped from `FORNITURA` /
  `LINK_FI` / `LINK_RCP`; 9-digit AIC leading zeros preserved.
- `CsvReferenceCatalogueImporter` (transactional replace,
  short-circuits on same recorded `snapshot_version`),
  `SqliteReferenceCatalogueQueryService` (raw-SQL adapter hitting
  the indexed `_norm` columns), `CatalogueTextNormalizer` (shared
  lowercase + diacritics stripping) and an `EmbeddedSnapshotProvider`
  stub (M2 will ship the first real snapshot).
- DI wiring for the catalogue ports; feature flag registered off by
  default, so no runtime behaviour changes.
- Tests: 35 new Domain tests (`CountryCode`, `AtcCode`), 25 new
  Application tests (fake-port union semantics for
  `SearchCatalogueUseCase`, `StaticCountryProfileProvider`,
  `LinkMedicineToReferenceUseCase`) and five new Infrastructure
  test files (`CatalogueSchemaTests`, `AifaSnapshotParserTests`,
  `CsvReferenceCatalogueImporterTests`,
  `SqliteReferenceCatalogueQueryServiceTests`, `CatalogueFixtures`)
  exercising the M0 fixture (168 kept / 29 Omeopatico skipped,
  idempotent replay, newer-version replace, Aspirina M2M).

#### Changed

- `Medicine` gains three optional catalogue fields — `NationalCode`,
  `AtcCode`, `LinkedReferenceMedicineId` — surfaced on the entity
  and mapped in `MedicineConfiguration` for fresh DBs.
- `DatabaseInitializer.InitializeAsync` now applies the catalogue
  DDL unconditionally on every boot (additive, idempotent — no
  `EnsureCreated` shortcut for the catalogue tables per §2.4) and
  adds the three Medicine columns to pre-existing DBs via
  `AddColumnIfMissingAsync`.
- `MedReminder.Infrastructure.Tests.csproj` copies the
  `tests/fixtures/catalogue/*.csv` files as content to the test
  output directory.

#### Docs

- No changes to `docs/ANALYSIS-DRUG-CATALOGUE.md`. Four
  M1-time deviations flagged in the PR description
  (fixture uses ASPIRINA not Augmentin, port carries a pre-computed
  `countryScope`, AIFA parser copies `CODICE_ATC` onto every
  ingredient of a row, catalogue table ids stored as `TEXT` to
  match how EF stores Guids elsewhere).

---

## PR #13 — Add drug reference catalogue design analysis (with M0 findings)

Link: [vger70/MedReminder#13](https://github.com/vger70/MedReminder/pull/13)
**Status:** merged (2026-09-17)
Branch: `claude/database-principi-attivi-gl3rnw`

### Docs

- `docs/ANALYSIS-DRUG-CATALOGUE.md` (new): engineering plan for
  the future reference catalogue of medicinal products (commercial
  name ↔ active ingredient ↔ ATC), country-aware from the start.
  Covers scope, Clean-Architecture impact on the four projects,
  Domain / Application / Infrastructure additions, SQLite schema
  sketch, snapshot layout and attribution obligations for AIFA
  (CC BY 4.0) and EMA Article 57, milestones M0–M5, testing
  strategy, localisation notes for the five shipped languages
  (`de`, `en`, `es`, `fr`, `it`), risks, effort estimate and the
  seven §12 decisions with their current status.
- §3.1 M0 marked as completed. Full field mapping and
  cardinalities are recorded in the M0 comment on issue
  [#9](https://github.com/vger70/MedReminder/issues/9#issuecomment-5718752090):
  85,697 imported packages, 9,619 commercial names,
  5,750 active ingredients, 2,269 ATC codes after applying the
  two documented import filters.
- §2.4 schema: three new optional columns on `reference_medicines`
  — `dispensing_regime` (from AIFA `FORNITURA`), `link_leaflet`
  (from `LINK_FI`), `link_spc` (from `LINK_RCP`).
- §3.2 M1: documented two AIFA import filters — skip
  `TIPO_PROCEDURA = 'Omeopatico'` (74k rows, 46%) and skip
  `PRINCIPIO_ATTIVO = 'N.D.'` in `PA_confezioni` (53k rows).
- §7 Risks: replaced the speculative snapshot-size row with the
  measured baseline (gzipped snapshot ~17–22 MB, SQLite growth
  ~60–80 MB).
- §12.1 marked Resolved with the resolved dataset choice
  (`confezioni_fornitura.csv` joined with `PA_confezioni.csv`).
- Follow-up to the discovery notes in
  [#9](https://github.com/vger70/MedReminder/issues/9).
- No source code changes; no runtime behaviour changes.

### Added

- `tests/fixtures/catalogue/aifa-confezioni-sample.csv`,
  `aifa-pa-sample.csv`, `aifa-atc-sample.csv` — 198 + 253 + 76
  rows stratified from real AIFA open data. Cover `Sospesa`
  status, `Procedura Centralizzata` (EU-authorised), OTC,
  hospital-only, well-known brands (Augmentin, Aspirina,
  Tachipirina, Cardura, Eutirox, Coumadin, Zoloft, …), common
  active ingredients (paracetamolo, ibuprofene, metformina,
  olmesartan, atorvastatina, simvastatina, omeprazolo,
  amoxicillina, ramipril, bisoprololo), and 37 multi-ingredient
  combinations so the M2M table is exercised. Consumed by M1
  integration tests once M1 lands.

## PR #8 — Bump WebView2 to 1.0.4191.47; drop unused WPF reference

Link: [vger70/MedReminder#8](https://github.com/vger70/MedReminder/pull/8)
**Status:** merged (2026-09-17)
Branch: `webview2-strip-wpf-ref`

### Changed

- `Microsoft.Web.WebView2` bumped from `1.0.2792.45` to
  `1.0.4191.47`.

### Build

- New MSBuild target `RemoveUnusedWebView2Wpf` in
  `src/MedReminder.UI/MedReminder.UI.csproj`, running
  `AfterTargets="ResolveAssemblyReferences"`, removes the unused
  `Microsoft.Web.WebView2.Wpf` reference and its copy-local entry
  from the WinForms-only host. This suppresses the `MSB3277`
  warning introduced by the new package version (its WPF assembly
  requires `WindowsBase 5.0.0.0`, unified against .NET 10's
  `WindowsBase 4.0.0.0`) and removes the dead ~50 KB DLL from the
  published output. `HelpViewerForm` and the native
  `WebView2Loader.dll` under `runtimes/` are unaffected.

### Fixed

- `MSB3277` warning about conflicting `WindowsBase` versions that
  appeared on every build after the WebView2 bump.
## PR #7 — Document SmartScreen warning; add French and Spanish user guides

Link: [vger70/MedReminder#7](https://github.com/vger70/MedReminder/pull/7)
**Status:** merged (2026-09-17)
Branch: `smartscreen-advice`
(previously `claude/jolly-mccarthy-xk8lc4`; renamed after first push)

### Added

- `docs/USER_GUIDE.fr.md` and `docs/USER_GUIDE.es.md`: native user
  guides for the French and Spanish UI locales, matching the
  content of `USER_GUIDE.en.md`. `HelpViewerForm` already resolves
  `USER_GUIDE.<lang>.md` dynamically — only the csproj wiring
  changed (`Content` + `EmbeddedResource`).

### Docs

- `README.md`: new "Windows SmartScreen warning on first run"
  section after the Download block, explaining the SmartScreen
  dialog and UAC "Unknown Publisher" prompt triggered by the
  intentionally unsigned release, with the two-click bypass steps.
- `docs/USER_GUIDE.en.md`, `docs/USER_GUIDE.it.md`,
  `docs/USER_GUIDE.fr.md`, `docs/USER_GUIDE.es.md`: matching
  "SmartScreen on first launch" subsection under *First start* /
  *Primo avvio* / *Premier démarrage* / *Primer inicio*, using the
  localized Windows dialog labels for each language.
- `README.md`: drop the obsolete "guide localized in EN and IT
  only" known limitation now that all four shipped languages have
  a native guide.
- `CLAUDE.md`: extend the English-only exceptions and the
  repository layout section to list all four shipped user guides.

### Build

- `src/MedReminder.UI/MedReminder.UI.csproj`: register the two new
  guides as `Content` (copied to `bin/localization/`) and
  `EmbeddedResource` (single-file publish fallback). Comment on
  the block updated to reflect the four supported languages.

---

## PR #5 — Add CLAUDE.md, English-only docs, strip .pdb/.xml in Release

Link: [vger70/MedReminder#5](https://github.com/vger70/MedReminder/pull/5)
**Status:** closed (not merged)
Branch: `claude/translate-in-english`
(previously `claude/compassionate-pasteur-qmmt3h`; renamed after
opening)

### Added

- `CLAUDE.md` at the repository root, with repository conventions,
  the English-only language policy (exception: `USER_GUIDE.it.md`
  and the JSON translation dictionaries under
  `assets/localization/`), build / test / publish commands, branch
  policy, data locations and pointers to CHANGE_LOG.md maintenance.
- `CHANGE_LOG.md` (this file) with per-PR entries and maintenance
  rules.

### Changed

- `docs/ANALYSIS.md`, `docs/ANALYSIS-MULTI-USER.md` and
  `packaging/msix/Assets/README.md` translated from Italian to
  English. `USER_GUIDE.en.md` and `USER_GUIDE.it.md` intentionally
  left as-is — they are shipped to the end user in each locale.
- Every Italian comment, log message, exception message and
  hardcoded fallback string in the C# source (Domain, Application,
  Infrastructure, UI and test projects) translated to English.
  Identifiers and public APIs are untouched; hardcoded fallback
  strings in `NotificationTexts` and `TherapyReport` now default to
  English (matching the app's default locale) — used only when no
  `ILocalizationService` is registered.
- Italian comments in the ancillary config files translated to
  English: `.csproj`, `.pubxml` publish profiles, WiX `Product.wxs`
  and `MedReminder.wixproj`, MSIX `Package.appxmanifest`,
  `MedReminder.mapping.txt` and `priconfig.xml`, packaging scripts
  (`build-installer.ps1`, `make-selfsigned-cert.ps1`,
  `sign-artifact.ps1`), the GitHub Actions workflow, WiX
  `License.rtf` and `assets/build/generate_icon.py`.

### Build

- New MSBuild target `StripReleaseDebugArtifacts` in
  `Directory.Build.props` that runs after `Build` and `Publish` when
  `$(Configuration) == Release`, deleting `*.pdb` and `*.xml` from
  `$(OutputPath)` and `$(PublishDir)`. Test projects are excluded.
  This keeps the shipped ZIP / MSI / MSIX free of debug symbols and
  documentation dumps without changing the CI workflow (which already
  builds `-c Release`).
- The GitHub Actions release workflow
  (`.github/workflows/dotnet-desktop.yml`) now also builds the WiX
  MSI installer from the same self-contained publish output and
  attaches `MedReminder-win-x64.msi` alongside the existing ZIP to
  every GitHub Release. The MSI version tracks the pushed `vX.Y.Z`
  tag. Italian strings introduced by the MSI step
  (`throw` messages) are translated to English inline with the
  rest of the workflow.

### Docs

- `README.md` license section updated from "MIT" to "Apache 2.0"
  (matching the `LICENSE` file).
