# ANALYSIS — Remote feeds for the EU, ES and FR catalogues

Design document, written before implementation. It extends the remote
AIFA feed (`ANALYSIS-CATALOGUE-REMOTE-FEED.md`, PR #131) to the three
other reference catalogues: EMA EPAR (`EU`), AEMPS CIMA (`ES`) and ANSM
BDPM (`FR`). **Implementation waits until the AIFA feed has been
tested on Windows and in production** (product-owner decision,
2026-09-29).

Epistemic classification, aligned with the sibling documents:
`[VERIFIED]` (checked against the tree at `6bc2a35`, or measured on the
embedded snapshots), `[SEARCH]` (found through a web search result on
2026-09-29 but not fetched: this session's network blocks
`www.ema.europa.eu`, `cima.aemps.es`, `www.aemps.gob.es` and
`base-donnees-publique.medicaments.gouv.fr`), `[INFERRED]`,
`[UNCERTAIN]`.

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
| `scripts/download_aifa.py` | `data/latest.json`, `data/aifa-*.zip` |

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
- **Transformation** (`scripts/feeds/ema.py`):
  1. Read the workbook with `openpyxl` (read-only mode); sheet
     `Medicine`, or the first sheet if absent.
  2. Header = first row whose first cell is `Category` (§1.3 point 3).
  3. Write `ema-epar.csv`: `;` delimiter, UTF-8 without BOM, all
     columns, cells quoted by `csv.writer` when they contain `;` or
     `"`; every `\r`, `\n`, `\t` inside a cell replaced by a single
     space and repeated spaces collapsed; dates as ISO `yyyy-mm-dd`;
     empty cells as empty strings.
  4. Zip as `ema-epar-<v>.zip` with the CSV at the root.
- **Validation.** The 8 required columns present; `Category = Human`
  rows ≥ 2 000 (today about 2 700 total rows, most of them Human
  [UNCERTAIN on the exact share]); at least 90% of the previous run's
  row count; no line of the output CSV breaks a record (re-read it with
  `csv.reader` and check every row has the header's column count).
- **Licence.** EMA legal notice, reuse with attribution (Commission
  Decision 2011/833/EU), per `CATALOGUE-DATA.md` §3.
- **Dependency.** `openpyxl` in the workflow (`pip install`); none in
  the app.

### 3.2 ES — AEMPS CIMA "Medicamentos"

- **URL: not established** [UNCERTAIN]. The CIMA site is a JavaScript
  application; the search results do not expose the direct link behind
  "Nomenclátor / Descargas → Medicamentos". Before implementing, the
  link must be captured once from a browser (DevTools → Network while
  clicking the download) and checked with `curl -I` from a GitHub
  runner (AEMPS may block cloud IP ranges; untested).
  Alternatives if no stable link exists:
  - the CIMA REST API (documented, paginated JSON) [SEARCH], with the
    XLSX rebuilt by the script. More work, and the result must match
    the parser's fixed 15-column header exactly;
  - keeping ES on the embedded, per-release path.
- **Transformation.** None beyond renaming: the download is already an
  XLSX with the expected layout. Rename to `aemps.xlsx`, zip as
  `aemps-<v>.zip`.
- **Validation.** Opens as a ZIP with `xl/workbook.xml`; the header
  row of the first sheet matches the 15 names of `CATALOGUE-DATA.md`
  §5 (the script reads it with `openpyxl`); data rows ≥ 20 000 (today
  26 743) and ≥ 90% of the previous run.
- **Licence.** Law 37/2007 reuse regime, attribution "Fuente: AEMPS".

### 3.3 FR — ANSM BDPM

- **URLs.** `https://base-donnees-publique.medicaments.gouv.fr/download/file/CIS_bdpm.txt`
  (also seen as `/index.php/download/file/CIS_bdpm.txt`) and the same
  pattern for `CIS_CIP_bdpm.txt` and `CIS_COMPO_bdpm.txt` [SEARCH]. The
  landing page moved from `telechargement.php` to `telechargement`
  [SEARCH], so the script scrapes the landing page for links ending in
  the three file names (as `download_aifa.py` does for AIFA) and uses
  the `/download/file/<name>` pattern as fallback. A mirror exists on
  data.gouv.fr ("Base de données publique des médicaments (base
  officielle)") [SEARCH], usable as a second fallback.
- **Update frequency.** The portal text found says "updated monthly";
  `CATALOGUE-DATA.md` §6 says daily [UNCERTAIN]. Irrelevant for a
  monthly feed.
- **Transformation.** None: keep the raw bytes (the parser decodes
  Windows-1252; `CIS_CIP_bdpm.txt` is UTF-8 upstream and unused). Zip
  the three files as `bdpm-<v>.zip`.
- **Validation.** Each file decodes as cp1252 (`CIS_CIP` as UTF-8 or
  cp1252); content type not HTML; `CIS_bdpm.txt`: tab-separated, the
  first full row has "Statut administratif" starting with
  `Autorisation` (the parser's own invariant), ≥ 12 000 rows;
  `CIS_COMPO_bdpm.txt` ≥ 25 000 rows; `CIS_CIP_bdpm.txt` ≥ 15 000 rows;
  each ≥ 90% of the previous run.
- **Licence.** Licence Ouverte Etalab 2.0, attribution required.

### 3.4 Thresholds summary

| Feed | Absolute floor | Today | Relative floor |
|------|----------------|-------|----------------|
| IT confezioni / PA | 100 000 / 200 000 | 160 024 / 338 722 | 90% of previous |
| EU ema-epar.csv | 2 000 | 2 734 | 90% |
| ES aemps.xlsx | 20 000 | 26 743 | 90% |
| FR CIS / COMPO / CIP | 12 000 / 25 000 / 15 000 | 15 859 / 32 400 / 20 879 | 90% |

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
- **Schedule:** days 2–7 like AIFA, staggered (`0 3`, `20 3`, `40 3`,
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
  aemps.py       download + validation (+ URL discovery, §3.2)
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
| AEMPS has no stable direct link, or blocks runner IPs | ES feed not feasible as designed | Capture the link first (§3.2); fall back to REST API or keep ES embedded |
| EMA changes column names | Parser rejects the snapshot | Script validation fails first, nothing is published |
| EMA XLSX has multi-line cells | Parser splits records | Collapse `\r\n\t`; re-read the CSV to check column counts |
| BDPM column order changes | Parser rejects (invariant check) | Script applies the same invariant before publishing |
| Scraped link layouts change (BDPM, AIFA) | Download fails | Retries, then fallback URL pattern, then a red run on each scheduled day 2–7 |
| Repository growth | Slow clones | Data branch with periodic reset (§4.2) |
| Four pushes in the same window | Push rejected | Concurrency group + rebase-and-retry |

---

## 8. Decisions to confirm

| # | Decision | Recommendation |
|---|----------|----------------|
| D1 | Layout | Option B, `data/<country>/` (or `<country>/` on the data branch) |
| D2 | Branch | Dedicated data branch with periodic reset, decided before the first release of PR #131 |
| D3 | Timing of the AIFA move | Together with D1/D2, before that release; otherwise keep writing `main/data/latest.json` for old clients |
| D4 | Feeds per client | Reference country + EU; per-feed flags for admins |
| D5 | AEMPS source | Capture the direct link first; if none, keep ES embedded (REST rebuild only if needed) |
| D6 | Thresholds | As in §3.4 |
| D7 | Order of work | EU and FR first (URLs known), ES after D5 |

---

## 9. Phases (after the AIFA feed is validated)

1. D1–D3: move the AIFA publication path (workflow + client default
   URL), no other change. Release-independent if done before #131
   ships.
2. Client generalisation (§5) with IT only configured; AIFA regression
   tests stay green.
3. `scripts/feeds/common.py` + EMA feed + workflow; enable `EU` in the
   client.
4. BDPM feed + workflow; enable `FR`.
5. AEMPS: link discovery (D5), then feed + workflow; enable `ES`.
6. Move `download_aifa.py` onto `common.py`.
7. Docs: `CATALOGUE-DATA.md` §3, §5, §6 rewritten like §2;
   `THIRD-PARTY-NOTICES.md` notes that the snapshots are also
   redistributed from the repository, with the attributions.

---

## 10. Verification of this document

- §2.1–§2.3: re-read against the tree at `6bc2a35`; row and size figures
  measured by unzipping the embedded snapshots in this session.
- §3 URLs: from web search results only (the four source domains are
  blocked by this environment's egress proxy); every URL must be
  checked with `curl -I` from a GitHub runner before implementation.
- Not verified: AEMPS download link, EMA and BDPM download behaviour
  from GitHub runners, EMA Human share of rows, BDPM update frequency.
