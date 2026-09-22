# ANALYSIS — C.3: Manual export / import (GDPR portability + device migration)

Design document, **prior** to implementation. Once approved, work
proceeds on branch `feature/export-import` (per `CLAUDE.md` §5).
Corresponds to `EVOLUTION.md` §4 (Group C, item C.3). Follows the
structure of `ANALYSIS-A5-DOSE-TIME-REMINDER.md` and
`ANALYSIS-A6-DONATION-SUPPORT.md`.

> **This is not a speculative analysis.** Every decision is
> technically motivated and delimits what will be written in code.
> The "Decisions still to confirm" section at the end is the only
> zone of ambiguity that needs input.

Epistemic classification, aligned with the sibling documents:
`[VERIFIED]` (checked against the current tree),
`[VERIFIED against ANALYSIS.md]` (specified there, tree check still
applies), `[INFERRED]` (deduction from verified facts),
`[UNCERTAIN]` (hypothesis pending confirmation).

---

## 1. Scope

### 1.1 Problem

Two distinct concerns are solved by one mechanism (`EVOLUTION.md`
§4.1):

- **GDPR art. 20 (data portability).** The user has a right to
  receive their data in a structured, commonly used,
  machine-readable format. MedReminder today has no such export.
- **Device migration.** Moving MedReminder from an old PC to a
  new one today requires copying `%LOCALAPPDATA%\MedReminder\` by
  hand — feasible for a technical user, unfriendly to everyone
  else.

The existing per-profile SQLite backup / restore path
(`BackupService.ExportProfileAsync` /
`BackupService.ImportProfileAsync`) `[VERIFIED]` is a raw DB copy:
it works, but it is Windows / DPAPI-bound (the SMTP credential
store, the toast AUMID, the file system layout are all specific
to the current install) and it is not a documented public format.
C.3 introduces a **portable, documented, cipher-agnostic** export
that is legible to a future MedReminder version — or to a
migration script written by a third party.

### 1.2 Goal

Add two commands under Settings → File:

- **Export all data.** Produces an **encrypted ZIP archive**
  (single file, `.mrz` extension by default) containing:
  - a **schema-versioned JSON document** covering every entity of
    the current profile (or, for admins, all profiles — variant
    in §3.5);
  - a small `manifest.json` with the schema version, the app
    version at export time, the export timestamp (UTC), and a
    payload hash;
  - optionally, non-DB shared settings (see §3.4).
- **Import from export.** Reads the archive, decrypts, validates
  the schema version and payload hash, and applies the content
  to the target profile in **Overwrite** mode.

The archive is encrypted with a **user-chosen passphrase**, keyed
via **Argon2id**, using **AES-GCM** for the payload
(`EVOLUTION.md` §4.2). DPAPI is deliberately **not** used —
DPAPI is bound to the Windows account and defeats the migration
use case (`EVOLUTION.md` §4.2).

The export format is documented publicly under
`docs/EXPORT-FORMAT.md`, so a user can migrate away to another
tool without lock-in (`EVOLUTION.md` §4.2).

### 1.3 What C.3 is NOT

- **Not sync.** C.3 is a one-shot, user-triggered export /
  import. Nothing in the app watches a folder or pushes to a
  service. Real-time sync is C.1 (`EVOLUTION.md` §7); assisted
  migration via a cloud folder is C.3+ (§5, sibling analysis).
- **No merge mode in the first cut.** `EVOLUTION.md` §4.2
  explicitly defers merge; §4.4 warns that merge rules are
  dangerous. First cut ships **Overwrite** only. Merge is called
  out as an open decision in §12 and left for a later PR.
- **No cross-user account concept.** MedReminder is single-account
  per Windows user; C.3 keeps that model. The archive is
  self-describing and does not bind to a user id.
- **No cloud target.** C.3 writes to a local file the user picks.
  A user may drop that file into OneDrive / Dropbox / iCloud
  themselves — the app does not do it and does not know about it.
  Assisted cloud drop is C.3+ (sibling analysis).
- **No plaintext DB dump.** The archive is always encrypted —
  even if the user's passphrase is empty, the export is refused
  rather than producing an unencrypted file. An unencrypted DB
  dump on disk (or in a synced folder) is a data-leak risk that
  `EVOLUTION.md` §5.4 already calls out for C.3+; C.3 inherits
  the same posture.
- **No inclusion of the SMTP password by default.**
  `EVOLUTION.md` §4.4 is explicit: include only on explicit
  opt-in, and only re-encrypted with the export passphrase (not
  DPAPI). §3.4 details the mechanism.
- **Not a medical-device concern.** C.3 moves data between
  devices; it does not read or write therapy state at runtime.
  Stays clear of EU MDR 2017/745 (`EVOLUTION.md` §8.2).

---

## 2. Preconditions — what already exists

- **Per-profile DB path.** `AppDataPaths.GetProfileDataDirectory`
  resolves `<%LOCALAPPDATA%>\MedReminder\profiles\<profile-id>\`;
  the DB lives at
  `Path.Combine(GetProfileDataDirectory(id), DatabaseFileName)`
  `[VERIFIED — see BackupService.ResolveProfileDatabasePath]`.
- **SQLite online-backup API.** `BackupService.ExportProfileAsync`
  already uses `SqliteConnection.BackupDatabase` to produce a
  torn-write-free copy of the live DB `[VERIFIED]`. C.3 will
  reuse this technique to read a consistent snapshot without
  quiescing the app.
- **EF Core model.** The domain entities that must be exported
  (medicines, stock movements, schedule history, suspensions,
  administration slots, notification events, dose reminder
  events) all have EF Core configurations under
  `src/MedReminder.Infrastructure/Persistence/Configurations/`
  `[INFERRED]`. Confirm the full entity list at implementation
  time — the JSON schema documented in
  `docs/EXPORT-FORMAT.md` must enumerate them explicitly.
- **Shared, admin-managed files.** `CLAUDE.md` §6 lists the
  shared JSON files under `%LOCALAPPDATA%\MedReminder\`
  (`profiles.json`, `smtp.settings.json`, `smtp.protected`,
  `backup.settings.json`, `backup.state.json`,
  `user.settings.json`). C.3 must decide, per file, whether it
  is included and under what conditions (§3.4).
- **Idempotent schema patches.** `DatabaseInitializer` applies
  additive, idempotent patches at boot (`ANALYSIS.md` §2.8).
  Import into an older-schema profile is safe: the initializer
  runs at next start and brings the schema forward. Import into
  a **newer**-schema profile is not — §4.3 defines the guard.
- **`.gitignore` posture.** `smtp.protected`, `*.pfx`, `*.p12`
  are already ignored (`CLAUDE.md` §8); nothing about C.3 relaxes
  this. The exported archive is user-produced runtime output, not
  a repository artefact.

---

## 3. Data model

### 3.1 The archive

`.mrz` is a plain ZIP file with a well-known internal layout:

```
export.mrz  (ZIP container)
├── manifest.json          (cleartext, versioned metadata)
├── payload.enc            (AES-GCM ciphertext of payload.json)
└── payload.enc.nonce      (12-byte nonce)
```

`manifest.json` is **cleartext** because it must be inspectable
without decrypting anything — it carries the schema version, the
KDF parameters and the payload hash. It contains **no** medical
data.

```json
{
  "format": "medreminder-export",
  "formatVersion": 1,
  "appVersion": "1.15.2",
  "createdAtUtc": "2026-09-21T14:03:22Z",
  "scope": "profile",           // or "all-profiles"
  "profileId": "…",             // present when scope == "profile"
  "kdf": {
    "algorithm": "Argon2id",
    "iterations": 3,
    "memoryKiB": 65536,
    "parallelism": 1,
    "saltBase64": "…"
  },
  "cipher": {
    "algorithm": "AES-GCM",
    "keyBits": 256,
    "nonceBase64": "…",
    "tagBase64": "…"
  },
  "payload": {
    "sha256Base64": "…",        // hash of the DECRYPTED payload.json
    "sizeBytes": 12345
  },
  "includes": {
    "smtpCredential": false,
    "userSettings": true,
    "backupSettings": true
  }
}
```

`payload.enc` is `AES-GCM(payload.json, key, nonce)` where
`payload.json` is the structured export described in §3.2 and
`key = Argon2id(passphrase, salt, params)`.

**Why a ZIP wrapper and not a single opaque file.** The ZIP
container lets a user (or a future migration script) inspect the
manifest without a passphrase. That is a portability win at zero
security cost — the manifest carries no medical data.

### 3.2 `payload.json` shape

A single JSON document with a versioned top-level envelope. Each
entity type has its own array; each row is a JSON object mirroring
the EF Core entity (public settable fields only). No cycles, no
lazy-loading, no navigation properties — dependent rows carry the
parent's `Id` explicitly.

```json
{
  "schemaVersion": 1,
  "profile": {
    "id": "…",
    "displayName": "…",
    "role": "user",
    "createdAt": "…"
  },
  "medicines": [ /* … */ ],
  "stockMovements": [ /* … */ ],
  "medicationScheduleHistory": [ /* … */ ],
  "medicationAdministrationSlots": [ /* … */ ],
  "medicationSuspensions": [ /* … */ ],
  "notificationEvents": [ /* … */ ],
  "doseReminderEvents": [ /* … */ ],       // if A5 is present
  "notificationSettings": { /* … */ },
  "shared": {
    "userSettings": { /* … */ },           // opt-in
    "backupSettings": { /* … */ },         // opt-in
    "smtpSettings": { /* … */ },           // never contains password
    "smtpPasswordEncrypted": null          // populated only when opt-in
  }
}
```

**`schemaVersion` is not `formatVersion`.** `formatVersion`
(manifest) tracks the archive envelope; `schemaVersion` (payload)
tracks the entity model. They can move independently — the SQLite
patch discipline (`ANALYSIS.md` §2.8) applies to `schemaVersion`.

### 3.3 Encryption details

**KDF:** Argon2id, chosen because `EVOLUTION.md` §7.3 already
adopts it for the C.1 track and consistency between C.3 and C.1
avoids two KDFs in one product. Suggested first-cut parameters:
`iterations = 3`, `memoryKiB = 65536` (64 MiB),
`parallelism = 1`. `[INFERRED — OWASP current guidance at
publication time; verify against OWASP's live recommendation at
implementation time, `EVOLUTION.md` §7.3]`

**Cipher:** AES-GCM with a 256-bit key and a 12-byte random
nonce, native to .NET via `System.Security.Cryptography.AesGcm`.
`[VERIFIED — AesGcm is in-box in .NET 8+, and the project targets
net10.0]`

**Library choice.** Two viable Argon2 bindings:

| Library | Verdict |
|---|---|
| **`Konscious.Security.Cryptography.Argon2`** | ✅ Recommended. Managed, MIT, no native dependency, in wide use. |
| `libsodium` via `NSec` | Better perf, native dep, matches `EVOLUTION.md` §7.3 for C.1. A native dep on Windows is not a blocker (the app is already Windows-only) but adds packaging weight. |

**Decision.** `Konscious.Security.Cryptography.Argon2` for C.3.
When (if) C.1 lands, the ports may re-consolidate on
`libsodium` / `NSec`. `[INFERRED]`

### 3.4 Shared / sensitive files — inclusion policy

`CLAUDE.md` §6 lists the shared files. Inclusion policy for the
first cut:

| File | Included in export | Rationale |
|---|---|---|
| `profiles.json` (registry) | ❌ | Cross-profile registry, not per-profile data. An "all profiles" export bakes it in implicitly (§3.5); a single-profile export does not need it. |
| `smtp.settings.json` (transport, no password) | ✅ opt-in | Contains host / port / username / from-address. Not secret in isolation but leaks user identity. Opt-in checkbox. |
| `smtp.protected` (DPAPI blob) | ❌ **never as-is** | DPAPI is bound to the Windows account — the blob is worthless on another machine. `EVOLUTION.md` §4.4 requires the password to be re-encrypted with the export passphrase if it is included at all. Under **explicit** opt-in and with an unambiguous confirmation dialog, decrypt with DPAPI, then re-encrypt into `payload.json` `shared.smtpPasswordEncrypted` (AES-GCM with the archive key). |
| `backup.settings.json`, `backup.state.json` | ✅ opt-in | User preferences, not secret. Restoring them on a new device saves reconfiguration. Opt-in for symmetry with the SMTP settings. |
| `user.settings.json` | ✅ opt-in | UI language + reference-catalogue country. Trivial to reconfigure but nice to carry. Opt-in. |
| `notifications.settings.json` | ✅ (implicit) | Per-profile — travels with the profile. |

All `✅ opt-in` items are represented in the export dialog as
individual checkboxes, unchecked by default. `smtp.protected` is
**always** unchecked and carries a warning that the SMTP password
will be re-encrypted with the export passphrase.

### 3.5 `scope` — single profile vs. all profiles

`EVOLUTION.md` §4.2 mentions an "admin-only variant" that covers
all profiles.

- **`scope = "profile"`.** Default. Exports the currently active
  profile only. Available to any profile.
- **`scope = "all-profiles"`.** Exports every profile on the
  system, plus `profiles.json`. Available **only** to
  admin-role profiles (`ANALYSIS-MULTI-USER.md`). Rejected for
  non-admin profiles at UI time and again at service time.

`payload.json` shape is unchanged for `scope = "profile"`. For
`scope = "all-profiles"` the top level becomes:

```json
{
  "schemaVersion": 1,
  "profilesRegistry": { /* … */ },
  "profiles": [
    { /* one payload-per-profile block */ },
    …
  ]
}
```

The admin variant is called out as an **open decision** in §12:
it is small in code but doubles the test matrix and the UI. First
cut may ship `profile` scope only.

### 3.6 No SQLite schema change

C.3 does not add columns or tables. It reads existing entities
via EF Core and rewrites them on import. No `DatabaseInitializer`
patch, no `ALTER TABLE`, no `EnsureCreated` concern
(`CLAUDE.md` §9).

---

## 4. Runtime

### 4.1 Export path

`ExportService.ExportAsync(passphrase, options, cancellationToken)`
in `MedReminder.Application.Export` (new namespace):

```
1. Validate: passphrase non-empty and >= MinPassphraseLength (§4.5).
2. Snapshot the DB: use BackupService.ExportProfileAsync to a
   temp file under Path.GetTempPath() / a per-session scratch
   directory. This is the same online-backup call already used,
   producing a torn-write-free copy without quiescing the app.
3. Read the snapshot into `payload.json` via a read-only EF Core
   context bound to the temp DB.
4. Collect the opt-in shared files (§3.4). For smtp.protected:
   DPAPI-decrypt the password, then hold it in a byte array to
   be re-encrypted with the archive key; zero the buffer after
   use.
5. Serialize payload.json (UTF-8, no BOM, camelCase, ISO-8601
   timestamps).
6. Derive key: key = Argon2id(passphrase, salt, params).
7. Encrypt: (nonce, tag, ciphertext) = AesGcm.Encrypt(key,
   payload.json).
8. Compute payload SHA-256 (over the plaintext) for the
   manifest.
9. Build manifest.json.
10. Write the ZIP: manifest.json + payload.enc + payload.enc.nonce.
11. Delete the temp DB snapshot.
```

The export runs off the UI thread; use a `Task` and a
`Progress<T>` reporter for the WinForms dialog.

### 4.2 Import path

`ImportService.ImportAsync(archivePath, passphrase, options,
cancellationToken)`:

```
1. Open the ZIP; parse manifest.json.
2. Verify manifest.format == "medreminder-export" and
   manifest.formatVersion is supported (§4.3).
3. Derive key from passphrase + kdf params in the manifest.
4. Decrypt payload.enc using cipher params in the manifest.
   A GCM tag mismatch here is the "wrong passphrase" case —
   surface it as a friendly error, not a stack trace.
5. Verify SHA-256(payload.json plaintext) matches
   manifest.payload.sha256Base64. Mismatch = tampering or
   truncation; abort.
6. Parse payload.json; check schemaVersion (§4.3).
7. Confirmation dialog (§5.3): overwrite target profile? For
   scope=all-profiles, list which profiles will be touched.
8. Backup the current profile DB before overwriting: reuse
   BackupService.ImportProfileAsync's own pre-import safety
   step (it renames the existing DB to `<db>.bak-YYYYMMDDHHmmss`
   before copying).
9. In a single EF Core transaction:
     a. Truncate the target profile's tables in FK-safe order.
     b. Insert the payload's rows.
     c. Commit.
10. Apply opt-in shared file restores (SMTP settings, backup
    settings, user settings). Decrypt smtpPasswordEncrypted with
    the archive key, then DPAPI-re-encrypt into smtp.protected on
    the target machine.
11. Report success. Prompt the user to restart the app so the
    hosted services pick up the new state cleanly.
```

Step 11 (restart prompt) matters because
`IOptionsMonitor` will re-emit some settings on file change but
the profile DB has been swapped from under EF Core; a clean
restart is the least surprising path. `[INFERRED]`

### 4.3 Version compatibility

- `manifest.formatVersion` must be `<= currentFormatVersion`.
  Newer archive → refuse import with a friendly "produced by a
  newer version of MedReminder" message.
- `payload.schemaVersion` must be `<= currentSchemaVersion`.
  Newer → same friendly refusal.
- Older payload `schemaVersion` → import proceeds; the additive
  patch discipline (`ANALYSIS.md` §2.8) means an older archive is
  always representable by rows the current schema accepts.
  Columns that did not exist in the older archive take their
  defaults.
- Older `formatVersion` → import proceeds if the KDF / cipher
  parameters in the manifest are still supported. The manifest
  is self-describing (§3.1); the importer honours what the
  manifest declares, not a hard-coded assumption.

### 4.4 Encryption failure surfaces

- **Wrong passphrase.** `AesGcm.Decrypt` throws
  `CryptographicException` on tag mismatch. Localized message:
  `"The passphrase does not match this file."` — do **not** echo
  the passphrase or hint at "close matches".
- **Corrupt archive.** Payload SHA-256 mismatch after successful
  decryption (rare — tampering or partial write). Localized
  message: `"The export file is damaged and cannot be imported."`.
- **Unsupported version.** Format or schema newer than the
  running app. Localized message: `"This export was produced by
  a newer version of MedReminder. Update the app and try again."`.

None of these paths logs the passphrase, the derived key, or the
plaintext. `CLAUDE.md` §9 already forbids logging PII / secrets;
this applies here.

### 4.5 Passphrase policy

- **Minimum length: 12 characters.** No composition rules — the
  Argon2id cost dominates. `[INFERRED — OWASP-derived minimum
  for user-chosen secrets with a KDF]`
- **No maximum.** Argon2id handles arbitrary-length inputs.
- **No recovery.** There is intentionally no server, no key
  escrow, no reset. A lost passphrase = a lost archive. The UI
  copy states this before the user picks the passphrase, and
  again in the export success dialog.
- **The passphrase is never logged, never persisted, never
  passed to another process.** It lives in a `SecureString` (or a
  `char[]` that is zeroed after key derivation) inside the
  export/import service and is dropped as soon as the key is
  derived.

### 4.6 Not a hosted service

C.3 is a **user-initiated command**, not a background job. There
is no `IHostedService`, no `PeriodicTimer`, and no scheduled
export. The user picks the destination, enters the passphrase,
clicks Export. The monitor threads (`MedicationMonitor`,
`DoseReminderHostedService`) keep running unchanged.

---

## 5. UI

### 5.1 Menu entries

Under `SettingsDialog` (or a new `Settings → File` sub-menu — the
exact placement follows the current `SettingsDialog` structure,
which already writes JSON files):

- `Ui.SettingsDialog.File.ExportData` — button "Export all
  data…".
- `Ui.SettingsDialog.File.ImportData` — button "Import from
  export…".

Both buttons open modal dialogs (`ExportDialog`, `ImportDialog`)
running the async service calls with a `Progress<T>` bar. Both
run on the UI thread with the underlying I/O off-thread.

### 5.2 Export dialog

- Destination file picker (`SaveFileDialog`, default filter
  `MedReminder export (*.mrz)`).
- Passphrase field + confirm-passphrase field. Show/hide toggle.
  Live check for `length >= 12` and `passphrase == confirm`.
- Scope radio: **This profile only** (default) / **All
  profiles** (visible **and enabled** only for admin profiles).
- "Include shared settings" checkboxes:
  - Include SMTP transport settings (host, port, username,
    from-address).
  - Include SMTP password. Off by default; when toggled on,
    shows an explicit warning that the password will be
    re-encrypted with the export passphrase and that a lost
    passphrase means a lost password.
  - Include backup preferences.
  - Include user preferences (language, catalogue country).
- Progress bar during the run.
- Success dialog: "Exported to `<path>`. **Store the passphrase
  safely** — MedReminder has no way to recover it."

### 5.3 Import dialog

- Source file picker (`OpenFileDialog`, filter as above).
- Passphrase field. Show/hide toggle.
- Info panel populated from the manifest (before decryption):
  format version, schema version, app version at export, export
  timestamp, scope, list of included shared files, target
  profile id.
- Confirmation checkbox: **"I understand that this will
  overwrite the current profile's data."** The Import button is
  disabled until the box is checked.
- Progress bar during the run.
- Restart prompt on success (§4.2 step 11).

### 5.4 Discoverability

Both commands live in `SettingsDialog` next to the existing
backup UI. No new top-level menu, no startup prompt, no wizard.
Users who look for "export my data" find it in the same place
they configure everything else.

---

## 6. Localization

New keys added to **every** dictionary under
`assets/localization/` (`en`, `it`, `fr`, `es`, `de`) per
`CLAUDE.md` §8. `DictionaryParityTests` fails the build on any
missing key. Proposed keys (final names to be aligned with
existing conventions):

Menu / dialog titles:

- `Ui.SettingsDialog.File.ExportData` / `.ImportData`
- `Ui.ExportDialog.Title` / `Ui.ImportDialog.Title`

Fields:

- `Ui.ExportDialog.Passphrase.Label` /
  `Ui.ExportDialog.PassphraseConfirm.Label`
- `Ui.ExportDialog.Scope.Label` / `.ThisProfile` /
  `.AllProfiles`
- `Ui.ExportDialog.Include.SmtpTransport` / `.SmtpPassword` /
  `.BackupPrefs` / `.UserPrefs`
- `Ui.ImportDialog.Confirm.Overwrite`

Warnings:

- `Ui.ExportDialog.Warning.LostPassphrase` — "Store the
  passphrase safely — a lost passphrase means the export cannot
  be read again."
- `Ui.ExportDialog.Warning.SmtpPasswordIncluded` — "The SMTP
  password will be re-encrypted with this export passphrase."

Error messages (§4.4):

- `Ui.Export.Error.PassphraseTooShort`
- `Ui.Import.Error.WrongPassphrase`
- `Ui.Import.Error.Corrupt`
- `Ui.Import.Error.UnsupportedVersion`

Following the A1 / A5 precedent, the four non-English
dictionaries may ship with `TODO(<lang>): <english fallback>`
placeholders; Italian wording requires maintainer sign-off.

Shipped user guides (`USER_GUIDE.*.md`) get an "Export and
import" section documenting: the passphrase discipline (no
recovery), the opt-in shared-settings checkboxes, the
overwrite-only semantics, and the "restart after import"
requirement. English ships with the PR; the four localized guides
may follow, mirroring A1 / A5.

---

## 7. Format documentation

A new file `docs/EXPORT-FORMAT.md` documents publicly:

- The ZIP layout (`manifest.json` / `payload.enc` /
  `payload.enc.nonce`).
- The full `manifest.json` schema with field-by-field semantics.
- The full `payload.json` schema, listing every entity type and
  the fields present in `schemaVersion = 1`.
- The KDF and cipher parameters and the reason for the choices.
- The evolution discipline: `formatVersion` and `schemaVersion`
  are additive; readers of version `N` must accept archives of
  versions `<= N`.
- An explicit "how to decrypt with off-the-shelf tools" section
  so a user can migrate away without the app (`EVOLUTION.md`
  §4.2 — "no lock-in").

`docs/EXPORT-FORMAT.md` is a **shipped documentation artifact**
of C.3. It is not the analysis: it is the public contract.

---

## 8. Tests

### 8.1 `MedReminder.Application.Tests`

- **Round-trip.** Populate an in-memory DB, export, delete the
  DB, import into a fresh DB, assert row-by-row equality of every
  exported entity.
- **Schema fidelity.** For every entity type, at least one row
  covering every nullable / defaultable field; round-trip
  preserves them.
- **Wrong passphrase.** Export with `"correct horse battery
  staple"`, attempt import with `"wrong"`, assert the failure
  surface (§4.4) and that no DB changes are made.
- **Tampered payload.** Flip one byte of `payload.enc`, attempt
  import, assert the failure surface (AES-GCM tag mismatch is
  indistinguishable from a wrong passphrase from the outside —
  document that).
- **Truncated archive.** Read the first N bytes only, attempt
  import, assert a friendly error (not a crash).
- **Version guard.** Craft a manifest with
  `formatVersion = currentFormatVersion + 1`, assert refusal.
- **Passphrase policy.** Export with a passphrase below the
  minimum length is refused before any file is written.
- **Progress reporter.** `Progress<T>` receives at least a
  0-and-100 pair.

### 8.2 `MedReminder.Infrastructure.Tests`

- **Argon2id parameters.** Derived key length is 32 bytes; two
  runs with the same salt + passphrase produce the same key; two
  runs with different salts produce different keys.
- **AES-GCM round-trip.** Encrypt / decrypt with a random 12-byte
  nonce and a 32-byte key; tampered ciphertext fails.
- **SMTP-password inclusion.** DPAPI-decrypt on export,
  re-encrypt with the archive key, import on a **different**
  Windows account, DPAPI-re-encrypt on that account: the
  round-trip preserves the password. (This test is Windows-only
  and gated on the DPAPI availability guard already in place.)
- **Snapshot isolation.** While the export is running, an
  interleaved write to the live DB does not corrupt the snapshot
  (relies on `SqliteConnection.BackupDatabase`'s guarantee —
  covers regression only).

### 8.3 UI tests / smoke checks

- **Export dialog flows.** Happy path, cancelled during
  progress, invalid passphrase.
- **Import dialog flows.** Happy path, wrong passphrase, corrupt
  file, unsupported version.
- **Restart prompt.** Appears on successful import; declining
  keeps the app running but marks the profile as "restart
  recommended".

---

## 9. Retro-compatibility

- **On-disk.** C.3 introduces no schema change and no new
  runtime file except the archives the user themselves produces.
  Existing installs are unaffected until the user invokes
  Export or Import.
- **Behaviour.** All existing paths (backup / restore, monitor,
  email) continue to work unchanged. C.3 is additive.
- **Revert-safety.** A later build that reverts C.3 loses the
  UI but the produced archives remain readable by any build that
  still ships the format; users can also decrypt them with the
  documented procedure in `docs/EXPORT-FORMAT.md`.
- **Interaction with C.3+.** C.3+ (sibling analysis) reuses this
  archive format as the cloud-folder snapshot payload. C.3 is
  therefore a strict precondition for C.3+ (`EVOLUTION.md` §5.1).

---

## 10. Risks and mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| Passphrase loss = data loss | High (user-perceived) | Explicit warning at export time and again in the success dialog (§5.2); documented in the user guide; no server-side recovery is possible by design (§4.5) |
| Weak KDF parameters age poorly | Medium | Parameters live in the manifest, not in code (§3.1). A future build can strengthen them without breaking older archives — the importer honours what the manifest declares |
| Wrong passphrase / tampered file are indistinguishable | Low | AES-GCM tag mismatch surfaces the same "wrong passphrase" copy; documented in `docs/EXPORT-FORMAT.md`. Not a security bug — a security property |
| SMTP password leak through an unencrypted export | High | The export is always encrypted (no plaintext mode, §1.3); the SMTP password is opt-in (§3.4) and re-encrypted with the archive key, never emitted in the clear |
| Import corrupts the target profile mid-write | High | Pre-import DB backup (§4.2 step 8) mirrors the existing `BackupService.ImportProfileAsync` posture; import runs in a single EF Core transaction |
| Import from a newer version bricks the target | Medium | `formatVersion` and `schemaVersion` guard (§4.3); refusal is loud and localized |
| DPAPI keying breaks round-trips across accounts | Medium | The exported `smtp.protected` is never carried as-is (§3.4); the password is re-encrypted with the archive key and re-wrapped in DPAPI at import time |
| Archive stored in a synced folder becomes an ambient leak vector | Low | Encrypted-at-rest by construction; even if the ZIP leaks, the payload requires the passphrase to open. This is the property that makes C.3+ possible |
| ZIP-slip / path traversal on import | Medium | The ZIP contains only three fixed entries (§3.1); anything else in the ZIP is ignored. Do not blindly extract member paths |
| Merge silently drops rows from either side | High | **Merge is out of scope** (§1.3); Overwrite only |

---

## 11. Non-goals recap

- No merge mode; overwrite only.
- No cloud target inside the app; the user handles that with
  their file explorer or with C.3+ (sibling analysis) once it
  ships.
- No plaintext export path.
- No passphrase recovery, no key escrow.
- No cross-account SMTP-blob transport — the password is
  re-encrypted with the archive key at export and re-wrapped in
  DPAPI at import.
- Not a hosted service; user-initiated only.
- Not a medical-device concern.

---

## 12. Decisions still to confirm

1. **Argon2id parameters** (§3.3). Suggested defaults
   `iterations = 3`, `memoryKiB = 65536`, `parallelism = 1`.
   Confirm against OWASP's current recommendation at
   implementation time. `[UNCERTAIN — living guidance]`
2. **Argon2 library** (§3.3). `Konscious.Security.Cryptography.
   Argon2` (managed, MIT) vs. `NSec` / `libsodium` (native).
   Recommendation is `Konscious`; confirm.
3. **`scope = "all-profiles"`** (§3.5). Ship in the first cut, or
   defer to a follow-up? Doubles the test matrix. Recommendation
   is to ship `profile` only in the first cut and follow up with
   the admin variant.
4. **Merge mode** (§1.3). Deferred by `EVOLUTION.md` §4.2 and
   this analysis; reopen only when a concrete merge-rule design
   exists.
5. **Archive extension** (`.mrz`, §3.1). Any objection? An
   MedReminder-specific extension aids OS-level file association
   and helps users recognize what the file is. Alternative:
   `.zip` and rely on the manifest to identify the format.
6. **Localized user guides.** English section ships with the PR;
   the four localized guides may follow, mirroring A1 / A5.
   Confirm this is acceptable.
7. **Passphrase minimum length** (§4.5). 12 characters is the
   proposed floor. Confirm 10 / 12 / 14.

---

## 13. Implementation plan

One PR on `feature/export-import`. Per `CLAUDE.md` §5, the PR is
opened **after the first commit**, and a `CHANGE_LOG.md` entry is
prepended when the PR opens. Indicative commit order:

1. **Application.** New namespace
   `MedReminder.Application.Export` with
   `ExportService` / `ImportService` interfaces and a POCO for
   `payload.json` shape (mirrors the entities). No I/O yet — this
   commit is types + shape only.
2. **Application.** `IArchiveCipher` port (Argon2id KDF +
   AES-GCM) with an in-memory test implementation; unit tests.
3. **Infrastructure.** `ArchiveCipher` adapter backed by
   `Konscious.Security.Cryptography.Argon2` + `AesGcm`. Full
   round-trip tests, tampered / wrong-passphrase failure tests
   (§8.2).
4. **Infrastructure.** `ExportService` implementation: snapshot
   the DB via `BackupService`, load entities via a temp EF Core
   context, serialize `payload.json`, encrypt, write ZIP. Unit
   tests on a synthetic DB.
5. **Infrastructure.** `ImportService` implementation: verify
   manifest, decrypt, validate hash, apply payload in a
   transaction, restore opt-in shared files. Unit tests including
   the pre-import DB backup safety step.
6. **UI.** `ExportDialog` and `ImportDialog` under
   `SettingsDialog`. `SaveFileDialog` / `OpenFileDialog`
   integration; `Progress<T>` wiring; error surfaces.
7. **Localization.** Keys in all five dictionaries. Italian
   awaits sign-off per §6.
8. **Docs.** `docs/EXPORT-FORMAT.md` (public format contract);
   `CHANGE_LOG.md` entry when the PR opens; short "Export and
   import" section in `docs/USER_GUIDE.en.md`.

Run `dotnet build` and `dotnet test` before every commit that
touches source (`CLAUDE.md` §8).

**Effort.** 2–3 developer-weeks including UI, format
documentation, tests and five-language localization
(`EVOLUTION.md` §4.3). `[INFERRED]`

---

## Change log for this document

- 2026-09-21 — initial draft (pre-implementation). Derived from
  `EVOLUTION.md` §4 and cross-checked against `BackupService`,
  `AppDataPaths`, `NotificationSettings`, `MailKitEmailNotificationService`
  and `CurrentProfile`. Chose Argon2id + AES-GCM (§3.3), ZIP
  wrapper with cleartext manifest (§3.1), overwrite-only import
  in the first cut (§1.3), and explicit opt-in for SMTP password
  inclusion with re-encryption (§3.4).
