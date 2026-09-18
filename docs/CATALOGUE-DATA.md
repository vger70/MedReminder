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

| Country | File                                                                    | Source                                                 | Terms                              |
|---------|-------------------------------------------------------------------------|--------------------------------------------------------|------------------------------------|
| `it`    | `Assets/Catalogue/it/aifa-<yyyymm>.zip`                                 | AIFA — Agenzia Italiana del Farmaco                    | CC BY 4.0                          |
| `eu`    | `Assets/Catalogue/eu/ema-epar-<yyyymm>.zip`                             | EMA — European public assessment reports (EPAR)        | EMA legal notice (reuse allowed)   |

The `<yyyymm>` suffix (e.g. `aifa-202609.zip`, `ema-epar-202609.zip`)
identifies the release / export date. That literal string is what
`EmbeddedSnapshotProvider.TryOpen` extracts from the resource name
and passes down to `CsvReferenceCatalogueImporter` as
`snapshot_version`. Every imported row carries it so the boot-time
importer can detect a newer snapshot and short-circuit when the DB
is already up to date. `CatalogueRefreshHostedService` iterates over
`{ IT, EU }` on startup (each country in its own transaction, per
`ANALYSIS-DRUG-CATALOGUE.md` §3.4) — a broken snapshot for one
country never blocks the other.

---

## 2. Refresh procedure (Italy)

Cadence: monthly, aligned to a MedReminder release
(`docs/ANALYSIS-DRUG-CATALOGUE.md` §12 point 2).

1. **Download the two CSV files from AIFA open data.**
   - Portal: <https://www.aifa.gov.it/opendata>
   - Files needed:
     - `confezioni_fornitura.csv` (one row per package;
       ~80 MB, ASCII, `;` delimiter)
     - `PA_confezioni.csv` (one row per package × active
       ingredient; ~11 MB, same encoding and delimiter)
   - Do not fetch `atc.csv` — `confezioni_fornitura.csv` already
     carries `CODICE_ATC` for every row.

2. **Build the ZIP.** Place the two CSV files at the **root** of the
   archive (no sub-directory). Both file names must be preserved
   verbatim (`AifaSnapshotParser` looks them up by name,
   case-insensitive):

   ```
   aifa-<yyyymm>.zip
   ├── confezioni_fornitura.csv
   └── PA_confezioni.csv
   ```

   Any standard tool works (`zip`, 7-Zip, Windows Explorer's
   "Send to → Compressed folder"). Expected compressed size is
   ~4–20 MB depending on the raw text redundancy.

3. **Name the archive** `aifa-<yyyymm>.zip` where `<yyyymm>` is the
   AIFA release date, e.g. `aifa-202609.zip`. This suffix ends up in
   the `snapshot_version` column of every imported row.

4. **Drop it into the repo** at
   `src/MedReminder.Infrastructure/Assets/Catalogue/it/aifa-<yyyymm>.zip`.
   The `<EmbeddedResource>` glob in
   `src/MedReminder.Infrastructure/MedReminder.Infrastructure.csproj`
   picks it up automatically — no csproj edit needed.

5. **Delete the previous month's ZIP** in the same folder. Only one
   AIFA snapshot must ship at a time; leaving two behind would let
   `EmbeddedSnapshotProvider.TryOpen` pick the wrong one (it takes
   the first resource matching the country prefix).

6. **Commit** with an imperative English message, e.g.
   `Refresh AIFA snapshot to 202609`.

7. **Verify** locally on Windows:
   ```powershell
   dotnet restore MedReminder.sln
   dotnet build   MedReminder.sln -c Release
   dotnet test    MedReminder.sln -c Release
   ```
   Then run the app once: the `CatalogueRefreshHostedService` logs
   `Reference-catalogue import for IT complete: inserted=… version=<yyyymm>`
   in `%LOCALAPPDATA%\MedReminder\logs\medreminder-*.log` on the
   first boot after the version changed. Subsequent boots log
   nothing because the importer short-circuits when the recorded
   `snapshot_version` already matches.

---

## 3. Refresh procedure (EU — EMA EPAR)

Cadence: monthly, aligned to a MedReminder release. The EMA
"Medicines" report is regenerated at least daily on
`www.ema.europa.eu`, so picking the current export at release-cut
time is enough.

1. **Download the EPAR "Medicines" report from EMA.**
   - Portal: <https://www.ema.europa.eu/en/medicines/download-medicine-data>
     — pick the "Medicines" download (Excel spreadsheet, one sheet
     named `Medicine`, ~2 700 rows, 39 columns).
   - No login needed; the file is public. Reuse governed by EMA's
     legal notice (Commission reuse decision 2011/833/EU), which
     allows redistribution with attribution.

2. **Convert the XLSX to a CSV.** MedReminder's importer reads
   CSV, not XLSX, so a one-shot conversion is required. Any tool
   works (LibreOffice `soffice --convert-to csv`, `xlsx2csv`, a
   two-line Python snippet with `openpyxl`, or Excel's "Save as
   CSV UTF-8"). Requirements:

   - Delimiter `;` (semicolon).
   - Encoding UTF-8, no BOM required.
   - Preserve **all 39** columns and their exact header names
     (the parser matches columns by name, case-insensitively;
     any of the expected columns missing fails the import loudly).
   - Drop the first 8 metadata rows and use the row that starts
     with `Category` as the header. Every subsequent row is data.
   - Collapse embedded newlines / tabs inside cells to single
     spaces (the parser reads one record per physical line).

3. **Build the ZIP.** Place the single CSV file at the **root** of
   the archive (no sub-directory). The name inside the archive
   must be `ema-epar.csv` (case-insensitive; `EmaEparParser` looks
   it up by name):

   ```
   ema-epar-<yyyymm>.zip
   └── ema-epar.csv
   ```

   Any standard tool works. Expected compressed size is ~400–600 KB
   (about 20–25 % of the raw CSV).

4. **Name the archive** `ema-epar-<yyyymm>.zip` where `<yyyymm>` is
   the export month (e.g. `ema-epar-202609.zip`). This suffix ends
   up in the `snapshot_version` column of every imported row and
   drives the `CatalogueRefreshHostedService` change-detection.

5. **Drop it into the repo** at
   `src/MedReminder.Infrastructure/Assets/Catalogue/eu/ema-epar-<yyyymm>.zip`.
   The `<EmbeddedResource>` glob in
   `src/MedReminder.Infrastructure/MedReminder.Infrastructure.csproj`
   picks it up automatically — no csproj edit needed.

6. **Delete the previous month's ZIP** in the same folder. Only one
   EPAR snapshot must ship at a time; leaving two behind lets
   `EmbeddedSnapshotProvider.TryOpen` pick whichever the reflection
   layer surfaces first for `country = "EU"`.

7. **Commit** with an imperative English message, e.g.
   `Refresh EMA EPAR snapshot to 202609`.

8. **Verify** locally on Windows:
   ```powershell
   dotnet restore MedReminder.sln
   dotnet build   MedReminder.sln -c Release
   dotnet test    MedReminder.sln -c Release
   ```
   Then run the app once: `CatalogueRefreshHostedService` logs
   `Reference-catalogue import for EU complete: inserted=… version=<yyyymm>`
   in `%LOCALAPPDATA%\MedReminder\logs\medreminder-*.log` on the
   first boot after the version changed. Subsequent boots log
   nothing because the importer short-circuits on the recorded
   `snapshot_version`.

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

---

## 5. Refresh cadence for other countries

Not shipping yet — AEMPS (`es`), ANSM / BDPM (`fr`), MHRA (`uk`)
and BfArM (`de`) land per country in milestone M4 of the
drug-catalogue plan. Each of those will gain its own subsection
here when it is implemented; the mechanical steps will mirror §2
with the source URL and terms adjusted.
