# ANALYSIS — Remote feeds for the EU, ES and FR catalogues

Design document, written before implementation. It extends the remote
AIFA feed (`ANALYSIS-CATALOGUE-REMOTE-FEED.md`, PR #131) to the three
other reference catalogues: EMA EPAR (`EU`), AEMPS CIMA (`ES`) and ANSM
BDPM (`FR`). Implementation waited until the AIFA feed had been
tested on Windows and in production (product-owner decision,
2026-09-29); it landed in PR #135, see §11.

Epistemic classification, aligned with the sibling documents:
`[VERIFIED]` (checked against the tree at `6bc2a35`, or measured on the
embedded snapshots), `[SEARCH]` (found through a web search result on
2026-09-29 but not fetched: this session's network blocks
`www.ema.europa.eu`, `cima.aemps.es`, `www.aemps.gob.es`,
`listadomedicamentos.aemps.gob.es` and
`base-donnees-publique.medicaments.gouv.fr`), `[OWNER]` (supplied by the
product owner), `[INFERRED]`, `[UNCERTAIN]`.

---

## 1. Scope

### 1.1 Goal

Each of the three catalogues gets the same pipeline as AIFA:

1. A scheduled GitHub workflow downloads the source, turns it into the
   ZIP layout the existing parser expects, validates it, and publishes
   the ZIP plus a manifest in the repository.
2. At startup the app reads the manifest and, when the version is
   newer than the open profile's catalogue for that country, downloads,
   verifies, imports and deletes the ZIP.

### 1.2 Out of scope

- Parser changes. The feeds must produce exactly the archives the
  current parsers read (`EmaEparParser`, `AempsCimaParser`,
  `AnsmBdpmParser`).
- New countries (UK and DE stay suspended, `CATALOGUE-DATA.md` §7).
- Removing the embedded snapshots: they stay the offline baseline.

### 1.3 Premise check

1. **"Rename `data/latest.json`".** Correct: the current path is
   AIFA-specific and four workflows writing one shared file would race
   (§4). The move is cheap **only before the first release that ships
   PR #131**: once a client reads `data/latest.json`, that path must
   keep working for as long as that release is in use. Decision D1.
2. **"Preserve all 39 EMA columns".** Not required by the parser:
   `EmaEparParser` needs 8 named columns
   (`EmaEparParser.cs:123-130`) [VERIFIED]. Keeping all 39 is still the
   right choice (no information loss, the same file serves future
   parser changes), but validation checks the 8.
3. **"Drop the first 8 metadata rows".** Brittle: EMA can add or
   remove a metadata line. The script locates the header as the first
   row whose first cell is `Category` [INFERRED; same rule the
   procedure states, applied by search instead of by count].
4. **Repository growth.** Four monthly binaries add about 10 MB per
   month to git history (§2.3), about 120 MB per year. Your note
   `docs/notes/REMOVE-FROM-GITHUB-HYSTORY.md` already proposes a
   dedicated, periodically reset data branch. That choice changes every
   feed URL, so it belongs in the same decision as D1.

---

## 2. Current state

### 2.1 What the parsers require [VERIFIED]

| Country | Archive | Entries read (case-insensitive name, any folder) | Format | Checks that throw |
|---------|---------|--------------------------------------------------|--------|-------------------|
| IT | `aifa-<v>.zip` | `confezioni_fornitura.csv`, `PA_confezioni.csv` | `;` CSV | missing entry, empty CSV, missing column |
| EU | `ema-epar-<v>.zip` | `ema-epar.csv` | `;` CSV, UTF-8 (BOM tolerated), quoted fields allowed (`CsvRow.Split`), **one record per physical line** | missing entry, empty CSV, missing one of 8 columns: `Category`, `Name of medicine`, `EMA product number`, `Medicine status`, `Active substance`, `ATC code (human)`, `Marketing authorisation developer / applicant / holder`, `Medicine URL` |
| ES | `aemps-<v>.zip` | `aemps.xlsx` | XLSX (OOXML) read with BCL `System.IO.Compression` + XML | missing entry; header mismatch at fixed positions (`Nº Registro`… `Laboratorio`, `Estado`, `Cód. ATC`, `Principios Activos`, `Observaciones`) (`AempsCimaParser.cs:630-646`) |
| FR | `bdpm-<v>.zip` | `CIS_bdpm.txt`, `CIS_COMPO_bdpm.txt` (`CIS_CIP_bdpm.txt` shipped, not read) | TAB, no header, Windows-1252 | missing entry; first full row whose "Statut administratif" does not start with `Autorisation` |

Sources: `EmaEparParser.cs:49-130,232-245`,
`AempsCimaParser.cs:57,300-316,630-646`,
`AnsmBdpmParser.cs:25-94,110-138,282`.

### 2.2 Embedded snapshots (2026-09) [VERIFIED]

| Country | ZIP size | Content | Data rows |
|---------|----------|---------|-----------|
| IT | 4 797 624 B | 2 CSV, 94 MB raw | 160 024 / 338 722 (published feed) |
| EU | 496 832 B | `ema-epar.csv`, 2.1 MB, 39 columns | 2 734 |
| ES | 2 755 556 B | `aemps.xlsx` (sheet1: 26 744 rows incl. header) | 26 743 |
| FR | 1 589 975 B | `CIS_bdpm.txt` 15 859, `CIS_COMPO_bdpm.txt` 32 400, `CIS_CIP_bdpm.txt` 20 879 lines | — |

`aemps.xlsx` is a real XLSX (`file`: Microsoft Excel 2007+), confirming
`CATALOGUE-DATA.md` §5 on the `.xls` label.

### 2.3 AIFA-specific code in the client [VERIFIED]

| Place | Hard-coded |
|-------|-----------|
| `CatalogueFeedManifestParser.FileNameFor` | `aifa-{version}.zip` |
| `CatalogueFeedOptions` | one manifest URL, one snapshot template, one size cap |
| `RemoteCatalogueRefresher` | country `IT`; the two AIFA entry names in `ValidateArchive`; 512 MB uncompressed cap |
| `CatalogueRefreshHostedService` | one remote step |
| `scripts/download_aifa.py` | `data/latest.json`, `data/aifa-*.zip` (moved to `data/it/` since, §11.4 of the AIFA analysis) |

Everything else is already country-neutral: `SnapshotVersion` (month +
build-time suffix), `IReferenceCatalogueImporter` (newer-only rule,
row floor, per-country state), `GitHubRawCatalogueFeedClient`,
`WriteGate` use, staging cleanup, `StartupUpdateCheckSignal`.

---

## 3. Sources

### 3.1 EU — EMA EPAR "Medicines" report

- **URL.** `https://www.ema.europa.eu/en/documents/report/medicines-output-medicines-report_en.xlsx`
  [SEARCH], listed on
  `https://www.ema.europa.eu/en/medicines/download-medicine-data`.
  The search result states the file is regenerated daily around
  18:00 CET [SEARCH]. It is a stable URL: no scraping needed. The
  script still falls back to scraping the landing page for an `href`
  ending in `medicines-output-medicines-report_en.xlsx` if the direct
  URL fails.
- **Runner test, 2026-09-29** [OWNER]: plain `GET` (curl User-Agent) and
  browser-header `GET` both return 200,
  `application/vnd.openxmlformats-officedocument.spreadsheetml.sheet`,
  901 861 bytes, XLSX magic bytes; no header filter. `HEAD` answers
  `content-length: 0` (a cached response), so the script must not rely
  on `HEAD` for the size. The landing page still links
  `/en/documents/report/medicines-output-medicines-report_en.xlsx`.
- **Workbook, measured on the runner:** one sheet `Medicine`; header on
  row 9 (8 metadata rows before it, as the manual procedure says); the
  8 required columns present; **2 746** data rows, **2 351** of them
  `Human`; 5 cells contain newlines or tabs; 2 260 cells contain `;`
  (multi-substance lists); no cell is typed as a date (dates are text).
  `openpyxl` in read-only mode reports **1 024** columns because the
  sheet's declared dimension is wider than the data: the script must
  drop trailing empty header cells (39 real columns) and cut every
  data row to the header width.
- **Transformation** (`scripts/feeds/ema.py`):
  1. Read the workbook with `openpyxl` (read-only mode); sheet
     `Medicine`, or the first sheet if absent.
  2. Header = first row whose first cell is `Category` (§1.3 point 3).
  3. Write `ema-epar.csv`: `;` delimiter, UTF-8 without BOM, all
     real columns (header trimmed of trailing empty cells, rows cut to
     the header width), cells quoted by `csv.writer` when they contain
     `;` or `"` (2 260 cells today, so quoting is mandatory); every
     `\r`, `\n`, `\t` inside a cell replaced by a single space and
     repeated spaces collapsed (5 cells today); a date-typed cell, should
     one appear, as ISO `yyyy-mm-dd`; empty cells as empty strings.
  4. Zip as `ema-epar-<v>.zip` with the CSV at the root.
- **Validation.** The 8 required columns present; data rows ≥ 2 000
  and `Category = Human` rows ≥ 1 800 (today 2 746 and 2 351); at least
  90% of the previous run's row count; no line of the output CSV breaks a record (re-read it with
  `csv.reader` and check every row has the header's column count).
- **Licence.** EMA legal notice, reuse with attribution (Commission
  Decision 2011/833/EU), per `CATALOGUE-DATA.md` §3.
- **Dependency.** `openpyxl` in the workflow (`pip install`); none in
  the app.

### 3.2 ES — AEMPS CIMA "Medicamentos"

- **URL.** `https://listadomedicamentos.aemps.gob.es/Medicamentos.xls`
  [OWNER]. The host is AEMPS's static download server: it also serves
  the XML Nomenclátor `prescripcion.zip` and `prescripcionvet.zip`
  [SEARCH]. It is a stable direct link, so no scraping and no CIMA
  JavaScript page are involved. Not fetched from this session (egress
  blocked); still to check from a GitHub runner, since AEMPS may
  filter cloud IP ranges [UNCERTAIN]:
  `curl -sSI https://listadomedicamentos.aemps.gob.es/Medicamentos.xls`
  (expected: `200`, a spreadsheet content type, a few MB).
- **Content check before use.** The file is labelled `.xls` but the
  embedded snapshot built from it is an XLSX (§2.2). The script must
  not trust the extension: it checks the first bytes (`PK\x03\x04`,
  ZIP container) and rejects a legacy BIFF `.xls` (`D0 CF 11 E0`) or
  an HTML error page, so a change of upstream format fails the run
  instead of publishing a file the parser cannot read. If AEMPS ever
  switches to real BIFF, the script can convert it (`xlrd` +
  `openpyxl`), which is out of scope until it happens.
- **Runner tests, 2026-09-29** [OWNER], GitHub-hosted `ubuntu-24.04`:

  | Request | Result |
  |---------|--------|
  | `HEAD`, curl default User-Agent | 403, `text/html`, `server: BlasDeLezo` |
  | `GET`, curl default User-Agent | 403, `text/html`, 150 bytes |
  | `GET`, browser User-Agent + `Accept`, `Accept-Language`, `Referer` headers | **200**, `application/vnd.ms-excel`, 2 758 433 bytes |

  The protection layer filters on request headers, not on runner IP
  ranges. The script therefore sends a browser User-Agent and the same
  `Accept` / `Accept-Language` / `Referer` headers (the AIFA script
  already sends a browser User-Agent). Which header is decisive was not
  isolated; sending all of them is cheap.
- **Content, verified on the runner:** the first bytes are
  `PK\x03\x04`, an XLSX container, as the embedded snapshot already
  showed; the size matches the embedded `aemps.xlsx` (2 755 386 bytes).
  `openpyxl` refuses to open the file under its `.xls` name (it checks
  the extension, not the content), so the script saves it as
  `aemps.xlsx` before reading the header. Read on the runner as
  `aemps.xlsx`: sheets `Hoja1`, `Hoja2`, `Hoja3`; the first sheet's
  header is exactly the 15 columns `AempsCimaParser` expects
  (`Nº Registro` … `¿Problemas de suministro?`), and it has **26 763**
  data rows (embedded snapshot: 26 743).
- **Protection layer.** The 403 body is a plain `openresty` error page,
  not a JavaScript challenge: a reverse proxy filtering on request
  headers [INFERRED].
- **REST fallback, verified reachable:** `GET
  https://cima.aemps.es/cima/rest/medicamentos?pagina=1` with a browser
  User-Agent returned 200, JSON, `totalFilas: 25476`,
  `tamanioPagina: 200` (about 128 pages). Its count differs from the
  XLSX (25 476 vs 26 763) and its fields differ from the XLSX columns
  (for example `nregistro`, `nombre`, `labtitular`, `comerc`), so using
  it would need a mapping to the parser's layout. Kept as fallback only.
- **Fallbacks, only if the header filter tightens later:** the CIMA
  REST API (documented, paginated JSON) [SEARCH] with the XLSX rebuilt
  by the script, if its host is not blocked too; a self-hosted runner
  on a machine that can reach AEMPS (the workflow runs there, the rest
  of the pipeline is unchanged); a semi-manual path where the
  maintainer commits `Medicamentos.xls` to an intake folder and a
  workflow triggered by that push validates, zips and publishes it;
  or keeping ES on the embedded, per-release path.
- **Transformation.** None beyond renaming: the download is already an
  XLSX with the expected layout. Save it as `aemps.xlsx` (bytes
  unchanged), zip as `aemps-<v>.zip`. Compression gains little (the
  XLSX is already a ZIP): about 2.8 MB per month (§2.2).
- **Validation.** Opens as a ZIP with `xl/workbook.xml`; the header
  row of the first sheet matches the 15 names of `CATALOGUE-DATA.md`
  §5 (the script reads it with `openpyxl`); data rows ≥ 20 000 (today
  26 743) and ≥ 90% of the previous run.
- **Licence.** Law 37/2007 reuse regime, attribution "Fuente: AEMPS".

### 3.3 FR — ANSM BDPM

- **URLs.** `https://base-donnees-publique.medicaments.gouv.fr/download/file/<name>`
  for `CIS_bdpm.txt`, `CIS_CIP_bdpm.txt`, `CIS_COMPO_bdpm.txt`. The
  script scrapes `https://base-donnees-publique.medicaments.gouv.fr/telechargement`
  for links ending in the three file names (as `download_aifa.py` does
  for AIFA) and uses the `/download/file/<name>` pattern as fallback.
- **Runner test, 2026-09-29** [OWNER]:
  - `/telechargement` → 200, links `/download/file/CIS_bdpm.txt`,
    `/download/file/CIS_CIP_bdpm.txt`, `/download/file/CIS_COMPO_bdpm.txt`.
  - `/telechargement.php` and `/telechargement.php?fichier=<name>` →
    **404**: the portal URL in `CATALOGUE-DATA.md` §6 is outdated.
  - `/download/file/<name>` and `/index.php/download/file/<name>` → 200,
    `application/octet-stream`, with or without a browser User-Agent
    (no header filter). Sizes: 3 175 357 / 4 140 976 / 2 735 840 bytes.
  - data.gouv.fr API (`/api/1/datasets/base-de-donnees-publique-des-medicaments-base-officielle/`)
    → 200, but no resource title matched the three file names, so the
    mirror's file layout is unconfirmed; dropped as fallback until
    checked [UNCERTAIN].
- **Update frequency.** The portal text found says "updated monthly";
  `CATALOGUE-DATA.md` §6 says daily [UNCERTAIN]. Irrelevant for a
  monthly feed.
- **Transformation.** None: keep the raw bytes (the parser decodes
  Windows-1252; `CIS_CIP_bdpm.txt` is UTF-8 upstream and unused). Zip
  the three files as `bdpm-<v>.zip`.
- **Content, measured on the runner:** `CIS_bdpm.txt` decodes as
  cp1252, 15 883 lines, all with 12 tab-separated columns, first row's
  statut `Autorisation active` (parser invariant holds), 1 316
  homeopathic rows (skipped by the parser); `CIS_COMPO_bdpm.txt`
  decodes as cp1252, 32 439 lines, all with 8 columns;
  `CIS_CIP_bdpm.txt` decodes as UTF-8, 20 862 lines, all with 13
  columns. Matches `CATALOGUE-DATA.md` §6 and the parser.
- **Validation.** Each file decodes as cp1252 (`CIS_CIP` as UTF-8 or
  cp1252); content type not HTML; every line has the expected column
  count (12 / 8 / 13, a change fails the run); `CIS_bdpm.txt`: tab-separated, the
  first full row has "Statut administratif" starting with
  `Autorisation` (the parser's own invariant), ≥ 12 000 rows;
  `CIS_COMPO_bdpm.txt` ≥ 25 000 rows; `CIS_CIP_bdpm.txt` ≥ 15 000 rows;
  each ≥ 90% of the previous run.
- **Licence.** Licence Ouverte Etalab 2.0, attribution required.

### 3.4 Thresholds summary

| Feed | Absolute floor | Today | Relative floor |
|------|----------------|-------|----------------|
| IT confezioni / PA | 100 000 / 200 000 | 160 024 / 338 722 | 90% of previous |
| EU ema-epar.csv (all / Human) | 2 000 / 1 800 | 2 746 / 2 351 | 90% |
| ES aemps.xlsx | 20 000 | 26 763 | 90% |
| FR CIS / COMPO / CIP | 12 000 / 25 000 / 15 000 | 15 883 / 32 439 / 20 862 | 90% |

"Today" is the runner measurement of 2026-09-29.

Client guard, unchanged: reject a snapshot with fewer than half the
rows the open profile holds for that country.

---

## 4. Repository layout and publication

### 4.1 Options

| Option | Layout | Pros | Cons |
|--------|--------|------|------|
| A | `data/aifa_latest.json`, `data/ema_latest.json`, … flat | Minimal change | Retention globs per prefix in one folder; names drift from country codes |
| **B** | `data/<country>/latest.json` + `data/<country>/<prefix>-<v>.zip` (`it`, `eu`, `es`, `fr`, as in `Assets/Catalogue/`) | One folder per feed: retention, validation state and manifest isolated; URL built from the country code | Moves the AIFA files |
| C | One index `data/catalogue.json` for all feeds | One request at startup | Four workflows rewrite one file: commit races and a broken feed can corrupt the others' entries |

**Recommendation: B.** Manifest schema unchanged (`version`, `file`,
`generated`, `csv_count` renamed `file_count`, `sha256`, `size`,
`rows`), plus `"country"` so a manifest cannot be served for the wrong
feed.

### 4.2 Branch

Two sub-options, independent of A/B/C:

- **main** (today). Simple; every archive stays in `main`'s history.
- **dedicated data branch** (for example `catalogue-feeds`), reset
  periodically as your note describes. URLs become
  `https://raw.githubusercontent.com/vger70/MedReminder/catalogue-feeds/<country>/latest.json`.
  `main` stays small. The workflows check out that branch
  (`actions/checkout` with `ref:`) instead of `main`. The reset must
  keep the latest archive and manifest of each country; resetting only
  drops history, which clients never read.

**Recommendation:** data branch, decided together with D1 and applied
**before** the first release that ships the AIFA feed, so no released
client ever reads `main/data/latest.json`. If PR #131 is already
released when this is implemented, the AIFA workflow must also keep
writing `main/data/latest.json` until that release is out of use.

### 4.3 Workflows

- **One workflow per source**: `download_aifa.yaml` (existing, path
  updated), `download_ema.yaml`, `download_aemps.yaml`,
  `download_bdpm.yaml`. A broken source then fails only its own run, and
  each can be re-run or forced alone. A matrix job was considered and
  rejected: parallel jobs pushing to the same branch race.
- **Shared concurrency group** `catalogue-feeds-publish`
  (`cancel-in-progress: false`) so pushes are serialized; each job also
  does `git pull --rebase` before `git push`, with one retry.
  *Superseded:* since the feeds moved to the `feeds` branch and run
  daily, each workflow has its own group and publishes only its own
  paths, rebuilding a publish whose lease was lost
  (`docs/CATALOGUE-DATA.md` §1.1).
- **Schedule:** the same days as AIFA (days 2, 9, 16 and 23 since
  2026-09-29; days 2–7 when this analysis was written), staggered (`0 3`, `20 3`, `40 3`,
  `0 4`) to spread load and simplify logs. Each script exits early when
  the month is already published; `force` input as for AIFA.
- **Permissions:** `contents: write` only.

### 4.4 Scripts

```
scripts/feeds/
  common.py      session with retries (same urllib3 Retry as AIFA),
                 already-published check, previous-row lookup,
                 manifest writer (sha256, size, rows, generated from one
                 UTC timestamp), retention (3 archives), ZIP writer
  ema.py         download + XLSX→CSV + validation
  aemps.py       download + magic-byte and header validation (§3.2)
  bdpm.py        scrape + download ×3 + validation
download_aifa.py later moved onto common.py; not in the first step, so
                 the tested AIFA script is not touched (only its output
                 path, if D1 moves it)
```

Unit tests for the scripts: `pytest` on the transformation and
validation functions with small fixtures (the existing test fixtures
under `tests/fixtures/catalogue/` can be reused as inputs), run in a
separate workflow on changes under `scripts/feeds/`.

---

## 5. Client changes

### 5.1 Configuration

```json
"Catalogue": {
  "RemoteFeed": {
    "Enabled": true,
    "BaseUrl": "https://raw.githubusercontent.com/vger70/MedReminder/<branch>/",
    "Feeds": {
      "IT": { "Enabled": true, "Prefix": "aifa",     "MaxDownloadBytes": 67108864 },
      "EU": { "Enabled": true, "Prefix": "ema-epar", "MaxDownloadBytes": 16777216 },
      "ES": { "Enabled": true, "Prefix": "aemps",    "MaxDownloadBytes": 16777216 },
      "FR": { "Enabled": true, "Prefix": "bdpm",     "MaxDownloadBytes": 16777216 }
    },
    "ManifestTimeoutSeconds": 10,
    "DownloadTimeoutSeconds": 120
  }
}
```

Manifest URL `{BaseUrl}{country lower}/latest.json`, archive URL
`{BaseUrl}{country lower}/{Prefix}-{version}.zip`. The current
`ManifestUrl` / `SnapshotUrlTemplate` keys remain accepted for IT, so a
config written for PR #131 keeps working [INFERRED; only needed if
#131 ships first].

### 5.2 Code

| Component | Change |
|-----------|--------|
| `CatalogueFeedManifestParser` | `TryParse(json, expectedPrefix, expectedCountry, …)`; `file` must equal `<prefix>-<version>.zip`; `country` must match when present |
| `ICatalogueFeedClient` | calls take a feed descriptor (country, URLs, cap) |
| `CatalogueFeedDescriptor` (new, Application) | country, prefix, required archive entries, uncompressed cap. IT: 2 AIFA CSV, 512 MB; EU: `ema-epar.csv`, 64 MB; ES: `aemps.xlsx`, 64 MB; FR: `CIS_bdpm.txt` + `CIS_COMPO_bdpm.txt`, 64 MB |
| `RemoteCatalogueRefresher` | `RunAsync(feed)`; country from the descriptor; `ValidateArchive` from the descriptor's entries; staging file names already distinct by prefix |
| `CatalogueRefreshHostedService` | after the embedded imports and the signal, loop over the selected feeds (§5.3) in the order IT, EU, ES, FR; one failure never stops the next (same isolation as the embedded loop) |

Unchanged: importer, `SnapshotVersion`, `WriteGate`, row floor, hash
and size checks, staging cleanup (clean once, before the loop).

### 5.3 Which feeds a client downloads

Downloading all four costs about 10 MB per month per profile.
The autocomplete uses the reference country's catalogue plus EU
(`USER_GUIDE` "Reference catalogue"). Recommendation (D4): fetch the
feed of `UserSettings.ReferenceCountry` (when it is IT, ES or FR) and
EU; other countries stay at their embedded version until the user
changes the reference country, after which the next start fetches the
new one. Per-feed `Enabled` flags let an admin override.

---

## 6. Tests

| Where | What |
|-------|------|
| Application.Tests | manifest parser with prefix/country; refresher per descriptor (entries, caps, country passed to the importer); feed selection by reference country; failure isolation across feeds |
| Infrastructure.Portable.Tests | client URL building per feed |
| Infrastructure.Tests (Windows) | importer newer-only and row floor already covered; add one end-to-end import per country from a fixture archive under a suffixed label |
| scripts (pytest) | EMA XLSX→CSV: header search, newline/tab collapsing, quoting of `;`, date format; AEMPS header check; BDPM `Autorisation` invariant and cp1252 decoding; thresholds; manifest content |

---

## 7. Risks

| Risk | Impact | Mitigation |
|------|--------|-----------|
| AEMPS header filter (observed: 403 without a browser User-Agent, 200 with it) tightens, for example into a JavaScript challenge | ES feed cannot download | Retries, then a red run on each scheduled day; fallbacks of §3.2 (REST API, self-hosted runner, semi-manual intake, embedded-only) |
| AEMPS changes `Medicamentos.xls` to real BIFF or to another layout | Parser cannot read it | Magic-byte check and header check in the script; nothing is published |
| EMA changes column names | Parser rejects the snapshot | Script validation fails first, nothing is published |
| EMA XLSX has multi-line cells | Parser splits records | Collapse `\r\n\t`; re-read the CSV to check column counts |
| BDPM column order changes | Parser rejects (invariant check) | Script applies the same invariant before publishing |
| Scraped link layouts change (BDPM, AIFA) | Download fails | Retries, then fallback URL pattern, then a red run on each scheduled day (BDPM already moved once: `telechargement.php` is now 404) |
| EMA sheet dimension wider than the data (observed: 1 024 columns) | Hundreds of empty CSV columns | Trim the header to its last non-empty cell and rows to the header width |
| Repository growth | Slow clones | Data branch with periodic reset (§4.2) |
| Four pushes in the same window | Push rejected | Concurrency group + rebase-and-retry |

---

## 8. Decisions to confirm

| # | Decision | Recommendation |
|---|----------|----------------|
| D1 | Layout | **Settled:** option B, `data/<country>/`; AIFA moved to `data/it/` in PR #131 |
| D2 | Branch | **Settled for now:** data stays on `main`; a dedicated data branch remains possible later, at the cost of changing the feed URLs again |
| D3 | Timing of the AIFA move | **Done** in PR #131, before any release reads `data/latest.json` |
| D4 | Feeds per client | **Confirmed** 2026-09-29: reference country + EU; per-feed flags for admins |
| D5 | AEMPS source | **Settled:** `https://listadomedicamentos.aemps.gob.es/Medicamentos.xls`, `GET` with browser headers (200 from a GitHub runner, §3.2) |
| D6 | Thresholds | As in §3.4 |
| D7 | Order of work | ES, FR, EU: all three downloads are proven from a runner; ES and FR need no transformation, EU needs the XLSX→CSV conversion |

---

## 9. Phases (after the AIFA feed is validated)

1. D1–D3: move the AIFA publication path (workflow + client default
   URL), no other change. **Done** in PR #131 (`data/it/`, manifest
   `country` field).
2. Client generalisation (§5) with IT only configured; AIFA regression
   tests stay green.
3. `scripts/feeds/common.py` + AEMPS feed + workflow (direct download
   with browser headers, no transformation); enable `ES` in the client.
4. BDPM feed + workflow; enable `FR`.
5. EMA feed (XLSX→CSV conversion) + workflow; enable `EU`.
6. Move `download_aifa.py` onto `common.py`.
7. Docs: `CATALOGUE-DATA.md` §3, §5, §6 rewritten like §2;
   `THIRD-PARTY-NOTICES.md` notes that the snapshots are also
   redistributed from the repository, with the attributions.

---

## 10. Verification of this document

- §2.1–§2.3: re-read against the tree at `6bc2a35`; row and size figures
  measured by unzipping the embedded snapshots in this session.
- §3 URLs: found through web search (the source domains are blocked
  by this environment's egress proxy), then verified from GitHub-hosted
  runners by the product owner on 2026-09-29 (EMA, BDPM, AEMPS probes;
  results in §3.1–§3.3).
- AEMPS URL supplied by the product owner (2026-09-29); the host is
  confirmed as AEMPS's static download server by search results. Not
  fetched from this session (egress proxy 403). From a GitHub runner
  (product owner's tests): 403 without a browser User-Agent, 200 with
  browser headers; the payload is an XLSX container of 2 758 433 bytes
  whose first sheet has the parser's 15-column header and 26 763 data
  rows. The CIMA REST API is also reachable from the runner.
- Not verified: the data.gouv.fr mirror's file layout, BDPM update
  frequency (irrelevant for a monthly feed).

---

## 11. Implementation notes (2026-09-29)

Implemented in PR #135 after the product owner confirmed both
preconditions of the implementation prompt: the first scheduled AIFA
run published `data/it/aifa-202609.zip`, and an app start imported it
with `source=remote feed`. Decision D4 was confirmed as written
(reference country + EU). D1–D3 and D5–D7 were applied as recorded in
§8.

### 11.1 Client

- `CatalogueFeedDescriptor` (Application) holds the four feeds with the
  prefixes, entries and caps of §5.2. `CatalogueFeedOptions` gained
  `BaseUrl` and `Feeds:<country>` (`Enabled`, `MaxDownloadBytes`);
  every feed is enabled by default and in `appsettings.json`. The
  prefix stays in the descriptor, not in the configuration: it is
  fixed by the parser. The top-level `MaxDownloadBytes` was removed; a
  feed missing from `Feeds` is off and its download cap is 0.
  `ManifestUrl` and `SnapshotUrlTemplate` remain Italy-only overrides,
  unset in the shipped configuration.
- The manifest parser checks `file` and `country` against the
  descriptor. A manifest of another feed is therefore reported by the
  client as unavailable (outcome `ManifestUnavailable`), where the
  AIFA-only refresher reported `Rejected`.
- `CatalogueFeedSelection` implements D4; an invalid reference country
  falls back to IT, a country without a feed (for example `DE`) or `EU`
  selects EU only.
- The per-feed loop is `CatalogueRefreshHostedService.RefreshFeedsAsync`
  (internal static), so the isolation rule is tested in
  `MedReminder.UI.Tests` without a host.
- Log lines are `Remote catalogue feed <country>: …`; the import line
  `Reference-catalogue import for <country> complete: … source=remote feed`
  is unchanged.

### 11.2 Scripts and workflows

- `download_aifa.py` moved to `scripts/feeds/aifa.py` (next to
  `common.py`, which it imports) instead of being rewritten in place;
  `download_aifa.yaml` calls the new path. Its requests, validation,
  thresholds and `data/it/` output are unchanged; the manifest's
  `csv_count` became `file_count`.
- All four workflows share the concurrency group, stage only their own
  `data/<country>/` folder, and push with one `git pull --rebase` and
  one retry. The AIFA workflow received the same push step.
- AEMPS: the `Referer` sent is the CIMA home page; which header the
  proxy checks was not isolated (§3.2). A legacy BIFF `.xls` is
  rejected with its own message.
- EMA: the check of §3.1 ("no line of the output CSV breaks a record")
  parses each physical line on its own. A single `csv.reader` over the
  whole file would join a quoted field split across two lines and hide
  the break. The `Human` count is recorded in `rows` as
  `ema-epar.csv (Human)`, so the next run can apply the 90% rule to it.
- `scripts_tests.yaml` runs `pytest scripts/feeds/tests` on changes
  under `scripts/` and `tests/fixtures/catalogue/`.
- New fixtures: `ema-epar-sample.xlsx` (the rows of
  `ema-epar-sample.csv` laid out like the EMA report, with 8 metadata
  rows, a wider declared sheet and a multi-line cell) and
  `ema-epar-from-xlsx.csv` (`ema.py`'s conversion of it). pytest checks
  that the script still produces that CSV; `EmaEparParserTests` checks
  that the parser reads it to the same 70 rows as the manual export.

### 11.3 Verification

- Windows session (.NET SDK 10.0.401): `dotnet build MedReminder.sln -c Release`
  succeeds and `dotnet test MedReminder.sln -c Release` passes (1 298
  tests). Earlier runs in the session each had one Infrastructure
  failure in `ExportServiceTests` or `SyncSetupServiceTests`, a
  different test each time, passing on re-run; this change does not
  touch those areas.
- `pytest scripts/feeds/tests`: 94 tests pass (Python 3.14).
- `python scripts/feeds/aifa.py` against the current `data/it/`
  exits "Version 202609 is already published".
- Not verified in this session (no access to the sources from here):
  the first `workflow_dispatch` of each new workflow, and the app check
  of §5 of the implementation prompt (reference country `ES`, then a
  second start reporting "up to date" for ES and EU).
