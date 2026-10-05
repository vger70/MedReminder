# Third-party notices

MedReminder ships with data taken from public sources. This file lists
those sources, their licences and the obligations MedReminder honours
when redistributing them. It complements the licence texts of the
NuGet packages consumed by the build (which the .NET tooling records
separately) and is surfaced in the app's About dialog through the
`about.dataSources.*` localisation keys.

## Reference catalogue

Each dataset below is redistributed twice, with the same attribution
and the same content: embedded in the build (the snapshot shipped with
it), and as a monthly archive the repository publishes under
`data/<country>/` on the `feeds` branch, which the app downloads at
startup (`docs/CATALOGUE-DATA.md` §1, §2.1). The US catalogue is
published as an archive only, with no embedded snapshot.

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
- **Also redistributed from:** `data/it/aifa-<yyyymm>.zip` (both CSV
  files unchanged), built monthly by `.github/workflows/download_aifa.yaml`.
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
- **Also redistributed from:** `data/es/aemps-<yyyymm>.zip` (the
  export of <https://listadomedicamentos.aemps.gob.es/Medicamentos.xls>
  unchanged, stored as `aemps.xlsx`), built monthly by
  `.github/workflows/download_aemps.yaml`.
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
- **Source:** <https://base-donnees-publique.medicaments.gouv.fr/telechargement>
- **Licence / terms of use:** Licence Ouverte Etalab 2.0 as
  declared on the download portal at retrieval time —
  <https://www.etalab.gouv.fr/licence-ouverte-open-licence>.
  Permits reuse, modification and redistribution (including
  commercially) subject to attribution.
- **Attribution:** "Source : ANSM — Base de données publique des
  médicaments (BDPM), diffusée sous Licence Ouverte Etalab 2.0."
- **Snapshot shipped with this build:** see
  `src/MedReminder.Infrastructure/Assets/Catalogue/fr/bdpm-<yyyymm>.zip`.
- **Also redistributed from:** `data/fr/bdpm-<yyyymm>.zip` (the three
  files unchanged), built monthly by `.github/workflows/download_bdpm.yaml`.
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
- **Also redistributed from:** `data/eu/ema-epar-<yyyymm>.zip`, built
  monthly by `.github/workflows/download_ema.yaml`. The spreadsheet is
  converted to `ema-epar.csv` with all its columns and rows; the only
  changes are layout (metadata rows above the header dropped, line
  breaks and tabs inside cells replaced by spaces, `;` as delimiter).
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

### FDA — U.S. Food and Drug Administration (United States)

- **Dataset:** National Drug Code (NDC) Directory, finished drug
  products and their packages, as exported by openFDA (endpoint
  `drug/ndc`).
- **Source:** <https://open.fda.gov/data/ndc/>; bulk files listed in
  <https://api.fda.gov/download.json>.
- **Licence:** "Unless otherwise noted, the content, data,
  documentation, code, and related materials on openFDA is public
  domain and made available with a Creative Commons CC0 1.0 Universal
  dedication" (<https://open.fda.gov/license/>, read 2026-10-05). The
  NDC data is not listed under the page's exemptions. CC0 does not
  affect trademark rights: brand names remain their owners' marks and
  are shown only to identify the product.
- **Attribution (not required by CC0, given as good practice):** "US
  medicine catalogue: FDA National Drug Code Directory, from openFDA
  (CC0 1.0 public domain dedication). Not endorsed by the FDA." No FDA
  logo is used, and nothing implies endorsement by the FDA.
- **Redistributed from:** `data/us/fda-ndc-<yyyymm>.zip`, built monthly
  by `.github/workflows/download_fda_ndc.yaml`. No snapshot is
  embedded in the build.
- **Modifications:** the export is converted to one TSV row per
  package (`docs/CATALOGUE-DATA.md` §11): only finished prescription,
  OTC and vaccine products are kept, sample packages are dropped, the
  package NDC is rewritten in the 12-digit 6-4-2 form, and packages
  whose marketing ended are marked "Discontinued". The DailyMed link
  of each package is built by the app from the SPL set id.

---

MedReminder is not a medical device and does not provide clinical
guidance. Every therapy decision must be taken with the user's
doctor.
