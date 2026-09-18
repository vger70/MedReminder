# Reference-catalogue data

Operational notes for the people who refresh the AIFA snapshot
shipped inside MedReminder. Read once before your first refresh, then
use it as a checklist each month.

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

| Country | File                                                                    | Source                                   | Licence      |
|---------|-------------------------------------------------------------------------|------------------------------------------|--------------|
| `it`    | `Assets/Catalogue/it/aifa-<yyyymm>.zip`                                 | AIFA — Agenzia Italiana del Farmaco      | CC BY 4.0    |

The `<yyyymm>` suffix (e.g. `aifa-202609.zip`) identifies the AIFA
release date. That literal string is what
`EmbeddedSnapshotProvider.TryOpen` extracts from the resource name
and passes down to `CsvReferenceCatalogueImporter` as
`snapshot_version`. Every imported row carries it so the boot-time
importer can detect a newer snapshot and short-circuit when the DB
is already up to date.

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

## 3. Reducing the fixture (for tests)

The M0 fixture under `tests/fixtures/catalogue/` is a curated ~200-row
subset of a real AIFA snapshot, stratified to cover the edge cases
the importer must handle (`Sospesa`, `Procedura Centralizzata`, OTC,
hospital-only, well-known brands, multi-ingredient combinations).
Regenerate it only when the AIFA schema itself changes; a routine
snapshot refresh does not touch the fixture.

To regenerate:

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

---

## 4. Refresh cadence for other countries

Not shipping yet — EMA Article 57 (`eu`), AEMPS (`es`), ANSM /
BDPM (`fr`), MHRA (`uk`) and BfArM (`de`) land per country in
milestones M3 and M4 of the drug-catalogue plan. Each of those will
gain its own subsection here when it is implemented; the mechanical
steps will mirror §2 with the source URL and licence adjusted.
