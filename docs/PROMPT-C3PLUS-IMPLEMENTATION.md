# Implementation prompt — C.3+: Backup to cloud folder + explicit restore

This file is the self-contained briefing for the Claude Code session
that will implement feature C.3+. Read it completely before touching
any source file.

---

## 0. What you are about to implement

**Feature C.3+** extends MedReminder's automatic backup to write
**encrypted snapshots** (C.3's `.mrz` archive format) into a
**local folder synchronized by the user's own cloud agent**
(OneDrive, iCloud Drive, Dropbox, Google Drive Desktop, or
equivalent). It also adds an explicit **Restore from cloud
folder** command that reads the most recent snapshot and applies
it to the current profile.

The user model is **single-writer, multiple-reader-on-demand**.
This is **not sync**. Two devices editing between two backups
diverge silently; the last restore wins.

**Hard precondition: C.3 must have shipped.** C.3+ reuses C.3's
`ExportService` / `ImportService` and its `.mrz` format. If C.3
is not present on `main` at the time work on this feature starts,
ship C.3 first and rebase.

The authoritative design lives in two documents — read them
before coding:

- **`docs/ANALYSIS-C3PLUS-CLOUD-BACKUP.md`** — the approved
  design. Sections §3–§12 describe the data model, runtime, UI,
  tests, risks and implementation order. Where this prompt and
  the analysis document disagree, **the analysis document wins**.
- **`docs/EVOLUTION.md` §5** — the original sketch, retained for
  context.

Also read before making any architectural change:

- **`docs/ANALYSIS-C3-EXPORT-IMPORT.md`** — the C.3 archive
  format and export/import services this feature relies on.
- **`CLAUDE.md`** — mandatory rules covering language policy,
  branch naming, PR workflow, localization, and the things to
  never do.
- **`docs/ANALYSIS.md`** — the base architecture (DPAPI usage,
  per-profile paths, hosted-service pattern).

---

## 1. Hard boundaries — do NOT cross these

C.3+ is scoped strictly to **encrypted snapshots in a local
folder + explicit restore**. The following are **out of scope by
design** (`ANALYSIS-C3PLUS` §1.3):

- **Not sync.** No conflict resolution, no vector clocks, no
  operational log, no server.
- **No cloud API usage.** The app writes to a local folder; the
  user's OS-level sync agent handles the upload.
- **No plaintext DB in a synced folder — ever.** Only C.3's
  encrypted `.mrz` format is written.
- **No live-DB file-sync.** Placing the live SQLite DB in a
  cloud-synced folder is rejected by `EVOLUTION.md` §7.1 and
  `ANALYSIS-C3PLUS` §1.3. Do not weaken this.
- **No automatic restore.** Restore is always explicit.
- **No cloud-folder detection.** The user picks the folder;
  the app does not guess where OneDrive / iCloud is installed.
- **No new NuGet outside what C.3 already brings in.**
- **No new `IHostedService`.** Extend the existing
  `AutomaticBackupHostedService`; do not add a second timer.
- **No `EnsureCreated`** — but C.3+ introduces no schema change,
  so the point does not arise.
- **No `SmtpClient` from `System.Net.Mail`** — MailKit remains
  the only supported SMTP client. C.3+ does not touch email,
  but do not add a second SMTP client for any reason.
- **Not a medical-device concern.**

If a reviewer asks for any of these, decline and reference
`ANALYSIS-C3PLUS` §1.3.

---

## 2. Preconditions — verify against the tree first

Before writing any code, confirm the following in the actual
source:

1. **C.3 is on `main`.**
   `MedReminder.Application.Export.IExportService` and
   `IImportService` exist and behave as documented in
   `ANALYSIS-C3` §4.1 and §4.2. If they are missing, stop and
   ship C.3 first.
2. `AutomaticBackupHostedService` runs on a daily tick driven by
   `BackupSettings`. Locate the tick body — you will extend it,
   not replace it.
3. `BackupService.BackupFileRegex` matches
   `medreminder-<profileId>-<ts>.db`. A sibling `.mrz` regex has
   to coexist.
4. `SmtpCredentialStore` reads / writes `smtp.protected` via
   DPAPI (CurrentUser scope). Confirm the exact API — you will
   mirror it for `cloud-backup.protected`.
5. `SettingsDialog` writes `backup.settings.json`. Locate the
   section responsible so the new "Cloud Backup" UI slots in
   next to it.
6. `DictionaryParityTests` (or its current equivalent) enforces
   parity across `assets/localization/strings.<lang>.json` for
   the five shipped languages.

Document what you find (brief notes in the commit message are
fine); update the analysis document if any fact is wrong.

---

## 3. Branch and PR

Per `CLAUDE.md` §5:

- Branch name: **`feature/cloud-folder-backup`**, based on
  `main`.
- Open a pull request **after the first commit**, not at the end.
- Prepend a `CHANGE_LOG.md` entry when the PR opens (follow the
  format documented at the top of that file).

---

## 4. Implementation order

Follow `ANALYSIS-C3PLUS` §12. Each step must leave `dotnet build`
and `dotnet test` green before the next commit.

### Step 1 — Application: `BackupSettings` extension

File:
`src/MedReminder.Application/Abstractions/BackupSettings.cs`.

Add three fields (`ANALYSIS-C3PLUS` §3.1):

```csharp
public bool CloudFolderEnabled { get; set; } = false;
public string CloudFolderDirectory { get; set; } = string.Empty;
public int CloudFolderRetention { get; set; } = 30;
```

Unit test in `MedReminder.Application.Tests`: deserialize a JSON
payload with and without the new keys; defaults apply when
absent.

### Step 2 — Application: passphrase store port

File:
`src/MedReminder.Application/Abstractions/ICloudBackupPassphraseStore.cs`
(new).

```csharp
public interface ICloudBackupPassphraseStore
{
    bool IsConfigured { get; }
    char[]? GetPassphrase();      // null when IsConfigured is false
    void SetPassphrase(char[] passphrase);
    void Clear();
}
```

The port lives in Application; the DPAPI-backed adapter lives in
Infrastructure (Step 3). Keep Domain / Application Windows-free
(`CLAUDE.md` §9).

### Step 3 — Infrastructure: DPAPI adapter for the passphrase

File:
`src/MedReminder.Infrastructure/Credentials/DpapiCloudBackupPassphraseStore.cs`.

Mirror `SmtpCredentialStore`'s DPAPI usage exactly (CurrentUser
scope). Read / write
`%LOCALAPPDATA%\MedReminder\cloud-backup.protected`.

- Never log the passphrase, the derived key, or the file
  contents.
- Zero the returned `char[]` on `Clear()`.
- Handle "file missing" as `IsConfigured = false`, not an
  exception.

**Infrastructure tests** (Windows-only, gated by the same DPAPI
guard already in place):

- Round-trip: `SetPassphrase(x)` then `GetPassphrase()` returns
  `x`.
- `IsConfigured` reflects the file presence.
- Tampered blob → `GetPassphrase()` throws / returns null with
  a warning log (choose one and stay consistent with
  `SmtpCredentialStore`'s current behaviour).

### Step 4 — Application/UI: extend `AutomaticBackupHostedService`

Extend the existing tick body per `ANALYSIS-C3PLUS` §4.1:

```
On scheduled tick:
  if BackupSettings.Enabled:
      run the existing raw-DB backup path (unchanged)
  if BackupSettings.CloudFolderEnabled:
      passphrase = passphraseStore.GetPassphrase()
      if passphrase == null:
          log warning ("cloud backup enabled but no passphrase configured")
          continue
      options = new ExportOptions {
          Scope = ExportScope.Profile,
          IncludeSmtpTransport = false,
          IncludeSmtpPassword = false,
          IncludeBackupPrefs = false,
          IncludeUserPrefs = false,
          Source = "automatic"
      }
      temp = await exportService.ExportAsync(options, passphrase, null, ct)
      final = Path.Combine(CloudFolderDirectory,
                           $"medreminder-{profileId}-{ts}.mrz")
      File.Move(temp, final)     // atomic; safe under sync agents
      await PruneCloudFolderAsync(CloudFolderDirectory,
                                  CloudFolderRetention, ct)
```

Design constraints:

- **Two targets run independently.** Wrap each in its own
  `try/catch`, log warnings, keep going. A failure on one target
  must not skip the other.
- **Temp-then-move.** Never write the archive in place — sync
  agents watch the folder and will start uploading a partial
  file.
- **Zero the passphrase buffer** after handing it to the export
  service. `IExportService.ExportAsync` in C.3 accepts a
  `char[]` for exactly this reason.
- **`IncludeShared* = false`** by default. The daily automatic
  cloud snapshot must not carry the SMTP password around.
- **Do not create the target folder if missing.** Log a warning
  and skip the tick (`ANALYSIS-C3PLUS` §4.3).

### Step 5 — Application: `PruneCloudFolderAsync`

Two paths are viable; pick one and stay consistent:

- **(a)** Add `BackupService.PruneCloudFolderAsync(directory,
  retention, ct)` using a new
  `medreminder-<profileId>-<ts>.mrz` regex, structurally
  identical to the existing pruning body.
- **(b)** Teach the existing `PruneOldBackupsAsync` a second
  regex (`.db` and `.mrz`) and a way to select which extension
  to prune.

Recommendation: **(a)** — a separate method keeps the two
extensions clearly separated and avoids accidental cross-pruning.
Group by `profileId` exactly as today so the most recent
`.mrz` of profile A does not shield old `.mrz` files of profile
B (`ANALYSIS-C3PLUS` §4.5).

**Infrastructure tests:**

- Files older than `CloudFolderRetention` days are removed;
  recent files survive.
- `.db` files are **never** touched by the cloud prune.
- Files that do not match the `.mrz` regex are left alone.

### Step 6 — Application: manifest additions

Extend the C.3 manifest (`ANALYSIS-C3` §3.1) with two optional
fields when the export runs from the automatic path
(`ANALYSIS-C3PLUS` §3.6):

```json
{
  "source": "automatic",
  "device": {
    "hostName": "SHA-256:<hex-of-Environment.MachineName>",
    "profileId": "…"
  }
}
```

- `Environment.MachineName` is hashed via SHA-256. The plain host
  name is **never** written into the archive or the log.
- Both fields are additive; readers of an older `formatVersion`
  ignore them.

Tests:

- Two consecutive automatic ticks on the same machine produce
  the same hash.
- With `MachineName` stubbed to a different value, the hash
  differs.

### Step 7 — Application: `ICloudRestoreService`

File:
`src/MedReminder.Application/Export/ICloudRestoreService.cs`.

A thin wrapper over `IImportService` that:

- Lists `.mrz` snapshots in a folder (returns a POCO for each
  with `Path`, `CreatedAtUtc` from the manifest, `DeviceHash`,
  `ProfileId`, `SchemaVersion`, `FormatVersion`).
- Delegates the actual restore to `IImportService.ImportAsync`
  with the same overwrite semantics.

Test:

- Given a folder with two valid `.mrz` files and one unrelated
  file, `ListAsync` returns exactly the two, sorted by date
  descending.
- A tampered manifest is surfaced as an entry with a
  `Corrupt = true` marker (or is skipped — pick one and document
  the choice in the User Guide).

### Step 8 — UI: Cloud Backup section in `SettingsDialog`

Extend `SettingsDialog` (`ANALYSIS-C3PLUS` §5.1):

- Section title `Ui.SettingsDialog.CloudBackup.Section.Title`.
- Checkbox `Ui.SettingsDialog.CloudBackup.Enabled`.
- `FolderBrowserDialog`-backed picker bound to
  `CloudFolderDirectory`.
- `NumericUpDown` bound to `CloudFolderRetention`.
- "Change passphrase…" button opening a small child dialog with
  passphrase + confirm-passphrase fields. On OK, call
  `ICloudBackupPassphraseStore.SetPassphrase`.
- Warning label with the "not sync" and "lost passphrase" copy
  (localized).
- Save validation:
  - When `CloudFolderEnabled` is on and no
    `cloud-backup.protected` exists yet, require the user to set
    a passphrase before Save is allowed.
  - `CloudFolderDirectory` must be a non-empty path when
    `CloudFolderEnabled` is on.

### Step 9 — UI: `RestoreCloudDialog`

Add a new dialog reachable via a `Ui.SettingsDialog.File.
RestoreFromCloud` button (`ANALYSIS-C3PLUS` §5.2):

- Folder picker (defaults to `CloudFolderDirectory`).
- `ListView` of snapshots (Date, Device, Profile, Schema).
  Sortable.
- Passphrase field with an "Use this device's saved backup
  passphrase" checkbox. When checked, pull the passphrase from
  `ICloudBackupPassphraseStore.GetPassphrase()`; when unchecked,
  read the textbox.
- Mandatory "I understand this will overwrite the current
  profile's data" confirmation checkbox — the Restore button is
  disabled until it is checked.
- Progress bar; `CancellationTokenSource` on Cancel.
- Restart prompt on success.

The restore itself delegates to `IImportService.ImportAsync`.
No new import code lives in this dialog.

### Step 10 — Localization

Add the keys from `ANALYSIS-C3PLUS` §6 to **every** dictionary
under `assets/localization/` (`en`, `it`, `fr`, `es`, `de`).
Missing keys fail the build via `DictionaryParityTests`.

For the four non-English dictionaries, follow the A1 / A5 / C.3
precedent: ship with `"TODO(<lang>): <english fallback>"`
placeholders and finalize wording after the form is inspected.
Italian wording requires maintainer sign-off before it lands.

### Step 11 — User guide, format doc, CHANGE_LOG.md

- **`CHANGE_LOG.md`**: prepend an entry when the PR opens.
  Follow the format at the top of that file.
- **`docs/USER_GUIDE.en.md`**: add a "Cloud folder backup"
  section covering (a) the two-device setup (device #1 sets
  folder + passphrase; device #2 installs the app and opens
  Restore from cloud folder with the same passphrase), (b) the
  "not sync" honest disclaimer, (c) the passphrase discipline,
  (d) the cloud-provider recycle-bin note (retention pruning
  only removes visible files).
- **`docs/EXPORT-FORMAT.md`** (from C.3): append a short note
  documenting the two optional manifest fields (`source`,
  `device`) that C.3+ populates.

The four localized guides may follow in a follow-up.

---

## 5. Decided items — not open for re-debate

These items in `ANALYSIS-C3PLUS` §11 are decided by the analysis
and must not be re-opened without the product owner's explicit
sign-off:

- **Passphrase model** (§3.4): Model C — a **separate backup
  passphrase**, distinct from the C.3 export passphrase,
  DPAPI-cached in `cloud-backup.protected`. Do not reuse the C.3
  passphrase.
- **Snapshot format** (§1.2): C.3's `.mrz` archive, unchanged.
  Do not invent a second format.
- **Two independent targets** (§4.1): the existing raw-DB backup
  and the new cloud target run independently; a failure on one
  does not skip the other.
- **No new hosted service** (§4.2): extend
  `AutomaticBackupHostedService`.
- **No cloud API usage** (§1.3): write to a local folder only.
- **No live-DB file-sync** (§1.3, `EVOLUTION.md` §7.1).
- **`IncludeShared* = false`** for the automatic snapshot (§4.1)
  in the first cut.

---

## 6. Still open — settle at implementation time

- **Retention default** (`ANALYSIS-C3PLUS` §11 item 3).
  Recommended: 30 days.
- **Cadence** (`ANALYSIS-C3PLUS` §11 item 4). Recommended:
  reuse the existing daily tick; no faster mode in the first
  cut.
- **Hostname hashing** (`ANALYSIS-C3PLUS` §11 item 6).
  Recommended: yes, SHA-256 of `Environment.MachineName`.
- **Localized user guides** (`ANALYSIS-C3PLUS` §11 item 5).
  Ship English with the PR; the four localized guides may
  follow.

Ask before implementing if any of these is not recorded in
`ANALYSIS-C3PLUS` §11 by the time you reach the relevant step.

---

## 7. Conventions and constraints (from `CLAUDE.md`)

- All identifiers, comments, log messages, exception messages,
  XML docs, and commit messages must be in **English**. Italian
  is used only in chat with the user and in the localized
  `strings.it.json` / `USER_GUIDE.it.md`.
- Run `dotnet build` and `dotnet test` before every commit that
  touches source.
- No `EnsureCreated()` — C.3+ introduces no schema change, so
  the point does not arise.
- No `SmtpClient` from `System.Net.Mail` — MailKit only.
- No plaintext passwords or PII to logs. In particular, never
  log the backup passphrase, the derived key, or archive
  contents.
- When adding a UI string, add the key to **every** localization
  dictionary.
- Keep Domain free of Windows-specific APIs and EF Core
  references. `ICloudBackupPassphraseStore` lives in
  `Application`; the DPAPI adapter lives in `Infrastructure`.
- **Never bypass** the single-instance mutex or force-close
  SQLite connections outside of the documented backup / restore
  paths (`CLAUDE.md` §9).

---

## 8. Acceptance criteria

The PR is ready to merge when:

1. `dotnet build MedReminder.sln -c Release` is green.
2. `dotnet test MedReminder.sln -c Release` is green, including
   the new tests in Steps 1, 3, 4, 5, 6, 7 and 9.
3. All localization keys from `ANALYSIS-C3PLUS` §6 are present
   in all five dictionaries (`DictionaryParityTests` passes).
4. With `BackupSettings.Enabled = true` and
   `CloudFolderEnabled = true`, a scheduled tick produces one
   `.db` in `Directory` AND one `.mrz` in
   `CloudFolderDirectory`. A read-only permission on one
   directory does not skip the other.
5. With `CloudFolderEnabled = true` and no
   `cloud-backup.protected`, the tick logs a warning and does
   not produce a `.mrz`.
6. Files older than `CloudFolderRetention` are pruned from the
   cloud folder; `.db` files are never pruned by the cloud
   pruning path.
7. `RestoreCloudDialog` performs a full round-trip: the current
   profile is overwritten by the chosen archive; the restart
   prompt appears; on restart, the app runs against the restored
   data.
8. A pre-C.3+ `backup.settings.json` file still loads and the
   app behaves exactly as before (cloud target off by default).
9. `CHANGE_LOG.md` has a new entry for this PR.
10. `docs/USER_GUIDE.en.md` has a "Cloud folder backup" section
    and `docs/EXPORT-FORMAT.md` documents the two new manifest
    fields.

---

*Generated 2026-09-21. Authoritative source:
`ANALYSIS-C3PLUS-CLOUD-BACKUP.md`. Precondition: C.3 shipped.*
