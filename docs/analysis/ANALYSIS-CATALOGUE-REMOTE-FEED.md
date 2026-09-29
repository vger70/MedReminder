# ANALYSIS — Remote AIFA catalogue feed (M5-lite)

Design document, written before implementation; §11 records how the
implementation departs from it. Work proceeds on branch
`claude/aifa-catalog-auto-update-jrkles`. It implements a reduced form
of `ANALYSIS-DRUG-CATALOGUE.md` §3.6 (M5 "Snapshot online updater"),
limited to Italy (AIFA), using the monthly archive produced by
`.github/workflows/download_aifa.yaml`. The implementation briefing is
`docs/prompt/Completed/PROMPT-CATALOGUE-REMOTE-FEED.md`.

Epistemic classification, aligned with the sibling documents:
`[VERIFIED]` (checked against the tree at commit `891ec94`, or against
the live endpoint on 2026-09-29), `[INFERRED]` (deduction from verified
facts), `[UNCERTAIN]` (hypothesis pending confirmation). §10 records
the verification pass.

---

## 1. Scope

### 1.1 Problem

Today a new AIFA snapshot reaches users only through a new release:
the ZIP is built by hand, embedded under
`src/MedReminder.Infrastructure/Assets/Catalogue/it/`, and imported at
boot by `CatalogueRefreshHostedService` (`docs/CATALOGUE-DATA.md` §2).
A user on an old build keeps an old catalogue
(`ANALYSIS-DRUG-CATALOGUE.md` risk table: "User stays on an old app
build for months"). The workflow `download_aifa.yaml` now publishes a
monthly archive and a manifest in `data/`, which removes the manual
build steps 1–3 but not the release dependency.

### 1.2 Goal

At application start, after the passive application update check:

1. Read `https://raw.githubusercontent.com/vger70/MedReminder/main/data/it/latest.json`
   (originally `data/latest.json`; moved per §11.4).
2. If its `version` is newer than the AIFA snapshot already imported
   in the open profile's database, download
   `https://raw.githubusercontent.com/vger70/MedReminder/main/data/it/aifa-<version>.zip`
   into a staging folder.
3. Validate it and import it through the existing
   `IReferenceCatalogueImporter` (country `IT`, version `<version>`).
4. Delete the downloaded file, whatever the outcome.

### 1.3 Out of scope

- EU, ES, FR catalogues: no remote feed exists for them. They keep the
  embedded-only path.
- Updating the databases of profiles that are not open (§4.4).
- A UI for the feature beyond what decision D1 requires (§9).
- Replacing the embedded snapshot: it remains the offline baseline for
  a first run without network (§4.6).
- Signed manifests (`ANALYSIS-DRUG-CATALOGUE.md` §3.6 asks for them);
  replaced here by HTTPS + SHA-256 in the manifest (§5.3, D3).

---

## 2. Current state (facts the design depends on)

| # | Fact | Source | Tag |
|---|------|--------|-----|
| F1 | `CatalogueRefreshHostedService` is registered only when `Catalogue:Enabled` is `true`; `appsettings.json` sets it to `true`. | `Program.cs:377-382`, `appsettings.json` `"Catalogue"` | [VERIFIED] |
| F2 | The hosted service runs fire-and-forget from `ExecuteAsync`, iterates IT → EU → ES → FR, one importer call per country, per-country error isolation. | `CatalogueRefreshHostedService.cs:34-96` | [VERIFIED] |
| F3 | Hosted services start in `Program.Main` (`host.StartAsync`, line 135) before `RunUi` (line 139). The passive update check starts later, from `MainForm.Load` (`MainForm.cs:123`), on a `Task.Run`. The two are therefore concurrent and unordered today. | `Program.cs:134-139`, `MainForm.cs:123,431-462` | [VERIFIED] |
| F4 | The passive update check returns early when `UserSettings.CheckForUpdatesOnStartup` is `false` (default `true`, exposed in `SettingsDialog`). | `MainForm.cs:438-441`, `UserSettings.cs:39`, `SettingsDialog.cs:310,443` | [VERIFIED] |
| F5 | `CsvReferenceCatalogueImporter.ImportAsync` short-circuits only when a row with the **same** `(country, snapshot_version)` exists. Any other version, older or newer, triggers delete + re-insert of the country. | `CsvReferenceCatalogueImporter.cs:63-69,99-116` | [VERIFIED] |
| F6 | The importer parses the whole snapshot into memory **before** opening the transaction; delete and insert run in one transaction. A parse failure leaves the catalogue untouched. | `CsvReferenceCatalogueImporter.cs:71-88` | [VERIFIED] |
| F7 | A snapshot that parses to zero rows (headers present, no data) deletes every IT row and inserts none: `InsertAsync` returns 0 on an empty list after `DeleteCountryAsync` ran. The parser throws only on a missing entry, an empty CSV (no header) or a missing column. | `CsvReferenceCatalogueImporter.cs:85-86,164-167`; `AifaSnapshotParser.cs:82,252,265` | [VERIFIED] |
| F8 | `AifaSnapshotParser` finds `confezioni_fornitura.csv` and `PA_confezioni.csv` by entry name, case-insensitive; other entries are ignored. It needs a seekable stream (buffers otherwise). | `AifaSnapshotParser.cs:29-30,50-56,73-83` | [VERIFIED] |
| F9 | One process owns one profile. The profile is chosen at boot and cannot change at runtime; switching restarts the process. The single-instance mutex is per Windows session, not per profile. | `ICurrentProfile.cs`, `Program.cs:46-50`, `docs/ANALYSIS.md` §7 | [VERIFIED] |
| F10 | Project rule: never open another profile's live database for writing. | `ANALYSIS-MULTI-USER-ROLES-OVERVIEW.md` §4.2 | [VERIFIED] |
| F11 | Reference tables are excluded from sync snapshots (they are deleted from the snapshot image). | `SqliteSyncSnapshotStore.cs:42-44` | [VERIFIED] |
| F12 | `LinkedReferenceMedicineId` on `Medicines` stores a reference-row `Guid`; every import regenerates those Guids; no code dereferences the id (lookups go through national code). Pre-existing, not worsened in kind by this feature, only in frequency. | `CsvReferenceCatalogueImporter.cs:205,260`; `grep LinkedReferenceMedicineId` | [VERIFIED] |
| F13 | `GitHubUpdateChecker` is the only outbound HTTP client in Infrastructure for GitHub; it owns an `HttpClient` with an 8 s timeout and a `MedReminder/<version>` User-Agent. | `GitHubUpdateChecker.cs:24-35,107-119` | [VERIFIED] |
| F14 | Runtime data must stay under `%LOCALAPPDATA%\MedReminder\`; `AppDataPaths.GetAppDataDirectory()` is the root. | `CLAUDE.md` §5, `AppDataPaths.cs:24-32` | [VERIFIED] |
| F15 | Embedded IT snapshot today: `aifa-202609.zip`. Published feed today: `latest.json` `version` `202609`. Same label, so the feature is a no-op until the workflow publishes `202610`. | `Assets/Catalogue/it/`, `data/latest.json` | [VERIFIED] |

### 2.1 The published feed

| Item | Value | Tag |
|------|-------|-----|
| `latest.json` | `{"version":"202609","file":"aifa-202609.zip","generated":"2026-09-29T11:38:01.151381+00:00","csv_count":3}`; served `200`, `text/plain; charset=utf-8`, 123 bytes. | [VERIFIED] live |
| `aifa-202609.zip` | `200`, `application/zip`, `content-length: 5016171`, `cache-control: max-age=300`, strong `etag`. | [VERIFIED] live |
| ZIP content | `PA_confezioni.csv` (11 571 996 B), `atc.csv` (198 442 B), `confezioni_fornitura.csv` (82 491 432 B). | [VERIFIED] `unzip -l data/aifa-202609.zip` |
| Retention | The script keeps the 3 newest `aifa-*.zip` in `data/`. | [VERIFIED] `download_aifa.py:130-134` |

### 2.2 Weaknesses of the workflow that affect the client

- **W1 — version is the run month, not the AIFA release date.**
  `ZIP_NAME` and `version` use `datetime.now()`
  (`download_aifa.py:14,115`). `CATALOGUE-DATA.md` §2 step 3 says the
  suffix is the AIFA release date. With the cron on day 2 of the month
  the two usually coincide [INFERRED]; the doc must be aligned (§8).
- **W2 — no content validation before publishing.** The script only
  rejects an HTML content type and warns on a file under 100 bytes; it
  does not check headers or row counts, and writes `latest.json`
  unconditionally (`download_aifa.py:72-89,114-124`). "Valid" in the
  requirement is therefore not enforced upstream: the client must
  validate (§5.4).
- **W3 — no integrity data.** `latest.json` carries no hash or size.
  Recommendation D3: add `sha256` and `size` (§5.3).
- **W4 — same-month re-runs overwrite the same version.** A
  `workflow_dispatch` in the same month rewrites `aifa-<yyyymm>.zip`
  with the same version; a client that already imported that version
  will not pick up the new content (F5 short-circuit). Resolved in
  §11.2 with a build-time suffix on the stored version.
- **W5 — `atc.csv` inside the current ZIP** (`csv_count: 3`). Harmless
  (F8). Commit `2315008` removed it from the link filter, so the next
  run produces 2 entries [INFERRED].
- **W6 — repository growth.** About 5 MB of binary per month enters git
  history even though only 3 files stay in the tree [INFERRED]. Low
  impact; a GitHub Release asset per month would avoid it. Not
  required for this feature.

---

## 3. Premise check

Two points of the requirement conflict with the project constraints or
with the code, and change the design:

1. **"Temporary folder".** `Path.GetTempPath()` resolves to
   `%LOCALAPPDATA%\Temp` (or `%TEMP%`), outside
   `%LOCALAPPDATA%\MedReminder\`, which `CLAUDE.md` §5 forbids (F14).
   The staging folder is `%LOCALAPPDATA%\MedReminder\catalogue\staging\`
   (§5.5).
2. **"Update the catalogue on the local database(s)".** Only the open
   profile's database can be written (F9, F10). Other profiles get the
   new snapshot at their next boot, with their own download (§4.4).
   Downloading 5 MB once per profile per month is cheaper than a
   shared cache that would contradict "delete the feed once processed".

A third point is a latent defect that the feature would trigger:

3. **Downgrade on the next boot.** After the remote import stores
   `202610`, the next boot still offers the embedded `202609`. Per F5
   the importer sees a different version and **replaces the newer
   catalogue with the older embedded one**; the remote step then
   downloads `202610` again. Every boot would import twice. The
   importer decision must become "newer than" instead of "different
   from" (§5.2). This is a required change, not an option.

---

## 4. Design

### 4.1 Sequence

```
Program.Main
  host.StartAsync ──► CatalogueRefreshHostedService (background task)
                        1. embedded imports IT, EU, ES, FR   (unchanged order,
                                                              newer-only rule §5.2)
                        2. await StartupUpdateCheckSignal     (timeout 60 s)
                        3. remote AIFA step (§4.2)            (IT only)
  RunUi ──► MainForm.Load ──► TryStartPassiveUpdateCheck
                                 CheckAsync (or skipped)
                                 finally: signal.MarkCompleted()
```

All catalogue writes stay on one sequential background task, so the
embedded and the remote import can never run concurrently or in the
wrong order [INFERRED from F2, F3]. "After the application update
check" is honoured by the signal; the timeout covers a form that never
loads (e.g. a crash before `Load`) and a disabled check.

### 4.2 Remote AIFA step

1. Gate (D1): feature flag `Catalogue:RemoteFeed:Enabled` (default
   `true`) **and** `UserSettings.CheckForUpdatesOnStartup`. Either off →
   log at Information and stop.
2. `GET latest.json` (timeout 10 s, response cap 4 KB). Parse into
   `CatalogueFeedManifest`. Reject when `version` does not match
   `^\d{6}$` with month 01–12, or when `file` (if present) differs from
   `aifa-<version>.zip`.
3. Read the imported IT version from the open database (§5.1). If the
   remote version is not newer (§5.2), log and stop. No download.
4. Delete leftovers in the staging folder (a previous crash).
5. Download `aifa-<version>.zip` to `staging\aifa-<version>.zip.part`
   (streamed, `ResponseHeadersRead`, timeout 120 s, size cap 64 MB),
   compute SHA-256 while writing, then rename to `.zip`.
6. Verify (§5.3, §5.4).
7. `ImportAsync(stream, IT, version, minimumRowCount, ct)` (§5.2).
8. `finally`: delete the `.part` and `.zip` files. A delete failure is
   logged at Warning and retried by step 4 of the next boot.

Every failure (network, HTTP status, parse, hash, validation, import)
is logged once and swallowed: the app must stay usable offline, exactly
like the update check (F13 pattern) and the embedded import (F2).

### 4.3 Where the code lives

| Layer | New / changed | Responsibility |
|-------|---------------|----------------|
| Application `Catalogue/` | `CatalogueFeedManifest` + `CatalogueFeedManifestParser` (pure, like `GitHubReleaseParser`) | Parse and validate `latest.json`. |
| Application `Catalogue/` | `SnapshotVersion.IsNewer(candidate, current)` (static, pure) | Version ordering rule (§5.2). |
| Application `Catalogue/` | `ICatalogueFeedClient` port | `GetLatestAsync`, `DownloadAsync(manifest, destinationPath)` returning size + SHA-256. |
| Application `Catalogue/` | `CatalogueFeedOptions` (`Catalogue:RemoteFeed`) | `Enabled`, `ManifestUrl`, `SnapshotUrlTemplate`, timeouts, size cap. URLs configurable for tests and forks. |
| Application `UpdateChecking/` | `StartupUpdateCheckSignal` (singleton, `TaskCompletionSource`) | Ordering between the update check and the remote step. |
| Application `Catalogue/` | `IReferenceCatalogueImporter`: add `GetImportStateAsync(country)` → `(Version?, RowCount)`; add `minimumRowCount` to `ImportAsync` (default 1) | Read side of version detection; empty-wipe guard (F7). |
| Infrastructure `Catalogue/` | `CsvReferenceCatalogueImporter` | Newer-only rule, row-count guard, `GetImportStateAsync`. |
| Infrastructure `Catalogue/` | `GitHubRawCatalogueFeedClient : ICatalogueFeedClient` | HTTP, caps, hashing; own `HttpClient` (singleton), same UA as F13. |
| Infrastructure `Storage/` | `AppDataPaths.GetCatalogueStagingDirectory()` | `%LOCALAPPDATA%\MedReminder\catalogue\staging\`. |
| UI `Hosting/` | `CatalogueRefreshHostedService` | Step 2–3 of §4.1. |
| UI `Forms/` | `MainForm.TryStartPassiveUpdateCheck` | `MarkCompleted()` in `finally`, including the early return of F4. |

Domain is untouched. `Infrastructure.Portable` is untouched except if
the implementer chooses to put the version query there; it is not
required.

### 4.4 Multi-profile behaviour

- The remote step writes only the open profile's database (F9, F10).
- Profile B, opened later, repeats the check at its own boot and
  downloads the same ZIP (5 MB) once. Each profile converges on the
  newest snapshot the first time it is opened after publication.
- No shared cache survives the run (requirement: delete the feed).

### 4.5 Concurrency with other writers

The import transaction holds the SQLite write lock for the duration of
the delete + insert. The remote import runs under `WriteGate`, so the
use cases, the monitor and the catch-up wait for it instead of hitting
the busy timeout (§11.1). The embedded import, which lives in the UI
and Infrastructure layers, cannot take the `internal` gate and keeps
its previous behaviour; it runs only when a release changes a
snapshot. The import duration is logged; the first field run measured
13.4 s for the Italian catalogue (§11.3), below the 30 s SQLite busy
timeout that writers outside the gate would hit.

### 4.6 Relation to the embedded snapshot

- The embedded ZIP stays: first run offline, and fallback when the feed
  is unreachable.
- With the newer-only rule the embedded ZIP no longer overrides a newer
  remote import.
- The release procedure (`CATALOGUE-DATA.md` §2) shrinks to: copy the
  current `data/it/aifa-<yyyymm>.zip` into
  `src/MedReminder.Infrastructure/Assets/Catalogue/it/`, remove the old
  one, commit. Refreshing the embedded snapshot becomes optional per
  release instead of mandatory per month.

---

## 5. Detailed rules

### 5.1 Reading the imported version

```sql
SELECT "snapshot_version", COUNT(*)
  FROM "reference_medicines"
 WHERE "country" = $country
 GROUP BY "snapshot_version";
```

One group is the normal case (the importer writes one version per
country, F5/F6). Zero rows → `Version = null`, `RowCount = 0`. More
than one group is not produced by the importer; if seen, take the
highest version and the total count.

### 5.2 Version ordering and import guard

- `SnapshotVersion.IsNewer(candidate, current)`: `true` when `current`
  is `null`; when both match `^\d{6}$`, ordinal comparison
  (`yyyymm` sorts lexically); otherwise fall back to "different"
  (current behaviour, so non-`yyyymm` labels keep working).
- `CsvReferenceCatalogueImporter.ImportAsync` becomes a no-op when the
  stored version is **equal or newer** than the candidate (report
  `Inserted = Deleted = 0`, `SnapshotVersion` = the stored one, so the
  hosted-service log shows what is actually in the DB).
- Row-count guard, checked after parsing and **before** the
  transaction: when `rows.Count < minimumRowCount`, throw
  `InvalidDataException` and leave the catalogue untouched. Default
  `minimumRowCount = 1` for every caller (closes F7 for embedded
  imports too). The remote step passes
  `max(1, currentRowCount / 2)`: a new AIFA month that loses half the
  packages is treated as a broken feed, not as data [INFERRED; the
  ratio is D4].

### 5.3 Integrity

- Transport: HTTPS to `raw.githubusercontent.com` only; redirects off
  (`AllowAutoRedirect = false`), so the URL cannot be bounced to
  another host [INFERRED; `raw.githubusercontent.com` served both files
  with `200` and no redirect on 2026-09-29].
- Size: reject `Content-Length` > cap before reading; enforce the cap
  while streaming as well (length may be absent).
- Hash (D3): when the manifest carries `sha256`, a mismatch rejects the
  file. When it does not, log at Information that the file was not
  hash-verified and continue (transitional, until the workflow adds the
  field). The workflow change is one line in `download_aifa.py` plus
  `size`; it is part of this work item (§8).

### 5.4 Content validation before import

Before handing the file to the importer:

1. Opens as a ZIP.
2. Contains `confezioni_fornitura.csv` and `PA_confezioni.csv` at any
   path, case-insensitive (F8 lookup rule).
3. Sum of `ZipArchiveEntry.Length` of the two entries ≤ 512 MB
   (zip-bomb guard; today 94 MB, §2.1).

Header and column checks are left to `AifaSnapshotParser`, which
already throws on a missing column (F7). The row-count guard (§5.2)
covers the empty-data case.

### 5.5 Files and paths

- Staging: `%LOCALAPPDATA%\MedReminder\catalogue\staging\`, shared by
  profiles (only one process runs at a time, F9).
- File names: `aifa-<version>.zip.part` while downloading,
  `aifa-<version>.zip` once complete.
- Cleanup: every file in `staging\` at the start of the step; both files
  in `finally`.
- The download stream is opened with `FileShare.None`.
  `FileOptions.DeleteOnClose` is not used: the importer reopens the
  file after the download closes it, so cleanup is the explicit delete
  in `finally`.

### 5.6 Logging

Information: gate decision, local vs remote version, download size and
duration, SHA-256 prefix (first 12 hex chars), import report and
duration. Warning: any failure, with the exception. No PII, no
medical data (`CLAUDE.md` §7). The existing message
`Reference-catalogue import for IT complete: ... version=<yyyymm>` is
reused for the remote import so the verification step of
`CATALOGUE-DATA.md` still applies.

---

## 6. Configuration

`appsettings.json`, under the existing `Catalogue` section:

```json
"Catalogue": {
  "Enabled": true,
  "RemoteFeed": {
    "Enabled": true,
    "ManifestUrl": "https://raw.githubusercontent.com/vger70/MedReminder/main/data/it/latest.json",
    "SnapshotUrlTemplate": "https://raw.githubusercontent.com/vger70/MedReminder/main/data/it/aifa-{version}.zip",
    "ManifestTimeoutSeconds": 10,
    "DownloadTimeoutSeconds": 120,
    "MaxDownloadBytes": 67108864
  }
}
```

No new user setting, no new UI string, if D1 keeps the reuse of
`CheckForUpdatesOnStartup`. That setting's label must then be read as
"check for updates (application and catalogue)"; the label text change
would touch the five `strings.<lang>.json` files and is part of D1.

---

## 7. Tests

| Project | Test | Covers |
|---------|------|--------|
| Application.Tests | `CatalogueFeedManifestParserTests`: valid, missing version, bad month `202613`, `file` mismatch, extra fields ignored, `sha256` optional, malformed JSON. | §4.2 step 2 |
| Application.Tests | `SnapshotVersionTests`: null current, older, equal, newer, non-`yyyymm` fallback. | §5.2 |
| Application.Tests | `StartupUpdateCheckSignalTests`: completes on mark, completes on timeout, idempotent mark. | §4.1 |
| Infrastructure.Tests | `CsvReferenceCatalogueImporterTests`: older version is a no-op (new); zero-row snapshot throws and keeps rows (new); `minimumRowCount` above parsed count throws and keeps rows (new); `GetImportStateAsync` on empty and populated DB (new). Existing tests stay green (they only upgrade `202609` → `202610`). | §5.1, §5.2, F7 |
| Infrastructure.Tests | `GitHubRawCatalogueFeedClientTests` with a fake `HttpMessageHandler` (pattern of the internal `GitHubUpdateChecker` constructor): 404, 500, timeout, oversize `Content-Length`, oversize stream without length, hash match, hash mismatch, redirect not followed. | §5.3 |
| Infrastructure.Tests | Remote step end to end against a temp profile DB and the AIFA fixture (`CatalogueFixtures`): imports when newer, skips when equal, deletes staging files on success and on failure. | §4.2 |

Infrastructure tests need Windows (`CLAUDE.md` §3); the Application
tests run anywhere.

---

## 8. Documentation and workflow changes

- `docs/CATALOGUE-DATA.md` §2: replace steps 1–3 with "take the ZIP
  published by `download_aifa.yaml` in `data/`"; state that the suffix
  is the download month (W1); keep steps 4–7 as the optional embedded
  refresh; fix step 7, which says subsequent boots log nothing, while
  `CatalogueRefreshHostedService` logs "Starting …" and
  "complete: inserted=0 …" on every boot
  (`CatalogueRefreshHostedService.cs:114-126`) [VERIFIED].
- `docs/CATALOGUE-DATA.md`: new section "Remote feed" describing
  `latest.json`, the client rules of §5, and the 3-archive retention.
- `docs/ANALYSIS-DRUG-CATALOGUE.md` §3.6: note that M5 is implemented
  for IT in reduced form, with a pointer to this document.
- `docs/ANALYSIS.md`: mention the remote step in the hosted-services
  list and the staging folder in the runtime-data layout.
- `CLAUDE.md` §5: add `catalogue\staging\` (transient) to the shared
  runtime data.
- `USER_GUIDE.{en,it,fr,es,de}.md`: one sentence stating that the
  Italian catalogue refreshes itself at startup when the update check
  is enabled.
- `scripts/download_aifa.py`: add `sha256` and `size` to
  `latest.json`; fail the run (non-zero exit, no `latest.json`
  rewrite) when either CSV is missing its header or has fewer than
  1 000 data rows (W2). The threshold is D4.

---

## 9. Decisions to confirm

| # | Decision | Recommendation |
|---|----------|----------------|
| D1 | Gate of the remote step. | Reuse `CheckForUpdatesOnStartup` (one switch for startup network calls) plus the admin flag `Catalogue:RemoteFeed:Enabled`. Alternative: a separate user setting, which costs a checkbox and five dictionary entries. |
| D2 | Profiles updated. | Open profile only; others at their next boot (§4.4). Forced by F10. |
| D3 | Integrity. | Add `sha256` and `size` to `latest.json` in this work item; the client verifies when present and tolerates absence during the transition. |
| D4 | Thresholds. | Client: reject a snapshot with fewer than half the current IT rows. Workflow: reject a CSV with fewer than 1 000 data rows. |
| D5 | User feedback. | None beyond the log. The autocomplete reads the database per query, so the new rows appear at the next dialog opening [VERIFIED: `MainForm.BuildCatalogueContext` builds a fresh context per dialog, `MainForm.cs:1094-1110`]. |
| D6 | Staging location. | `%LOCALAPPDATA%\MedReminder\catalogue\staging\` instead of the system temp folder (§3 point 1). |

---

## 10. Verification pass (2026-09-29)

Every `[VERIFIED]` row was re-checked against the tree at `891ec94`
after writing:

- F1–F15: re-read the cited lines; the line numbers match.
- §2.1: `curl` of both URLs (status, content type, length,
  `cache-control`, `etag`) and `unzip -l` of `data/aifa-202609.zip`.
- F7: confirmed by reading `DeleteCountryAsync` / `InsertAsync`
  ordering and the three `throw` sites of `AifaSnapshotParser`; not
  confirmed by a running test (no Windows host in this session).
- §3 point 3 (downgrade): follows from F5 and the embedded version
  `202609`; not reproduced at runtime [INFERRED].
- Existing importer tests use only `202609` then `202610`, so the
  newer-only rule does not break them [VERIFIED: `grep ImportAsync(`
  in `CsvReferenceCatalogueImporterTests.cs`].
- Not verified: SQLite busy behaviour during a long import (§4.5), the
  exact `Load` timing when the app starts minimized (the 60 s signal
  timeout covers it either way).

---

## 11. Implementation notes (2026-09-29)

Decisions D1–D6 were applied as recommended. Departures from §4.3:

- `RemoteCatalogueRefresher` (orchestration, archive validation) lives
  in `MedReminder.Application/Catalogue/`, not in the UI hosted
  service. It depends only on ports (`ICatalogueFeedClient`,
  `IReferenceCatalogueImporter`, `IAppDataLocation`), so its tests run
  on any platform. The hosted service keeps the gate (D1) and the wait
  on `StartupUpdateCheckSignal`.
- `GitHubRawCatalogueFeedClient` lives in
  `MedReminder.Infrastructure.Portable/Catalogue/`, not in
  `MedReminder.Infrastructure`: it has no Windows dependency, and the
  Windows-only Infrastructure test project cannot run on Linux.
- The staging path is
  `RemoteCatalogueRefresher.GetStagingDirectory(IAppDataLocation.DataDirectory)`;
  no `AppDataPaths` getter was added.
- `IReferenceCatalogueImporter` gained a second `ImportAsync` overload
  with `minimumRowCount` instead of an optional parameter, so existing
  call sites are unchanged.
- The remote import logs the same "Reference-catalogue import for IT
  complete" line, with `source=remote feed` and the elapsed time
  appended (§4.5 asks for the duration).

Verification in this session (Linux, .NET SDK 10.0.112): the solution
builds with `EnableWindowsTargeting=true`; Domain, Application,
Infrastructure.Portable and DataImporter tests pass. The new
`CsvReferenceCatalogueImporterTests` cases compile but need Windows to
run (the test project requires the WindowsDesktop runtime). The
workflow's CSV validation was run against the real
`data/aifa-202609.zip` content (160 024 and 338 722 data rows).

### 11.1 Review remediation (2026-09-29)

Fixes applied after the code review of PR #131:

- **Connection lifetime.** The embedded imports dispose their DI scope,
  and with it the importer's open SQLite connection, before the remote
  step starts. The remote step checks `Catalogue:RemoteFeed:Enabled`
  and `CheckForUpdatesOnStartup` before waiting on the signal, and
  opens its own scope only for the refresh. Previously one handle
  stayed open for up to about three minutes, long enough to break the
  file move done by backup restore, archive import and sync join.
- **Write lock.** The remote import runs under `WriteGate` (§4.5).
- **Manifest read errors.** The client maps an `IOException` while
  reading the body to "manifest unavailable"; the refresher guards
  the call as well.
- **Staging cleanup.** Leftovers are removed at the start of every
  run, also when offline or up to date.
- **Workflow.** One UTC timestamp drives the archive name, `version`
  and `generated`. Row checks: absolute floors of 100 000 / 200 000
  rows and at least 90% of the previous run's counts, recorded in
  `latest.json` under `rows`.

The same-month limitation (§2.2 W4) was first accepted here and then
resolved in §11.2.

### 11.2 Same-month republish (2026-09-29)

- Remote imports store `snapshot_version` as
  `yyyymm+yyyyMMddTHHmmssZ`, the suffix being the manifest's
  `generated` in UTC, truncated to seconds
  (`RemoteCatalogueRefresher.LabelFor`, `SnapshotVersion.Compose`).
  Embedded snapshots keep the bare month. The column is TEXT and no UI
  shows it, so no schema change is needed.
- `SnapshotVersion.IsNewer` orders by month, then by suffix; a bare
  month is older than any suffixed label of the same month. Examples:
  embedded `202609` < `202610+…`; `202610+20261005…` >
  `202610+20261002…`; embedded `202610` < remote `202610+…` (one
  extra import when the release and the feed carry the same month).
- The suffix is used only when the manifest also has `sha256`.
  `raw.githubusercontent.com` caches for five minutes, so right after a
  republish a client can get the new manifest with the old archive; the
  hash mismatch rejects that pair and the next start retries. Without a
  hash the old content would be stored under the new label for good.
- The workflow publishes once per month (first successful run from
  day 2 to day 7); a republish is a manual run with `force`.
- Still not possible: rolling back to an older build. A bad month is
  corrected by publishing good data, which carries a later `generated`.

### 11.3 First field run (2026-09-29, Windows, product owner)

Build of PR #131, feed `latest.json` on `main` with `sha256` and
`generated` (`202609`, `2026-09-29T13:34:03Z`); open profile holding
the embedded snapshot `202609`.

First start (18:23, local time):

```
Reference-catalogue import for IT complete: inserted=0, deleted=0, skipped=0, version=202609, ...
Remote AIFA feed: newer snapshot available (local=202609, remote=202609+20260929T133403Z).
Remote AIFA feed: downloaded 4953126 bytes in 915 ms (sha256 b8eeca3aafb6…).
Reference-catalogue import for IT complete: inserted=85711, deleted=85697, skipped=74313, version=202609+20260929T133403Z, ..., source=remote feed, elapsedMs=13434.
```

Second start (18:40):

```
Remote AIFA feed: up to date (local=202609+20260929T133403Z, remote=202609+20260929T133403Z).
```

What this confirms:

- Embedded import of an equal version is a no-op (newer-only rule).
- A manifest with `sha256` and `generated` yields the suffixed label,
  which is newer than the embedded month (§11.2), so the month is
  imported once from the feed.
- Download size and SHA-256 match `latest.json` (`size: 4953126`,
  `sha256: b8eeca3a…`).
- The import replaced the Italian catalogue (85 697 rows deleted,
  85 711 inserted) in 13.4 s, parse included; use cases wait for that
  long at most (`WriteGate`).
- The next start recognises the stored label and downloads nothing.

The Windows test suites (`MedReminder.Infrastructure.Tests`,
`MedReminder.UI.Tests`) pass (product owner, 2026-09-29). The
`catalogue\staging\` folder is empty after the run (product owner).
Not yet checked by hand: a backup restore or archive import started
within the first minute after launch (connection-lifetime fix, §11.1).
PR #131 merged on 2026-09-29.

### 11.4 Per-country publication path (2026-09-29)

Decisions D1–D3 of `ANALYSIS-CATALOGUE-REMOTE-FEEDS-EU-ES-FR.md`, applied
before the first release that ships the feed:

- The AIFA feed publishes under `data/it/` (`latest.json`,
  `aifa-<yyyymm>.zip`) instead of `data/`; the other feeds will use
  `data/eu/`, `data/es/`, `data/fr/`. The data stays on `main` (no
  dedicated data branch).
- The manifest gains `"country": "IT"`. `CatalogueFeedManifestParser`
  reads it (optional, must be a country code when present) and
  `RemoteCatalogueRefresher` rejects, before downloading, a manifest
  that declares another country.
- The published files were moved with their content unchanged
  (`generated` and `sha256` kept), so a client that already imported
  `202609+20260929T133403Z` sees the feed as up to date.
- A build made before this change (the product owner's test build)
  still reads `main/data/latest.json`; after the merge it gets 404 and
  logs "manifest unavailable", which is harmless. No release reads the
  old path.

