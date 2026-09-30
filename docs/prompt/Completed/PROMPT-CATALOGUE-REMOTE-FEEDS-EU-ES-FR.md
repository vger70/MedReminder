# Implementation prompt — Remote feeds for the EU, ES and FR catalogues

Briefing for the Claude Code session that implements
`docs/analysis/ANALYSIS-CATALOGUE-REMOTE-FEEDS-EU-ES-FR.md`. Read it
fully, then the files listed in §2, before changing anything.

---

## 0. Preconditions

Do not start until the product owner confirms both:

1. The first scheduled AIFA run after PR #131 published
   `data/it/aifa-<yyyymm>.zip` and `data/it/latest.json` on `main`, and
   an app start logged `Remote AIFA feed: newer snapshot available …`
   followed by the import line with `source=remote feed`. The EU/ES/FR
   feeds reuse that exact pipeline.
2. Decision D4 of the analysis (which feeds a client downloads) is
   confirmed as written: the reference country's feed (IT, ES or FR)
   plus EU. If the owner changes it, the confirmed value wins over §3.5.

Settled decisions, not to reopen: D1 per-country folders
`data/<country>/`; D2 data on `main`; D3 AIFA already at `data/it/`;
D5 AEMPS source `https://listadomedicamentos.aemps.gob.es/Medicamentos.xls`
with browser headers; D6 thresholds of analysis §3.4; D7 order ES, FR,
EU.

Branching (`CLAUDE.md` §4): ask the owner before creating a branch;
propose `claude/catalogue-feeds-eu-es-fr`. Open the PR after the first
commit and keep `CHANGE_LOG.md` updated.

## 1. Goal

Each of EMA EPAR (`EU`), AEMPS CIMA (`ES`) and ANSM BDPM (`FR`) gets
the AIFA pipeline:

- a GitHub workflow publishes `data/<country>/<prefix>-<yyyymm>.zip`
  and `data/<country>/latest.json` in the format the existing parser
  reads, validated before anything under `data/` changes;
- at startup the app refreshes, from those feeds, the catalogues it
  uses (reference country + EU), with the same checks and ordering as
  AIFA.

No parser changes. The embedded snapshots stay.

## 2. Context to read first

- `CLAUDE.md` (§2 language, §4 PR workflow, §5 runtime data, §7).
- `docs/analysis/ANALYSIS-CATALOGUE-REMOTE-FEEDS-EU-ES-FR.md` (all).
  §3.1–§3.3 hold the runner measurements that drive the scripts.
- `docs/analysis/ANALYSIS-CATALOGUE-REMOTE-FEED.md` §4, §5, §11 (the
  AIFA design, review fixes, versioning, field run).
- `docs/CATALOGUE-DATA.md` §2 (remote feed), §3, §5, §6.
- Code:
  - `src/MedReminder.Application/Catalogue/`: `RemoteCatalogueRefresher.cs`,
    `CatalogueFeedManifest.cs`, `CatalogueFeedOptions.cs`,
    `ICatalogueFeedClient.cs`, `SnapshotVersion.cs`,
    `IReferenceCatalogueImporter.cs`
  - `src/MedReminder.Infrastructure.Portable/Catalogue/GitHubRawCatalogueFeedClient.cs`
  - `src/MedReminder.Infrastructure/InfrastructureServiceCollectionExtensions.cs`
    (catalogue registrations)
  - `src/MedReminder.UI/Hosting/CatalogueRefreshHostedService.cs`
  - `src/MedReminder.UI/appsettings.json` (`Catalogue:RemoteFeed`)
  - `src/MedReminder.Application/Abstractions/UserSettings.cs`
    (`ReferenceCountry`: ISO alpha-2 or `EU`, default `IT`)
  - Parsers: `src/MedReminder.Infrastructure/Catalogue/Parsers/EmaEparParser.cs`,
    `AempsCimaParser.cs`, `AnsmBdpmParser.cs`
  - `scripts/download_aifa.py`, `.github/workflows/download_aifa.yaml`
  - Tests: `tests/MedReminder.Application.Tests/Catalogue/RemoteCatalogueRefresherTests.cs`,
    `CatalogueFeedManifestParserTests.cs`,
    `tests/MedReminder.Infrastructure.Portable.Tests/Catalogue/GitHubRawCatalogueFeedClientTests.cs`,
    fixtures in `tests/fixtures/catalogue/`

## 3. Work items, in order

Each step keeps the solution building and every existing test green.
One commit per step at least; imperative English messages that explain
why.

### Step 1 — Generalise the client (IT only configured)

Behaviour for Italy must not change; the AIFA regression tests are the
guard.

1. **Descriptor** (`Application/Catalogue/CatalogueFeedDescriptor.cs`):
   `Country` (`CountryCode`), `Prefix`, `RequiredEntries`
   (case-insensitive entry names, any folder), `MaxUncompressedBytes`,
   `FileNameFor(version) => $"{Prefix}-{version}.zip"`. A static
   catalogue of the four known feeds:

   | Country | Prefix | Required entries | Max uncompressed |
   |---------|--------|------------------|------------------|
   | IT | `aifa` | `confezioni_fornitura.csv`, `PA_confezioni.csv` | 512 MB |
   | EU | `ema-epar` | `ema-epar.csv` | 64 MB |
   | ES | `aemps` | `aemps.xlsx` | 64 MB |
   | FR | `bdpm` | `CIS_bdpm.txt`, `CIS_COMPO_bdpm.txt` | 64 MB |

2. **Options** (`CatalogueFeedOptions`): add `BaseUrl`
   (default `https://raw.githubusercontent.com/vger70/MedReminder/main/data/`)
   and `Feeds` (dictionary keyed by country code; per feed `Enabled`,
   `MaxDownloadBytes`). URLs: manifest `{BaseUrl}{country lower}/latest.json`,
   archive `{BaseUrl}{country lower}/{prefix}-{version}.zip`. Keep
   `ManifestUrl` and `SnapshotUrlTemplate` as optional **IT-only
   overrides** (a released build may carry them in its config); when
   set they win for IT. Defaults: IT enabled, 64 MB; EU/ES/FR present
   but `Enabled: false` until their feeds exist (steps 3–5 flip them).
3. **Manifest parser**: `TryParse(json, descriptor, out manifest, out error)`;
   `file` must equal `descriptor.FileNameFor(version)`; `country`, when
   present, must equal the descriptor's country (move this check from
   the refresher into the parser). Remove `ExpectedFileName` from the
   record or make it take the descriptor.
4. **Feed client**: `GetLatestAsync(descriptor, ct)` and
   `DownloadAsync(descriptor, manifest, destinationPath, ct)`; URL
   building and the size cap come from the options for that country.
   Everything else unchanged (HTTPS, no redirects, 4 KB manifest cap,
   streaming cap, `.part` + rename, per-call timeouts, `IOException` →
   unavailable).
5. **Refresher**: `RunAsync(descriptor, ct)`. Country, entries and caps
   from the descriptor; `ValidateArchive(stream, descriptor)`. Keep
   `LabelFor`, the SHA-256/size checks, the row floor (half the
   country's current rows), `WriteGate`, staging cleanup at the start
   of each run and deletion in `finally`. Log prefix becomes
   `Remote catalogue feed {Country}: …`; keep the line
   `Reference-catalogue import for {Country} complete: … source=remote feed, elapsedMs=…`
   unchanged (documented in `CATALOGUE-DATA.md`).
6. **Selection** (`Application/Catalogue/CatalogueFeedSelection.cs`,
   pure): given `ReferenceCountry` and the options, return the enabled
   descriptors in the order IT, EU, ES, FR, keeping the reference
   country when it is IT, ES or FR, plus EU. `EU` as reference → EU
   only. Any other valid code (for example `DE`) → EU only. An invalid
   value falls back to `IT`, as `MainForm.BuildCatalogueContext` does.
7. **Hosted service**: after the gates and the signal wait (unchanged),
   loop over the selection; one DI scope per feed, disposed right after
   it (the connection-lifetime rule of the AIFA review, §11.1); a
   failure in one feed is logged and never stops the next.
8. **Tests** (Application.Tests, Infrastructure.Portable.Tests):
   descriptor file names; parser with prefix and country (match,
   mismatch, absent); client URL building per country and IT
   overrides; refresher per descriptor (entries, caps, country passed
   to the importer, floor per country); selection table (IT, ES, FR,
   EU, DE, invalid, disabled feeds); isolation (feed 1 throws, feed 2
   still runs). Existing AIFA tests stay green, adapted only in
   signatures.

### Step 2 — Shared script module

`scripts/feeds/common.py`, used by the new feeds only (AIFA moves in
step 6):

- `session()` with the same `urllib3.Retry` as `download_aifa.py`
  (5 retries, 429/5xx, back-off 0/30/60/120/120 s, `Retry-After`).
- `BROWSER_HEADERS` (User-Agent, `Accept`, `Accept-Language`,
  `Referer` parameter).
- `run_time()` → one UTC timestamp; `version_for(ts)` → `yyyymm`.
- `already_published(data_dir, version, force)`; `previous_rows(data_dir)`.
- `check_rows(name, count, absolute_floor, previous)` → raise when
  below the floor or below 90% of the previous count.
- `looks_like_html(bytes)`; `write_zip(path, entries)`;
  `publish(data_dir, country, version, zip_path, file_count, rows)`
  → sha256, size, manifest (`country`, `version`, `file`, `generated`,
  `file_count`, `sha256`, `size`, `rows`), move, retention (3 archives).
- `pytest` tests in `scripts/feeds/tests/` for every function that does
  not touch the network; a workflow `scripts_tests.yaml` runs them on
  changes under `scripts/feeds/`.

### Step 3 — ES feed (AEMPS)

- `scripts/feeds/aemps.py`: `GET https://listadomedicamentos.aemps.gob.es/Medicamentos.xls`
  **with browser headers** (without them the server answers 403, analysis
  §3.2); reject HTML and anything not starting with `PK\x03\x04`;
  save as `aemps.xlsx` (bytes unchanged; `openpyxl` rejects the `.xls`
  name); check the first sheet's header equals the 15 names of
  `CATALOGUE-DATA.md` §5 in order; data rows ≥ 20 000 and ≥ 90% of
  previous; zip as `aemps-<v>.zip`; publish to `data/es/`.
- `.github/workflows/download_aemps.yaml`: same schedule days as
  `download_aifa.yaml` on `main` (today `2,9,16,23`), at `20 3`;
  `force` input; `pip install requests beautifulsoup4 openpyxl`;
  concurrency group `catalogue-feeds-publish`
  (`cancel-in-progress: false`); commit `data/es/`, then
  `git pull --rebase` and `git push`, one retry on rejection.
- Add the same concurrency group to `download_aifa.yaml`.
- Client: `Feeds:ES:Enabled` → `true` in `appsettings.json` and the
  options default.

### Step 4 — FR feed (ANSM BDPM)

- `scripts/feeds/bdpm.py`: scrape
  `https://base-donnees-publique.medicaments.gouv.fr/telechargement`
  for `href`s ending in `CIS_bdpm.txt`, `CIS_CIP_bdpm.txt`,
  `CIS_COMPO_bdpm.txt`; fall back to `/download/file/<name>`
  (`telechargement.php` is 404 since 2026). Keep raw bytes. Checks:
  not HTML; `CIS_bdpm.txt` and `CIS_COMPO_bdpm.txt` decode as cp1252,
  `CIS_CIP_bdpm.txt` as UTF-8 (cp1252 accepted); every line has 12 /
  8 / 13 tab-separated columns; first `CIS_bdpm.txt` row's column 4
  starts with `Autorisation`; rows ≥ 12 000 / 25 000 / 15 000 and ≥ 90%
  of previous. Zip the three files as `bdpm-<v>.zip`; publish to
  `data/fr/`.
- `download_bdpm.yaml` at `40 3`, otherwise as step 3.
- Client: enable `FR`.

### Step 5 — EU feed (EMA EPAR)

- `scripts/feeds/ema.py`: `GET https://www.ema.europa.eu/en/documents/report/medicines-output-medicines-report_en.xlsx`
  (fallback: scrape `https://www.ema.europa.eu/en/medicines/download-medicine-data`
  for that `href`). Do not rely on `HEAD` (it reports length 0).
  Conversion with `openpyxl` (read-only), sheet `Medicine` or the first:
  - header = first row whose first cell is `Category` (row 9 today);
  - **trim** the header to its last non-empty cell (the sheet declares
    1 024 columns for 39 real ones) and cut every row to that width;
  - skip fully empty rows;
  - cells: `None` → empty; `str` with `\r`, `\n`, `\t` → single spaces,
    repeated spaces collapsed, trimmed; integral floats without `.0`;
    dates as ISO `yyyy-mm-dd`;
  - write `ema-epar.csv`: `;` delimiter, UTF-8 without BOM,
    `csv.QUOTE_MINIMAL` (2 260 cells contain `;` today), `\n` line ends.
  - Validate by re-reading the CSV with `csv.reader(delimiter=";")`:
    every row has the header's column count; the 8 columns
    `EmaEparParser` requires are present; rows ≥ 2 000, `Human` rows
    ≥ 1 800, both ≥ 90% of previous. Zip as `ema-epar-<v>.zip`; publish
    to `data/eu/`.
- Parser compatibility test (Infrastructure.Tests, Windows): feed a
  CSV produced by `ema.py` from a small XLSX fixture (build one with
  `openpyxl` from `tests/fixtures/catalogue/ema-epar-sample.csv`,
  adding a cell with `;`, one with a newline and 8 metadata rows) to
  `EmaEparParser` and assert the row count.
- `download_ema.yaml` at `0 4`, otherwise as step 3.
- Client: enable `EU`.

### Step 6 — Move AIFA onto the shared module

Rewrite `download_aifa.py` on `common.py` with identical output
(`data/it/`, same validation, same thresholds, `file_count` instead of
`csv_count`; nothing reads `csv_count`). Check with the script tests
and one manual `workflow_dispatch` (it must exit "already published").

### Step 7 — Documentation

- `CATALOGUE-DATA.md` §3, §5, §6 rewritten like §2 (remote feed first,
  embedded refresh optional), with the runner facts of analysis §3.
- `THIRD-PARTY-NOTICES.md`: the EMA, AEMPS and BDPM snapshots are also
  redistributed from `data/<country>/`, with their attributions.
- `ANALYSIS.md`: outbound calls and hosted-service row list all feeds.
- User guides (`docs/USER_GUIDE.{en,it,fr,es,de}.md`): the sentence on
  the self-updating Italian catalogue becomes "the catalogue of your
  reference country and the EU one".
- Analysis EU/ES/FR: §11 implementation notes (departures, verification).
- `CHANGE_LOG.md`.

## 4. Constraints

- English only in code, comments, logs, scripts and docs (`CLAUDE.md`
  §2); the user guides keep their language.
- No parser change. No change to `SnapshotVersion`, the importer's
  newer-only rule, `WriteGate` use or the row floor.
- Never write outside `%LOCALAPPDATA%\MedReminder\`; staging stays
  `catalogue\staging\`.
- No new NuGet package. Python dependencies only in workflows
  (`requests`, `beautifulsoup4`, `openpyxl`, `pytest`).
- One workflow per source; all share the `catalogue-feeds-publish`
  concurrency group; `contents: write` only.
- A script never touches `data/` before every check passed.
- Do not log file contents or anything profile-specific.

## 5. Verification

- Linux session: build with `-p:EnableWindowsTargeting=true`; run
  Domain, Application, Infrastructure.Portable, DataImporter tests and
  `pytest scripts/feeds/tests`.
- Ask the owner to run on Windows:
  ```powershell
  dotnet restore MedReminder.sln
  dotnet build   MedReminder.sln -c Release
  dotnet test    MedReminder.sln -c Release
  ```
- Per feed, after merge: one manual `workflow_dispatch` must publish
  `data/<country>/`; a second one must exit "already published".
- App check (owner): with reference country `ES`, a start logs
  `Remote catalogue feed ES: newer snapshot available …` and the import
  line for ES with `source=remote feed`, then the same for EU; a second
  start logs "up to date" for both; `catalogue\staging\` is empty.

## 6. Deliverables

- One PR, opened after the first commit, with `CHANGE_LOG.md` updated.
- Steps 3–5 may ship as separate PRs if the owner prefers enabling the
  feeds one at a time; each PR then enables only its own country.
