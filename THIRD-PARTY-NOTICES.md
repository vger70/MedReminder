# Third-party notices

MedReminder ships with data taken from public sources. This file lists
those sources, their licences and the obligations MedReminder honours
when redistributing them. It complements the licence texts of the
NuGet packages consumed by the build (which the .NET tooling records
separately) and is surfaced in the app's About dialog through the
`about.dataSources.*` localisation keys.

## Reference catalogue

### AIFA — Agenzia Italiana del Farmaco (Italy)

- **Dataset:** AIFA open data — medicine catalogue
  (`confezioni_fornitura.csv` joined with `PA_confezioni.csv`).
- **Source:** <https://www.aifa.gov.it/opendata>
- **Licence:** Creative Commons Attribution 4.0 International
  (CC BY 4.0) — <https://creativecommons.org/licenses/by/4.0/>
- **Attribution:** "Italian medicine catalogue: AIFA open data
  (CC BY 4.0)."
- **Snapshot shipped with this build:** see
  `src/MedReminder.Infrastructure/Assets/Catalogue/it/aifa-<yyyymm>.zip`.
  The `<yyyymm>` suffix identifies the AIFA release date and is the
  value written to the `snapshot_version` column of every imported
  row.
- **Modifications:** two documented row filters are applied at import
  time (`docs/ANALYSIS-DRUG-CATALOGUE.md` §3.2) —
  `TIPO_PROCEDURA = 'Omeopatico'` and
  `PRINCIPIO_ATTIVO = 'N.D.'` rows are dropped.

### EMA — European Medicines Agency (EU centrally authorised)

- **Dataset:** EMA EPAR — *European public assessment reports*
  (Medicines report, human medicines). Covers every medicinal
  product authorised through the EU centralised procedure and
  therefore valid across the EU / EEA. The `EU` catalogue in
  MedReminder is populated from this dataset.
- **Source:** <https://www.ema.europa.eu/en/medicines/download-medicine-data>
  ("Medicines" report, exported as an Excel spreadsheet and
  converted to CSV before being packaged into the shipped ZIP).
- **Terms of use:** the EMA website content is public and may be
  reused with attribution under EMA's legal notice
  (<https://www.ema.europa.eu/en/about-us/legal-notice>), which
  applies the European Commission's *Reuse of Commission documents*
  policy (Commission Decision 2011/833/EU). Attribution required;
  no endorsement implied.
- **Attribution:** "European centrally authorised medicines: EMA
  EPAR dataset (European public assessment reports)."
- **Snapshot shipped with this build:** see
  `src/MedReminder.Infrastructure/Assets/Catalogue/eu/ema-epar-<yyyymm>.zip`.
  The `<yyyymm>` suffix identifies the export month and is written
  to the `snapshot_version` column of every imported row.
- **Modifications:** one documented row filter is applied at import
  time (`docs/ANALYSIS-DRUG-CATALOGUE.md` §3.4) — rows whose
  `Category` is not `Human` are dropped. Every imported row lands
  with `country = 'EU'`; the EMA long form `European Union` is
  normalised to the two-character `EU` code via `CountryCode.Parse`
  and never reaches the database. Note that the shipped catalogue
  is EPAR (centralised procedure), not the wider EMA Article 57
  dataset which also carries per-country national authorisations —
  a distinction preserved in the localisation key name
  `about.dataSources.emaArticle57` (kept for compatibility with the
  M2 dictionaries) and the surrounding docs.

---

MedReminder is not a medical device and does not provide clinical
guidance. Every therapy decision must be taken with the user's
doctor.
