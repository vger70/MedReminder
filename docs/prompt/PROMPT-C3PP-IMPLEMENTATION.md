# Implementation prompt — C.3++: IArchiveStorage abstraction + LocalFolderArchiveStorage

This file is the self-contained briefing for the Claude Code session
that will implement feature C.3++ (Phase 1). Read it completely
before touching any source file.

---

## 0. What you are about to implement

**Feature C.3++ (Phase 1)** introduces a pluggable storage
abstraction — `IArchiveStorage` — that decouples archive production
(`ExportService`) from archive delivery (where the archive is
stored). The only concrete implementation delivered in this phase is
`LocalFolderArchiveStorage`, which preserves the current C.3+
behaviour exactly: encrypted `.mrz` archives are written atomically
to a user-specified local folder synchronized by the user's own OS-
level sync agent.

**This is an infrastructure refactor, not a user-visible feature.**
The user sees no new UI, no new settings, and no change in backup
behaviour. The value is architectural: once `IArchiveStorage` exists,
native cloud-provider backends (OneDrive via Microsoft Graph, Google
Drive, Dropbox) can be added in a future phase without changing the
export/import services, the archive format, or the backup host.

The phase boundary and the rationale for deferring native providers
are detailed in `ANALYSIS-C3PP` §3, §4 and §14. The trigger for
Phase 2 (native provider integration) is the B.1 mobile companion
client (`EVOLUTION.md` §6).

**Hard precondition: C.3 and C.3+ must have shipped.** C.3++
introduces no archive format changes and reuses C.3's
`IExportService` and the C.3+ backup host. If either is absent on
`main`, ship those features first and rebase.

The authoritative design lives in one document — read it before
coding:

- **`docs/analysis/ANALYSIS-C3PP-CLOUD-PROVIDERS.md`** — the
  approved design. Sections §7 and §16 are the primary references
  for this phase. Where this prompt and the analysis document
  disagree, **the analysis document wins**.

Also read before making any architectural change:

- **`docs/analysis/ANALYSIS-C3PLUS-CLOUD-BACKUP.md`** — the C.3+
  backup host (`AutomaticBackupHostedService`) you will refactor.
- **`docs/analysis/ANALYSIS-C3-EXPORT-IMPORT.md`** — the archive
  format and services this feature relies on.
- **`CLAUDE.md`** — mandatory rules covering language policy, branch
  naming, PR workflow, localization, and the things to never do.
- **`docs/ANALYSIS.md`** — the base architecture (DI registration,
  per-profile paths, hosted-service pattern).

---

## 1. Hard boundaries — do NOT cross these

C.3++ Phase 1 is scoped strictly to the **abstraction layer and its
local-folder implementation**. The following are **out of scope by
design** (`ANALYSIS-C3PP` §1.3 and §13):

- **No native cloud provider APIs.** OneDrive, Google Drive, and
  Dropbox implementations are Phase 2 (`ANALYSIS-C3PP` §14). Do
  not add MSAL, `Microsoft.Graph`, `Google.Apis.Drive.v3`, or
  `Dropbox.Api` references now.
- **No `BackupSettings.CloudProvider` discriminator.** The field is
  not added until the first native provider ships (`ANALYSIS-C3PP`
  §9.1). Do not add a dead JSON field to `BackupSettings` in this
  phase.
- **No new user-visible UI.** The abstraction is invisible to the
  user. No new settings section, no new dialog, no new localization
  key.
- **No change to the `.mrz` archive format.** The format is the
  long-term interoperability contract. `IArchiveStorage` delivers
  archives; it does not produce them.
- **No new encryption scheme.** Argon2id + AES-GCM from C.3 remain
  the only encryption. The storage layer never decrypts.
- **No new `IHostedService`.** Refactor the existing
  `AutomaticBackupHostedService`; do not add a second timer.
- **No `EnsureCreated`.** C.3++ introduces no schema change.
- **No `SmtpClient` from `System.Net.Mail`.** MailKit is the only
  supported SMTP client. C.3++ does not touch email.
- **No live-DB file-sync.** The abstraction stores `.mrz` archives;
  it never exposes the live SQLite database.
- **Not a medical-device concern.**

If a reviewer asks for any of these, decline and reference
`ANALYSIS-C3PP` §1.3.

---

## 2. Preconditions — verify against the tree first

Before writing any code, confirm the following in the actual source:

1. **C.3 is on `main`.**
   `MedReminder.Application.Export.IExportService` exists and its
   `ExportAsync` method accepts a `char[]` passphrase (needed to
   zero the buffer after use). If missing, stop and ship C.3 first.

2. **C.3+ is on `main`.**
   `AutomaticBackupHostedService` has a `CloudFolderTick` path (or
   equivalent) that currently writes `.mrz` archives via an inline
   `File.Move` call. Locate the exact method and line — this is the
   code you will refactor to call `IArchiveStorage.UploadAsync`.

3. `BackupSettings` has `CloudFolderEnabled`, `CloudFolderDirectory`
   and `CloudFolderRetention` fields (added in C.3+,
   `ANALYSIS-C3PLUS` §3.1). Confirm their exact names.

4. The existing `PruneCloudFolderAsync` (or equivalent) in
   `BackupService` prunes `.mrz` files by date. Confirm whether
   pruning is called from the host or from a service. After the
   refactor, pruning continues to use `IArchiveStorage.DeleteAsync`
   indirectly via `ListAsync`.

5. `DictionaryParityTests` (or its current equivalent) enforces
   key parity across all five `assets/localization/strings.<lang>.json`
   files. Since C.3++ adds no localization keys, this test must
   still pass without modification.

6. Confirm the DI composition root — where `IExportService` and the
   backup host are registered. You will add `IArchiveStorage` and
   `LocalFolderArchiveStorage` to the same composition root.

Document what you find (brief notes in the commit message); update
`ANALYSIS-C3PP` §2.2 if any fact is wrong.

---

## 3. Branch and PR

Per `CLAUDE.md` §5:

- Branch name: **`feature/archive-storage-abstraction`**, based on
  `main`.
- Open a pull request **after the first commit**, not at the end.
- Prepend a `CHANGE_LOG.md` entry when the PR opens (follow the
  format documented at the top of that file).

---

## 4. Implementation order

Follow `ANALYSIS-C3PP` §16. Each step must leave `dotnet build`
and `dotnet test` green before the next commit.

### Step 1 — Application: `ArchiveInfo` and `IArchiveStorage`

Files (both new):

- `src/MedReminder.Application/Abstractions/ArchiveInfo.cs`
- `src/MedReminder.Application/Abstractions/IArchiveStorage.cs`

`ArchiveInfo` (`ANALYSIS-C3PP` §7.2):

```csharp
public sealed record ArchiveInfo(
    string Id,
    string Name,
    DateTimeOffset CreatedAtUtc,
    long SizeBytes);
```

`IArchiveStorage` (`ANALYSIS-C3PP` §7.2):

```csharp
public interface IArchiveStorage
{
    Task<string> UploadAsync(
        Stream archive,
        string suggestedName,
        CancellationToken ct);

    Task<Stream> DownloadAsync(string id, CancellationToken ct);

    Task<IReadOnlyList<ArchiveInfo>> ListAsync(CancellationToken ct);

    Task DeleteAsync(string id, CancellationToken ct);
}
```

Key constraints:

- Both types live in `MedReminder.Application.Abstractions`.
  Domain is free of Windows APIs and EF Core; Application must
  remain free of `net10.0-windows` dependencies.
- `Id` is **opaque** — callers must never parse or construct an `Id`
  value. Document this in the XML summary on the `Id` property.
- `UploadAsync` receives the archive stream with the position at 0
  and must close/dispose the stream on exit.
- `DownloadAsync` returns a stream **owned by the caller** (caller
  disposes it).
- No tests at this step — the contract test base (Step 4) requires
  a concrete implementation.

### Step 2 — Infrastructure: `LocalFolderArchiveStorage`

File (new):
`src/MedReminder.Infrastructure/Backup/LocalFolderArchiveStorage.cs`

`Id` = absolute file path. Implementation details
(`ANALYSIS-C3PP` §7.3):

- **`UploadAsync`**: write the stream to a temp file in the system
  temp directory, then `File.Move(temp, final, overwrite: false)`.
  This is the same atomic-move discipline C.3+ already uses; do not
  change it.  The `final` path is
  `Path.Combine(configuredFolder, suggestedName)`.
- **`DownloadAsync`**: return `File.OpenRead(id)`.
- **`ListAsync`**: enumerate `*.mrz` in the configured folder; return
  entries sorted by `CreatedAtUtc` descending. Populate
  `CreatedAtUtc` from `File.GetCreationTimeUtc`. Return an empty
  list when the folder does not exist (do not throw).
- **`DeleteAsync`**: call `File.Delete(id)`. Let `FileNotFoundException`
  propagate — the caller handles missing files.

Constraints:

- The configured folder is passed via the constructor (or via
  `IOptions<BackupSettings>`). Do not hard-code a path.
- If the folder does not exist when `UploadAsync` is called, log a
  warning and throw a `DirectoryNotFoundException` (the host catches
  this and skips the tick per `ANALYSIS-C3PP` §4.3).
- Never log the passphrase, archive contents, or file byte ranges.
- Mark the class `internal sealed`.

Register in the DI container in the Infrastructure project's
composition extension. Bind `IArchiveStorage` →
`LocalFolderArchiveStorage` with the folder sourced from
`BackupSettings.CloudFolderDirectory`. This registration replaces
the inline path that was previously embedded in the backup host.

### Step 3 — Application: refactor `AutomaticBackupHostedService`

Locate the `CloudFolderTick` body (or equivalent) in
`AutomaticBackupHostedService`. Currently it calls
`ExportService.ExportAsync`, obtains a temp path, and moves it
with `File.Move` into `CloudFolderDirectory`.

Replace the inline move with:

```csharp
await using var archiveStream = File.OpenRead(tempPath);
await _archiveStorage.UploadAsync(
    archiveStream,
    $"medreminder-{profileId:N}-{timestamp:yyyyMMdd-HHmmss}.mrz",
    ct);
```

Then delete the temp file after the upload completes (or on failure
in a `finally` block). `LocalFolderArchiveStorage.UploadAsync`
performs the atomic move internally — the host no longer owns that
responsibility.

Adjust the existing pruning call (if any) to use
`_archiveStorage.ListAsync` + `_archiveStorage.DeleteAsync` instead
of directly enumerating the folder. If pruning currently lives in
`BackupService.PruneCloudFolderAsync`, keep that method and have it
accept an `IArchiveStorage` parameter — or inline the list+delete
directly in the host. Pick one approach and stay consistent.

Constraints:

- The existing raw-DB backup path (if separate from the
  cloud-folder path) must remain unchanged.
- Wrap the cloud-folder path in its own `try/catch`; log warnings
  on failure; do not let a storage failure skip or abort the raw-DB
  path.
- Zero the passphrase buffer after handing it to `ExportService`
  (unchanged from C.3+).

Adjust the integration test (if present) that previously tested the
inline `File.Move` so that it now tests through
`LocalFolderArchiveStorage`.

### Step 4 — Application.Tests: `IArchiveStorage` contract test base

File (new):
`tests/MedReminder.Application.Tests/Backup/ArchiveStorageContractTests.cs`

Write an abstract test base that exercises the four operations on any
`IArchiveStorage` implementation (`ANALYSIS-C3PP` §10.1):

- **Happy path.** `UploadAsync` (1 KB random bytes) → `ListAsync`
  returns one entry with the correct name and size → `DownloadAsync`
  round-trips the bytes → `DeleteAsync` removes it → `ListAsync`
  returns empty.
- **Atomic move.** Inject a fault that throws after the temp file is
  written but before `File.Move` completes. Confirm the target folder
  contains no partial file.
- **Folder missing.** Call `UploadAsync` when the configured folder
  does not exist; confirm the expected exception type.
- **Retention.** Upload N + 1 archives; call `DeleteAsync` on the
  oldest entry returned by `ListAsync`; confirm N files remain.

Add a concrete `LocalFolderArchiveStorageContractTests` class that
derives from the base and supplies an instance of
`LocalFolderArchiveStorage` backed by a temporary folder.

The abstract base is the seam that prevents contract drift when
future implementations are added.

### Step 5 — Documentation

- **`CHANGE_LOG.md`**: prepend an entry when the PR opens. Follow
  the format at the top of that file. Mention that the change is
  internal (no user-visible behaviour change) and note the Phase 1
  scope.
- **`docs/analysis/ANALYSIS-C3PLUS-CLOUD-BACKUP.md`**: append a
  short paragraph at the end of §4 noting that the inline
  `File.Move` is now abstracted behind `IArchiveStorage`, and that
  `LocalFolderArchiveStorage` preserves the atomic-move discipline
  from the original design.
- Do **not** add a new user-guide section. The abstraction is
  invisible to the user.

---

## 5. Decided items — not open for re-debate

These items in `ANALYSIS-C3PP` §15 are decided by the analysis and
must not be re-opened without the product owner's explicit sign-off:

- **`IArchiveStorage` introduction timing** (§15.1): introduce the
  abstraction and `LocalFolderArchiveStorage` in Phase 1, alongside
  or immediately after C.3+.
- **`ArchiveInfo.Id` type** (§15.2): opaque `string`. No
  discriminated union, no `ArchiveId` value object.
- **No `BackupSettings.CloudProvider` field** (§15.3): not added
  until the first native provider ships.
- **No native provider in this PR** (§14): `LocalFolderArchiveStorage`
  is the only implementation. OneDrive and Google Drive are Phase 2.
- **Single-writer discipline** (§1.2, §13): `IArchiveStorage` does
  not introduce conflict resolution. The user model from C.3+ is
  unchanged.

---

## 6. Still open — settle at implementation time

- **Pruning call site** (`ANALYSIS-C3PP` §16, Step 3). Recommendation:
  inline list+delete in the host's cloud-folder tick. Only extract a
  helper if the resulting method exceeds a comfortable size. Decide
  when you locate the existing pruning code.
- **Temp-file cleanup on `UploadAsync` failure.** Recommendation:
  delete the temp file in a `finally` block inside the host, before
  propagating the exception. Confirm that `LocalFolderArchiveStorage`
  does not attempt its own temp-file cleanup (the host owns the
  temp file until `UploadAsync` succeeds).

Ask before implementing if any of these is unclear at the relevant
step.

---

## 7. Conventions and constraints (from `CLAUDE.md`)

- All identifiers, comments, log messages, exception messages, XML
  docs, and commit messages must be in **English**. Italian is used
  only in chat with the user and in the localized `strings.it.json`
  / `USER_GUIDE.it.md`.
- Run `dotnet build` and `dotnet test` before every commit that
  touches source.
- No `EnsureCreated()` — C.3++ introduces no schema change.
- No `SmtpClient` from `System.Net.Mail` — MailKit only.
- No plaintext passwords or PII to logs.
- No new localization keys — C.3++ is user-invisible.
- Keep Domain free of Windows-specific APIs and EF Core.
  `IArchiveStorage` and `ArchiveInfo` live in `Application`; the
  DPAPI adapter and `LocalFolderArchiveStorage` live in
  `Infrastructure`.
- **Never bypass** the single-instance mutex or force-close SQLite
  connections outside of the documented backup / restore paths
  (`CLAUDE.md` §9).

---

## 8. Acceptance criteria

The PR is ready to merge when:

1. `dotnet build MedReminder.sln -c Release` is green.
2. `dotnet test MedReminder.sln -c Release` is green, including the
   new tests from Step 4.
3. `AutomaticBackupHostedService` no longer contains a direct
   `File.Move` call for the cloud-folder path — it delegates to
   `IArchiveStorage.UploadAsync`.
4. `IArchiveStorage` and `ArchiveInfo` are in
   `MedReminder.Application.Abstractions` with no Windows-specific
   type references.
5. `LocalFolderArchiveStorage` is `internal sealed` and registered
   in the DI container.
6. A scheduled tick with `CloudFolderEnabled = true` produces the
   same `.mrz` in the same folder as before — user-visible behaviour
   is identical to C.3+.
7. The `IArchiveStorage` contract test base exists and
   `LocalFolderArchiveStorageContractTests` derives from it.
8. No new localization keys were added; `DictionaryParityTests`
   passes without modification.
9. `CHANGE_LOG.md` has a new entry for this PR.
10. `docs/analysis/ANALYSIS-C3PLUS-CLOUD-BACKUP.md` notes that the
    inline move is now abstracted behind `IArchiveStorage`.

---

*Generated 2026-09-23. Authoritative source:
`ANALYSIS-C3PP-CLOUD-PROVIDERS.md`. Phase 1 only (abstraction + local
implementation). Native providers deferred to Phase 2 / B.1 milestone.
Preconditions: C.3 and C.3+ shipped.*
