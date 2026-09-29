# Implementation prompt — Remote AIFA catalogue feed

Briefing for the Claude Code session that implements
`docs/analysis/ANALYSIS-CATALOGUE-REMOTE-FEED.md`. Read it fully, then
read the referenced files before changing code. Do not start until the
product owner has confirmed decisions D1–D6 of the analysis (§9); if a
decision was changed, the confirmed value wins over this prompt.

---

## 1. Goal

At startup, after the passive application update check, the app reads
`data/latest.json` from the repository, and when its `version` is newer
than the AIFA snapshot in the open profile's database it downloads
`data/aifa-<version>.zip` into a staging folder under
`%LOCALAPPDATA%\MedReminder\`, validates it, imports it for country
`IT`, and deletes the file. Failures are logged and never block the
app.

## 2. Context to read first

- `CLAUDE.md` (language policy, runtime data §5, constraints §7, PR
  workflow §4).
- `docs/ANALYSIS.md` §4.4 (`WriteGate`), §7 (process lifecycle), §8.1
  (schema patches).
- `docs/analysis/ANALYSIS-DRUG-CATALOGUE.md` §2.6, §3.6 (M5).
- `docs/analysis/ANALYSIS-CATALOGUE-REMOTE-FEED.md` (all).
- `docs/CATALOGUE-DATA.md` §2.
- Code:
  - `src/MedReminder.UI/Hosting/CatalogueRefreshHostedService.cs`
  - `src/MedReminder.UI/Forms/MainForm.cs` (`TryStartPassiveUpdateCheck`,
    `BuildCatalogueContext`)
  - `src/MedReminder.UI/Program.cs` (`BuildHost`, catalogue flag)
  - `src/MedReminder.UI/appsettings.json` (`Catalogue` section)
  - `src/MedReminder.Application/Catalogue/IReferenceCatalogueImporter.cs`,
    `ImportReport.cs`, `CatalogueFeatureOptions.cs`
  - `src/MedReminder.Application/UpdateChecking/UpdateCheck.cs`
    (`GitHubReleaseParser`: pattern for a pure parser)
  - `src/MedReminder.Infrastructure/Catalogue/CsvReferenceCatalogueImporter.cs`
  - `src/MedReminder.Infrastructure/Catalogue/Parsers/AifaSnapshotParser.cs`
  - `src/MedReminder.Infrastructure/Catalogue/EmbeddedSnapshotProvider.cs`
  - `src/MedReminder.Infrastructure/UpdateChecking/GitHubUpdateChecker.cs`
    (HTTP pattern, internal test constructor)
  - `src/MedReminder.Infrastructure/Storage/AppDataPaths.cs`
  - `src/MedReminder.Infrastructure/InfrastructureServiceCollectionExtensions.cs`
    (catalogue and update-check registrations)
  - `scripts/download_aifa.py`, `.github/workflows/download_aifa.yaml`
  - Tests: `tests/MedReminder.Infrastructure.Tests/Catalogue/CsvReferenceCatalogueImporterTests.cs`,
    `CatalogueFixtures.cs`

## 3. Work items, in order

Each step builds and keeps the existing tests green before the next.

### Step 1 — Importer: newer-only rule and row-count guard

In `IReferenceCatalogueImporter` / `CsvReferenceCatalogueImporter`:

1. Add `Task<CatalogueImportState> GetImportStateAsync(CountryCode country, CancellationToken ct)`
   returning `(string? Version, int RowCount)` with the query of
   analysis §5.1. Put `CatalogueImportState` in
   `Application/Catalogue/`.
2. Add `SnapshotVersion.IsNewer(string candidate, string? current)` in
   `Application/Catalogue/` (analysis §5.2).
3. Replace `SameVersionAlreadyImportedAsync` with the state query:
   no-op when `!IsNewer(snapshotVersion, state.Version)`. The no-op
   report carries the **stored** version.
4. Add an optional `int minimumRowCount = 1` parameter to `ImportAsync`.
   After parsing and before `BeginTransactionAsync`, throw
   `InvalidDataException` when `rows.Count < minimumRowCount`.
5. Tests (Infrastructure.Tests): older version is a no-op; zero-row
   snapshot throws and keeps rows; `minimumRowCount` above parsed count
   throws and keeps rows; `GetImportStateAsync` empty and populated.
   Build a zero-row AIFA ZIP in memory with headers only: the first
   line of `tests/fixtures/catalogue/aifa-confezioni-sample.csv` and
   `aifa-pa-sample.csv`, zipped as `CatalogueFixtures.BuildAifaSnapshotStream`
   does.

This step alone fixes the downgrade described in analysis §3 point 3,
for all four countries.

### Step 2 — Manifest, options and signal (Application)

1. `CatalogueFeedManifest` record: `Version`, `File?`, `Generated?`,
   `Sha256?`, `Size?`. `CatalogueFeedManifestParser.TryParse(string json, out CatalogueFeedManifest?, out string? error)`
   with `System.Text.Json`, rules of analysis §4.2 step 2. Unknown
   fields ignored (`csv_count` today).
2. `CatalogueFeedOptions` bound from `Catalogue:RemoteFeed` (analysis
   §6). `SnapshotUrlTemplate` contains `{version}`.
3. `ICatalogueFeedClient`:
   - `Task<CatalogueFeedManifest?> GetLatestAsync(CancellationToken)` —
     `null` on any failure (logged inside the adapter).
   - `Task<CatalogueFeedDownload> DownloadAsync(CatalogueFeedManifest, string destinationPath, CancellationToken)`
     returning byte count and lowercase hex SHA-256; throws on failure.
4. `StartupUpdateCheckSignal` (sealed class, singleton):
   `void MarkCompleted()` (idempotent) and
   `Task WaitAsync(TimeSpan timeout, CancellationToken)` that completes
   on mark or timeout, never throws on timeout.
5. Tests (Application.Tests): parser cases, `SnapshotVersion` cases,
   signal cases (analysis §7).

### Step 3 — HTTP adapter and paths (Infrastructure)

1. `AppDataPaths.GetCatalogueStagingDirectory()` →
   `<AppData>\catalogue\staging\`, created on demand.
2. `GitHubRawCatalogueFeedClient : ICatalogueFeedClient, IDisposable`
   in `Infrastructure/Catalogue/`:
   - Own `HttpClient` over a `SocketsHttpHandler` with
     `AllowAutoRedirect = false`; User-Agent as in `GitHubUpdateChecker`.
     Per-call timeouts from options via linked
     `CancellationTokenSource`, not `HttpClient.Timeout`.
   - Manifest: reject bodies over 4 KB.
   - Download: `HttpCompletionOption.ResponseHeadersRead`; reject
     `Content-Length` > `MaxDownloadBytes`; copy through
     `IncrementalHash` (SHA-256) with a running byte cap; write to
     `<dest>.part`, then `File.Move(part, dest, overwrite: true)`.
   - Internal constructor taking `HttpMessageHandler` for tests, like
     `GitHubUpdateChecker`.
3. Register in `AddMedReminderInfrastructure`: options, client as
   singleton, `StartupUpdateCheckSignal` as singleton.
4. Tests (Infrastructure.Tests) with a fake handler: 404, 500, timeout,
   oversize length, oversize stream, 302 not followed, hash returned.

### Step 4 — Remote step in the hosted service (UI)

In `CatalogueRefreshHostedService.RunOnceAsync`, after the embedded
loop:

1. `await signal.WaitAsync(TimeSpan.FromSeconds(60), ct)`.
2. Gate: `CatalogueFeedOptions.Enabled` and
   `UserSettings.CheckForUpdatesOnStartup` (D1). Log and return when off.
3. Follow analysis §4.2 steps 2–8 in a new private method
   `RefreshFromRemoteFeedAsync`. Keep it in its own `try/catch`: a
   failure logs a Warning and never affects the embedded imports that
   already ran.
4. Validate the ZIP (analysis §5.4) before calling the importer. Open
   the file with `FileStream` (seekable) and pass
   `minimumRowCount: Math.Max(1, state.RowCount / 2)` (D4).
5. Verify the hash only when `manifest.Sha256` is present (D3).
6. `finally`: delete `<file>` and `<file>.part`; catch and log
   `IOException` / `UnauthorizedAccessException`.
7. Log per analysis §5.6. Reuse the existing
   "Reference-catalogue import for {Country} complete: …" message.

Consider extracting the step into an Infrastructure or Application
class (`RemoteCatalogueRefresher`) so the end-to-end test does not need
the UI project; the hosted service then only orchestrates. Preferred.

### Step 5 — Signal from the update check (UI)

In `MainForm.TryStartPassiveUpdateCheck`, wrap the body so
`StartupUpdateCheckSignal.MarkCompleted()` runs in `finally`, including
the early return when `CheckForUpdatesOnStartup` is false. Do not
signal from `ManualUpdateCheckAsync`.

### Step 6 — Configuration

Add the `RemoteFeed` block of analysis §6 to
`src/MedReminder.UI/appsettings.json`. If D1 changes the settings
label, update `Ui.SettingsDialog.General.CheckUpdates` (and its tooltip
key, set right after it in `SettingsDialog.cs:311`) in all five
`assets/localization/strings.<lang>.json`.

### Step 7 — Workflow

In `scripts/download_aifa.py`:

1. After building the ZIP, compute `sha256` and `size` and add them to
   `latest.json`.
2. Before writing `latest.json`, check that each CSV has the expected
   header columns (the ones `AifaSnapshotParser` requires) and at least
   1 000 data rows (D4); on failure exit non-zero without touching
   `data/`.
3. Keep the script's existing behaviour otherwise (retention of 3
   archives, file naming).

Comments and messages in the script stay as they are unless you touch
the line; new lines are in English (`CLAUDE.md` §2).

### Step 8 — Documentation

Apply analysis §8: `CATALOGUE-DATA.md` (§2 rewrite, new "Remote feed"
section, step 7 log statement fix), `ANALYSIS-DRUG-CATALOGUE.md` §3.6
pointer, `ANALYSIS.md` hosted services and runtime data, `CLAUDE.md` §5
(`catalogue\staging\`), one sentence in each `USER_GUIDE.<lang>.md`,
`CHANGE_LOG.md` entry.

## 4. Constraints

- English only in code, comments, logs, docs (`CLAUDE.md` §2).
- Never write outside `%LOCALAPPDATA%\MedReminder\`; do not use
  `Path.GetTempPath()`.
- Never open another profile's database.
- Do not remove the embedded AIFA snapshot or change
  `EmbeddedSnapshotProvider`.
- No network call outside startup; no call when the gate is off.
- No new NuGet package: `System.Net.Http`, `System.Text.Json`,
  `System.Security.Cryptography`, `System.IO.Compression` are enough.
- Domain untouched; `Infrastructure.Portable` untouched unless
  justified in the PR.
- Do not log file contents, URLs with query strings, or anything
  profile-specific beyond the profile id already logged elsewhere.

## 5. Verification

The session cannot run Infrastructure tests on Linux. Ask the user to
run on Windows:

```powershell
dotnet restore MedReminder.sln
dotnet build   MedReminder.sln -c Release
dotnet test    MedReminder.sln -c Release
```

Manual check on Windows:

1. Set `Catalogue:RemoteFeed:ManifestUrl` to a local HTTP server (or a
   test branch raw URL) serving a manifest with `version` one month
   ahead of the imported one. Start the app. The log shows the update
   check, then "remote AIFA feed … newer", the download, and
   "Reference-catalogue import for IT complete: … version=<new>".
2. `%LOCALAPPDATA%\MedReminder\catalogue\staging\` is empty afterwards.
3. Restart: the embedded import reports the stored (newer) version as a
   no-op; the remote step logs "up to date" and downloads nothing.
4. Disable "check for updates at startup": no request to the feed.
5. Serve a ZIP whose SHA-256 differs from the manifest: import skipped,
   staging empty, catalogue unchanged.

## 6. Deliverables

- One PR from the feature branch, opened after the first commit
  (`CLAUDE.md` §4), with `CHANGE_LOG.md` updated.
- Commits per step, imperative English messages explaining why.
