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

### AEMPS — Agencia Española de Medicamentos y Productos Sanitarios (Spain)

- **Dataset:** AEMPS CIMA — "Medicamentos" register (the tabular
  XLSX export served by the CIMA portal; the alternative XML
  "Prescripción" bundle is not the one MedReminder ships).
- **Source:** <https://cima.aemps.es/cima/publico/home.html>
  ("Nomenclátor / Descargas" section).
- **Licence / terms of use:** Spanish public-sector information
  reuse regime (Ley 37/2007, de 16 de noviembre, sobre reutilización
  de la información del sector público), allowing redistribution with
  attribution and without implying endorsement. Confirm the exact
  wording of the "Aviso legal" on the CIMA download page at
  retrieval time — if AEMPS ever narrows the terms, MedReminder
  must stop shipping the snapshot until the situation is resolved.
- **Attribution:** "Fuente: Agencia Española de Medicamentos y
  Productos Sanitarios (AEMPS)."
- **Snapshot shipped with this build:** see
  `src/MedReminder.Infrastructure/Assets/Catalogue/es/aemps-<yyyymm>.zip`.
  The `<yyyymm>` suffix identifies the export month and is the value
  written to the `snapshot_version` column of every imported row.
- **Modifications:** the parser drops no rows on ingest — the CIMA
  "Medicamentos" register is human-only (veterinary products live in
  the separate CIMAvet portal, not shipped here). `PharmaceuticalForm`
  and `Dosage` are left null on the row schema (form and dose are
  embedded inside the CommercialName text in this dataset); a future
  increment can switch to the XML variant to lift them out without
  changing the row shape. `DispensingRegime` is populated verbatim
  from the free-text `Observaciones` column.

### ANSM — Agence nationale de sécurité du médicament (France)

- **Dataset:** ANSM BDPM — *Base de données publique des
  médicaments*. Three TSV files (`CIS_bdpm.txt`, `CIS_CIP_bdpm.txt`,
  `CIS_COMPO_bdpm.txt`); MedReminder consumes CIS and COMPO, keeps
  CIP in the shipped ZIP for symmetry with what ANSM publishes.
- **Source:** <https://base-donnees-publique.medicaments.gouv.fr/telechargement.php>
- **Licence / terms of use:** Licence Ouverte Etalab 2.0 as
  declared on the download portal at retrieval time —
  <https://www.etalab.gouv.fr/licence-ouverte-open-licence>.
  Permits reuse, modification and redistribution (including
  commercially) subject to attribution.
- **Attribution:** "Source : ANSM — Base de données publique des
  médicaments (BDPM), diffusée sous Licence Ouverte Etalab 2.0."
- **Snapshot shipped with this build:** see
  `src/MedReminder.Infrastructure/Assets/Catalogue/fr/bdpm-<yyyymm>.zip`.
- **Modifications:** rows whose `Type de procédure AMM` starts with
  `Enreg homéo` are skipped at parse time (recorded as `Skipped` in
  `ImportReport`) — homeopathic products carry no meaningful
  active-ingredient signal for the autocomplete, mirroring the
  Omeopatico filter applied to the Italian AIFA dataset.
  `DispensingRegime`, `LinkLeaflet` and `LinkSpc` are left null:
  BDPM base does not expose them as structured columns.
  `ActiveIngredientRow.Atc` is always null: BDPM base has no
  structured ATC column. The `Titulaires` column is left-trimmed
  (upstream ships it with a leading space).

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
