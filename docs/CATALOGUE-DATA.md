# Reference-catalogue data

Operational notes for the people who refresh the reference-catalogue
snapshots shipped inside MedReminder. Read once before your first
refresh, then use it as a checklist each month.

The engineering design behind the catalogue lives in
[`docs/ANALYSIS-DRUG-CATALOGUE.md`](ANALYSIS-DRUG-CATALOGUE.md).
This file only covers the mechanical steps: where to fetch the data,
how to name and drop the file into the repo, and how it flows through
the build.

---

## 1. What ships and where

For each supported country the Infrastructure assembly embeds a
single ZIP snapshot under
`src/MedReminder.Infrastructure/Assets/Catalogue/<country>/`. The
current shipped set:

| Country | File                                                                    | Source                                                 | Terms                                                 |
|---------|-------------------------------------------------------------------------|--------------------------------------------------------|-------------------------------------------------------|
| `it`    | `Assets/Catalogue/it/aifa-<yyyymm>.zip`                                 | AIFA — Agenzia Italiana del Farmaco                    | CC BY 4.0                                             |
| `eu`    | `Assets/Catalogue/eu/ema-epar-<yyyymm>.zip`                             | EMA — European public assessment reports (EPAR)        | EMA legal notice (reuse allowed)                      |
| `es`    | `Assets/Catalogue/es/aemps-<yyyymm>.zip`                                | AEMPS CIMA — "Medicamentos" register                   | Public-sector reuse (Spain Law 37/2007), attribution  |
| `fr`    | `Assets/Catalogue/fr/bdpm-<yyyymm>.zip`                                 | ANSM BDPM — Base de données publique des médicaments   | Licence Ouverte Etalab 2.0                            |

The `<yyyymm>` suffix (e.g. `aifa-202609.zip`, `ema-epar-202609.zip`,
`aemps-202609.zip`, `bdpm-202609.zip`) identifies the release / export
date. That literal string is what `EmbeddedSnapshotProvider.TryOpen`
extracts from the resource name and passes down to
`CsvReferenceCatalogueImporter` as `snapshot_version`. Every imported
row carries it so the boot-time importer can detect a newer snapshot
and short-circuit when the DB is already up to date.
`CatalogueRefreshHostedService` iterates over `{ IT, EU, ES, FR }` on
startup (each country in its own transaction, per
`ANALYSIS-DRUG-CATALOGUE.md` §3.4) — a broken snapshot for one
country never blocks the others.

The same four catalogues are also published monthly by GitHub
workflows under `data/<country>/` on the `feeds` branch (§1.1), and
the app refreshes from there at startup without a new release (§2.1). The embedded
snapshots stay the offline baseline:

| Country | Workflow (UTC, days 2, 9, 16, 23) | Script | Published |
|---------|-----------------------------------|--------|-----------|
| `it` | `download_aifa.yaml`, 03:07 | `scripts/feeds/aifa.py` | `data/it/aifa-<yyyymm>.zip` |
| `es` | `download_aemps.yaml`, 03:27 | `scripts/feeds/aemps.py` | `data/es/aemps-<yyyymm>.zip` |
| `fr` | `download_bdpm.yaml`, 03:47 | `scripts/feeds/bdpm.py` | `data/fr/bdpm-<yyyymm>.zip` |
| `eu` | `download_ema.yaml`, 04:07 | `scripts/feeds/ema.py` | `data/eu/ema-epar-<yyyymm>.zip` |

Each folder also holds `latest.json` (§2.1) and keeps the 3 newest
archives. The scripts share `scripts/feeds/common.py` (retries, run
timestamp, row floors, publication); their unit tests
(`scripts/feeds/tests/`) run in `scripts_tests.yaml`. Each workflow
has its own concurrency group (`feed-<workflow name>`) and publishes
only its own paths, so feeds may run at the same time (§1.1). Each
script changes nothing under `data/` until every check has passed.

### 1.1 The `feeds` branch

Every published archive used to stay in `main`'s history: about 9.5 MB
a month for the four catalogues, whatever the retention in `data/`.
The published files now live on the `feeds` branch, which always holds
a single parentless commit (`data/`, `.gitattributes`, a `README.md`).
Each publish replaces that commit, so the branch costs the current
files only; the replaced archives become unreachable and GitHub
removes them at its own garbage collection.

- Clients read
  `https://raw.githubusercontent.com/vger70/MedReminder/feeds/data/`
  (`CatalogueFeedOptions.DefaultBaseUrl`, `Catalogue:RemoteFeed:BaseUrl`
  in `appsettings.json`).
- Each workflow runs `scripts/feeds/feeds_branch.sh load` before its
  script, so the script decides what is new against the published
  state, then `feeds_branch.sh publish "<message>" <pathspec>...`
  with the paths the feed owns (`data/es`; `data/it` without
  `data/it/shortages` and `data/it/equivalents` for AIFA). A publish
  starts from the branch as it is at push time and replaces only those
  paths, on a temporary index; the checkout of `main` is not touched.
  The push uses `--force-with-lease`: when another feed published in
  between, the publish is rebuilt on the new branch (up to 5 attempts).
  A publish whose content equals the branch is skipped. Feeds therefore
  need no shared concurrency group: with one, GitHub keeps a single
  pending run per group, and a queued run of one feed would cancel the
  queued run of another.
  Only a run on the default branch publishes; a run dispatched from
  another branch does not replace what clients download.
- The first run of any feed workflow after the merge creates the
  branch from `main`'s `data/`. Run one by hand (without `force`)
  before releasing a client that reads `feeds`.
- **Transition.** Releases up to v2.12.1 read `main/data/`. Until they
  are out of use, the "Mirror to main" step keeps committing the same
  files to `main`. Then set the repository variable
  `FEEDS_MAIN_MIRROR` to `false` (Settings → Secrets and variables →
  Actions → Variables): the step is skipped and `main` stops growing.
  `data/` on `main` can then be removed in a normal commit; its history
  stays unless rewritten, which is not planned.
- The embedded snapshots under
  `src/MedReminder.Infrastructure/Assets/Catalogue/` add about 9.3 MB
  to `main` at each refresh. With the remote feeds they are only the
  offline baseline: refresh them a few times a year, not every month.

---

## 2. Refresh procedure (Italy)

The Italian catalogue has two delivery paths:

- **Remote feed (monthly, no release needed).** The workflow
  `.github/workflows/download_aifa.yaml` runs on days 2, 9, 16 and 23
  of each month at 03:07 UTC (and on demand), builds
  `data/it/aifa-<yyyymm>.zip` and rewrites `data/it/latest.json`. The first successful run of the month
  publishes; later runs find the month's version in `latest.json` and
  exit without changes. HTTP errors from AIFA (429, 5xx, timeouts,
  resets) are retried five times over about 5.5 minutes before a run
  fails; a failed run is retried by the next scheduled run, a week
  later. A manual
  run with the `force` input rebuilds an already published month;
  clients re-import it (see "Republishing a month" below). The app
  downloads it at startup (§2.1). No manual step.
- **Embedded snapshot (per release, optional).** The ZIP embedded in
  the Infrastructure assembly is the baseline for a first run without
  network. Refreshing it at each release keeps that baseline recent;
  skipping it is harmless, because a newer remote import is never
  overwritten by an older embedded one.

### 2.1 Remote feed

`data/it/latest.json`, written by `scripts/feeds/aifa.py`. Every feed
writes the same manifest in its own `data/<country>/` folder:

```json
{
  "country": "IT",
  "version": "202610",
  "file": "aifa-202610.zip",
  "generated": "2026-10-02T03:00:12.345678+00:00",
  "file_count": 2,
  "sha256": "<64 hex characters>",
  "size": 5016171,
  "rows": {
    "confezioni_fornitura.csv": 160024,
    "PA_confezioni.csv": 338722
  }
}
```

- `rows` holds the row counts the script validated, by file; the next
  run compares against them. Manifests written before the shared
  module carry `csv_count` instead of `file_count`; clients ignore
  both.
- `version` is the **download month** in UTC, not an AIFA release
  date. With the cron on day 2 the two usually coincide. The archive
  name, `version` and `generated` all come from one timestamp taken
  when the run starts.
- The script fails, leaving `data/` untouched, when either CSV is
  missing, lacks a column `AifaSnapshotParser` requires, has fewer
  than 100 000 (`confezioni_fornitura.csv`) or 200 000
  (`PA_confezioni.csv`) data rows, or has fewer than 90% of the rows
  recorded under `rows` by the previous run. If AIFA genuinely shrinks
  a file by more than 10%, lower the previous count in
  `data/it/latest.json` on the `feeds` branch by hand and re-run.
- `data/it/` keeps the 3 newest archives.
- **Republishing a month** (any feed). A forced run in the same month
  overwrites `<prefix>-<yyyymm>.zip` and writes a new `generated` and `sha256`. Clients
  store remote imports as `yyyymm+<generated, UTC>` (for example
  `202610+20261005T030012Z`), so the later build replaces the earlier
  one at their next start. This needs `sha256` in the manifest: without
  it clients store the bare month and do not re-import. A month cannot
  be rolled back to an older build; publish corrected data instead.

Client behaviour, the same for every feed (`RemoteCatalogueRefresher`,
`docs/analysis/ANALYSIS-CATALOGUE-REMOTE-FEED.md`,
`ANALYSIS-CATALOGUE-REMOTE-FEEDS-EU-ES-FR.md`):

- Runs once at startup, after the passive update check, when
  `Catalogue:RemoteFeed:Enabled` and the user's *Check for updates on
  startup* setting are both on.
- Refreshes the feeds the autocomplete reads: the reference country's
  (when it is IT, ES or FR) and EU, in the order IT, EU, ES, FR. A
  reference country without a feed refreshes EU only. Other countries
  keep their current catalogue until the user switches to them.
  `Catalogue:RemoteFeed:Feeds:<country>:Enabled` turns one feed off;
  `MaxDownloadBytes` caps its archive. A failure in one feed does not
  stop the next.
- URLs: `{BaseUrl}<country>/latest.json` and
  `{BaseUrl}<country>/<prefix>-<version>.zip`. The manifest must name
  that feed's archive and, when present, its country.
- Downloads only when `version` is newer than the open profile's
  catalogue for that country; other profiles update at their own next
  start.
- Stages the file in `%LOCALAPPDATA%\MedReminder\catalogue\staging\`,
  checks size and SHA-256 (when present) and the entries the country's
  parser reads, imports it, and deletes it in every outcome.
- Rejects a snapshot with fewer than half the rows of the current
  catalogue for that country, leaving it unchanged.
- Logs `Remote catalogue feed <country>: …` (`up to date`,
  `newer snapshot available`, rejections) and, after an import,
  `Reference-catalogue import for <country> complete: … source=remote feed`.

### 2.2 Embedded snapshot

1. **Take the archive published by the workflow**:
   `data/it/aifa-<yyyymm>.zip` from the `feeds` branch. It already has the layout the
   parser expects (both CSV files at the root; an extra `atc.csv` in
   older archives is ignored):

   ```
   aifa-<yyyymm>.zip
   ├── confezioni_fornitura.csv
   └── PA_confezioni.csv
   ```

   To build one by hand instead, download `confezioni_fornitura.csv`
   and `PA_confezioni.csv` from <https://www.aifa.gov.it/liste-dei-farmaci>,
   keep the file names verbatim (`AifaSnapshotParser` looks them up by
   name, case-insensitive) and zip them at the archive root.

2. **Copy it into the repo** at
   `src/MedReminder.Infrastructure/Assets/Catalogue/it/aifa-<yyyymm>.zip`.
   The `<EmbeddedResource>` glob in
   `src/MedReminder.Infrastructure/MedReminder.Infrastructure.csproj`
   picks it up automatically — no csproj edit needed.

3. **Delete the previous ZIP** in the same folder. Only one AIFA
   snapshot must ship at a time; leaving two behind would let
   `EmbeddedSnapshotProvider.TryOpen` pick the wrong one (it takes
   the first resource matching the country prefix).

4. **Commit** with an imperative English message, e.g.
   `Refresh AIFA snapshot to 202610`.

5. **Verify** locally on Windows:
   ```powershell
   dotnet restore MedReminder.sln
   dotnet build   MedReminder.sln -c Release
   dotnet test    MedReminder.sln -c Release
   ```
   Then run the app once and check
   `%LOCALAPPDATA%\MedReminder\logs\medreminder-*.log`. On the first
   boot after the version changed, `CatalogueRefreshHostedService` logs
   `Reference-catalogue import for IT complete: inserted=<n> … version=<yyyymm>`.
   On later boots the same line reports `inserted=0` and the version
   stored in the database, which may be newer than the embedded one
   when the remote feed already delivered a later month.

---

## 3. Refresh procedure (EU — EMA EPAR)

Two delivery paths, as for Italy:

- **Remote feed (monthly, no release needed).**
  `.github/workflows/download_ema.yaml` runs `scripts/feeds/ema.py` on
  days 2, 9, 16 and 23 at 04:07 UTC (and on demand, with the same
  `force` input as AIFA) and publishes `data/eu/ema-epar-<yyyymm>.zip`
  and `data/eu/latest.json` (§2.1). No manual step.
- **Embedded snapshot (per release, optional)**, §3.2.

### 3.1 Remote feed

Source: the EMA "Medicines" report,
<https://www.ema.europa.eu/en/documents/report/medicines-output-medicines-report_en.xlsx>,
linked from <https://www.ema.europa.eu/en/medicines/download-medicine-data>
(the script falls back to that page's link if the direct URL fails).
EMA regenerates it daily. No login, no header filter. Reuse is governed
by EMA's legal notice (Commission reuse decision 2011/833/EU), which
allows redistribution with attribution.

Facts measured from a GitHub runner on 2026-09-29: 901 861 bytes of
XLSX; one sheet `Medicine`; 8 metadata rows, then the header on row 9;
39 real columns although the sheet declares 1 024; 2 746 data rows,
2 351 of them `Human`; 2 260 cells contain `;` (multi-substance lists)
and 5 contain line breaks or tabs; dates are text. `HEAD` reports a
length of 0 (a cached response), so the script never relies on it.

The parser reads CSV, so the script converts the workbook to
`ema-epar.csv`:

- sheet `Medicine`, or the first sheet;
- header = the first row whose first cell is `Category`; trailing
  empty header cells dropped and every row cut to the header width;
- fully empty rows skipped;
- cells: CR, LF and TAB become spaces and runs of spaces collapse (the
  parser reads one record per physical line); integral numbers lose
  the `.0`; a date-typed cell becomes `yyyy-mm-dd`; empty cells stay
  empty;
- `;` delimiter, UTF-8 without BOM, `\n` line ends, fields quoted only
  when needed (every `;` list is quoted).

The archive holds `ema-epar.csv` at its root. The run fails, leaving
`data/` untouched, when the download is not an XLSX, when the CSV
re-read line by line has a line whose column count differs from the
header's, when one of the 8 columns `EmaEparParser` requires is
missing (`Category`, `Name of medicine`, `EMA product number`,
`Medicine status`, `Active substance`, `ATC code (human)`,
`Marketing authorisation developer / applicant / holder`,
`Medicine URL`), or when the data rows fall below 2 000 or the `Human`
rows below 1 800, or either below 90% of the previous run (recorded in
`rows` as `ema-epar.csv` and `ema-epar.csv (Human)`).

### 3.2 Embedded snapshot

1. **Take the archive published by the workflow**:
   `data/eu/ema-epar-<yyyymm>.zip` from the `feeds` branch. It already has the
   layout the parser expects:

   ```
   ema-epar-<yyyymm>.zip
   └── ema-epar.csv
   ```

   To build one by hand instead, download the report, convert it with
   the rules of §3.1 (`convert()` in `scripts/feeds/ema.py` implements
   them) and zip the CSV at the archive root as `ema-epar.csv`
   (case-insensitive; `EmaEparParser` looks it up by name).

2. **Copy it into the repo** at
   `src/MedReminder.Infrastructure/Assets/Catalogue/eu/ema-epar-<yyyymm>.zip`.
   The `<EmbeddedResource>` glob in
   `src/MedReminder.Infrastructure/MedReminder.Infrastructure.csproj`
   picks it up automatically — no csproj edit needed.

3. **Delete the previous ZIP** in the same folder. Only one EPAR
   snapshot must ship at a time; leaving two behind lets
   `EmbeddedSnapshotProvider.TryOpen` pick whichever the reflection
   layer surfaces first for `country = "EU"`.

4. **Commit** with an imperative English message, e.g.
   `Refresh EMA EPAR snapshot to 202610`.

5. **Verify** locally on Windows:
   ```powershell
   dotnet restore MedReminder.sln
   dotnet build   MedReminder.sln -c Release
   dotnet test    MedReminder.sln -c Release
   ```
   Then run the app once: `CatalogueRefreshHostedService` logs
   `Reference-catalogue import for EU complete: inserted=… version=<yyyymm>`
   in `%LOCALAPPDATA%\MedReminder\logs\medreminder-*.log` on the
   first boot after the version changed. On later boots the line
   reports `inserted=0` and the stored version, which may be a newer
   remote import.

The EMA Article 57 dataset (pan-EEA, one row per national
authorisation, ~160 000 rows) is a **different** product and does
not populate MedReminder's `EU` catalogue. If a future release
wants to widen the scope to national EEA rows, that goes through a
new parser and a new snapshot — not through this file.

---

## 4. Reducing the fixture (for tests)

Fixtures under `tests/fixtures/catalogue/` are curated subsets of
real snapshots, stratified to cover the edge cases the importers
must handle. Regenerate a fixture only when the upstream schema
itself changes; a routine snapshot refresh does not touch fixtures.

### 4.1 AIFA fixture

Curated ~200-row subset covering `Sospesa`, `Procedura Centralizzata`,
OTC, hospital-only, well-known brands and multi-ingredient
combinations.

1. Pick a snapshot ZIP whose CSVs still parse cleanly with the M1
   parser.
2. Filter down to the rows you want to keep — preserve at least one
   row per case listed in
   `docs/ANALYSIS-DRUG-CATALOGUE.md` §3.1 (bullet "Fixture").
3. Overwrite the three fixture CSVs
   (`aifa-confezioni-sample.csv`, `aifa-pa-sample.csv`,
   `aifa-atc-sample.csv`) and update the CHANGE_LOG entry that
   documents the fixture composition.
4. Re-run `dotnet test tests/MedReminder.Infrastructure.Tests` and
   fix any assertion that now counts the wrong number of rows —
   don't silently loosen them.

### 4.2 EMA EPAR fixture

`ema-epar-sample.csv` is a stratified ~70-row Human-only subset
plus 3 Veterinary rows (to exercise the parser's `Category` filter),
covering every non-veterinary `Medicine status` value EMA emits
(`Authorised`, `Withdrawn`, `Refused`, `Suspended`, `Lapsed`,
`Application withdrawn`, `Expired`, `Revoked`, `Opinion`), plus at
least one multi-active-substance row separated by `;` (DuoPlavin,
Symtuza, Qdenga) and a few well-known brands to keep assertions
readable.

1. Convert the current EPAR XLSX to CSV per §3 above.
2. Filter to the same coverage as the current fixture (Human status
   buckets, multi-substance, no-ATC, well-known names) — see the
   `EmaEparParserTests` assertions for the exact required rows.
3. Overwrite `tests/fixtures/catalogue/ema-epar-sample.csv` and
   adjust the row counts in
   `EmaEparParserTests`, `CsvReferenceCatalogueImporterTests`
   (the M3 IT+EU tests) and `SqliteReferenceCatalogueQueryServiceTests`
   (the M3 cross-country tests). Don't silently loosen counts.
4. Re-run `dotnet test tests/MedReminder.Infrastructure.Tests`.

`ema-epar-sample.xlsx` holds the same rows laid out like the EMA
report (8 metadata rows above the header, a sheet declared wider than
its 39 columns, one cell with a line break, a tab and double spaces).
`ema-epar-from-xlsx.csv` is `scripts/feeds/ema.py`'s conversion of it:
`test_ema.py` checks that the script still produces exactly that file,
and `EmaEparParserTests` checks that the parser reads it to the same
rows as `ema-epar-sample.csv`. After changing `ema-epar-sample.csv`,
rebuild the XLSX with `openpyxl` along those lines and regenerate the
CSV with `ema.convert()`.

### 4.3 AEMPS / CIMA fixture

`aemps-cima-sample.xlsx` is a stratified ~140-row subset of the real
CIMA "Medicamentos" export, generated by
`scripts/build_aemps_fixture.py` at fixture-refresh time (kept as a
reference in the repo; not run by CI). Covers:

- each Estado bucket (`Autorizado`, `Anulado`, `Suspenso`) — the
  parser must let all three through;
- multi-ingredient rows (`Nº P. Activos ≥ 2`) — exercises the
  `", "` splitter;
- the four dispensing-regime free-text buckets AEMPS emits
  (prescription, hospital, diagnostic, biológicos) — they land on
  `DispensingRegime` verbatim;
- well-known Spanish brands (NOLOTIL, ENANTYUM, GELOCATIL,
  PARACETAMOL, AUGMENTINE, ATORVASTATINA, …) so assertions read
  naturally.

1. Save the current CIMA XLSX locally (the exporter labels it
   `Medicamentos.xls` even though the payload is XLSX). Rename to
   `.xlsx` so openpyxl accepts it.
2. Run `scripts/build_aemps_fixture.py` (or its documented
   equivalent), pointing `SRC` at the local file. It writes
   `tests/fixtures/catalogue/aemps-cima-sample.xlsx`.
3. Adjust the row counts in `AempsCimaParserTests`,
   `CsvReferenceCatalogueImporterTests` (the M4 IT+EU+ES+FR tests)
   and `SqliteReferenceCatalogueQueryServiceTests` (the M4
   cross-country tests). Don't silently loosen counts.
4. Re-run `dotnet test tests/MedReminder.Infrastructure.Tests`.

### 4.4 ANSM / BDPM fixture

Three sibling fixtures — `bdpm-cis-sample.txt`,
`bdpm-compo-sample.txt`, `bdpm-cip-sample.txt` — carrying a curated
~100-CIS subset with matching COMPO / CIP rows. Generated by
`scripts/build_bdpm_fixture.py` at fixture-refresh time. Coverage:

- each `Statut administratif AMM` bucket (`Autorisation active`,
  `Autorisation abrogée`, `Autorisation retirée`,
  `Autorisation archivée`);
- at least one row per `Type de procédure AMM` value the parser
  skips (`Enreg homéo (Proc. Nat.)`, `Enreg homéo (Proc. Décentr.)`);
- multi-ingredient CIS (2+ COMPO rows per CIS) so the join is
  exercised;
- accented characters (é, è, ù, à, œ) so the ISO-8859-15 encoding
  path round-trips.

1. Download the current BDPM TSVs from
   <https://base-donnees-publique.medicaments.gouv.fr/telechargement>.
2. Run `scripts/build_bdpm_fixture.py`, pointing it at the local
   TSVs. It rewrites the three fixture files, preserving the exact
   ISO-8859-15 encoding and CRLF terminators.
3. Adjust the row counts in `AnsmBdpmParserTests`,
   `CsvReferenceCatalogueImporterTests` and
   `SqliteReferenceCatalogueQueryServiceTests`. Don't silently
   loosen counts.
4. Re-run `dotnet test tests/MedReminder.Infrastructure.Tests`.

The parser ignores `CIS_CIP_bdpm.txt` (CIP is a packaging-level key
that MedReminder's reference catalogue does not use); the file is
included in both the shipped ZIP and the fixture for completeness
so the on-disk layout stays symmetric with what ANSM publishes.

---

## 5. Refresh procedure (Spain — AEMPS CIMA)

Two delivery paths, as for Italy:

- **Remote feed (monthly, no release needed).**
  `.github/workflows/download_aemps.yaml` runs `scripts/feeds/aemps.py`
  on days 2, 9, 16 and 23 at 03:27 UTC (and on demand, with the same
  `force` input as AIFA) and publishes `data/es/aemps-<yyyymm>.zip` and
  `data/es/latest.json` (§2.1). No manual step.
- **Embedded snapshot (per release, optional)**, §5.2.

### 5.1 Remote feed

Source: <https://listadomedicamentos.aemps.gob.es/Medicamentos.xls>,
AEMPS's static download server (the CIMA "Medicamentos" export). No
login. Reuse is governed by Spain's public-sector information reuse
regime (Law 37/2007), which allows redistribution with attribution
("Fuente: AEMPS").

Facts measured from a GitHub runner on 2026-09-29: without browser
request headers the server answers **403** (an `openresty` proxy
filtering on headers, not on runner IP ranges); with a browser
User-Agent plus `Accept`, `Accept-Language` and `Referer` it answers
200 with 2 758 433 bytes. The file is labelled `.xls` but is an XLSX
(a ZIP container); `openpyxl` refuses the `.xls` name, so the script
saves it as `aemps.xlsx`. First sheet `Hoja1`, 26 763 data rows.

The script sends the browser headers and stores the bytes unchanged as
`aemps.xlsx` at the archive root. The run fails, leaving `data/`
untouched, when the response is an HTML page or not an XLSX container
(a legacy BIFF `.xls` is rejected with its own message), when the
first sheet's header is not exactly these 15 columns in this order
(case-insensitive):

`Nº Registro | Medicamento | Laboratorio | Fecha Aut. | Estado |
Fecha Estado | Cód. ATC | Principios Activos | Nº P. Activos |
¿Comercializado? | ¿Triangulo Amarillo? | Observaciones |
¿Sustituible? | ¿Afecta conducción? | ¿Problemas de suministro?`

or when the data rows fall below 20 000 or below 90% of the previous
run.

If the header filter tightens (for example into a JavaScript
challenge), the fallbacks listed in
`docs/analysis/ANALYSIS-CATALOGUE-REMOTE-FEEDS-EU-ES-FR.md` §3.2 apply:
the CIMA REST API, a self-hosted runner, a semi-manual intake, or the
embedded path alone.

### 5.2 Embedded snapshot

1. **Take the archive published by the workflow**:
   `data/es/aemps-<yyyymm>.zip` from the `feeds` branch:

   ```
   aemps-<yyyymm>.zip
   └── aemps.xlsx
   ```

   To build one by hand instead, download the export from the URL of
   §5.1 in a browser, rename it to `aemps.xlsx` (the name inside the
   archive is case-insensitive; `AempsCimaParser` looks it up by name)
   and zip it at the archive root.

2. **Copy it into the repo** at
   `src/MedReminder.Infrastructure/Assets/Catalogue/es/aemps-<yyyymm>.zip`.
   The `<EmbeddedResource>` glob in
   `src/MedReminder.Infrastructure/MedReminder.Infrastructure.csproj`
   picks it up automatically — no csproj edit needed.

3. **Delete the previous ZIP** in the same folder. Only one AEMPS
   snapshot must ship at a time; leaving two behind lets
   `EmbeddedSnapshotProvider.TryOpen` pick whichever the reflection
   layer surfaces first for `country = "ES"`.

4. **Commit** with an imperative English message, e.g.
   `Refresh AEMPS CIMA snapshot to 202610`.

5. **Verify** locally on Windows:
   ```powershell
   dotnet restore MedReminder.sln
   dotnet build   MedReminder.sln -c Release
   dotnet test    MedReminder.sln -c Release
   ```
   Then run the app once: `CatalogueRefreshHostedService` logs
   `Reference-catalogue import for ES complete: inserted=… version=<yyyymm>`
   in `%LOCALAPPDATA%\MedReminder\logs\medreminder-*.log` on the
   first boot after the version changed.

The alternative CIMA distribution — an XML "Prescripción" bundle
(~200 MB decompressed, relational, dictionary-driven) — is **not**
what we ship. The tabular XLSX is 6× smaller, comes without a
dependency on an XLSX / XML library beyond what .NET's BCL already
provides, and covers every field the autocomplete needs. See
`AempsCimaParser.cs` for the rationale.

---

## 6. Refresh procedure (France — ANSM BDPM)

Two delivery paths, as for Italy:

- **Remote feed (monthly, no release needed).**
  `.github/workflows/download_bdpm.yaml` runs `scripts/feeds/bdpm.py`
  on days 2, 9, 16 and 23 at 03:47 UTC (and on demand, with the same
  `force` input as AIFA) and publishes `data/fr/bdpm-<yyyymm>.zip` and
  `data/fr/latest.json` (§2.1). No manual step.
- **Embedded snapshot (per release, optional)**, §6.3.

### 6.1 Source and wire format

Three files of the "Base de données publique des médicaments", linked
from <https://base-donnees-publique.medicaments.gouv.fr/telechargement>
as `/download/file/<name>`:

- `CIS_bdpm.txt` (one row per medicinal product, key = CIS)
- `CIS_CIP_bdpm.txt` (one row per package, key = CIP — shipped for
  symmetry with what ANSM publishes; the parser does not read it)
- `CIS_COMPO_bdpm.txt` (one row per medicinal product × active
  substance, join key = CIS)

No login, no header filter. The older `telechargement.php` URLs answer
404 since 2026. Reuse is governed by Licence Ouverte Etalab 2.0 —
attribution required, no endorsement implied.

Wire format (verified against the current BDPM export):

- Delimiter: **TAB** (`\t`).
- Encoding: **Windows-1252** on `CIS_bdpm.txt` and
  `CIS_COMPO_bdpm.txt`. The ANSM portal documents the encoding as
  ISO-8859-15, but the actual bytes include cp1252-only 0x92 (the
  curly single-quote `’` used as apostrophe in French denominations —
  `d’organes`, `Pack d’initiation`, `CARMIN D’INDIGO`). Reading those
  files as strict ISO-8859-15 turns the byte into a U+0092 C1 control
  character; Windows-1252 covers both the documented spec and the
  actual content, so the parser uses it. `CIS_CIP_bdpm.txt` is UTF-8
  upstream — a separate ANSM inconsistency — and MedReminder does not
  parse it, so that mismatch is inert.
- **No header row**: columns are positional and documented on the
  portal ("Description des fichiers de la BDPM"). The parser
  hard-codes the column indices it needs, re-validates the row length
  before mapping, and asserts on the first non-short row that the
  Statut administratif AMM column starts with `Autorisation`
  (invariant prefix of every value ANSM writes there) so a future
  column-order change trips loudly instead of silently corrupting
  every row's MAH / MarketingStatus.

Facts measured from a GitHub runner on 2026-09-29: 3 175 357 /
4 140 976 / 2 735 840 bytes; 15 883 lines of 12 columns in
`CIS_bdpm.txt` (first status `Autorisation active`), 20 862 lines of 13
columns in `CIS_CIP_bdpm.txt`, 32 439 lines of 8 columns in
`CIS_COMPO_bdpm.txt`.

### 6.2 Remote feed

The script scrapes the download page for the three links, falls back
to `/download/file/<name>` for any link it does not find, and stores
the three files unchanged at the archive root. The run fails, leaving
`data/` untouched, when a response is HTML, when `CIS_bdpm.txt` or
`CIS_COMPO_bdpm.txt` does not decode as Windows-1252
(`CIS_CIP_bdpm.txt`: UTF-8 or Windows-1252), when a non-empty line has
a column count other than 12 / 13 / 8, when column 4 of the first
`CIS_bdpm.txt` row does not start with `Autorisation`, or when the rows
fall below 12 000 / 15 000 / 25 000 (`CIS_bdpm.txt` /
`CIS_CIP_bdpm.txt` / `CIS_COMPO_bdpm.txt`) or below 90% of the previous
run.

### 6.3 Embedded snapshot

1. **Take the archive published by the workflow**:
   `data/fr/bdpm-<yyyymm>.zip` from the `feeds` branch:

   ```
   bdpm-<yyyymm>.zip
   ├── CIS_bdpm.txt
   ├── CIS_CIP_bdpm.txt
   └── CIS_COMPO_bdpm.txt
   ```

   To build one by hand instead, download the three files from the
   page of §6.1 and zip them at the archive root with their names
   verbatim (`AnsmBdpmParser` looks them up by name, case-insensitive).

2. **Copy it into the repo** at
   `src/MedReminder.Infrastructure/Assets/Catalogue/fr/bdpm-<yyyymm>.zip`.
   The `<EmbeddedResource>` glob picks it up automatically.

3. **Delete the previous ZIP** in the same folder. Same
   `EmbeddedSnapshotProvider.TryOpen` rule as for the other
   countries: keep only one snapshot per country per build.

4. **Commit** with an imperative English message, e.g.
   `Refresh ANSM BDPM snapshot to 202610`.

5. **Verify** on Windows:
   ```powershell
   dotnet restore MedReminder.sln
   dotnet build   MedReminder.sln -c Release
   dotnet test    MedReminder.sln -c Release
   ```
   The boot log line is
   `Reference-catalogue import for FR complete: inserted=… version=<yyyymm>`
   on the first boot after the version changed.

Mapping notes:
- `NationalCode` = CIS (8 digits).
- `MarketingAuthorisationHolder` = the "Titulaires" column,
  left-trimmed (upstream ships it with a leading space).
- `DispensingRegime`, `LinkLeaflet`, `LinkSpc` are all `null`:
  BDPM base does not expose them as structured columns.
- `ActiveIngredientRow.Atc` is always `null`: BDPM base has no
  structured ATC column. A future increment could enrich these
  from a separate ATC dataset without touching this parser.
- Rows whose `Type de procédure AMM` starts with `Enreg homéo`
  are skipped, mirroring the Omeopatico filter in
  `AifaSnapshotParser` — homeopathic products carry no meaningful
  active-ingredient signal for the autocomplete.

---

## 8. Shortage list (Italy — AIFA "farmaci carenti")

Not a catalogue: the list of medicines in temporary shortage, used to
mark the medicines of a profile whose package is listed and to notify
the user once (`docs/notes/EVOLUTION-PROPOSALS-2.md` §3.3).

| Item | Value |
|---|---|
| Source | AIFA, `elenco_medicinali_carenti.csv` ("Carenze e indisponibilità"); CC BY 4.0, cited in the app as "AIFA list of medicines in shortage of <date>" |
| Workflow | `download_aifa_shortages.yaml`, daily at 04:27 UTC; checks with ETag / Last-Modified whether AIFA changed the file, then publishes only a newer list date, or the same date with other content (a correction); an older date is refused, even when forced. A new list is on the feed within a day and on clients, which check once a day, within about two days. Concurrency group `feed-<workflow name>` |
| Script | `scripts/feeds/aifa_shortages.py` (`--input FILE` publishes a file already downloaded); tests in `scripts/feeds/tests/test_aifa_shortages.py`, fixture `tests/fixtures/catalogue/aifa-shortages-sample.csv` |
| Published | `data/it/shortages/shortages-<yyyymmdd>.json` on the `feeds` branch (§1.1) (the list date) and `latest.json` (`version`, `file`, `sha256`, `size`, `rows.entries`, `source` with the ETag / Last-Modified of the AIFA file, ignored by clients); the 3 newest files are kept, never the one `latest.json` names |
| Client | `ShortageRefresher` with `GitHubRawShortageFeedClient`, after the catalogue feeds, only with Italy as reference country and the same settings (remote feeds on, automatic update check on); `Catalogue:RemoteFeed:ShortagesEnabled` (default true), `ShortagesMaxDownloadBytes` (default 4 MiB) |
| Stored | `%LOCALAPPDATA%\MedReminder\catalogue\shortages\shortages-it.json`, shared by every profile; not in any profile database, not synced, not exported |
| Line endings | `.gitattributes` marks `data/**/*.json` as `-text`: a checkout with `core.autocrlf` must not turn LF into CRLF, or the file no longer matches the manifest's size and SHA-256 |

Source file format (checked on the list of 29/09/2026, 2,514 rows):
Windows-1252, two free-text lines before the header (the second holds
"aggiornato al dd/mm/yyyy", the list date), `;` separator, 13 columns,
quoted fields with line breaks and doubled quotes, two codes listed
twice. The 9-digit `Codice AIC` is the package code of the catalogue
(`Medicine.NationalCode`). The script publishes per code only the
start, the expected end (often empty), whether AIFA reports
equivalents, and a reason category (`production`, `demand`,
`withdrawn`, `suspended`, `commercial`, `regulatory`, `other`). AIFA's
free-text suggestions and notes are not published.

A listed shortage stays current after its expected end: AIFA keeps a
medicine listed until the holder confirms the end. A start later than
today is shown as an announced shortage.

---

## 9. Equivalent medicines (Italy — AIFA "Lista di trasparenza")

Not a catalogue: the monthly list of off-patent class A medicines with
at least one equivalent, grouped, with reference and public prices.
Used by the Equivalent medicines window and the shortage tooltip
(`docs/analysis/ANALYSIS-IT-EQUIVALENTS-AND-INFO-LINK.md` §2).

| Item | Value |
|---|---|
| Source | AIFA, `Lista_farmaci_equivalenti.csv` ("Liste dei farmaci", stable name); cited in the app as "AIFA transparency list of <date>". Licence: CC BY 4.0 inferred from the AIFA open-data page, to confirm before release (analysis §2.1) |
| Workflow | `download_aifa_equivalents.yaml`, daily at 04:41 UTC; checks with ETag / Last-Modified whether AIFA changed the file, then publishes only a newer list date, or the same date with other content (a correction); an older date is refused, even when forced. Concurrency group `feed-<workflow name>`; no mirror to main |
| Script | `scripts/feeds/aifa_equivalents.py` (`--input FILE` publishes a file already downloaded); tests in `scripts/feeds/tests/test_aifa_equivalents.py`, fixture `tests/fixtures/catalogue/aifa-equivalents-sample.csv` |
| Published | `data/it/equivalents/equivalents-<yyyymmdd>.json` on the `feeds` branch (§1.1) (the list date) and `latest.json` (`version`, `file`, `sha256`, `size`, `rows.groups`, `rows.packages`, `source` as for the shortage list); the 3 newest files are kept, never the one `latest.json` names |
| Client | `EquivalenceRefresher` with `GitHubRawEquivalenceFeedClient`, after the shortage list, only with Italy as reference country and the same settings (remote feeds on, automatic update check on); `Catalogue:RemoteFeed:EquivalentsEnabled` (default true), `EquivalentsMaxDownloadBytes` (default 4 MiB) |
| Stored | `%LOCALAPPDATA%\MedReminder\catalogue\equivalents\equivalents-it.json`, shared by every profile; not in any profile database, not synced, not exported |

Source file format (checked on the list of 15/09/2026, 8,560 rows,
1,010 groups): Windows-1252, `;` separator, columns `Principio attivo;
Confezione di riferimento; ATC; AIC; Farmaco; Confezione; Ditta; Prezzo
riferimento SSN; Prezzo Pubblico <d month yyyy>; Differenza; Nota;
Codice gruppo equivalenza`. The script:

- left-pads `AIC` to 9 digits (the file drops the leading zeros);
- reads the list date from the name of the public-price column, matched
  by its `Prezzo Pubblico` prefix;
- converts prices (`5,63 €`) to cents;
- rejects a package listed in two groups;
- keeps `Nota` verbatim: a note can restrict substitution inside the
  group.

The client joins the list with a medicine only through
`Medicine.NationalCode` when it is a valid AIC, never by name or active
ingredient.

---

## 7. Suspended countries (M4b: UK / MHRA and DE / BfArM)

Not shipping. Both remaining target countries on the M4 shortlist
— MHRA (`gb`) and BfArM (`de`) — are **suspended**, tracked in
[`docs/ANALYSIS-DRUG-CATALOGUE.md`](ANALYSIS-DRUG-CATALOGUE.md) §3.5
under "M4b status" and closed in PR #21. Nothing under
`Assets/Catalogue/gb/` or `Assets/Catalogue/de/` is committed; the
boot-time importer iterates over `{ IT, EU, ES, FR }` only.

The suspension is a redistribution-licence problem, not an
engineering one. §3.5 point 2 of the analysis is a hard gate:
MedReminder ships the reference catalogue inside the binary, so
redistribution must be explicitly permitted by whatever licence the
upstream portal declares at retrieval time. Both agencies currently
fail that gate:

- **UK / MHRA.** The `products.mhra.gov.uk` portal exposes the
  Products dictionary through a search UI and per-product HTML
  pages, not through a bulk structured export that could be dropped
  into `Assets/Catalogue/gb/`. The realistic alternative — NHS BSA
  *Dictionary of Medicines and Devices* (dm+d) — carries a
  separate NHS BSA licence that does not permit silent
  redistribution inside a third-party binary.
- **DE / BfArM.** The public medicines registry (AMIS-öffentlich,
  now AMIce Public) has changed layout multiple times and the
  current portal does not surface a stable bulk export **with a
  licence declared at the point of download**. The Datenlizenz
  Deutschland – Namensnennung 2.0 policy that would apply is not
  attached to the download itself, and past bulk endpoints have
  been withdrawn without notice.

Reopening either country requires **all** of the following, in one
PR:

1. A specific bulk endpoint URL that returns a structured export
   (CSV, TSV, XML or XLSX — no HTML-scraping) covering the fields
   listed in §3.5 (national code, commercial name, active
   ingredients, ATC where available, form, dosage, MAH, marketing
   status, homoeopathic filter signal for DE).
2. A licence declared **on the download page** that permits
   redistribution inside the shipped MedReminder binary with
   attribution — OGL 3.0 for the UK, DL-DE-BY-2.0 (or an
   equivalent open licence) for Germany. A licence inferred from
   policy but absent from the landing page is not enough.
3. A `<yyyymm>` snapshot ZIP dropped at
   `src/MedReminder.Infrastructure/Assets/Catalogue/<gb|de>/`,
   with fixture and parser mirroring §4.3 (AEMPS) and §4.4 (BDPM)
   respectively.

Until then the section stays as a checklist. When both gates open
the mechanical refresh procedure will land here, mirroring §5 and
§6 with the agency-specific URL, encoding and column layout
substituted. `IncludesEuCentralised` handling for the two
countries is already encoded in `StaticCountryProfileProvider`
(UK/GB explicitly `false`, DE defaulting to `true`), so nothing in
the country-profile layer needs to change when the milestones
reopen.
