# Implementation prompt — C.3: Manual export / import

This file is the self-contained briefing for the Claude Code session
that will implement feature C.3. Read it completely before touching
any source file.

---

## 0. What you are about to implement

**Feature C.3** adds two user-triggered commands to MedReminder:

- **Export all data** — writes an **encrypted ZIP archive**
  (`.mrz`) containing the current profile's data plus optional
  shared settings. The archive is portable across Windows accounts
  and machines, so it also serves as the recommended device
  migration path.
- **Import from export** — reads a previous archive, verifies its
  integrity and version, and applies it to the current profile in
  **Overwrite** mode. Merge is out of scope.

Encryption uses **Argon2id** (KDF) + **AES-GCM** (cipher) with a
user-chosen passphrase. **DPAPI is deliberately not used** —
DPAPI ties data to the Windows account and defeats migration.

The authoritative design lives in two documents — read them before
coding:

- **`docs/analsis/ANALYSIS-C3-EXPORT-IMPORT.md`** — the approved design.
  Sections §3–§13 describe the archive layout, encryption details,
  runtime, UI, localization, tests and implementation order. Where
  this prompt and the analysis document disagree, **the analysis
  document wins**.
- **`docs/EVOLUTION.md` §4** — the original sketch, retained for
  context.

Also read before making any architectural change:

- **`CLAUDE.md`** — mandatory rules covering language policy,
  branch naming, PR workflow, localization, and the things to
  never do.
- **`docs/ANALYSIS.md`** — the base architecture (data model, EF
  Core patterns, DPAPI usage, per-profile paths).
- **`docs/analsis/ANALYSIS-MULTI-USER.md`** §3 and §11 — the per-profile
  data layout the export follows.

---

## 1. Hard boundaries — do NOT cross these

C.3 is scoped strictly to **user-triggered encrypted export /
import in Overwrite mode**. The following are **out of scope by
design** (`ANALYSIS-C3` §1.3):

- **No plaintext export path.** The archive is always encrypted.
  If the user's passphrase is empty or below the minimum length,
  refuse the export — do not produce an unencrypted file.
- **No merge mode.** Overwrite only. Merge is deferred by both
  `EVOLUTION.md` §4.2 and `ANALYSIS-C3` §1.3.
- **No cloud target inside the app.** C.3 writes to a local file
  the user picks. Cloud drop is C.3+ (sibling analysis).
- **No passphrase recovery, no key escrow.** A lost passphrase
  means a lost archive by design.
- **No cross-account carry of `smtp.protected` as-is.** DPAPI is
  bound to the Windows account. The SMTP password may be included
  only opt-in and only after being re-encrypted with the archive
  key (`ANALYSIS-C3` §3.4).
- **No new `IHostedService`.** C.3 is user-initiated only. Do not
  add a `PeriodicTimer`, do not add a scheduled export.
- **Not a medical-device concern.** No adherence, no clinical
  wording. C.3 moves data; it does not read therapy state at
  runtime.
- **No `EnsureCreated`** — but C.3 introduces no schema change,
  so the point does not arise (`CLAUDE.md` §9).

If a reviewer asks for any of these, decline and reference
`ANALYSIS-C3` §1.3.

---

## 2. Preconditions — verify against the tree first

Before writing any code, confirm the following in the actual
source:

1. `BackupService.ExportProfileAsync` still uses
   `SqliteConnection.BackupDatabase` and can produce a
   torn-write-free copy of the live DB (§4.1 step 2).
2. `AppDataPaths.GetProfileDataDirectory` resolves to the
   per-profile directory expected by
   `BackupService.ResolveProfileDatabasePath`.
3. Enumerate the EF Core entities and their configurations under
   `src/MedReminder.Infrastructure/Persistence/Configurations/`.
   `payload.json` must cover every entity type. If A5 has landed,
   `DoseReminderEvent` is one of them; if A3 has landed, the
   `NotificationSettings.CaregiverAddress` field is part of the
   per-profile `notifications.settings.json`.
4. `SettingsDialog` writes `notifications.settings.json` /
   `smtp.settings.json` / `backup.settings.json` /
   `user.settings.json` (search each filename and follow the
   writer).
5. `SmtpCredentialStore` reads / writes `smtp.protected` via
   DPAPI (`CurrentUser` scope). Confirm the API used to
   decrypt / re-encrypt.
6. `DictionaryParityTests` (or its current equivalent) enforces
   parity across `assets/localization/strings.<lang>.json` for
   the five shipped languages.

Document what you find (brief notes in the commit message are
fine); update the analysis document if any fact is wrong.

---

## 3. Branch and PR

Per `CLAUDE.md` §5:

- Branch name: **`feature/export-import`**, based on `main`.
- Open a pull request **after the first commit**, not at the end.
- Prepend a `CHANGE_LOG.md` entry when the PR opens (follow the
  format documented at the top of that file).

---

## 4. Implementation order

Follow `ANALYSIS-C3` §13. Each step must leave `dotnet build` and
`dotnet test` green before the next commit.

### Step 1 — Application: shapes and ports

Files under
`src/MedReminder.Application/Export/` (new namespace):

- `ExportManifest` — POCO matching `ANALYSIS-C3` §3.1 fields.
- `ExportPayload` — POCO matching `ANALYSIS-C3` §3.2 fields
  (one property per entity type + a `Shared` sub-object).
- `IExportService` — `Task<string> ExportAsync(
   ExportOptions options, char[] passphrase, IProgress<int>?
   progress, CancellationToken ct)`.
- `IImportService` — `Task ImportAsync(string archivePath,
   char[] passphrase, ImportOptions options, IProgress<int>?
   progress, CancellationToken ct)`.
- `IArchiveCipher` — abstraction for Argon2id + AES-GCM (KDF +
   encrypt / decrypt). Two methods:
   `byte[] DeriveKey(char[] passphrase, byte[] salt, Argon2Params
   p)` and
   `(byte[] nonce, byte[] tag, byte[] ciphertext) Encrypt(byte[]
   key, byte[] plaintext)` + matching `Decrypt`.

No I/O yet. This commit is types + interfaces only.

### Step 2 — Application: unit tests for `IArchiveCipher`

Provide a testable in-memory implementation for
`IArchiveCipher` (skeleton with real Argon2id + AesGcm). Cover:

- Deterministic KDF (same passphrase + salt → same key).
- Distinct salts → distinct keys.
- Round-trip encrypt/decrypt on a small payload.
- Wrong key throws on decrypt.
- Tampered ciphertext throws on decrypt.

### Step 3 — Infrastructure: `ArchiveCipher` adapter

File:
`src/MedReminder.Infrastructure/Export/ArchiveCipher.cs`.

Add NuGet reference:
`Konscious.Security.Cryptography.Argon2` (managed, MIT — the
choice recommended in `ANALYSIS-C3` §3.3). AES-GCM comes from
`System.Security.Cryptography.AesGcm` (in-box).

Parameters for the first cut (`ANALYSIS-C3` §3.3):

- Argon2id: `iterations = 3`, `memoryKiB = 65536`,
  `parallelism = 1`, output = 32 bytes.
- AES-GCM: 256-bit key, 12-byte random nonce, 16-byte tag.

The adapter never logs the passphrase, the derived key, or the
plaintext (`CLAUDE.md` §9). Zero the derived key buffer after
use.

Extend the tests from Step 2 to also cover this concrete adapter.

### Step 4 — Infrastructure: `ExportService`

File:
`src/MedReminder.Infrastructure/Export/ExportService.cs`.

Algorithm exactly as `ANALYSIS-C3` §4.1:

1. Validate passphrase (`length >= MinPassphraseLength`, default
   12 — `ANALYSIS-C3` §4.5).
2. Snapshot the current profile's DB into a temp file via
   `BackupService.ExportProfileAsync`. Do **not** read the live
   DB directly — the snapshot is the torn-write-free source.
3. Load rows via a temporary read-only EF Core context bound to
   the temp DB. Serialize into `ExportPayload`.
4. Collect opt-in shared files per `ExportOptions.Include*`
   flags (`ANALYSIS-C3` §3.4). For SMTP password: DPAPI-decrypt
   into a `byte[]`; then re-encrypt with the archive key into
   `payload.Shared.SmtpPasswordEncrypted`; zero the intermediate
   buffer.
5. Serialize `payload.json` (UTF-8, no BOM, camelCase, ISO-8601
   timestamps, `System.Text.Json`).
6. Derive key from the passphrase and a fresh random salt.
7. AES-GCM-encrypt `payload.json` → `(nonce, tag, ciphertext)`.
8. Compute SHA-256 over the plaintext for the manifest hash.
9. Build the manifest (`ExportManifest`).
10. Write the ZIP via `System.IO.Compression.ZipArchive`:
    `manifest.json` (cleartext), `payload.enc` (ciphertext + tag
    appended, or as two entries — pick one and document it in
    `docs/EXPORT-FORMAT.md`).
11. Delete the temp DB snapshot.

Report progress via `IProgress<int>` at least at 0 %, 50 % and
100 %.

**Infrastructure tests** cover the round-trip (populate an
in-memory DB, export to a temp file, delete the DB, import into
a new empty DB, assert row equality — see Step 6).

### Step 5 — Infrastructure: `ImportService`

File:
`src/MedReminder.Infrastructure/Export/ImportService.cs`.

Algorithm exactly as `ANALYSIS-C3` §4.2:

1. Open the ZIP, parse `manifest.json`.
2. Verify `manifest.format == "medreminder-export"` and
   `formatVersion <= currentFormatVersion` (§4.3). Reject newer
   with a friendly localized message.
3. Derive key from passphrase + `manifest.kdf.*`.
4. Decrypt `payload.enc` using `manifest.cipher.*`. A tag
   mismatch surfaces as "wrong passphrase" (§4.4).
5. Verify `SHA-256(plaintext) == manifest.payload.sha256Base64`.
   Mismatch → "corrupt archive" error.
6. Parse `payload.json`; check `schemaVersion <=
   currentSchemaVersion`.
7. UI confirmation dialog (Step 7) has already been shown by the
   caller; the service assumes user consent.
8. Pre-import safety backup: rename the current profile DB to
   `<db>.bak-YYYYMMDDHHmmss` (mirrors
   `BackupService.ImportProfileAsync`).
9. In a single EF Core transaction: truncate the target
   profile's tables in FK-safe order, insert the payload's rows,
   commit.
10. Apply opt-in shared file restores:
    - `smtp.settings.json` from `payload.Shared.SmtpSettings`.
    - `backup.settings.json` from `payload.Shared.BackupSettings`.
    - `user.settings.json` from `payload.Shared.UserSettings`.
    - `smtp.protected`: if `payload.Shared.SmtpPasswordEncrypted`
      is present, AES-GCM-decrypt it with the archive key,
      DPAPI-re-encrypt into `smtp.protected` on the current
      Windows account. Zero the intermediate buffer.
11. Return success. The UI (Step 7) surfaces the restart prompt.

Report progress via `IProgress<int>`.

**Failure surfaces** must match `ANALYSIS-C3` §4.4 verbatim (wrong
passphrase, corrupt archive, unsupported version — three distinct
localization keys).

### Step 6 — Infrastructure tests (round-trip + failure modes)

Cover the full test list from `ANALYSIS-C3` §8:

- Round-trip of every entity type (assert equality field-by-field).
- Wrong passphrase → localized failure, target DB untouched.
- Tampered `payload.enc` byte → same failure surface as wrong
  passphrase; documented in `docs/EXPORT-FORMAT.md`.
- Truncated ZIP → friendly error, not a crash.
- `formatVersion = currentFormatVersion + 1` → refused.
- `schemaVersion = currentSchemaVersion + 1` → refused.
- Older `schemaVersion` → imports; missing columns take
  defaults.
- Passphrase shorter than `MinPassphraseLength` → export refused
  before any file is written.
- Argon2id parameter round-trip (same passphrase + salt + params
  → same key).
- Snapshot isolation: interleaved write to the live DB during
  export does not corrupt the snapshot (regression test only).

### Step 7 — UI: `ExportDialog` and `ImportDialog`

Files under
`src/MedReminder.UI/Forms/` (search the folder for the current
dialog conventions):

- Entry points in `SettingsDialog` next to the existing backup
  UI. Add two buttons wired to
  `Ui.SettingsDialog.File.ExportData` and
  `Ui.SettingsDialog.File.ImportData`.
- `ExportDialog` implements `ANALYSIS-C3` §5.2: destination
  picker, passphrase + confirm-passphrase fields, scope radio
  (admin-only "All profiles" is disabled for non-admin
  profiles), opt-in checkboxes, warnings, progress bar, success
  dialog.
- `ImportDialog` implements `ANALYSIS-C3` §5.3: source picker,
  passphrase field, info panel populated from the manifest before
  decryption, mandatory "I understand" confirmation checkbox,
  progress bar, restart prompt on success.

Both dialogs run the service call on a background task with
`Progress<int>` bound to the progress bar. Cancellation via
`CancellationTokenSource` on a Cancel button.

**Passphrase handling in the UI.** Bind the two passphrase
textboxes to `char[]` buffers (or read `TextBox.Text` and zero it
in `Dispose`); never log the text; disable clipboard copy on the
passphrase textboxes only if the WinForms API for it is available
without extra dependencies — otherwise document the caveat and
move on.

### Step 8 — Localization

Add all keys from `ANALYSIS-C3` §6 to **every** dictionary under
`assets/localization/` (`en`, `it`, `fr`, `es`, `de`). Missing
keys fail the build via `DictionaryParityTests`.

For the four non-English dictionaries, follow the A1 / A5
precedent: ship with `"TODO(<lang>): <english fallback>"`
placeholders and finalize wording after the form is inspected.
Italian wording requires maintainer sign-off before it lands.

### Step 9 — Format documentation

Create `docs/EXPORT-FORMAT.md`. Include:

- The ZIP layout (§3.1).
- The full `manifest.json` schema, field by field.
- The full `payload.json` schema, listing every entity type and
  the fields present in `schemaVersion = 1`.
- The Argon2id parameters and the AES-GCM parameters.
- The evolution discipline (additive `schemaVersion` /
  `formatVersion`).
- A "decrypt with off-the-shelf tools" section showing how to
  reproduce the KDF + AES-GCM decryption in a plain script
  (`ANALYSIS-C3` §7).

This document is a **shipped public contract** — not internal
notes.

### Step 10 — User guide and CHANGE_LOG.md

- **`CHANGE_LOG.md`**: prepend an entry when the PR opens.
  Follow the format at the top of that file.
- **`docs/USER_GUIDE.en.md`**: add an "Export and import"
  section documenting the passphrase discipline (no recovery),
  the opt-in checkboxes, the overwrite-only semantics, and the
  restart-after-import step. The four localized guides may
  follow in a follow-up.

---

## 5. Decided items — not open for re-debate

These items in `ANALYSIS-C3` §12 are decided by the analysis and
must not be re-opened without the product owner's explicit
sign-off:

- **KDF / cipher choice** (§3.3): Argon2id + AES-GCM. Do not
  substitute PBKDF2, scrypt, or AES-CBC.
- **DPAPI-free archive** (§1.2): the archive key is derived from
  the passphrase; DPAPI is used only to rewrap the SMTP password
  on the target machine.
- **Overwrite-only import** (§1.3): merge is out of scope.
- **No plaintext mode** (§1.3): the export is always encrypted.
- **No cloud target inside the app** (§1.3): C.3+ owns that.
- **No hosted service** (§4.6): user-initiated only.

---

## 6. Still open — settle at implementation time

- **Argon2id parameters** (`ANALYSIS-C3` §12 item 1). Confirm the
  proposed defaults against OWASP's current recommendation.
- **`scope = "all-profiles"`** (`ANALYSIS-C3` §12 item 3). Ship
  in the first cut, or defer to a follow-up? Recommendation:
  ship `profile` only in the first cut.
- **Archive extension** (`ANALYSIS-C3` §12 item 5). `.mrz` or
  plain `.zip`? Recommendation: `.mrz`.
- **Passphrase minimum length** (`ANALYSIS-C3` §12 item 7).
  Recommendation: 12 characters.
- **Localized user guides** (`ANALYSIS-C3` §12 item 6). Ship
  English with the PR; the four localized guides may follow.

Ask before implementing if any of these is not recorded in
`ANALYSIS-C3` §12 by the time you reach the relevant step.

---

## 7. Conventions and constraints (from `CLAUDE.md`)

- All identifiers, comments, log messages, exception messages,
  XML docs, and commit messages must be in **English**. Italian
  is used only in chat with the user and in the localized
  `strings.it.json` / `USER_GUIDE.it.md`.
- Run `dotnet build` and `dotnet test` before every commit that
  touches source.
- No `EnsureCreated()` — C.3 introduces no schema change, so the
  point does not arise.
- No `SmtpClient` from `System.Net.Mail` — MailKit only. C.3
  does not touch SMTP transport code, but do not add a second
  SMTP client for any reason.
- No plaintext passwords or PII to logs. In particular, never
  log the passphrase, the derived key, or `payload.json`
  contents.
- When adding a UI string, add the key to **every** localization
  dictionary.
- Keep Domain free of Windows-specific APIs and EF Core
  references. `IArchiveCipher` lives in `Application`; the
  DPAPI-touching path lives in `Infrastructure`.

---

## 8. Acceptance criteria

The PR is ready to merge when:

1. `dotnet build MedReminder.sln -c Release` is green.
2. `dotnet test MedReminder.sln -c Release` is green, including
   the new tests in Steps 2, 3, 4, 5 and 6.
3. All localization keys from `ANALYSIS-C3` §6 are present in
   all five dictionaries (`DictionaryParityTests` passes).
4. A round-trip export → delete DB → import restores every
   entity to a byte-identical row set.
5. Wrong passphrase, tampered payload, and unsupported version
   each surface a localized error and leave the target DB
   untouched.
6. A passphrase shorter than the configured minimum refuses the
   export before any file is written.
7. `smtp.protected` is never carried across accounts as-is; when
   opt-in, the password round-trips via the archive key and is
   DPAPI-re-encrypted on the target machine.
8. `docs/EXPORT-FORMAT.md` documents the archive layout, the
   manifest schema, the payload schema, and the KDF / cipher
   parameters completely enough for a third party to write a
   decrypter.
9. `CHANGE_LOG.md` has a new entry for this PR.
10. `docs/USER_GUIDE.en.md` has an "Export and import" section.

---

*Generated 2026-09-21. Authoritative source:
`ANALYSIS-C3-EXPORT-IMPORT.md`.*
