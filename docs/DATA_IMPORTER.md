# AIFA data importer

`MedReminder.DataImporter` imports the current AIFA pharmaceutical CSV data into PostgreSQL. It is a separate .NET 10 command-line application; it does not reference or change the WinForms runtime architecture.

## Prerequisites

- .NET SDK 10 or later
- PostgreSQL 18 with the base pharmaceutical schema from `medreminder_drug_schema_v1.sql` applied
- the additive importer extension script at `database/medreminder_drug_schema_v1_importer_extensions.sql` applied
- the three current AIFA UTF-8 CSV files: `confezioni_fornitura.csv`, `PA_confezioni.csv`, and `atc.csv`

The importer has verified the current files use `;` as delimiter. `CsvHelper` streams them with BOM and quoted-field support; files are never loaded into memory as a whole.

## Configuration

Set the connection string by one of these mechanisms, in descending precedence:

1. `--connection-string`
2. environment variable `ConnectionStrings__MedReminder`
3. `src/MedReminder.DataImporter/appsettings.json` (do not commit credentials)

Example:

```powershell
$env:ConnectionStrings__MedReminder = 'Host=localhost;Port=5432;Database=medreminder;Username=medreminder;Password=replace-me'
```

## Commands

```powershell
dotnet run --project src/MedReminder.DataImporter -- --help

dotnet run --project src/MedReminder.DataImporter -- validate-aifa `
  --packages 'C:\AIFA\confezioni_fornitura.csv' `
  --ingredients 'C:\AIFA\PA_confezioni.csv' `
  --atc 'C:\AIFA\atc.csv'

dotnet run --project src/MedReminder.DataImporter -- import-aifa `
  --packages 'C:\AIFA\confezioni_fornitura.csv' `
  --ingredients 'C:\AIFA\PA_confezioni.csv' `
  --atc 'C:\AIFA\atc.csv'

dotnet run --project src/MedReminder.DataImporter -- export-sqlite `
  --output 'C:\MedReminder\medreminder-pharma.db'
```

`validate-aifa` creates an import run and staging records for diagnostics, but never writes `core`. `import-aifa` normalizes only after staging validation has passed.

`export-sqlite` copies the current PostgreSQL pharmaceutical catalog to a separate SQLite database. It never reads from or writes to the WinForms application database (`medreminder.db`). The destination must not already exist unless `--overwrite` is specified.

## Import flow and recovery

For every run the importer hashes each source file (SHA-256), records its metadata, then streams rows directly into PostgreSQL binary `COPY` staging tables. Staging validation detects missing keys, duplicate AIC/ATC codes, bad ATC structure, unlinked ingredients, and package ATCs absent from the provided reference data.

The core normalization uses one transaction. A failure rolls it back, leaving staging and the failed run available for investigation. Ctrl+C cancels the active work and marks the run failed when the database is still reachable.

`core.package_atc` is the authoritative package/ATC relationship. The legacy `core.package.atc_code` is deliberately written as `NULL`; it is retained in the base schema for compatibility rather than dropped. Marketing-authorisation values remain in the existing lightweight administrative-status and procedure fields. Documents are stored per package because that is the current base schema, although AIFA URLs may be product-level in practice.

Rows no longer included in the current AIFA package feed are marked inactive. Deactivation is constrained to medicinal products with the AIFA source and never deletes data or affects future sources.

## Exit codes

| Code | Meaning |
| --- | --- |
| 0 | Successful import or validation |
| 1 | Import failure or cancellation |
| 2 | Validation failure; `core` was not updated |
| 3 | Invalid command or configuration |

## Performance

The importer processes one CSV record at a time and writes it through `NpgsqlBinaryImporter` (`COPY ... FORMAT BINARY`). Its working memory is therefore approximately independent of CSV size. It intentionally does not download linked documents or derive pharmaceutical-unit conversions that are not unambiguous.
