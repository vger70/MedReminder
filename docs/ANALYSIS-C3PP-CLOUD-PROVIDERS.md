# ANALYSIS — C.3++: Native Cloud Provider Integration Strategy

Design document, **prior** to implementation. Corresponds to the
architecture evolution sketched in `EVOLUTION.md` §2.0 (the
multi-device track beyond C.3+). Follows the structure of
`ANALYSIS-C3PLUS-CLOUD-BACKUP.md`.

> **This is a decision and deferral document, not an implementation
> plan.** Its purpose is to record — with full technical motivation
> — why native cloud-provider APIs are not introduced alongside C.3
> and C.3+, what the condition for reconsidering that decision is,
> and what the storage abstraction must look like so the door stays
> open without locking the codebase into a premature dependency.
> The "Decisions still to confirm" section is the only remaining
> zone of ambiguity that needs input.

Epistemic classification, aligned with the sibling documents:
`[VERIFIED]` (checked against the current tree),
`[VERIFIED against ANALYSIS.md]` (specified there, tree check still
applies), `[INFERRED]` (deduction from verified facts),
`[UNCERTAIN]` (hypothesis pending confirmation).

**Hard precondition: C.3 and C.3+ must have shipped, or be in
progress.** C.3++ is not an alternative to C.3+; it is the
evolution that becomes available once C.3+ has established the
`.mrz` archive format and the `IArchiveStorage` abstraction
introduced here.

---

## 1. Scope

### 1.1 Problem

C.3 and C.3+ intentionally route all cloud storage through the
operating-system file system: the app writes encrypted `.mrz`
archives to a local folder; the user's sync agent (OneDrive,
iCloud Drive, Dropbox, Google Drive Desktop) handles the upload.
This design is deliberate — see §3 for why it is the right
starting point — but it has two limitations that compound as the
product grows:

- **User-facing friction for the non-technical audience.** The
  typical MedReminder user is managing their own or a relative's
  medication, not configuring sync agents. "Pick a cloud-synced
  folder" requires the user to know what a synced folder is and
  where the provider installed it, which is opaque on Windows
  (OneDrive's folder may be under `C:\Users\<name>\OneDrive –
  Personal\` or under a tenant-routed path on domain-joined
  machines) and even more opaque for iCloud on Windows.
- **Future mobile client (B.1, `EVOLUTION.md` §6).** A phone
  companion that wants to read the latest snapshot cannot rely on
  a provider's Windows sync agent: the mobile device receives
  files through the provider's own API, not through the filesystem.
  The file-system shortcut that works cleanly on the desktop has
  no equivalent on Android or iOS — both sandboxes expose the
  provider's content only through a `ContentProvider` or a
  `UIDocumentPickerViewController`, which resolve to the provider
  API at runtime.

The question this document answers is: **should MedReminder
introduce native cloud-provider API integrations now, together
with C.3+, or later, and under what conditions?**

### 1.2 Goal

1. Decide whether native cloud-provider APIs (OneDrive via
   Microsoft Graph, Google Drive, Dropbox) belong in the same
   release as C.3+, in a later release, or never.
2. Define an `IArchiveStorage` port in `MedReminder.Application`
   that makes the storage backend pluggable **without any
   change to the C.3 archive format, the C.3 services, or the
   C.3+ automatic-backup host** — regardless of which
   implementation ships first.
3. Analyse each major provider for feasibility, OAuth complexity,
   platform support and maintenance cost.
4. Document the Android / iOS scenario that would make native
   APIs necessary, so the trigger is explicit rather than
   reactive.

### 1.3 What C.3++ is NOT

- **Not a replacement for C.3+.** The local-folder model ships
  with C.3+. C.3++ adds an optional native-API backend; it does
  not remove the folder-based one. A user may use both
  simultaneously (the abstraction layer routes them independently)
  or neither.
- **Not a sync engine.** Native provider APIs provide reliable
  upload / download / list / delete, not conflict resolution.
  The single-writer discipline from C.3+ (`ANALYSIS-C3PLUS` §1.2)
  is preserved verbatim in every `IArchiveStorage` implementation.
- **Not a medical-device concern.** Same posture as C.3 and C.3+
  (`CLAUDE.md` §1).
- **Not a backend service.** The app continues to store only
  `.mrz` ciphertext archives in the provider's storage. No
  server component is introduced; the end-to-end-encrypted sync
  with a dedicated backend is C.1 (`EVOLUTION.md` §7), a
  separate and much larger undertaking.
- **Not a cloud-provider detection heuristic.** The app does not
  try to detect which providers the user has installed. The user
  selects a backend explicitly in settings, exactly as they
  select a folder today.
- **Not a commitment.** This document explicitly defers native
  provider integration until the B.1 mobile client justifies the
  added complexity. Until that trigger fires, only
  `LocalFolderArchiveStorage` is shipped.

---

## 2. Background — the C.3 / C.3+ foundation

### 2.1 C.3 archive format (`ANALYSIS-C3`)

C.3 defines the `.mrz` container: a ZIP with a cleartext
`manifest.json` carrying the schema version, app version, export
timestamp (UTC), payload hash, and the optional `source` /
`device.hostName` fields introduced by C.3+. The payload is
encrypted via AES-GCM, keyed via Argon2id. The format is
intentionally **provider-independent**: it is safe to store on
any object storage, any file system, or any cloud drive. `[VERIFIED
against ANALYSIS-C3 §3.1]`

### 2.2 C.3+ backup host (`ANALYSIS-C3PLUS`)

C.3+ extends `AutomaticBackupHostedService` with a `CloudFolder`
target that writes `.mrz` archives to a user-specified local
folder. The target is abstracted behind a move-then-name primitive
rather than a direct call to a storage API, so the archive
production and the storage delivery are already logically
separated. The `IArchiveStorage` abstraction introduced in §7.2
makes that separation explicit. `[INFERRED from ANALYSIS-C3PLUS
§4.1]`

### 2.3 The strategic separation

The design enforces a three-layer pipeline:

```
Data → MRZ Archive → Storage / Transport
```

`ExportService` owns the first arrow (domain entities → archive).
`IArchiveStorage` owns the second (archive → wherever it lives).
This means a `OneDriveArchiveStorage` can be dropped in without
touching `ExportService`, `ImportService`, the Argon2id key
derivation, or the AES-GCM encryption. The archive format is the
long-term interoperability contract; the storage backend is a
replaceable adapter.

---

## 3. Why the current approach is the right starting point

### 3.1 Provider independence

A single `LocalFolderArchiveStorage` implementation works with
every provider that exposes a sync folder on the local file
system:

- Microsoft OneDrive (personal and work)
- iCloud Drive (Windows app)
- Dropbox
- Google Drive Desktop
- Box Drive
- Synology Drive
- Nextcloud Desktop
- NAS synchronization tools
- Any future provider

No provider-specific code is required. A user switching from
Dropbox to OneDrive reconfigures the OS-level sync agent and
points MedReminder at the new folder — no app update needed.

### 3.2 Operational simplicity

The application at the storage layer:

- writes files
- reads files
- moves files atomically before the sync agent sees them

There are no OAuth flows, no token refreshes, no SDK dependencies,
no app registrations in any developer portal, no cloud API quotas,
no rate-limiting, and no network failure modes inside the archive
write path. `[VERIFIED — filesystem write is the only I/O
primitive used]`

### 3.3 Security alignment

Only encrypted `.mrz` archives leave the device. Cloud providers
never receive plaintext medical data. This property is guaranteed
by the `ExportService` layer regardless of which
`IArchiveStorage` implementation is used — it is an architectural
invariant, not an implementation choice.

### 3.4 Long-term stability

Provider API changes — renamed endpoints, revised OAuth scopes,
changed SDK major versions, altered rate limits — cannot break
the backup write path when the storage layer is the local file
system. The filesystem is the stable compatibility layer between
the app and the network. When a native provider integration is
later added, that stability guarantee is replaced by a versioned
SDK dependency and a supported-API surface — a trade-off that is
acceptable only when the concrete benefit (mobile access) justifies
it.

---

## 4. Limitations of the current approach

### 4.1 Limited user experience for the non-technical audience

The file-system approach requires the user to:

1. Install the provider's sync agent (OneDrive, iCloud for
   Windows, Dropbox, Google Drive Desktop).
2. Know where the synced folder lives on disk.
3. Navigate to that folder in MedReminder's folder picker.
4. Understand why the app "only writes a file" instead of
   "uploading to the cloud".

Steps 2–4 are non-obvious to the intended audience. The folder
path is especially confusing on domain-joined machines where
OneDrive maps to `C:\Users\<name>\OneDrive – <Tenant>\` rather
than `C:\Users\<name>\OneDrive`. `[INFERRED — path confusion is
a known OneDrive Windows issue]`

### 4.2 No cloud-state visibility

The application cannot know:

- whether the sync agent has uploaded the latest archive
- the current upload progress
- whether the remote copy is available on a second device yet
- whether a sync conflict has occurred in the folder

This is not a correctness problem (the app only reads its own
archives) but it is a diagnostic gap when troubleshooting
cross-device restore failures.

### 4.3 Mobile incompatibility

A future B.1 mobile client (`EVOLUTION.md` §6) cannot use the
file-system shortcut. On iOS and Android, file storage inside
another app's sandbox (such as a provider's sync folder) is not
directly accessible to a third-party app without explicit user
interaction. The mobile client would need to invoke the provider
via its native API or SDK. This is the single concrete forcing
function for native integration — **B.1 is the trigger**.

---

## 5. Native cloud API analysis

The sections below assess each provider on feasibility,
authentication model, .NET support, maintenance cost and verdict.
The assessment is grounded in publicly documented API surfaces as
of the analysis date; provider APIs evolve and these details must
be re-verified at implementation time. `[UNCERTAIN — current as
of 2026-09-22; re-verify before coding]`

---

### 5.1 Microsoft OneDrive (via Microsoft Graph)

#### Feasibility

Excellent.

#### Authentication

OAuth 2.0 Authorization Code + PKCE flow via Azure Entra ID
(formerly Azure AD). MedReminder would register as a public client
application in the Azure portal (no client secret — the app
is a desktop binary distributed to end users, not a server-side
confidential client). The MSAL library (`Microsoft.Identity.Client`)
handles the token acquisition and refresh, and supports a silent
token cache backed by the system credential store on Windows.
`[INFERRED — MSAL silent-auth cache uses DPAPI on Windows;
re-verify with the current MSAL version]`

Required OAuth scopes:

```
Files.ReadWrite.AppFolder  (preferred — app-isolated storage)
```

`Files.ReadWrite.AppFolder` limits the app to its own subfolder
under `Apps/MedReminder` in the user's OneDrive. The user never
grants access to personal documents. This is the recommended scope
for the first cut; `Files.ReadWrite` is not needed and should not
be requested.

#### .NET support

First-class: `Microsoft.Graph` and `Microsoft.Identity.Client`
NuGet packages target `netstandard2.0` and `net6.0+`, both
compatible with `net10.0`. `[INFERRED — NuGet target frameworks;
verify at implementation time]`

#### Advantages

- Mature SDK with long-term Microsoft support commitment.
- Versioned API surface (Graph v1.0 for production, beta for
  preview features).
- Application folder isolation protects user's personal files.
- Built-in versioning of uploaded files.
- Delta-query API allows efficient enumeration of changes since
  the last sync.
- Natural fit for Windows-first users (OneDrive is the dominant
  cloud sync agent in the Windows ecosystem). `[INFERRED]`
- MSAL token cache on Windows uses DPAPI — aligned with the
  existing `smtp.protected` security posture.

#### Disadvantages

- OAuth flow requires launching a system browser or embedding an
  auth dialog (MSAL handles this but adds UI complexity).
- Token cache must be managed (refresh, revocation on sign-out).
- App registration in the Azure portal adds a one-time
  maintainer step and an ongoing dependency on the app's
  registration not being deleted or restricted.
- Graph SDK is a large transitive dependency (multi-MB); evaluate
  whether direct REST calls are preferable for the narrow
  use case (upload / download / list / delete of `.mrz` files).
- SDK major-version bumps have historically required non-trivial
  migration work. `[INFERRED — common pattern for Microsoft SDKs]`
- Testing requires a real Azure tenant or a mock — integration
  tests cannot be hermetic without a stub.

#### Verdict

Highest-value native integration candidate. Should be the first
native provider when B.1 justifies the work.

---

### 5.2 Google Drive

#### Feasibility

Excellent.

#### Authentication

OAuth 2.0 Authorization Code + PKCE via Google Identity Platform.
Google's .NET client libraries handle token acquisition,
refresh, and storage. The recommended scope:

```
https://www.googleapis.com/auth/drive.appdata
```

`drive.appdata` limits the app to the hidden `appDataFolder`
in the user's Drive, invisible to the user's own file browser.
This is the correct scope for storing application data without
cluttering the user's personal Drive.

#### .NET support

`Google.Apis.Drive.v3` and `Google.Apis.Auth` NuGet packages.
Both target `netstandard2.0`. `[INFERRED — verify at
implementation time]`

#### Advantages

- Strong alignment with B.1 Android client (Google Drive is
  native to Android; the same `drive.appdata` scope works from
  `com.google.android.gms.drive`).
- Mature, versioned API (Drive API v3 is stable).
- Large user base: Google Drive is the second most used personal
  cloud storage service after iCloud. `[INFERRED]`
- Application folder isolation — user's personal files are never
  accessible to the app.
- Resumable upload API handles large files reliably over slow
  connections.

#### Disadvantages

- OAuth verification process: Google requires apps requesting
  sensitive scopes to pass a security assessment; `drive.appdata`
  is classified as a restricted scope and may require OAuth
  verification or at minimum a privacy policy linked at app
  registration. `[UNCERTAIN — scope restrictions evolve;
  re-verify with Google's current OAuth policy]`
- Google's .NET client library is heavy (similar weight to the
  Graph SDK).
- Token cache on desktop is managed by the library (typically a
  file-based store) — less integrated with the Windows credential
  store than MSAL.
- The library's active-development signal has been inconsistent
  for the desktop .NET target. `[UNCERTAIN]`

#### Verdict

Second priority after OneDrive. Essential when B.1 targets
Android, because Drive is the natural storage backend on that
platform.

---

### 5.3 Dropbox

#### Feasibility

Excellent.

#### Authentication

OAuth 2.0 with PKCE. Dropbox recommends the Authorization Code
flow for desktop apps. The Dropbox .NET SDK (`Dropbox.Api`) or
direct REST calls to `https://api.dropboxapi.com/2/` are both
options; the REST surface is simple enough that the SDK may be
unnecessary overhead for a read/write/list/delete use case.

Required scope:

```
files.content.write
files.content.read
files.metadata.read
```

Scoped to a specific path under `/Apps/MedReminder/` by
convention; Dropbox does not provide an equivalent to
`Files.ReadWrite.AppFolder` or `drive.appdata`, so the app must
self-impose a path restriction. `[INFERRED — Dropbox does not
expose a sandboxed app folder in the same way as Microsoft Graph
or Google Drive's appdata scope]`

#### Advantages

- Simple, well-documented REST API.
- Reliable storage model with no complex quota handling.
- Straightforward integration — the OAuth flow and the file
  operations are both short.
- Strong desktop presence (Dropbox Desktop is widely installed
  among technical and creative professionals). `[INFERRED]`

#### Disadvantages

- Smaller user base than OneDrive or Google Drive; cost/benefit
  ratio is lower.
- No sandboxed app-folder scope: a path-restriction bug would
  expose the user's personal Dropbox to read/write by the app.
  Path hardening is a correctness requirement, not a nice-to-have.
- Additional maintenance burden (a third OAuth integration, a
  third SDK or REST client, a third set of token lifecycle
  operations).
- Less natural for the B.1 mobile story — Dropbox has a mobile
  SDK but it is less tightly integrated into the Android/iOS
  experience than Google Drive or iCloud. `[INFERRED]`

#### Verdict

Good optional provider for a later phase. Not justified before
OneDrive and Google Drive are stable.

---

### 5.4 Apple iCloud Drive

#### Feasibility

Limited on Windows; impractical for a native API integration.

#### Authentication

iCloud APIs are exposed through Apple's CloudKit framework, which
is available on Apple platforms only (`com.apple.cloudkit` is a
macOS / iOS framework). There is no supported CloudKit .NET SDK
for Windows. `[VERIFIED — Apple does not publish a Windows
CloudKit SDK]`

The practical approach for a Windows app is the iCloud Drive
Windows sync client — which places files on the local file system.
This is exactly the model that C.3+ already uses for all
providers: the app writes to a folder, and the sync agent handles
the upload. There is no native API path available that is better
than the file-system approach.

#### Advantages

- iCloud Drive is the default storage for iOS / macOS users.
- The sync agent for Windows is available (iCloud for Windows,
  downloadable from the Microsoft Store). `[VERIFIED]`

#### Disadvantages

- No Windows-native CloudKit SDK — a native integration would
  require Apple-platform intermediaries or an undocumented HTTP
  surface.
- iCloud for Windows sync agent has a historically weaker track
  record on reliability and path discoverability than OneDrive's
  native Windows integration. `[INFERRED — known limitation,
  not a permanent engineering fact]`
- Poor cost/benefit: even if a workaround were found, it would
  need to be maintained against undocumented behaviour.
- The mobile scenario (B.1 on iOS) would use CloudKit via the
  MAUI or native iOS layer, not via the Windows C# code —
  the two would share the archive format and passphrase only.

#### Verdict

Continue using the synchronized-folder model for iCloud Drive.
Do not pursue native API integration on Windows. If B.1 ships on
iOS, iCloud becomes a natural backend for that platform via
CloudKit, independent of the Windows code path.

---

## 6. Android / iOS considerations

### 6.1 Scenario A — continue using synchronized folders

Mobile companion reads archives from a folder exposed by a
provider's app via the OS storage picker.

Advantages:

- No OAuth integration in the mobile client.
- Reuses the same `.mrz` archive format and passphrase verbatim.
- Minimal incremental engineering — `ExportService` and
  `ImportService` are already `net10.0` (no Windows dependency)
  and can be reused directly in a MAUI host.

Disadvantages:

- User experience is weaker: the user must locate the archive in
  a storage picker rather than choosing a named provider.
- The storage picker UI on Android (Storage Access Framework) and
  iOS (`UIDocumentPickerViewController`) is unfamiliar to
  non-technical users.
- The flow does not feel "connected" — restore is a manual file
  selection, not "sign in and choose your backup".

### 6.2 Scenario B — native provider APIs in the mobile client

Mobile companion uses the provider's mobile SDK (Google Drive on
Android, iCloud / CloudKit on iOS, OneDrive via MSAL on both).

Advantages:

- Significantly improved UX: the user signs in once, sees their
  archives by date, taps to restore.
- Account-centric model matches the mental model of a mobile
  user ("my backups are in my Google account").
- Simpler restore flow on the B.1 client.

Disadvantages:

- OAuth integration must be written for each platform / provider
  combination: Android + Google Drive, iOS + iCloud, Android/iOS
  + OneDrive.
- Authentication on mobile involves additional system-level
  permissions and app-store review requirements.
- The Windows backup path and the mobile restore path share only
  the archive format; each platform writes its own storage adapter.
- Substantially higher complexity in the first cut of B.1.

### 6.3 Recommended approach for B.1

Implement Scenario A (file picker) in B.1's first release.
Introduce Scenario B (native provider) in a B.1 follow-on release
once the basic companion is stable. This prevents the
multi-device story from blocking on OAuth complexity.

---

## 7. The IArchiveStorage abstraction

### 7.1 Motivation

The abstraction exists to:

1. Decouple archive production (`ExportService`) from archive
   delivery (wherever the archive lives).
2. Allow `LocalFolderArchiveStorage` to ship now, and
   `OneDriveArchiveStorage` / `GoogleDriveArchiveStorage` to be
   added later, without changing anything above the storage layer.
3. Provide a clean seam for unit testing: storage can be
   stubbed with an in-memory implementation without any
   filesystem or network dependency.

### 7.2 Port definition

Placed in `MedReminder.Application.Abstractions` (same namespace
as `BackupSettings`, `IImportService`, `IExportService`). The
interface covers the four operations needed by both the automatic
backup tick and the restore dialog:

```csharp
/// <summary>
/// Delivers and retrieves encrypted MRZ archives to / from a
/// storage backend. Implementations must be thread-safe.
/// The archive content is always an AES-GCM-encrypted .mrz file;
/// the storage layer never decrypts it.
/// </summary>
public interface IArchiveStorage
{
    /// <summary>
    /// Stores the archive. The stream position is at 0 on entry.
    /// Implementations must close / dispose the stream on exit.
    /// </summary>
    Task<string> UploadAsync(
        Stream archive,
        string suggestedName,
        CancellationToken ct);

    /// <summary>
    /// Downloads the archive identified by <paramref name="id"/>
    /// (as returned by <see cref="ListAsync"/>). The returned
    /// stream is owned by the caller and must be disposed.
    /// </summary>
    Task<Stream> DownloadAsync(string id, CancellationToken ct);

    /// <summary>
    /// Lists available archives, newest first.
    /// </summary>
    Task<IReadOnlyList<ArchiveInfo>> ListAsync(
        CancellationToken ct);

    /// <summary>
    /// Deletes the archive identified by <paramref name="id"/>.
    /// </summary>
    Task DeleteAsync(string id, CancellationToken ct);
}
```

`ArchiveInfo` is a value object in
`MedReminder.Application.Abstractions`:

```csharp
public sealed record ArchiveInfo(
    string Id,          // opaque, backend-specific identifier
    string Name,        // display name (typically the filename)
    DateTimeOffset CreatedAtUtc,
    long SizeBytes);
```

The `Id` is opaque to callers — it carries whatever identifier
the backend needs (a local file path, a Graph item ID, a Dropbox
path). Callers must not parse or construct `Id` values.

### 7.3 Initial implementation — LocalFolderArchiveStorage

```csharp
/// <summary>
/// Stores archives as files in a local (or locally-mounted) folder.
/// This is the C.3+ implementation, sufficient for any OS-level
/// sync agent (OneDrive, iCloud Drive, Dropbox, Google Drive
/// Desktop, Nextcloud, …).
/// </summary>
internal sealed class LocalFolderArchiveStorage : IArchiveStorage
{
    // ...
}
```

`Id` = absolute file path. `SuggestedName` → filename.
`UploadAsync` writes to a temp file then moves atomically into
the configured folder (preserving the C.3+ temp-then-move
discipline; `ANALYSIS-C3PLUS` §4.1). `DownloadAsync` opens a
`FileStream`. `ListAsync` enumerates `*.mrz` in the folder.
`DeleteAsync` calls `File.Delete`.

### 7.4 Future implementations

When B.1 justifies native provider integration:

```csharp
// OneDrive via Microsoft Graph — Files.ReadWrite.AppFolder scope
internal sealed class OneDriveArchiveStorage : IArchiveStorage { }

// Google Drive — drive.appdata scope
internal sealed class GoogleDriveArchiveStorage : IArchiveStorage { }

// Dropbox — files.content.write + files.content.read scopes
internal sealed class DropboxArchiveStorage : IArchiveStorage { }
```

Each implementation is in `MedReminder.Infrastructure` and is
registered in the DI container via a keyed service or a
factory that reads `BackupSettings.CloudProvider` (see §9.1).
`LocalFolderArchiveStorage` remains the default and the
fallback when no provider is configured.

### 7.5 Interaction with the existing backup host

`AutomaticBackupHostedService` currently calls
`ExportService.ExportAsync` → produces a temp file → moves it to
`CloudFolderDirectory`. With the abstraction in place, the host
instead calls:

```csharp
using var archiveStream = await _exportService.ExportAsync(...);
await _archiveStorage.UploadAsync(
    archiveStream,
    $"medreminder-{profileId:N}-{timestamp:yyyyMMdd-HHmmss}.mrz",
    ct);
```

`LocalFolderArchiveStorage.UploadAsync` implements the same
atomic-move discipline as today. `OneDriveArchiveStorage.UploadAsync`
calls `Graph.Me.Drive.Special.AppRoot.Children[name].Content.PutAsync(...)`.
The host does not change between the two.

---

## 8. Localization

No new localization keys are introduced by C.3++ itself, because
the abstraction layer is infrastructure — it has no user-visible
UI. If a future native provider implementation introduces an
OAuth sign-in flow, that flow will require keys in every
dictionary under `assets/localization/` per `CLAUDE.md` §8:

- `Ui.SettingsDialog.CloudBackup.Provider.Label`
- `Ui.SettingsDialog.CloudBackup.Provider.LocalFolder`
- `Ui.SettingsDialog.CloudBackup.Provider.OneDrive`
- `Ui.SettingsDialog.CloudBackup.Provider.GoogleDrive`
- `Ui.SettingsDialog.CloudBackup.Provider.Dropbox`
- `Ui.CloudBackup.SignIn.Button`
- `Ui.CloudBackup.SignOut.Button`
- `Ui.CloudBackup.SignIn.AccountLabel`
- `Ui.CloudBackup.Error.AuthFailed`
- `Ui.CloudBackup.Error.TokenRefreshFailed`

These keys are placeholders; their exact names must be aligned
with the existing dictionary conventions when the work starts.
The four non-English dictionaries may ship with
`TODO(<lang>): <english fallback>` placeholders per the
precedent established in A1 / A5 / C.3+ (`ANALYSIS-C3PLUS` §6).

---

## 9. Data model

### 9.1 BackupSettings extension (deferred)

When a native provider is added, `BackupSettings` will need a
`CloudProvider` discriminator to select the implementation:

```csharp
// New in C.3++ — only added when the first native provider ships
public string CloudProvider { get; set; } = "LocalFolder";
// "LocalFolder" | "OneDrive" | "GoogleDrive" | "Dropbox"
```

The DI container resolves the correct `IArchiveStorage`
implementation based on this value. Defaults to `"LocalFolder"`,
which matches the C.3+ model exactly — existing
`backup.settings.json` files without this field deserialize to
`"LocalFolder"` without any migration needed.

### 9.2 Token store (deferred)

OAuth access tokens and refresh tokens must be stored securely.
On Windows the natural store is a `*.protected` file under
`%LOCALAPPDATA%\MedReminder\`, DPAPI-encrypted in CurrentUser
scope — mirroring `smtp.protected` and `cloud-backup.protected`
(`ANALYSIS-C3PLUS` §3.5).

When the first native provider ships, add:

```
%LOCALAPPDATA%\MedReminder\onedrive.protected
%LOCALAPPDATA%\MedReminder\googledrive.protected
```

The token store is scoped per provider and is never logged. The
token lifetime is managed by the provider's SDK; the app reads
the cached token and allows the SDK to refresh it silently.
`[INFERRED — MSAL and Google Auth libraries support transparent
refresh via a serialized token cache]`

### 9.3 No SQLite schema change

C.3++ introduces no new tables, no `ALTER TABLE`, no
`EnsureCreated` call (`CLAUDE.md` §9). All state is in JSON
settings files and DPAPI-encrypted blobs.

---

## 10. Tests

### 10.1 Abstraction layer — MedReminder.Application.Tests

Tests that are relevant now (before any native provider ships):

- **`IArchiveStorage` contract test suite.** A parameterized
  test base that exercises `UploadAsync` → `ListAsync` →
  `DownloadAsync` → `DeleteAsync` on any implementation. Concrete
  tests derive from it and supply their specific storage instance.
  This prevents contract drift when new implementations are added.
- **`LocalFolderArchiveStorage` — happy path.** Upload a 1 KB
  stream; list returns one entry; download round-trips the bytes;
  delete removes the file.
- **`LocalFolderArchiveStorage` — atomic move.** Upload with a
  fault-injecting wrapper that throws after the temp file is
  written; confirm no file appears in the target folder.
- **`LocalFolderArchiveStorage` — folder missing.** Upload throws
  a documented exception; host logs a warning and continues.
- **`LocalFolderArchiveStorage` — retention.** Upload N + 1
  archives; call `DeleteAsync` on the oldest one returned by
  `ListAsync`; confirm N files remain.

### 10.2 Future native provider tests

When a native provider implementation ships, add:

- **OAuth token acquisition mock.** Token is resolved from the
  stubbed cache; no real network call.
- **Token refresh.** Expired access token triggers a silent
  refresh; the upload succeeds.
- **Token revocation.** Revoked token surfaces an
  `AuthorizationException`; the host logs a warning and prompts
  the user to re-authenticate on next settings open.
- **Graph / Drive API failure.** Upload fails with a 503;
  the host logs a warning and skips the tick (no retry — the
  daily tick provides the retry by design, matching
  `LocalFolderArchiveStorage` behaviour).
- **`ArchiveInfo.Id` round-trip.** `Id` returned by `ListAsync`
  can be passed to `DownloadAsync` and `DeleteAsync` without
  modification.

### 10.3 Infrastructure tests

- **Token store round-trip.** Write / read via DPAPI; tampered
  blob throws on decrypt.
- **`BackupSettings.CloudProvider` deserialization.** JSON with
  and without the field binds correctly; default is
  `"LocalFolder"`.

---

## 11. Retro-compatibility

- **Existing `BackupSettings` files** have no `CloudProvider`
  field and will deserialize to the default `"LocalFolder"`,
  which maps to `LocalFolderArchiveStorage`. No migration needed.
- **C.3+ archives** produced by `LocalFolderArchiveStorage` are
  byte-identical to archives that would be produced by any
  future `IArchiveStorage` implementation — the archive format
  is defined by `ExportService`, not by the storage layer.
- **Revert-safety.** A build that reverts C.3++ (removes the
  `IArchiveStorage` abstraction) falls back to the inline
  temp-then-move pattern used by C.3+ before the abstraction.
  The JSON settings file is unaffected.

---

## 12. Risks and mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| Early native-API integration increases complexity before B.1 justifies it | High | Defer all native implementations; ship only `LocalFolderArchiveStorage` now |
| Multiple provider maintenance burden (OAuth flows, SDK major-version bumps) | Medium | `IArchiveStorage` abstraction isolates each provider; add one at a time starting with OneDrive |
| Android / iOS client requires cloud-native workflow before the Windows side is ready | Medium | Abstraction and archive format are platform-independent; B.1 can add its own `IArchiveStorage` implementation without modifying the Windows code path |
| iCloud / CloudKit has no supported Windows .NET integration | High | Keep iCloud in the file-system model on Windows permanently; address it on iOS via CloudKit when B.1 targets iOS |
| OAuth / token refresh failures cause silent backup loss | Medium | Failure is caught and logged; user is prompted to re-authenticate on next settings dialog open; daily tick retries implicitly |
| App registration deleted or restricted in Azure / Google developer portal | Medium | Document the registration details in the maintainer runbook (not in this document, which ships to the repo); rotating credentials requires a new DPAPI-cached token |
| `ArchiveInfo.Id` opaqueness violated by a caller that parses the path | Low | `Id` type is `string` with no structural contract; document in the port definition and enforce via a code-review checklist |
| Dropbox path restriction not enforced, exposing personal files | High | `DropboxArchiveStorage` hardcodes the `/Apps/MedReminder/` path prefix and validates it before every operation |

---

## 13. Non-goals recap

- Not a replacement for C.3+: `LocalFolderArchiveStorage` ships
  with C.3+ and remains the default.
- Not a sync engine: single-writer discipline is preserved.
- Not a backend: no server component, no C.1 work.
- Not a cloud-provider detection heuristic.
- Not a change to the `.mrz` archive format.
- Not a new encryption scheme: Argon2id + AES-GCM from C.3 remain.
- Not a medical-device concern.
- No native provider integration in the current release: the
  abstraction is introduced now; the native implementations are
  deferred to the B.1 milestone or later.

---

## 14. Roadmap

### Phase 1 — current release (alongside C.3 and C.3+)

Deliver:

- `IArchiveStorage` port in `MedReminder.Application.Abstractions`.
- `LocalFolderArchiveStorage` in `MedReminder.Infrastructure`.
- Refactor `AutomaticBackupHostedService` to call
  `IArchiveStorage.UploadAsync` instead of the inline move.
- `IArchiveStorage` contract test base.

### Phase 2 — when B.1 is approved

Deliver:

- `OneDriveArchiveStorage` with MSAL token cache.
- `GoogleDriveArchiveStorage` with Google Auth token cache.
- `BackupSettings.CloudProvider` discriminator.
- Provider-selection UI in `SettingsDialog`.
- OAuth sign-in / sign-out flow.
- Localization keys for the provider-selection UI.

### Phase 3 — optional, after Phase 2 is stable

Deliver:

- `DropboxArchiveStorage`.
- Enterprise providers (SharePoint, Box, Nextcloud — REST-only,
  no managed SDK required if the interface is already in place).

### Phase 4 — re-evaluate on adoption data

- iCloud on Windows: re-evaluate only if Apple publishes a
  supported Windows CloudKit SDK or if adoption data makes the
  workaround complexity worthwhile.
- C.1 (dedicated backend): revisit if the product owner accepts
  the shift from "desktop tool" to "small service"
  (`EVOLUTION.md` §7.8).

---

## 15. Decisions still to confirm

1. **`IArchiveStorage` introduction timing.** Recommendation:
   introduce the abstraction and `LocalFolderArchiveStorage`
   together with C.3+, in the same PR. Confirm — or defer the
   abstraction to a later cleanup and keep the inline move in
   `AutomaticBackupHostedService` for now.
2. **`ArchiveInfo.Id` type.** Recommendation: `string` (opaque).
   An alternative is a discriminated union or a strongly typed
   `ArchiveId` value object. The opaque `string` is simpler for
   an interface that currently has only one implementation.
   Confirm.
3. **`BackupSettings.CloudProvider` field inclusion.** The field
   is not needed until Phase 2. Recommendation: do not add it in
   Phase 1 (no dead JSON field in a pre-release feature). Add it
   when the first native provider ships. Confirm.
4. **Token store naming.** Recommendation:
   `onedrive.protected` / `googledrive.protected` under
   `%LOCALAPPDATA%\MedReminder\` per the existing `*.protected`
   convention (`CLAUDE.md` §6). Confirm the naming convention
   at Phase 2 start.
5. **Dropbox path restriction model.** Recommendation: hardcode
   `/Apps/MedReminder/` as a write root in
   `DropboxArchiveStorage`; reject any configured path that does
   not fall under it. Confirm — or restrict by scope if Dropbox
   introduces an equivalent to `drive.appdata` before Phase 3
   starts.
6. **B.1 mobile storage model.** Recommendation: Scenario A
   (file picker) in the first B.1 cut, Scenario B (native API)
   in a follow-on. Confirm before B.1 scoping begins.
7. **OneDrive scope.** Recommendation:
   `Files.ReadWrite.AppFolder` (not `Files.ReadWrite`). This
   isolates the app to `Apps/MedReminder/` and requires no
   additional OAuth verification step. Confirm at Phase 2.

---

## 16. Implementation plan

Phase 1 is the only phase deliverable now. One PR alongside
`feature/cloud-folder-backup` (C.3+) or as a follow-on commit
on the same branch — decide based on PR size. Per `CLAUDE.md`
§5, the PR is opened after the first commit, and a
`CHANGE_LOG.md` entry is prepended at that time. Indicative
commit order for Phase 1:

1. **Application.** Add `ArchiveInfo` record and
   `IArchiveStorage` interface to
   `MedReminder.Application.Abstractions`. No test yet (the
   contract test base requires an implementation).
2. **Infrastructure.** `LocalFolderArchiveStorage` implementing
   `IArchiveStorage`. Register in the DI container; wire into
   the existing `BackupSettings.CloudFolderDirectory`.
   Unit tests for `LocalFolderArchiveStorage` (happy path,
   atomic-move fault, folder-missing, retention).
3. **UI / Application.** Refactor
   `AutomaticBackupHostedService.CloudFolderTick` to call
   `IArchiveStorage.UploadAsync`. Adjust the integration test
   that previously tested the inline move; it should now test
   the move via `LocalFolderArchiveStorage`.
4. **Application.Tests.** `IArchiveStorage` contract test base.
   Derive a concrete `LocalFolderArchiveStorageContractTests`
   from it.
5. **Docs.** Short append to `docs/ANALYSIS-C3PLUS-CLOUD-BACKUP.md`
   noting that the inline move is now abstracted behind
   `IArchiveStorage`. No new user-guide section — the
   abstraction is invisible to the user in Phase 1.

Run `dotnet build` and `dotnet test` before every commit that
touches source (`CLAUDE.md` §8).

**Effort for Phase 1.** 2–4 developer-days on top of C.3+,
overlapping substantially with the `AutomaticBackupHostedService`
work already scoped there. `[INFERRED]`

---

## 17. Strategic recommendation

| Decision | Recommendation |
|---|---|
| Approve C.3 unchanged | ✅ |
| Approve C.3+ unchanged | ✅ |
| Introduce `IArchiveStorage` abstraction now (Phase 1) | ✅ |
| Preserve `.mrz` as the long-term interoperability contract | ✅ |
| Ship `LocalFolderArchiveStorage` as the only implementation in Phase 1 | ✅ |
| Defer native cloud APIs (OneDrive, Google Drive, Dropbox) until B.1 | ✅ |
| Prioritise OneDrive, then Google Drive, when native APIs become necessary | ✅ |
| Keep iCloud in the file-system model on Windows permanently | ✅ |
| Use Scenario A (file picker) for B.1's first cut on mobile | ✅ |

The architecture that results is: **one archive format, one
pluggable storage port, one implementation that ships now, and
a clear on-ramp for native providers when the mobile client
makes them necessary.** This maximises immediate value, minimises
implementation risk, preserves provider independence, and leaves
the codebase open for future Android / iOS evolution without any
premature dependency.

---

## Change log for this document

- 2026-09-22 — full rewrite from the initial sketch. Added
  structured scope (§1), C.3 / C.3+ background (§2), detailed
  rationale for the file-system approach (§3), limitations (§4),
  provider-by-provider analysis with OAuth scopes, .NET support
  and verdicts (§5), Android / iOS scenario analysis (§6),
  `IArchiveStorage` port definition with `ArchiveInfo` value
  object and interaction with the backup host (§7), localization
  key placeholders (§8), data model for deferred fields (§9),
  test plan covering the contract base and future provider tests
  (§10), retro-compatibility (§11), expanded risk table (§12),
  non-goals recap (§13), four-phase roadmap (§14), seven open
  decisions (§15), Phase 1 implementation plan (§16), and
  consolidated strategic recommendation (§17). Epistemic
  classification applied throughout. No architectural decisions
  changed relative to the original sketch; the document now
  provides the technical motivation and implementation detail
  required to proceed directly to code.
