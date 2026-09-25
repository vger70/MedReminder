# ANALYSIS — C.3+: Backup to a user-controlled cloud folder + explicit restore

Design document, **prior** to implementation. Once approved, work
proceeds on branch `feature/cloud-folder-backup` (per
`CLAUDE.md` §5). Corresponds to `EVOLUTION.md` §5 (Group C, item
C.3+). Follows the structure of `ANALYSIS-C3-EXPORT-IMPORT.md`.

> **This is not a speculative analysis.** Every decision is
> technically motivated and delimits what will be written in code.
> The "Decisions still to confirm" section at the end is the only
> zone of ambiguity that needs input.

Epistemic classification, aligned with the sibling documents:
`[VERIFIED]` (checked against the current tree),
`[VERIFIED against ANALYSIS.md]` (specified there, tree check
still applies), `[INFERRED]` (deduction from verified facts),
`[UNCERTAIN]` (hypothesis pending confirmation).

**Hard precondition: C.3 must have shipped.** C.3+ reuses C.3's
encrypted archive format as its snapshot payload. If C.3 has not
landed at the time work on this item starts, do C.3 first
(`EVOLUTION.md` §5.1, `ANALYSIS-C3` §9).

---

## 1. Scope

### 1.1 Problem

The existing automatic-backup path
(`AutomaticBackupHostedService` + `BackupService`) `[VERIFIED]`
already writes daily DB snapshots to a **local** folder configured
in `backup.settings.json`. It solves "yesterday I broke something,
give me back the DB". It does **not** solve two adjacent problems:

- **Multi-device operation without a backend.** A user with a PC
  at home and a PC at work today has no non-technical path to
  keep the two in step. `EVOLUTION.md` §5 frames C.3+ as *the*
  honest, low-cost approximation of that use case — assisted
  migration, not sync.
- **Off-machine durability.** Local backups on the same disk as
  the live DB do not survive disk failure. A cloud-synced folder
  makes the backup off-machine without the app becoming a
  service.

C.3+ closes both gaps by letting the user point the automatic
backup at **any local folder**, including a folder that is
already synchronized by OneDrive / iCloud Drive / Dropbox / Google
Drive Desktop / equivalent. The app writes atomic **encrypted**
snapshots into that folder; on a second device, an explicit
**Restore from cloud folder** command reads the most recent
snapshot and applies it in Overwrite mode.

### 1.2 Goal

Extend the automatic-backup destination to accept a
cloud-synced local folder, using **C.3's encrypted archive
format** (`.mrz`, `ANALYSIS-C3` §3.1) as the snapshot payload
rather than the raw `.db` file used today. Add an explicit
**Restore from cloud folder** command that:

- lists the recent snapshots in the configured folder;
- decrypts the chosen one with the user's C.3 passphrase;
- applies it to the current profile via the C.3 import service.

The user model is **single-writer, multiple-reader-on-demand**
(`EVOLUTION.md` §5.2): the user consciously switches which device
is "active". Two devices editing between two backups diverge
silently, and the last restore wins. The UI copy has to make
this explicit — this is **not sync**.

### 1.3 What C.3+ is NOT

- **Not sync.** No conflict resolution, no vector clocks, no
  operational log, no server. Two-device concurrent editing is
  unsupported by design; the user picks who writes.
- **No new transport.** The app never talks to OneDrive, iCloud,
  Dropbox, or Google Drive's API. It writes to a local folder;
  the user's OS-level sync agent handles the upload.
- **No plaintext DB in a synced folder — ever.** `EVOLUTION.md`
  §5.4 is explicit: "Never write a plaintext DB to a
  cloud-synced folder." The C.3 archive format is the mandatory
  wrapper.
- **No live-DB file-sync.** Placing the live SQLite DB in a
  cloud-synced folder is **rejected** on technical grounds and
  covered by `EVOLUTION.md` §7.1 (C.2 rejection). C.3+ writes
  atomic snapshots, not the live DB.
- **No cloud-folder detection.** `EVOLUTION.md` §5.4 warns
  against guessing where OneDrive / iCloud lives; the user
  picks a folder with an ordinary file browser.
- **No automatic restore.** Restore is always explicit. Opening
  the app on a second device does not silently overwrite the
  local profile with the latest cloud snapshot.
- **No mobile client integration in this item.** A phone
  companion consuming cloud-folder snapshots is B.1
  (`EVOLUTION.md` §6), a separate track.
- **No overwrite of the raw-DB backup path.** `BackupService`'s
  existing `medreminder-<profileId>-YYYYMMDD-HHmmss.db` naming
  and pruning logic `[VERIFIED]` continues to work for
  local-only backups. C.3+ **coexists** with it — see §3.3 for
  the two-target model.
- **Not a medical-device concern.** Same posture as C.3.

---

## 2. Preconditions — what already exists

- **C.3 archive format.** `ANALYSIS-C3` §3.1 defines the `.mrz`
  container (ZIP with cleartext manifest + AES-GCM payload +
  nonce). C.3+ reuses it verbatim; the manifest's `scope` remains
  `"profile"` (or `"all-profiles"` when admin variant lands).
- **C.3 export / import services.** `IExportService` /
  `IImportService` in `MedReminder.Application.Export`
  (introduced by C.3). C.3+ calls into these — it does not
  re-implement archive production or consumption.
- **Automatic-backup host.**
  `src/MedReminder.UI/Hosting/AutomaticBackupHostedService.cs`
  runs on a daily schedule driven by `BackupSettings`
  (`Enabled`, `Directory`, `PreferredTime`, `RetentionDays`)
  `[VERIFIED]`. C.3+ extends it: same schedule, same retention
  discipline, different output (archive instead of raw DB) when
  a new "cloud target" flag is set.
- **Backup state store.** `BackupStateStore` /
  `backup.state.json` holds last-tick state so the host can
  survive a restart without re-running yesterday's backup
  `[VERIFIED]`. C.3+ inherits this.
- **`BackupService`.** Retention pruning
  (`PruneOldBackupsAsync`) uses a filename regex to group by
  `profileId` `[VERIFIED]`. C.3+ needs a **second regex** for
  the `.mrz` naming pattern (§3.3).
- **Passphrase discipline (C.3).** The user's C.3 passphrase is
  never persisted (`ANALYSIS-C3` §4.5). C.3+ has an interesting
  problem here: an *automatic* daily export needs a passphrase
  the user is not there to type. §3.4 is the whole subsection on
  how this is handled.

---

## 3. Data model

### 3.1 `BackupSettings` extension

Add three fields to
`MedReminder.Application.Abstractions.BackupSettings` (POCO,
persisted to `backup.settings.json`). All backwards-compatible
JSON additions — an older file omitting them deserializes to the
defaults.

```csharp
// New in C.3+
public bool CloudFolderEnabled { get; set; } = false;

// Second target directory, distinct from Directory. Empty when
// CloudFolderEnabled is false.
public string CloudFolderDirectory { get; set; } = string.Empty;

// Number of encrypted snapshots kept in the cloud folder. 0 =
// unlimited (not recommended). Independent from RetentionDays,
// which governs the local raw-DB backups.
public int CloudFolderRetention { get; set; } = 30;
```

`Enabled` still governs the **local raw-DB backup**;
`CloudFolderEnabled` is an **independent** toggle for the
encrypted cloud snapshot. A user may enable one, both, or
neither.

### 3.2 No SQLite schema change

C.3+ writes files (archives), reads files, and extends a JSON
POCO. No `DatabaseInitializer` patch, no `ALTER TABLE`, no
`EnsureCreated` concern (`CLAUDE.md` §9).

### 3.3 File naming in the cloud folder

`BackupService.ExportProfileAsync` uses
`medreminder-<profileId>-YYYYMMDD-HHmmss.db` `[VERIFIED]`. The
C.3+ counterpart uses the C.3 extension:

```
medreminder-<profileId>-YYYYMMDD-HHmmss.mrz
```

A new regex sibling to `BackupService.BackupFileRegex` recognises
the `.mrz` shape; retention pruning groups by `profileId` exactly
as today (`EVOLUTION.md` §5.2 — same pattern).

Locating the two file types in two different folders (local vs.
cloud) is not strictly required; users may configure the same
path for both. The naming schema keeps them disambiguated by
extension so pruning does not accidentally cross the streams.

### 3.4 Passphrase for the automatic export — the real problem

C.3's threat model treats the passphrase as user-typed each
time. C.3+ runs unattended. Three viable models; the analysis
recommends **Model C**.

| Model | Where the passphrase lives | Trade-off | Verdict |
|---|---|---|---|
| **A — Type per snapshot** | UI prompt the user every day at the scheduled time | Defeats the "automatic" adjective; punctuality collapses when the user is away | ❌ Rejected |
| **B — DPAPI-cache the passphrase** | Encrypted with DPAPI in a new `cloud-backup.protected` next to `smtp.protected` | Simple; **but** the archive key can now be recovered by anyone with the local Windows account. That is exactly the failure mode DPAPI defeats for `smtp.protected` (a locally-authorized password), and it makes sense there because SMTP auth is a means to a service, not a snapshot of medical data. Applying DPAPI to the archive key is defensible for a low-value personal use, but it substantially weakens the "the cloud archive is only readable with the passphrase" property that made C.3 safe to drop in a synced folder | ⚠ Defensible but weakens C.3 |
| **C — Separate long-lived archive key derived from a distinct backup passphrase** | User configures a **backup passphrase** (once) in the C.3+ settings dialog; the derived key is held in **memory** for the running process and is re-derived on each restart from the DPAPI-cached passphrase; the passphrase itself is stored DPAPI-encrypted in `cloud-backup.protected` | Same DPAPI weakening as B, but with a **distinct** passphrase (not the C.3 export passphrase), so a compromise of the automatic-backup passphrase does not compromise the user's chosen C.3 archives, and the user can rotate it without affecting existing C.3 archives; also isolates blast radius per feature | ✅ Recommended |

**Decision.** Model C. The cloud backup uses a **backup
passphrase**, separate from the C.3 export passphrase (which the
user types by hand). The backup passphrase is DPAPI-encrypted in
`cloud-backup.protected` under `%LOCALAPPDATA%\MedReminder\`
(shared, admin-managed — same layout tier as `smtp.protected`).
On each device the user wants to receive snapshots on, the
**same** backup passphrase must be entered once — that is the
irreducible "the second device is *my* device" step.

The security posture is honestly stated in the user guide and
in the Restore dialog: an attacker with local Windows-account
access to the device that produced the backups can read them.
An attacker with only the cloud folder's contents cannot. The
threat model that C.3+ meaningfully defeats is exactly the one
`EVOLUTION.md` §5 targets: an ambient leak of the cloud provider
or a shared PC in the household.

### 3.5 Shared / admin-managed file `cloud-backup.protected`

Mirrors `smtp.protected`:

- Location: `%LOCALAPPDATA%\MedReminder\cloud-backup.protected`.
- Content: DPAPI-encrypted (CurrentUser scope) bytes of the
  backup passphrase.
- Never leaves the machine, never logged.
- Ignored by `.gitignore` (the pattern already covers
  `*.protected` — verify).

### 3.6 Manifest additions

C.3's `manifest.json` (`ANALYSIS-C3` §3.1) is already
self-describing; C.3+ adds two optional metadata fields:

```json
{
  "source": "automatic",           // or "user"
  "device": {
    "hostName": "SHA-256:<hex>",   // hashed, NOT the plain host name
    "profileId": "…"
  }
}
```

`source` distinguishes an automatic C.3+ snapshot from a
user-triggered C.3 export — useful for retention rules and for
UI grouping in the Restore dialog. `device.hostName` is stored
**hashed** (SHA-256 of the raw name) so a stolen archive does
not leak the source machine's hostname; the hash is enough to
group snapshots by originating device in the Restore UI. The
plain host name is never written to the archive or to the log.
`[INFERRED]`

Both fields are additive; readers of an older `formatVersion`
ignore them (`ANALYSIS-C3` §4.3).

---

## 4. Runtime

### 4.1 Automatic export tick

Extend `AutomaticBackupHostedService` (the existing daily-tick
service) to run **both** targets when they are enabled:

```
On scheduled tick:
  if BackupSettings.Enabled:
    BackupService.ExportProfileAsync(currentProfileId, Directory, …)
    BackupService.PruneOldBackupsAsync(Directory, RetentionDays, …)

  if BackupSettings.CloudFolderEnabled:
    passphrase = CloudBackupPassphraseStore.GetOrThrow()   // DPAPI
    archivePath = ExportService.ExportAsync(
        options = { Scope = Profile, IncludeShared* = false, Source = "automatic" },
        passphrase,
        progress = null,
        ct)
    Move(archivePath, CloudFolderDirectory / "medreminder-<id>-<ts>.mrz")
    PruneCloudFolderAsync(CloudFolderDirectory, CloudFolderRetention, ct)
```

Design points:

- **Every profile (2026-09-25).** The cloud target now exports one
  `.mrz` per registered profile, like the raw-DB target, through
  `ExportOptions.ProfileId`. All archives use the admin's
  DPAPI-cached backup passphrase, so whoever knows it can read every
  profile, PIN-protected ones included; the user guides say so. A
  failure on one profile is recorded (`cloud <profileId>: …`) and does
  not stop the others. Restore still overwrites the active profile:
  the dialog preselects the active profile's newest snapshot and
  asks for confirmation when another profile's archive is chosen.
- **Two targets run independently.** A failure on the raw-DB
  target must not skip the cloud target and vice versa. Wrap
  each call in its own `try/catch`, log warnings, continue.
- **Atomic write.** The C.3 archive is produced in a temp file
  under `Path.GetTempPath()` first, then **moved** into the
  cloud folder. This is important: sync agents watch the folder
  and start uploading the moment a file appears. A partial write
  in place could produce a truncated upload of a torn file. The
  atomic-move discipline matches `ExportService`'s existing
  temp-then-write pattern.
- **`IncludeShared* = false` by default.** An automatic cloud
  snapshot must not carry the SMTP password around. The user
  can trigger a one-shot C.3 export from Settings when they
  want to seed a second device with SMTP settings; the daily
  automatic snapshot deliberately does not.
- **No progress bar.** The service runs silently; failures land
  in the daily rolling log.

### 4.2 No new hosted service

C.3+ **extends** `AutomaticBackupHostedService`; it does not add
a second timer. A second `PeriodicTimer` would race the first and
produce out-of-order snapshots when both targets are enabled.

### 4.3 Cloud folder validation

At save time in the settings dialog and at each tick:

- **Folder must exist and be writable.** If missing, log a
  warning, skip the tick, keep going.
- **Do not create the folder if it is missing** — an
  unintentionally missing folder is more likely to be a sync
  agent quirk (the folder was moved) than a user mistake. Failing
  fast preserves the invariant "we only write where the user
  said".
- **Never write to a network path directly.** The user's chosen
  folder must resolve to a local file system path (a UNC path
  or a mapped drive resolves to a local file system too, so this
  is a UI copy issue more than a code check).
- **Do not verify that the folder is a "cloud-synced" folder.**
  The app cannot reliably detect that (`EVOLUTION.md` §5.4).
  The user's choice is final.

### 4.4 Restore path

`ICloudRestoreService.RestoreAsync(archivePath, passphrase, ct)`
in `MedReminder.Application.Export` (adjacent to
`IImportService`):

```
1. Take the caller-provided archive path (from the Restore
   dialog's picker).
2. Delegate to IImportService.ImportAsync with the same
   overwrite semantics as C.3 (§4.2 of ANALYSIS-C3).
3. On success, surface the C.3 restart prompt.
```

The Restore dialog (§5.2) enumerates the archives in the cloud
folder for convenience, but the actual restore step reuses C.3's
`IImportService` verbatim. There is no C.3+-specific import
code — C.3+ is a *scheduler + UI* layer over C.3's format and
services.

The dialog accepts the **backup passphrase** (from
`cloud-backup.protected` when present on this device) OR a
user-typed passphrase (when restoring on a fresh device that has
not seen the automatic backup passphrase yet — the user typed it
into the first device once). Both paths converge on
`IImportService.ImportAsync`.

### 4.5 Retention pruning in the cloud folder

`BackupService.PruneOldBackupsAsync` currently applies to
`medreminder-<id>-<ts>.db` files via `BackupFileRegex`
`[VERIFIED]`. C.3+ adds a sibling regex and a sibling
`PruneCloudFolderAsync` (or teaches the existing method a
second pattern — an implementation choice, both are fine).
Group by `profileId` exactly as today: the most recent
`.mrz` of profile A does not shield old `.mrz` files of profile B.

**Retention interaction with the cloud provider's own recycle
bin.** OneDrive keeps deleted files for 30 days by default;
iCloud and Dropbox do similar. C.3+'s retention only removes
files from the visible folder — it does not purge the provider's
recycle bin. This is a documented user-facing note, not a
technical concern.

### 4.6 Failure surfaces

- **Cloud folder missing / not writable.** Warning-log,
  skip-tick, keep the app running. Do not queue and retry — the
  daily tick will pick it up next time.
- **Passphrase unavailable on this device.** If
  `cloud-backup.protected` is missing / DPAPI decryption fails,
  the tick logs a warning and does nothing. The user is prompted
  the next time they open the Cloud Backup settings dialog to
  re-enter the passphrase.
- **Export succeeds but move fails.** The temp file is deleted;
  a warning is logged; next tick tries again. Do not leave an
  orphaned `.mrz` in the temp directory.
- **Restore failures.** Inherited from C.3 (`ANALYSIS-C3` §4.4):
  wrong passphrase, corrupt archive, unsupported version — each
  surfaces its own localized message.

### 4.7 Delivery behind `IArchiveStorage` (C.3++ Phase 1)

The inline `File.Move` of §4.1 and the folder enumeration of §4.5
are now behind the `IArchiveStorage` port
(`ANALYSIS-C3PP-CLOUD-PROVIDERS` §7). The host still exports to a
temp `.mrz`, then calls `IArchiveStorage.UploadAsync`;
`PruneCloudFolderAsync` goes through `ListAsync` + `DeleteAsync`.
The only implementation, `LocalFolderArchiveStorage`, keeps the
temp-then-move discipline of §4.1, reads `CloudFolderDirectory`
on every call, and signals a missing folder with
`DirectoryNotFoundException`, which the host logs and skips as
§4.3 / §4.6 require. The host still checks that the folder exists
before exporting: a missing folder leaves the day open, so without
the check every 15-minute tick would run an Argon2id export only
for the upload to reject it. The `DirectoryNotFoundException` path
covers a folder that disappears during the export. User-visible
behaviour is unchanged.

---

## 5. UI

### 5.1 Cloud Backup settings section

Extend `SettingsDialog` (the existing writer of
`backup.settings.json`) with a new grouped section under the
existing Backup section:

- Checkbox `Ui.SettingsDialog.CloudBackup.Enabled`.
- Folder picker (`FolderBrowserDialog`) bound to
  `CloudFolderDirectory`.
- Retention numeric-updown bound to `CloudFolderRetention`.
- **Backup passphrase** field with a "Change…" button. The
  passphrase, once set, is not re-displayed. Changing it
  re-encrypts `cloud-backup.protected` in place.
- A short helper block that names the trade-off explicitly:
  "Snapshots in this folder are encrypted with a passphrase you
  choose. Losing the passphrase means losing the ability to
  restore. This is not real-time sync — two devices editing
  between two backups will diverge."

Enable / disable behaviour:

- The cloud section is enabled independently from the
  raw-DB backup section (§3.1).
- The Change-passphrase button is enabled only when
  `CloudFolderEnabled` is on.
- Saving requires a non-empty backup passphrase when
  `CloudFolderEnabled` is on and no `cloud-backup.protected`
  exists yet.

### 5.2 Restore from cloud folder dialog

A new dialog under Settings → File → **Restore from cloud folder**
(new `Ui.SettingsDialog.File.RestoreFromCloud` key):

- Cloud folder picker (defaults to
  `CloudFolderDirectory`; may be pointed at any folder that
  contains `.mrz` snapshots).
- List of snapshots (from filename metadata + `manifest.json`
  read for `createdAtUtc`, `device.hostName` hash, `profileId`).
  Sortable by date. `device.hostName` shows as
  "Device: `<first 12 hex of the hash>`" — enough to group
  snapshots by originating machine, not enough to identify it.
- Passphrase field. Auto-populated from `cloud-backup.protected`
  when present on this device (checkbox: "Use this device's
  saved backup passphrase" — off when a different passphrase is
  needed).
- Mandatory "I understand this will overwrite the current
  profile's data" confirmation checkbox (`ANALYSIS-C3` §5.3
  pattern).
- Progress bar during the run.
- Restart prompt on success (§4.4 / `ANALYSIS-C3` §4.2 step 11).

### 5.3 Discoverability

Both extensions live in `SettingsDialog` — same place as
`SettingsDialog.File.ExportData` / `ImportData` from C.3, next to
the existing Backup section. No new top-level menu, no startup
prompt, no wizard.

---

## 6. Localization

New keys added to **every** dictionary under
`assets/localization/` (`en`, `it`, `fr`, `es`, `de`) per
`CLAUDE.md` §8. `DictionaryParityTests` fails the build on any
missing key. Proposed keys (final names to be aligned with
existing conventions):

Settings section:

- `Ui.SettingsDialog.CloudBackup.Section.Title`
- `Ui.SettingsDialog.CloudBackup.Enabled`
- `Ui.SettingsDialog.CloudBackup.Directory.Label`
- `Ui.SettingsDialog.CloudBackup.Retention.Label`
- `Ui.SettingsDialog.CloudBackup.Passphrase.Label`
- `Ui.SettingsDialog.CloudBackup.Passphrase.Change`
- `Ui.SettingsDialog.CloudBackup.Warning.NotSync`
- `Ui.SettingsDialog.CloudBackup.Warning.LostPassphrase`

Restore dialog:

- `Ui.SettingsDialog.File.RestoreFromCloud`
- `Ui.RestoreCloudDialog.Title`
- `Ui.RestoreCloudDialog.Folder.Label`
- `Ui.RestoreCloudDialog.Snapshot.Column.Date`
- `Ui.RestoreCloudDialog.Snapshot.Column.Device`
- `Ui.RestoreCloudDialog.Snapshot.Column.Profile`
- `Ui.RestoreCloudDialog.Passphrase.UseSaved`
- `Ui.RestoreCloudDialog.Confirm.Overwrite`

Error messages (reuse C.3's error keys where the surface is the
same — wrong passphrase, corrupt archive, unsupported version).
Add:

- `Ui.CloudBackup.Error.FolderMissing`
- `Ui.CloudBackup.Error.PassphraseMissing`

Following the A1 / A5 precedent, the four non-English
dictionaries may ship with `TODO(<lang>): <english fallback>`
placeholders; Italian wording requires maintainer sign-off.

Shipped user guides (`USER_GUIDE.*.md`) get a "Cloud folder
backup" section documenting:

- The setup on device #1 (pick the folder, set the backup
  passphrase).
- The setup on device #2 (install MedReminder, open Restore from
  cloud folder, enter the same backup passphrase).
- The "not sync" honest disclaimer (single-writer discipline).
- The passphrase discipline (no recovery, mirrors C.3).

English ships with the PR; the four localized guides may follow,
mirroring A1 / A5.

---

## 7. Tests

### 7.1 `MedReminder.Application.Tests`

- **Automatic tick with cloud target enabled.** Simulated tick
  produces exactly one `.mrz` file in the target folder; its
  manifest carries `source = "automatic"` and a hashed
  `device.hostName`.
- **Both targets enabled.** Simulated tick produces one `.db` in
  `Directory` AND one `.mrz` in `CloudFolderDirectory`.
- **Failure isolation.** With `CloudFolderDirectory` set to a
  read-only path, the raw-DB target still runs; and vice versa.
- **Passphrase missing.** With `CloudFolderEnabled = true` but
  no `cloud-backup.protected`, the tick logs a warning and does
  not produce a `.mrz`.
- **Retention pruning.** Files older than
  `CloudFolderRetention` days are removed; recent files survive;
  the pruning does not touch `.db` files (different regex).
- **Atomic move.** With a fault-injecting file system, a partial
  write leaves nothing in the target folder (the temp file is
  cleaned up).
- **Restore via `IImportService`.** Round-trip
  `ExportService.ExportAsync` → move into the cloud folder →
  `IImportService.ImportAsync` on a second in-memory DB → row
  equality.
- **Hashed hostname.** Two ticks on the same machine produce the
  same hash; two ticks on a stubbed different machine produce a
  different hash.

### 7.2 `MedReminder.Infrastructure.Tests`

- **`cloud-backup.protected` round-trip.** Write / read via
  DPAPI (CurrentUser); tampered blob throws on decrypt.
- **`BackupSettings` deserialization.** JSON with and without
  the new fields binds correctly (defaults when absent).
- **Regex disambiguation.** `medreminder-<id>-<ts>.db` and
  `medreminder-<id>-<ts>.mrz` match the intended regex and only
  that one.

### 7.3 UI tests / smoke checks

- **Cloud section flows.** Enable, set folder, set passphrase,
  save. Reload settings and verify persistence.
- **Restore dialog flows.** Happy path, wrong passphrase, no
  snapshots in folder (list empty), corrupt file selected.
- **Retention pruning is user-visible.** Setting retention to
  N and letting the tick run more than N times produces at most
  N files.

---

## 8. Retro-compatibility

- **On-disk.** Existing `backup.settings.json` files without
  the new fields deserialize to the defaults (cloud target
  disabled). Existing installs are unaffected until the user
  opts in.
- **`AutomaticBackupHostedService`.** The extension keeps the
  existing raw-DB path working when only that path is enabled.
- **C.3 archives.** C.3+'s cloud snapshots and C.3's
  user-triggered exports use the same format. A user may import
  a cloud snapshot with the C.3 Import dialog, and vice versa,
  provided they know the matching passphrase.
- **Revert-safety.** A later build that reverts C.3+ ignores
  `CloudFolder*` fields; the JSON file stays valid. Same posture
  as A5 (`ANALYSIS-A5` §9) and C.3 (`ANALYSIS-C3` §9).

---

## 9. Risks and mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| Users treat C.3+ as real-time sync and lose edits on the passive device | High (user-perceived) | Explicit "not sync" copy in Settings and in the Restore dialog (§5.1, §5.2); User Guide restates it. `EVOLUTION.md` §5.4 is the source of this warning |
| Passphrase loss = data loss | High (user-perceived) | Explicit warning at setup and in the Restore dialog; documented in the user guide; passphrase never leaves the machine (§3.4) |
| DPAPI-cached passphrase weakens C.3's "only the passphrase decrypts" property on the source machine | Medium | Threat model honestly stated (§3.4); a distinct backup passphrase isolates blast radius from C.3 user archives |
| Partial write to the cloud folder produces a torn upload | Medium | Temp-then-move discipline (§4.1); the sync agent only sees the finished file |
| Retention pruning removes a file the sync agent has not uploaded yet | Low | Provider-side recycle bin (§4.5) covers the common case; documented in the user guide |
| Live SQLite DB accidentally placed in the cloud folder | High (data corruption) | The Cloud Backup section writes only `.mrz`; the raw-DB backup section writes only `.db`; the two sections have separate directories. `EVOLUTION.md` §7.1 covers why the live DB must never be there — inherited by copy in the user guide |
| Automatic hourly cloud writes stress the sync agent | Low | Default cadence stays at the daily tick already in use; there is no new "more frequent" mode in C.3+ |
| Two devices with different profile ids restore each other's snapshots | Medium | The Restore dialog surfaces the archive's `profileId`; explicit "overwrite" confirmation copy names the target profile |
| Hostname leak via `device.hostName` | Low | Hashed at write time (§3.6); the plain host name is never written |

---

## 10. Non-goals recap

- Not sync, not P2P, not conflict resolution.
- No cloud API usage — the app writes to a local folder only.
- No plaintext DB in a synced folder.
- No live-DB file-sync (`EVOLUTION.md` §7.1 rejection stands).
- No automatic restore.
- No new transport, no new NuGet outside of what C.3 already
  brings in.
- Not a hosted service beyond extending
  `AutomaticBackupHostedService`.
- Not a medical-device concern.

---

## 11. Decisions still to confirm

1. **Passphrase model** (§3.4). Recommendation: **Model C**
   (separate backup passphrase, DPAPI-cached). Confirm this over
   Model B (reuse the C.3 passphrase) or Model A (prompt every
   time).
2. **`IncludeShared*` in automatic snapshots** (§4.1). The
   analysis picks `false` by default (no SMTP password, no user
   preferences in the daily automatic archive). Confirm — or opt
   into `IncludeShared* = true` at the cost of stronger
   passphrase discipline.
3. **Retention default** (§3.1). `CloudFolderRetention = 30`
   mirrors the local backup default. Confirm.
4. **Cadence** (§4.1). Reuse the existing daily tick. Confirm
   no faster cadence is wanted in the first cut.
5. **Localized user guides.** English section ships with the PR;
   the four localized guides may follow, mirroring A1 / A5.
   Confirm this is acceptable.
6. **Hostname hashing** (§3.6). Include a hashed `device.hostName`
   in the manifest, or omit it entirely? Recommendation: hashed;
   confirm.
7. **`.mrz` retention interacts with cloud recycle bins** (§4.5).
   Document the behaviour in the user guide, do not attempt to
   purge the provider's recycle bin. Confirm.

---

## 12. Implementation plan

One PR on `feature/cloud-folder-backup`. Per `CLAUDE.md` §5, the
PR is opened **after the first commit**, and a `CHANGE_LOG.md`
entry is prepended when the PR opens. Indicative commit order:

1. **Application.** Extend `BackupSettings` with `CloudFolder*`
   fields (§3.1). Unit test deserialization with and without
   the new keys.
2. **Application.** New port
   `ICloudBackupPassphraseStore` (get / set / clear); UI /
   Infrastructure implement it. Unit tests for
   `ICloudRestoreService` (delegating to `IImportService`).
3. **Infrastructure.** `DpapiCloudBackupPassphraseStore` backed
   by `cloud-backup.protected` under
   `%LOCALAPPDATA%\MedReminder\`. Round-trip test; tampering
   test.
4. **UI / Application.** Extend `AutomaticBackupHostedService`
   (§4.1) to run the cloud target when
   `BackupSettings.CloudFolderEnabled` is on. Add a
   fault-injecting integration test to prove failure isolation
   between the two targets.
5. **Infrastructure.** `PruneCloudFolderAsync` (or teach
   `PruneOldBackupsAsync` a second regex — pick one and stay
   consistent). Tests.
6. **UI.** Cloud Backup section in `SettingsDialog` (§5.1).
   `RestoreFromCloudDialog` (§5.2). Both dialogs use
   `Progress<int>` and `CancellationTokenSource`.
7. **Application.** Emit `source = "automatic"` and hashed
   `device.hostName` into the manifest on the automatic path
   (§3.6). Corresponding tests.
8. **Localization.** Keys in all five dictionaries. Italian
   awaits sign-off per §6.
9. **Docs.** `CHANGE_LOG.md` entry when the PR opens; "Cloud
   folder backup" section in `docs/USER_GUIDE.en.md`; a short
   append to `docs/EXPORT-FORMAT.md` covering the two optional
   manifest fields (`source`, `device`).

Run `dotnet build` and `dotnet test` before every commit that
touches source (`CLAUDE.md` §8).

**Effort.** 1–2 developer-weeks on top of C.3, matching
`EVOLUTION.md` §5.3. `[INFERRED]`

---

## Change log for this document

- 2026-09-21 — initial draft (pre-implementation). Derived from
  `EVOLUTION.md` §5 and cross-checked against
  `AutomaticBackupHostedService`, `BackupService`, `BackupSettings`,
  `AppDataPaths` and `SmtpCredentialStore`. Reuses the C.3
  archive format as the snapshot payload (§1.2). Chose Model C
  for the automatic-export passphrase (§3.4). Two independent
  targets (raw DB local + `.mrz` cloud folder) coexisting inside
  the existing daily-tick host (§4.1). No new `IHostedService`;
  no new transport (§4.2, §1.3).
- 2026-09-25 — the cloud target covers every profile instead of the
  active one only (§4.1); restore warns before applying another
  profile's snapshot.
