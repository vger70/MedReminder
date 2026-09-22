# ANALYSIS — Drug reference catalogue

Design document, **prior** to implementation. Discovery notes and
open decisions live in issue
[#9](https://github.com/vger70/MedReminder/issues/9); this file is
the engineering plan that will be executed once the outstanding
decisions in §12 are resolved.

> **This is not a speculative analysis.** Every decision here is
> already technically motivated and delimits what will actually be
> written in code. The "Decisions still to confirm" section at the
> end is the only remaining zone of ambiguity that requires input.

---

## 1. Scope

### 1.1 Goal

Introduce a normalised **reference catalogue** of medicinal products
so that, when the user creates or edits a medicine in MedReminder,
the app can:

1. Autocomplete against a local table where both the **commercial
   name** and the **active ingredient** (`principio attivo`) are
   first-class searchable fields.
2. Populate the missing side of that pair automatically: pick a
   commercial name → the active ingredient is filled in, and vice
   versa.
3. Filter the reference data **by country**, so the app can be
   deployed to users outside Italy without touching the schema. The
   default country is Italy. The country filter must also include
   supranational (EU-wide) authorisations.
4. Expose stable identifiers (`AtcCode`, `NationalCode`) that later
   increments can build on (duplicate-therapy detection, ATC-based
   reminders, leaflet links).

### 1.2 What this increment does not do

- No clinical decision support (drug-drug interactions, dose
  checking, contraindications). MedReminder remains a
  non-medical-device reminder tool per its charter.
- No live network calls at reminder time. Reminders keep working
  offline exactly as today.
- No dependency on a commercially-licensed medicine database
  (DrugBank, SNOMED CT, WHO ATC/DDD spreadsheet, Farmadati).
- No auto-update of the snapshot files. The snapshot ships with the
  app build in v1; the online updater is a separate optional
  milestone (§8).

### 1.3 Data sources

Settled in issue #9, restated here for the record:

- **AIFA open data** — primary source for Italy, CC BY 4.0 licence,
  files in CSV / XML. Compatible with redistribution inside the
  shipped build. Provides `{AIC, commercial name, active ingredient,
  ATC, form, dosage, MAH, marketing status}` per record.
- **EMA Article 57** — secondary source for the `EU` country
  (EU-centralised authorisations valid in every member state) and
  for future non-Italian national catalogues. Publicly downloadable
  from `data.europa.eu` and `ema.europa.eu`.
- **`dati.salute.gov.it` — foreign medicines** — fills the gap for
  medicines used in Italian NHS structures but not authorised in
  Italy. IODL v2.0 licence. Optional add-on, only if AIFA coverage
  turns out to be insufficient.
- WHO ATC/DDD Index, DrugBank, RxNorm, openFDA, SNOMED CT — all
  ruled out for the reasons documented in issue #9.

---

## 2. Architecture

Clean Architecture direction is preserved: `UI → Application →
Domain`, with `Infrastructure` implementing the ports declared in
`Application`. No new cross-layer references.

### 2.1 New Domain types

Added to `MedReminder.Domain` (project targets `net10.0`, no
Windows or EF dependencies):

```csharp
public readonly record struct CountryCode
{
    public string Value { get; }
    public bool IsSupranational => Value == "EU";
    public static CountryCode Parse(string raw);
    // Normalises "European Union" → "EU"; validates ISO 3166-1
    // alpha-2 otherwise.
}

public readonly record struct AtcCode
{
    public string Value { get; }
    public static AtcCode Parse(string raw);
    // Validates the 7-character ATC pattern (e.g. "A10BA02").
}

public sealed class ReferenceActiveIngredient
{
    public Guid Id;
    public CountryCode Country;
    public string Name;
    public AtcCode? Atc;
}

public sealed class ReferenceMedicine
{
    public Guid Id;
    public CountryCode Country;
    public string NationalCode;              // AIC for IT, EMA product number for EU
    public string CommercialName;
    public string? PharmaceuticalForm;
    public string? Dosage;
    public string? MarketingAuthorisationHolder;
    public string? MarketingStatus;
    public string SnapshotVersion;
    public IReadOnlyList<ReferenceActiveIngredient> ActiveIngredients;
}
```

### 2.2 New Application ports

Added to `MedReminder.Application`:

```csharp
public interface IReferenceCatalogueQueryService
{
    Task<IReadOnlyList<ReferenceMedicine>> SearchByCommercialNameAsync(
        string prefix, CountryCode userCountry, int limit, CancellationToken ct);

    Task<IReadOnlyList<ReferenceMedicine>> SearchByActiveIngredientAsync(
        string prefix, CountryCode userCountry, int limit, CancellationToken ct);

    Task<ReferenceMedicine?> GetByNationalCodeAsync(
        CountryCode country, string nationalCode, CancellationToken ct);
}

public interface IReferenceCatalogueImporter
{
    Task<ImportReport> ImportAsync(
        Stream snapshot, CountryCode expectedCountry,
        string snapshotVersion, CancellationToken ct);
}

public sealed record ImportReport(
    int Inserted, int Updated, int Deleted, int Skipped,
    string SnapshotVersion, DateTimeOffset CompletedAt);
```

**Country filter, defined once in Application.** Every query
translates the user's selected country into

```
WHERE country IN (@user, 'EU')   -- default for EU-member countries
WHERE country =  @user           -- for countries flagged as
                                 -- not-covered-by-EU (e.g. UK GB)
```

The choice is driven by a per-country `IncludesEuCentralised`
flag on the country profile (§12 point 7). The UI never
recomputes the set. When the user deliberately switches to `EU`,
only supranational rows are returned.

Use cases live here as thin coordinators:

- `SearchCatalogueUseCase` — orchestrates the two `SearchBy…`
  entry points behind a single façade for the UI.
- `LinkMedicineToReferenceUseCase` — associates a user-authored
  `Medicine` with a `ReferenceMedicine`, populating `AtcCode`,
  `ActiveIngredient`, `NationalCode` without touching user notes.

### 2.3 Infrastructure additions

`MedReminder.Infrastructure` gains:

- `SqliteReferenceCatalogueQueryService` — EF Core adapter with
  compiled queries against the new tables.
- `CsvReferenceCatalogueImporter` — generic CSV importer that
  delegates parsing to a strategy per source:
  - `AifaSnapshotParser` (IT rows)
  - `EmaArticle57Parser` (EU rows, normalises
    `European Union` → `EU` before writing)
- `EmbeddedSnapshotProvider` — surfaces the snapshot files shipped
  as embedded resources under `Assets/Catalogue/`.

### 2.4 Persistence: schema

New tables in `medreminder.db`, created by an **additive idempotent
DDL step on boot** per the rule in `ANALYSIS.md` §2.8. No
`EnsureCreated()` shortcut.

```sql
CREATE TABLE IF NOT EXISTS reference_medicines (
    id                     BLOB PRIMARY KEY,
    country                TEXT NOT NULL,          -- "IT", "ES", …, or "EU"
    national_code          TEXT NOT NULL,
    commercial_name        TEXT NOT NULL,
    commercial_name_norm   TEXT NOT NULL,          -- lowercase, no diacritics
    pharmaceutical_form    TEXT,
    dosage                 TEXT,
    mah                    TEXT,
    marketing_status       TEXT,
    dispensing_regime      TEXT,                   -- prescription / OTC / hospital-only
    link_leaflet           TEXT,                   -- URL to package leaflet (FI)
    link_spc               TEXT,                   -- URL to summary of product characteristics (RCP)
    snapshot_version       TEXT NOT NULL,
    UNIQUE (country, national_code)
);
CREATE INDEX IF NOT EXISTS ix_ref_med_country_name
    ON reference_medicines(country, commercial_name_norm);

CREATE TABLE IF NOT EXISTS reference_active_ingredients (
    id             BLOB PRIMARY KEY,
    country        TEXT NOT NULL,
    name           TEXT NOT NULL,
    name_norm      TEXT NOT NULL,                  -- lowercase, no diacritics
    atc_code       TEXT,
    UNIQUE (country, name_norm)
);
CREATE INDEX IF NOT EXISTS ix_ref_ing_country_name
    ON reference_active_ingredients(country, name_norm);

CREATE TABLE IF NOT EXISTS reference_medicine_ingredients (
    medicine_id     BLOB NOT NULL
        REFERENCES reference_medicines(id) ON DELETE CASCADE,
    ingredient_id   BLOB NOT NULL
        REFERENCES reference_active_ingredients(id),
    PRIMARY KEY (medicine_id, ingredient_id)
);
```

The `_norm` columns are computed at import time so that
autocomplete queries do `LIKE 'para%'` against an indexed column
instead of running `LOWER()` at query time.

### 2.5 User `Medicine` entity — additive changes

Three optional fields on the existing `Medicine`:

- `NationalCode` (nullable string)
- `AtcCode` (`AtcCode?`)
- `LinkedReferenceMedicineId` (nullable `Guid`, weak FK)

Existing user-authored medicines remain valid; all new fields are
nullable and the free-text fallback is preserved. The `Medicine`
table gets the three columns via the same additive migration step.

### 2.6 Snapshot layout

Files ship as embedded resources inside `MedReminder.Infrastructure`
under `Assets/Catalogue/<country>/`:

```
Assets/Catalogue/it/aifa-<yyyymm>.csv.gz
Assets/Catalogue/eu/ema-article57-<yyyymm>.csv.gz
```

`<yyyymm>` is the `snapshot_version` written to every imported row.

At boot the app compares the embedded `snapshot_version` for each
enabled country against what is recorded in the DB. On mismatch,
the importer runs in the background (does not block the UI thread)
and, once complete, refreshes the autocomplete cache.

### 2.7 Attribution

- Add a `THIRD-PARTY-NOTICES.md` at repository root listing:
  - AIFA open data — CC BY 4.0
  - EMA Article 57 dataset — per EMA's stated terms
- Surface the same list in the About dialog (new localised
  strings).
- Do not remove either mention without also removing the data.

---

## 3. Milestones

| # | Milestone                       | Deliverable                                                                                                     |
|---|---------------------------------|-----------------------------------------------------------------------------------------------------------------|
| M0 | Discovery spike                | Field mapping AIFA → schema, fixture in `tests/fixtures/catalogue/`, comment posted to issue #9                 |
| M1 | Schema + import scaffolding    | Domain types, Application ports, additive migration, generic CSV importer, AIFA parser, tests                   |
| M2 | Autocomplete Italy             | AIFA snapshot embedded, autocomplete on Add/Edit medicine, country setting, user guide updated in all languages |
| M3 | EU centralised authorisations  | EMA Article 57 parser, `European Union` → `EU` normalisation, dedup rule against IT rows                        |
| M4 | Additional national catalogues | One country per PR: ES (AEMPS) or FR (ANSM/BDPM) first                                                          |
| M5 | Snapshot online updater        | Optional; signed manifest, opt-in, hash verification                                                            |

v1 shippable set is M0–M3. M4 and M5 are follow-ons.

### 3.1 M0 — Discovery spike (completed)

Ran against the current AIFA open-data files
(`drive.aifa.gov.it/farmaci/`). Full findings and the field mapping
are recorded in issue
[#9](https://github.com/vger70/MedReminder/issues/9); the essentials:

- **Source files**: `confezioni_fornitura.csv` (78.6 MB, 160,008
  rows, one per package) as the primary source, joined on
  `CODICE_AIC` with `PA_confezioni.csv` (11.0 MB, 338,527 rows,
  one per package × active ingredient). `atc.csv` (193 KB) is
  not needed for M1 — every row in `confezioni_fornitura`
  already carries `CODICE_ATC`.
- **Encoding**: ASCII throughout, delimiter `;`, `CODICE_AIC` is
  a 9-digit `TEXT` in 100% of rows.
- **Post-filter cardinalities** (after applying the two import
  rules in §3.2 below): 85,697 reference_medicines,
  9,619 distinct commercial names, 5,750 distinct active
  ingredients, 2,269 distinct ATC codes. Zero kept AIC lack
  active-ingredient data.
- **Data volume**: ~60–80 MB additional SQLite pages;
  gzipped snapshot ~17–22 MB inside the installer.
- **Fixture**: `tests/fixtures/catalogue/aifa-confezioni-sample.csv`
  (198 rows), `aifa-pa-sample.csv` (253 rows),
  `aifa-atc-sample.csv` (76 rows). ~110 KB total, deliberately
  covers `Sospesa`, `Procedura Centralizzata`, OTC,
  hospital-only, well-known brands, and 37 multi-ingredient
  combinations.
- **OTC/SOP coverage**: 78,610 rows in the current file are OTC
  ("da banco") or non-prescription — the previously-flagged
  OTC gap does not exist.

### 3.2 M1 — Schema + import scaffolding

- Add the Domain types listed in §2.1, with pure unit tests
  (`CountryCode.Parse` covers `European Union`, ISO codes, invalid
  input; `AtcCode.Parse` covers the ATC pattern).
- Add the Application ports listed in §2.2, plus the two use cases,
  with tests that use fake ports and prove the `country IN (@user,
  'EU')` semantics.
- Add the additive DDL step in `MedReminderDbContext.OnBoot`.
- Add the Infrastructure adapters listed in §2.3, wired through
  DI. Feature flag off by default so no UI is affected yet.
- Add EF Core query-compilation for the two `SearchBy…` methods.
- **AIFA import rules** (`AifaSnapshotParser`):
  - Read `confezioni_fornitura.csv` as the primary source; join
    on `CODICE_AIC` with `PA_confezioni.csv` for the M2M
    ingredient rows.
  - **Skip rows where `TIPO_PROCEDURA = 'Omeopatico'`** — 74,310
    rows in the current file (46%). Homeopathic products carry no
    active ingredient (`PA_confezioni` returns `N.D.` for them),
    so the autocomplete has nothing meaningful to show; also
    aligns with the "no clinical decision support" charter. A
    Settings toggle can re-enable them later if requested.
  - **Skip rows where `PRINCIPIO_ATTIVO = 'N.D.'`** in
    `PA_confezioni` — 52,920 rows. Do not create
    `reference_active_ingredients` for `N.D.` and do not create
    join rows in `reference_medicine_ingredients`.
  - **Keep `Sospesa` rows** and let the UI badge them per §12.6.
    Only 33 packages are `Sospesa` in the current file, so the
    badge fires on the exceptions.
  - **Assign `country = 'IT'`** to every row imported from
    `confezioni_fornitura`, regardless of `TIPO_PROCEDURA`. Rows
    tagged `Procedura Centralizzata` are also physically
    authorised on the Italian market, so from an Italian user's
    point of view they are legitimately Italian rows. The EMA
    `EU` catalogue lands in M3 as a second row per §12.3
    (do-not-deduplicate).
  - Populate `dispensing_regime` from `FORNITURA`, `link_leaflet`
    from `LINK_FI`, `link_spc` from `LINK_RCP`.
- Integration tests on SQLite in-memory using the M0 fixture
  under `tests/fixtures/catalogue/`:
  - Import once → expected row counts (198 raw rows in
    `aifa-confezioni-sample.csv` → 198 − (Omeopatico in sample)
    reference_medicines).
  - Import twice with the same `snapshot_version` → zero delta
    (idempotency).
  - Import a newer `snapshot_version` → stale rows for that
    country are removed.
  - `SearchByCommercialNameAsync("aug", "IT", 20, …)` returns the
    fixture's Augmentin rows; the same call with country
    `"EU"` returns only supranational rows (none in the
    Italian-only fixture, so the result is empty as expected).
  - Multi-ingredient AIC in the fixture (37 combinations) resolve
    to the expected set of active ingredients via the M2M table
    (Augmentin → amoxicillin + clavulanic acid;
    Aspirina → acetylsalicylic + ascorbic acid).

### 3.3 M2 — Autocomplete Italy

- Embed `aifa-<yyyymm>.csv.gz` as an assembly resource; document
  the refresh procedure in `docs/CATALOGUE-DATA.md` (new file).
- Extend `Medicine` with the three optional fields from §2.5,
  through the same additive migration step.
- New WinForms control `MedicineAutocompleteBox`:
  - Text input with a virtualised dropdown.
  - 150 ms debounce on keystrokes.
  - Row template: `commercial_name — active_ingredient — dosage`.
  - Limit 20 rows per query.
- The medicine form gets **two** autocomplete boxes — one on
  "Commercial name" and one on "Active ingredient". Picking a row
  in either fills the other via `OnReferenceMedicineSelected`.
- The user can always type freely: input that does not match a
  reference row is stored verbatim, `LinkedReferenceMedicineId`
  stays `NULL`. No validation blocks saving.
- New Settings entry "Reference country" (default `IT`, dropdown
  over ISO codes present in the local DB plus `EU`).
- Localisation: add every new UI key to every
  `assets/localization/strings.<lang>.json` file (currently
  `de`, `en`, `es`, `fr`, `it`). The existing
  `DictionaryParityTests` fail the build on any missing key.
- User guide: update every `docs/USER_GUIDE.<lang>.md` shipped
  with the app (currently `de`, `en`, `es`, `fr`, `it`) with a
  section explaining the autocomplete, what to do for a medicine
  not in the catalogue, and the source of the data with its
  licence.

### 3.4 M3 — EU centralised authorisations

- Embed `ema-article57-<yyyymm>.csv.gz` under
  `Assets/Catalogue/eu/`.
- `EmaArticle57Parser` normalises `European Union` → `EU` at write
  time, so the DB never carries the long-form value.
- Dedup rule (confirmed in §12 point 3): **do not deduplicate**.
  Both an AIFA row and an EMA `EU` row for the same product may
  appear; the autocomplete simply shows them.
- No UI changes: the `country IN (@user, 'EU')` filter is already
  in place from M1.
- Integration test: a query for a well-known centrally-authorised
  product with country `IT` returns at least one `EU` row.

### 3.5 M4 — Additional national catalogues

For each new country:

1. Identify the national agency and its open-data offering.
2. **Verify the licence** allows redistribution inside the shipped
   binary. If not, do not proceed.
3. Write a dedicated parser strategy under
   `Infrastructure/Catalogue/Parsers/`.
4. Embed the snapshot under `Assets/Catalogue/<ISO>/`.
5. Add integration tests with a fixture reduced to ~200 rows.
6. Update the four user guides.

Confirmed target set (§12 point 4): **ES (AEMPS / CIMA)**, **FR
(ANSM / BDPM)**, **UK (MHRA)**, **DE (BfArM)**. One PR per
country. Ordering is deferred until M2 is live; ES and FR are the
natural first candidates given the maturity of their open-data
offerings.

**M4 status:**

| Country | Agency        | Status                     | Shipped as of                          |
|---------|---------------|----------------------------|----------------------------------------|
| ES      | AEMPS / CIMA  | Shipped (M4, PR #20)       | `Assets/Catalogue/es/aemps-<yyyymm>.zip` |
| FR      | ANSM / BDPM   | Shipped (M4, PR #20)       | `Assets/Catalogue/fr/bdpm-<yyyymm>.zip`  |
| UK / GB | MHRA          | **Suspended (M4b, PR #21)**| —                                      |
| DE      | BfArM         | **Suspended (M4b, PR #21)**| —                                      |

**Why M4b is suspended.** MedReminder ships the reference catalogue
inside the binary, so §3.5 point 2 (verify the licence allows
redistribution inside the shipped build, otherwise do not proceed)
is a hard gate. Both remaining target countries fail that gate as
of 2026-09:

- **UK / MHRA.** The `products.mhra.gov.uk` portal exposes the
  Products dictionary through a search UI and per-product HTML
  pages, not through a bulk structured export. The realistic
  alternative — NHS BSA's *Dictionary of Medicines and Devices*
  (dm+d) — has the coverage MedReminder would need but is
  distributed under a NHS BSA licence that requires a separate
  agreement and does not allow silent redistribution inside a
  third-party product binary. OpenPrescribing UK is derivative,
  not authoritative, and inherits the same underlying licence
  question. Until a bulk MHRA export appears (or NHS BSA changes
  their licensing stance) M4b for UK stays closed.
- **DE / BfArM.** The German public-medicines registry has been
  distributed under several successive names (AMIS-öffentlich,
  now AMIce Public) and the layout has changed shape multiple
  times. At the time of writing the current portal does not offer
  a stable bulk export with a clearly declared open-data licence
  at the point of download — the "Datenlizenz Deutschland –
  Namensnennung 2.0" that would apply by policy is not surfaced on
  the download landing page, and BfArM has in the past withdrawn
  bulk endpoints without notice. Until both prerequisites are met
  simultaneously (stable bulk endpoint + licence declared at
  retrieval time), M4b for DE stays closed.

Reopening either country requires that the corresponding gate is
met with a specific URL, a specific ZIP layout and a specific
licence text captured at retrieval time. `IncludesEuCentralised`
handling for both is already encoded in
`StaticCountryProfileProvider` (UK/GB explicitly `false`, DE
defaulting to `true`), so no country-profile work is blocking. See
`docs/CATALOGUE-DATA.md` §7 for the operational rationale mirror.

> **UK is a special case.** Since the UK left the EU and EMA, an
> EMA `EU` centralised authorisation is no longer valid for the
> Great Britain market (Northern Ireland still follows EU rules
> under the Windsor Framework). The UK importer, when it lands,
> must ingest MHRA's own product register, and for country `GB`
> (or its `UK` alias) the query must **not** union with `EU` rows
> (see §12 point 7). This is the only country in the confirmed
> target set that breaks the default "national ∪ EU" rule.

### 3.6 M5 — Snapshot online updater (optional)

Only if we later decide the "ship with the app" cadence is not
enough. Design outline:

- Signed manifest published at
  `https://<hostname>/catalogue/manifest.json`, listing SHA-256 and
  URL of each snapshot per country / version.
- Downloader that verifies the hash before importing.
- Opt-in flag in Settings.
- Runs at app startup only, respecting the "no network at reminder
  time" invariant.

---

## 4. Data flow

### 4.1 Import path

```
[embedded resource: aifa-<yyyymm>.csv.gz]
    │
    ▼
EmbeddedSnapshotProvider.Open(country: "IT")
    │  Stream
    ▼
CsvReferenceCatalogueImporter.ImportAsync(stream, "IT", "<yyyymm>")
    │  strategy dispatch
    ▼
AifaSnapshotParser
    │  yields ReferenceMedicineRow records
    ▼
Transactional upsert into reference_medicines,
reference_active_ingredients, reference_medicine_ingredients
    │
    ▼
Rows with country = "IT" and snapshot_version ≠ "<yyyymm>" removed
    │
    ▼
ImportReport { Inserted, Updated, Deleted, Skipped, … }
```

Import always runs inside one transaction per country. Failure
rolls back the entire import for that country; other countries
already imported remain intact.

### 4.2 Query path

```
UI: MedicineAutocompleteBox
    │  user types "para"
    ▼
SearchCatalogueUseCase.SearchByCommercialName("para")
    │  reads user country from Settings ("IT")
    ▼
IReferenceCatalogueQueryService.SearchByCommercialNameAsync(
    "para", CountryCode.IT, limit: 20, …)
    │  SQL: WHERE country IN ('IT','EU')
    │        AND commercial_name_norm LIKE 'para%'
    ▼
Rows sorted by exact-prefix > contains, then by commercial_name,
serialised into the dropdown.
```

Query normalisation of the input (`para` → `para`, `Aùgmentin` →
`augmentin`) happens in the adapter, mirroring the transformation
applied to `_norm` at import time.

---

## 5. Testing strategy

| Level                   | What                                                                       | Project                                 |
|-------------------------|----------------------------------------------------------------------------|-----------------------------------------|
| Domain unit             | `CountryCode`, `AtcCode`, parsing, normalisation                           | `MedReminder.Domain.Tests`              |
| Application unit        | Use cases, `country IN (@user, 'EU')` semantics, via fake ports            | `MedReminder.Application.Tests`         |
| Infrastructure integ.   | Import of AIFA and EMA fixtures against SQLite in-memory                   | `MedReminder.Infrastructure.Tests`      |
| Infrastructure integ.   | Idempotency: same-version import → zero delta                              | idem                                    |
| Infrastructure integ.   | Newer-version import removes stale rows of the same country                | idem                                    |
| Infrastructure integ.   | Query returns Italian rows plus EU rows when country = `IT`                | idem                                    |
| Infrastructure integ.   | Query returns only EU rows when country = `EU`                             | idem                                    |
| Manual UI               | Add medicine via autocomplete → save → reopen → data preserved             | PR checklist                            |
| Manual UI               | Change country in Settings → autocomplete content changes accordingly      | PR checklist                            |
| Manual UI               | Free-text entry (no autocomplete pick) still saves and reopens correctly   | PR checklist                            |

No new test relies on DPAPI or the registry, so the new
infrastructure tests are cross-platform even though the project
itself targets `net10.0-windows`.

---

## 6. Localisation

Every new user-visible string is added to all dictionaries in one
commit (currently five):

- `assets/localization/strings.de.json`
- `assets/localization/strings.en.json`
- `assets/localization/strings.es.json`
- `assets/localization/strings.fr.json`
- `assets/localization/strings.it.json`

The `DictionaryParityTests` in
`tests/MedReminder.Application.Tests/Localization/` fail the
build if a key is present in one dictionary and missing in
another, so parity is enforced automatically.

New keys (indicative, final names to be aligned with existing
conventions):

- `medicine.field.commercialName`
- `medicine.field.activeIngredient`
- `medicine.field.atcCode`
- `medicine.autocomplete.hint`
- `medicine.autocomplete.noMatches`
- `settings.referenceCountry.label`
- `settings.referenceCountry.help`
- `about.dataSources.aifa`
- `about.dataSources.emaArticle57`

---

## 7. Risks and mitigations

| Risk                                                                               | Impact             | Mitigation                                                                                                                     |
|------------------------------------------------------------------------------------|--------------------|--------------------------------------------------------------------------------------------------------------------------------|
| AIFA snapshot bloats the installer                                                 | Low (measured)     | M0 measured ~17–22 MB for the gzipped snapshot (raw `confezioni_fornitura.csv` 78.6 MB + `PA_confezioni.csv` 11.0 MB). SQLite table growth ~60–80 MB. Within budget; reconsider M5 only if a future dataset grows materially. |
| Autocomplete is slow on large tables                                               | High (UX)          | `_norm` indexes, EF Core compiled queries, 150 ms debounce, hard limit of 20 rows                                              |
| Wrong dedup between EU and national rows                                           | High (correctness) | Default is not to dedup (§3.4); revisit only with an explicit mapping table between AIC and EMA product number                 |
| Attribution string dropped from the About dialog                                   | High (licence)     | Test that verifies the presence of the AIFA / EMA lines in the About dialog and in `THIRD-PARTY-NOTICES.md`                    |
| User stays on an old app build for months and never gets fresh data                | Low in v1          | Documented in the user guide; M5 addresses it if it becomes a real problem                                                     |
| Release into a country whose dataset is not CC-BY-compatible                       | High (licence)     | Verify the licence **before** writing the parser in M4                                                                         |
| WinForms autocomplete flicker on large result sets                                 | Medium (UX)        | Virtualised dropdown, results delivered on the UI thread only when the debounce window closes                                  |

---

## 8. Release strategy

- M0–M1 land as one PR titled "Reference catalogue foundations".
  No user-visible change; feature flag stays off.
- M2 lands as "Reference catalogue: AIFA snapshot + autocomplete
  (Italy)". Screenshots in the PR description; user guides updated.
- M3 lands as "Reference catalogue: EMA Article 57 (EU
  centralised)".
- M4 lands one PR per country.
- M5, if adopted, lands as "Reference catalogue: optional online
  snapshot updater".

Every PR gets a `CHANGE_LOG.md` entry prepended per the repository
rule.

---

## 9. Effort estimate

| Phase   | Person-days |
|---------|-------------|
| M0      | 1–2         |
| M1      | 3–5         |
| M2      | 4–6         |
| M3      | 2–3         |
| **v1 shippable (M0–M3)** | **10–16** |
| M4 (per country) | 5–7 |
| M5      | 4–6         |

---

## 10. Files added / touched

New files (indicative):

- `docs/CATALOGUE-DATA.md` — mapping tables and refresh procedure.
- `THIRD-PARTY-NOTICES.md` — data-source attributions.
- `src/MedReminder.Domain/Catalogue/CountryCode.cs`
- `src/MedReminder.Domain/Catalogue/AtcCode.cs`
- `src/MedReminder.Domain/Catalogue/ReferenceMedicine.cs`
- `src/MedReminder.Domain/Catalogue/ReferenceActiveIngredient.cs`
- `src/MedReminder.Application/Catalogue/IReferenceCatalogueQueryService.cs`
- `src/MedReminder.Application/Catalogue/IReferenceCatalogueImporter.cs`
- `src/MedReminder.Application/Catalogue/SearchCatalogueUseCase.cs`
- `src/MedReminder.Application/Catalogue/LinkMedicineToReferenceUseCase.cs`
- `src/MedReminder.Infrastructure/Catalogue/SqliteReferenceCatalogueQueryService.cs`
- `src/MedReminder.Infrastructure/Catalogue/CsvReferenceCatalogueImporter.cs`
- `src/MedReminder.Infrastructure/Catalogue/Parsers/AifaSnapshotParser.cs`
- `src/MedReminder.Infrastructure/Catalogue/Parsers/EmaArticle57Parser.cs`
- `src/MedReminder.Infrastructure/Catalogue/EmbeddedSnapshotProvider.cs`
- `src/MedReminder.Infrastructure/Assets/Catalogue/it/aifa-<yyyymm>.csv.gz`
- `src/MedReminder.Infrastructure/Assets/Catalogue/eu/ema-article57-<yyyymm>.csv.gz`
- `src/MedReminder.UI/Controls/MedicineAutocompleteBox.cs`
- `tests/MedReminder.Domain.Tests/Catalogue/CountryCodeTests.cs`
- `tests/MedReminder.Domain.Tests/Catalogue/AtcCodeTests.cs`
- `tests/MedReminder.Application.Tests/Catalogue/SearchCatalogueUseCaseTests.cs`
- `tests/MedReminder.Infrastructure.Tests/Catalogue/CsvReferenceCatalogueImporterTests.cs`
- `tests/MedReminder.Infrastructure.Tests/Catalogue/SqliteReferenceCatalogueQueryServiceTests.cs`
- `tests/fixtures/catalogue/aifa-confezioni-sample.csv` — added
  by M0 as part of this design PR.
- `tests/fixtures/catalogue/aifa-pa-sample.csv` — added by M0.
- `tests/fixtures/catalogue/aifa-atc-sample.csv` — added by M0.
- `tests/fixtures/catalogue/ema-article57-sample.csv` — added in
  the M3 spike.

Touched files (indicative):

- `src/MedReminder.Domain/Medicines/Medicine.cs` — three optional
  fields.
- `src/MedReminder.Infrastructure/Persistence/MedReminderDbContext.cs`
  — the additive DDL step.
- `src/MedReminder.UI/Forms/MedicineEditForm.cs` — two autocomplete
  boxes.
- `src/MedReminder.UI/Forms/SettingsForm.cs` — reference country
  dropdown.
- `assets/localization/strings.{de,en,es,fr,it}.json` — new keys.
- `docs/ANALYSIS.md` §2.8 — reference to the new migration step.
- `docs/USER_GUIDE.{de,en,es,fr,it}.md` — autocomplete section.
- `CHANGE_LOG.md` — per PR.

---

## 11. Non-goals recap

- No clinical decision support.
- No live API calls at reminder time.
- No commercial database dependency.
- No auto-updater in v1 (deferred to optional M5).

---

## 12. Decisions

Six of the seven points below have been resolved. Point 1 is an
engineering discovery output of M0 rather than a product decision;
point 7 is a new sub-decision that emerged from the confirmed M4
target set (§3.5) and is deferred until M4 planning starts.

1. **AIFA dataset selection.**
   *Status:* **Resolved by the M0 spike (§3.1).** Source is
   `confezioni_fornitura.csv` joined with `PA_confezioni.csv` on
   `CODICE_AIC`. `atc.csv` is not needed for M1 because
   `confezioni_fornitura` already carries `CODICE_ATC` for every
   row. Full field mapping is recorded in the M0 comment on
   issue [#9](https://github.com/vger70/MedReminder/issues/9).

2. **AIFA snapshot cadence.**
   *Status:* **Confirmed — monthly, aligned to a MedReminder app
   release.** Data stays within a 30–60 day freshness window and
   releases remain predictable.

3. **EU / IT dedup.** Whether to deduplicate an EU-centralised
   product when it also appears as a separately-listed AIFA row.
   *Status:* **Confirmed — do not deduplicate.** Both rows remain
   visible; no AIC ↔ EMA product-number mapping is required.
   §3.4 M3 is aligned.

4. **Second country in M4 and target set.**
   *Status:* **Confirmed — target set is ES, FR, UK, DE.**
   Specific ordering deferred until M2 is live; ES and FR are the
   natural first candidates given their open-data maturity. §3.5
   M4 is aligned.

5. **Country switching UX.** Does the user pick exactly one
   country (plus `EU`), or can they enable several national
   catalogues simultaneously?
   *Status:* **Confirmed — one national country + `EU`.** Settings
   exposes a single dropdown; the query always unions with `EU`
   unless the selected country is flagged otherwise (see point 7).

6. **Marketing status filter.**
   *Status:* **Confirmed — show all rows, badge withdrawn ones.**
   The autocomplete does not filter by `marketing_status`;
   `MedicineAutocompleteBox` renders a visual marker on rows whose
   `marketing_status` indicates the product is no longer
   commercialised.

7. **Per-country EU coverage flag.** New sub-decision surfaced by
   §3.5 M4. Most target countries (IT, ES, FR, DE) are EU / EEA
   members whose users benefit from seeing EMA `EU` centralised
   rows alongside their national catalogue. UK left the EU and
   EMA: for the Great Britain market, EMA `EU` rows are no longer
   valid, so a UK user should see only MHRA rows. This implies a
   per-country `IncludesEuCentralised` flag on the country profile
   (default `true`; explicitly `false` for `UK`). Where the flag
   lives — a small `country_profiles` table populated by the
   importer, or a static lookup shipped with the code — is to be
   decided when the UK importer is scheduled.

Once point 1 is answered by M0 and point 7 is decided at M4
planning time, no product decisions remain outstanding for the
v1 scope (M0–M3).
