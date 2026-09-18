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

### EMA — European Medicines Agency (reserved for a future release)

The EMA Article 57 dataset covers centrally authorised medicinal
products valid in every EU / EEA member state. Its integration is
planned in a future release (M3 in
`docs/ANALYSIS-DRUG-CATALOGUE.md` §3.4). Once landed, this file will
be updated with the corresponding attribution and the About dialog
will surface it through `about.dataSources.emaArticle57`.

---

MedReminder is not a medical device and does not provide clinical
guidance. Every therapy decision must be taken with the user's
doctor.
