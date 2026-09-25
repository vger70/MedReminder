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
**Status:** open

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
**Status:** open — feature-complete (all implementation steps 1–10 of
`ANALYSIS-C3-EXPORT-IMPORT.md` §13 done), awaiting review and the
pre-merge manual QA checklist in the PR description.

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
**Status:** open

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
**Status:** open

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
**Status:** merged [2026-09-22]

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
**Status:** merged [2026-09-21]

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
**Status:** merged [2026-09-21]

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
