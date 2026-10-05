# ANALYSIS — US (FDA) and UK (MHRA, NHS dm+d) reference catalogues

Design document, written before implementation. It evaluates four
sources proposed by the product owner on 2026-10-05 and maps them onto
the existing catalogue pipeline (embedded snapshot, remote feed on the
`feeds` branch, per-country parser):

| # | Source | Proposed for |
|---|--------|--------------|
| S1 | FDA Orange Book data files (`products.txt`, `patent.txt`, `exclusivity.txt`) | US |
| S2 | FDA National Drug Code (NDC) Directory (FDA text files or openFDA bulk JSON) | US |
| S3 | MHRA "Category lists of products" (Windsor Framework) | UK |
| S4 | NHSBSA dm+d (Dictionary of Medicines and Devices), via TRUD | UK |

Tags follow the sibling documents: `[VERIFIED]` (checked against the
tree at `c6f1f58`), `[SEARCH]` (found through a web search on
2026-10-05, page not fetched: this session's egress proxy blocks
`www.fda.gov`, `open.fda.gov`, `www.accessdata.fda.gov`, `www.gov.uk`,
`isd.digital.nhs.uk` and `www.nhsbsa.nhs.uk`), `[OWNER]` (supplied by
the product owner), `[INFERRED]`, `[UNCERTAIN]`. Field lists marked
`[UNCERTAIN]` come from prior knowledge of the formats and must be
confirmed on a GitHub runner before any parser is written (§8, step 0).

---

## 1. Summary

- **US: use the NDC Directory (S2) as the catalogue, not the Orange
  Book (S1).** The Orange Book covers only drugs approved under an NDA
  or ANDA, keyed on application and product number, with no package
  code. The NDC Directory lists every marketed listing, prescription
  and OTC, down to the package NDC printed (as a GTIN) on the box. The
  Orange Book is useful only for its therapeutic-equivalence (TE)
  codes, the US counterpart of the Italian equivalents list
  (`ANALYSIS-IT-EQUIVALENTS-AND-INFO-LINK.md`); patents and
  exclusivity have no use in a reminder app.
- **UK: use dm+d (S4), not the MHRA category lists (S3).** The
  category lists exist to assign each product a Windsor Framework
  category (1 or 2); they are a regulatory classification, not a
  product dictionary [SEARCH; content to verify]. dm+d is the NHS
  reference for every prescribable medicine and pack, released weekly,
  with GTIN mapping.
- **dm+d licence: the project's earlier conclusion needs revision.**
  `ANALYSIS-DRUG-CATALOGUE.md` §3.5 and `CATALOGUE-DATA.md` §7 state
  that dm+d "does not allow silent redistribution inside a third-party
  product binary". Search results on 2026-10-05 indicate that the
  NHSBSA dm+d release (TRUD item 24) is published under the Open
  Government Licence v3.0 [SEARCH]. If confirmed on the TRUD licence
  page, the UK gate of `CATALOGUE-DATA.md` §7 point 2 is met for dm+d,
  subject to two open points: the TRUD account terms (download is
  behind a personal account and API key) and SNOMED CT (§4.4).
- **Blocking gaps in the current client** (§5): US is not flagged as
  outside EMA coverage; the reference-country dropdown offers only
  countries already in the local catalogue; the barcode lookup matches
  national codes only, so a US or UK GTIN scan finds nothing.
- **No ATC for either country.** Neither the NDC Directory nor the
  OGL part of dm+d carries ATC codes; the ATC mapping in the UK Drug
  bonus files is under the SNOMED CT affiliate licence [SEARCH].
  Features keyed on ATC stay Italy/EU/ES/FR only.

---

## 2. Current state [VERIFIED]

| Area | What exists | Impact on US/UK |
|------|-------------|-----------------|
| Parsers | `IReferenceSnapshotParser` per country (`AifaSnapshotParser`, `EmaEparParser`, `AempsCimaParser`, `AnsmBdpmParser`), yielding `ReferenceMedicineRow` (country, national code, name, form, dosage, MAH, status, dispensing regime, leaflet/SPC links, ingredients with optional ATC) | Two new parsers; the row shape fits both sources (§3.3, §4.3) |
| Schema | `reference_medicines` unique on `(country, national_code)`; no GTIN column (`CatalogueSchema.cs`) | Barcode support needs a GTIN rule (US) or a GTIN table (UK), §5.3 |
| Feeds | `CatalogueFeedDescriptor.All = [IT, EU, ES, FR]`; `CatalogueFeedSelection.Select` always adds EU to the reference country | Two descriptors; EU must not be fetched for US/GB (§5.1) |
| Country profile | `StaticCountryProfileProvider.NonEuCovered = { "GB", "UK" }` | `US` missing: a US user would see EMA rows (§5.1) |
| Embedded import | `CatalogueRefreshHostedService.ImportOrder = { IT, EU, ES, FR }` | Decide whether US/GB ship embedded (§6, D3) |
| Settings | `PopulateReferenceCountryCombo` offers `IT`, `EU` plus countries present in the local catalogue | Without an embedded snapshot, US/GB can never be selected (§5.2) |
| Barcodes | `BarcodeParser`: GS1 DataMatrix → GTIN, Code 32 → AIC, EAN-13 → GTIN; `RestockByScanQuery.FindByNationalCodeAsync` matches `Medicine.NationalCode` | UPC-A (12 digits, US) not recognised as such; no GTIN → catalogue lookup (§5.3) |
| Info links | `MedicineInfoLink` builds Codifa URLs from a valid AIC | US: DailyMed by SPL set id is possible (§3.5); UK: none planned |
| Dated lists | Shortages, equivalents, regional services are Italy-only (`CatalogueFeedSelection.IsItaly`) | Orange Book TE codes would be a new dated list (§3.4) |

---

## 3. United States

### 3.1 S1 — Orange Book

- **Content** [SEARCH]: one ZIP, three ASCII files, `~` delimited:
  `products.txt` (ingredient, dosage form and route, trade name,
  applicant, strength, application type N/A, application number,
  product number, TE code, approval date, RLD/RS flags, marketing
  type Rx/OTC/DISCN, applicant full name [UNCERTAIN on exact column
  set]), `patent.txt`, `exclusivity.txt`. Updated monthly.
- **Coverage**: NDA and ANDA products only. OTC monograph drugs (most
  analgesics, antacids, many OTC packs bought in a US pharmacy) and
  unapproved marketed drugs are not in it [INFERRED from the scope of
  the publication, "Approved Drug Products with Therapeutic
  Equivalence Evaluations"].
- **Identifier**: application number + product number. No NDC, no
  package level. It cannot be matched to a scanned box.
- **Licence** [OWNER]: US Government work, no copyright in the US.
  Outside the US the status of US federal works is not uniform across
  jurisdictions [UNCERTAIN]; no restriction is known to be enforced.
  Attribution and the FDA logo rule (§3.6) apply as good practice.
- **Verdict**: not a catalogue source. Candidate for a later, optional
  "US equivalents" list (§3.4).

### 3.2 S2 — NDC Directory

Two distributions of the same data:

| Distribution | URL | Format | Licence |
|--------------|-----|--------|---------|
| FDA text files | `https://www.accessdata.fda.gov/cder/ndctext.zip` [SEARCH] | `product.txt`, `package.txt`, tab-delimited [UNCERTAIN on delimiter] | US Government work [OWNER] |
| openFDA bulk | listed in `https://api.fda.gov/download.json`, endpoint `drug/ndc`, zipped JSON, about 27 MB [SEARCH] | JSON, one object per product with nested `packaging[]` and an `openfda` block | CC0 1.0 "unless otherwise noted" (`open.fda.gov/license`) [SEARCH] |

- **Update frequency**: daily [SEARCH]. The feed stays weekly like the
  others (days 2, 9, 16, 23); a daily cadence brings nothing to a
  reminder app.
- **Recommendation: openFDA bulk JSON.** Explicit CC0 worldwide
  waiver, which removes the "US work abroad" doubt of §3.1; nested
  packages avoid a join; `openfda.spl_set_id` gives the DailyMed link
  (§3.5). Fallback: `ndctext.zip`, same fields, join on `PRODUCTID`.
  Decision D1.
- **Fields used** [UNCERTAIN on exact names; openFDA names given]:

  | `ReferenceMedicineRow` | openFDA NDC field |
  |------------------------|-------------------|
  | `NationalCode` | `packaging[].package_ndc` (package level, see below) |
  | `CommercialName` | `brand_name` (+ `brand_name_suffix`); fallback `generic_name` |
  | `PharmaceuticalForm` | `dosage_form` |
  | `Dosage` | `active_ingredients[].strength`, joined |
  | `MarketingAuthorisationHolder` | `labeler_name` |
  | `MarketingStatus` | `marketing_category` + `packaging[].marketing_end_date` (ended → withdrawn badge) |
  | `DispensingRegime` | `product_type` (`HUMAN PRESCRIPTION DRUG` / `HUMAN OTC DRUG`) + `dea_schedule` |
  | `LinkLeaflet` | DailyMed URL from `openfda.spl_set_id` (§3.5) |
  | ingredients | `active_ingredients[].name`, ATC `null` |

- **Granularity: one row per package NDC**, as Italy (one row per AIC
  package). It is what a box carries and what the barcode resolves to
  (§5.3). The autocomplete shows more near-duplicates than with a
  product-level key; the same is true today for Italy.
- **NDC normalisation.** NDCs are 10 digits in three segments
  (4-4-2, 5-3-2, 5-4-1). Store the hyphenated form as published (for
  display) and match barcodes on the 10 digits without hyphens. The
  script must fail if two package NDCs collapse to the same 10 digits
  [UNCERTAIN whether FDA's labeler-code allocation already excludes
  it; checking is cheap].
- **Filters**: keep `HUMAN PRESCRIPTION DRUG` and `HUMAN OTC DRUG`;
  drop bulk ingredients, vaccines only if the owner wants (D2),
  unfinished drugs, and packages flagged excluded
  (`ndc_exclude_flag` in the text files) [UNCERTAIN].
- **Size** [UNCERTAIN]: on the order of 100 000 package rows. The
  script emits a compact TSV; the archive should be comparable to the
  Italian one (about 5 MB).

### 3.3 Parser and feed

- `OpenFdaNdcParser` (`SupportedCountries = { US }`) reads one TSV
  produced by the feed script, not the raw JSON: the transformation and
  the shape checks happen on the runner (as `ema.py` turns XLSX into
  CSV), the client stays small. Archive `fda-ndc-<yyyymm>.zip`, entry
  `fda-ndc.tsv`.
- `scripts/feeds/fda_ndc.py`, workflow `download_fda_ndc.yaml`, on the
  shared `common.py`: download, JSON → TSV, validation (required
  columns, rows ≥ an absolute floor set from the first runner
  measurement, ≥ 90% of the previous run), publish `data/us/`.

### 3.4 Optional: therapeutic equivalence from the Orange Book

The Orange Book `products.txt` gives, per approved product, a TE code
(for example `AB`, meaning therapeutically equivalent to the reference
listed drug). A list "products with the same ingredient, form, route,
strength and an `A*` TE code" is the US analogue of the AIFA
transparency list. Mapping it to NDC rows needs the application number,
which the NDC Directory carries (`application_number`, for example
`NDA012345` / `ANDA071234`) [UNCERTAIN on format], so the join is
feasible on the runner.

Not proposed for the first phase: it reverses the "no product is
suggested" stance for a second country, and the Italian feature was
accepted only after an explicit owner decision
(`ANALYSIS-IT-EQUIVALENTS-AND-INFO-LINK.md` §2.6). Decision D5.

### 3.5 Information link

DailyMed serves the US label by SPL set id
(`https://dailymed.nlm.nih.gov/dailymed/drugInfo.cfm?setid=<uuid>`)
[UNCERTAIN on the exact path; to verify]. The parser fills
`LinkLeaflet` with it only when the set id parses as a GUID, so nothing
read from data reaches the shell (same rule as `MedicineInfoLink`),
and `MedicineEditDialog`'s host allow-list gains `dailymed.nlm.nih.gov`.

### 3.6 Attribution

`THIRD-PARTY-NOTICES.md` and the About dialog: "Contains data from the
U.S. Food and Drug Administration (openFDA), CC0 1.0. Not endorsed by
FDA." No FDA logo anywhere; the user guides must not suggest that FDA
approves or reviews MedReminder [OWNER: FDA terms].

---

## 4. United Kingdom

### 4.1 S3 — MHRA category lists

- **What they are** [SEARCH]: lists published by MHRA from 20
  December 2024 assigning each product to Category 1 (formerly EU
  centralised products and their generics, hybrids, biosimilars) or
  Category 2 (all others), under the Windsor Framework. From 1 January
  2025 both categories are licensed UK-wide by MHRA.
- **Premise correction.** The owner's note describes them as "all
  medicines authorised in the UK, updated". Their purpose is the
  category assignment; whether they cover every licensed product, how
  often they are updated and which columns they carry (product name,
  PL number, holder, active substance) could not be checked from this
  session [UNCERTAIN]. Even if complete, they would lack form,
  strength, pack and GTIN, which dm+d has.
- **Licence**: OGL v3.0 for GOV.UK content [OWNER], attribution
  required.
- **Verdict**: not a catalogue source. Possible secondary use: a PL
  number → category lookup, which has no user-facing value in a
  reminder app [INFERRED]. Not proposed.

### 4.2 S4 — NHSBSA dm+d

- **Content** [SEARCH]: weekly XML release (every Monday) of five
  linked classes — VTM (therapeutic moiety), VMP (generic product),
  AMP (branded product), VMPP (generic pack), AMPP (branded pack, with
  price) — plus a GTIN file mapping GTINs to AMPPs, and lookup tables.
  Devices and appliances are in the same dictionary.
- **Distribution**: TRUD, item "NHSBSA dm+d". Download needs a free
  TRUD account, subscription to the item, and the account's API key
  for automation [SEARCH].
- **Licence**: Open Government Licence v3.0 [SEARCH], which permits
  commercial reuse and redistribution with attribution ("Contains
  public sector information licensed under the Open Government Licence
  v3.0"). To confirm by reading the licence attached to item 24 on
  TRUD, not the test-files item. Separate items (UK Drug bonus files,
  SNOMED CT UK Drug Extension) are under the SNOMED CT UK Affiliate
  Licence [SEARCH] and are **not** used.

### 4.3 Mapping

One row per AMPP (branded pack), the level that carries the GTIN and
the legal category:

| `ReferenceMedicineRow` | dm+d [UNCERTAIN on exact element names] |
|------------------------|------------------------------------------|
| `NationalCode` | AMPP id (`APPID`) — see §4.4 |
| `CommercialName` | AMPP description (`NM`), or AMP name + pack size |
| `PharmaceuticalForm` | VMP form (`DFORMCD` → lookup) |
| `Dosage` | VMP ingredient strengths (`VPI`) |
| `MarketingAuthorisationHolder` | AMP supplier (`SUPPCD` → lookup) |
| `MarketingStatus` | AMPP discontinued flag / AMP availability restriction; invalid records dropped |
| `DispensingRegime` | AMPP legal category (POM, P, GSL; controlled drug from VMP) |
| ingredients | VMP ingredients (`VPI` → `ING`), ATC `null` |

Filters: drop records flagged invalid; drop devices and appliances
(no VMP ingredients / device flag) [UNCERTAIN on the flag];
`CountryCode` `GB` (already in `NonEuCovered`). Northern Ireland: from
2025 licences are UK-wide, so one `GB` catalogue serves the whole UK
[INFERRED from §4.1].

### 4.4 Open points before any code

1. **SNOMED CT identifiers.** dm+d ids (VTM/VMP/AMP/VMPP/AMPP) are
   SNOMED CT UK Drug Extension concept ids [UNCERTAIN; widely stated,
   not verified this session]. MedReminder is distributed worldwide
   (Microsoft Store, GitHub). Whether shipping those ids to users
   outside the UK, as opaque keys, falls under the SNOMED International
   affiliate rules is a legal question, not an engineering one. Two
   ways to stay clear of it, to choose in D6:
   - fetch the GB feed only when the reference country is GB (already
     the rule of `CatalogueFeedSelection`) and never embed it, so only
     UK-configured installs download it;
   - use the first GTIN of the AMPP as `NationalCode` and drop the
     dm+d ids from the published archive (AMPPs without GTIN would then
     need a synthetic key, or be dropped) [INFERRED].
   The owner should ask NHS England (`information.standards@nhs.net`,
   the contact listed for dm+d [SEARCH]) before the first release.
2. **TRUD account terms.** The OGL governs the data; the TRUD service
   terms govern the account. Publishing a derived archive on the public
   `feeds` branch is redistribution: allowed by the OGL, to check
   against the TRUD terms [UNCERTAIN].
3. **Secret handling.** `TRUD_API_KEY` as a repository secret, used
   only by `download_dmd.yaml`; never logged, never written under
   `data/`. A forked PR cannot read it, so the workflow must not run on
   `pull_request` from forks.
4. **Size** [UNCERTAIN]: the full XML release is large (tens of MB
   compressed). The script must stream-parse (`xml.etree.iterparse`)
   and publish only the TSV.

### 4.5 Parser and feed

`DmdParser` (`SupportedCountries = { GB }`) reads `dmd.tsv` (and
`dmd-gtin.tsv`, §5.3) from `nhs-dmd-<yyyymm>.zip`.
`scripts/feeds/dmd.py`, workflow `download_dmd.yaml`: TRUD API → latest
release ZIP → iterparse VMP/AMP/AMPP/lookups/GTIN → TSV → validation →
`data/gb/`.

---

## 5. Client changes

### 5.1 Country profile and feed selection

- Add `US` to `StaticCountryProfileProvider.NonEuCovered`; the set
  becomes "not covered by EMA centralised authorisations"
  (`US`, `GB`, `UK`). Without it a US user would see EMA rows.
- `CatalogueFeedSelection.Select`: add EU only when
  `ICountryProfileProvider.GetProfile(reference).IncludesEuCentralised`
  (today EU is always added). Saves one download per start for US/GB
  users.
- `CatalogueFeedDescriptor`: `UnitedStates` (`fda-ndc`,
  `["fda-ndc.tsv"]`, 256 MiB) and `UnitedKingdom` (`nhs-dmd`,
  `["dmd.tsv"]`, 256 MiB); `All` gains both, after FR.
- `appsettings.json`: `Catalogue:RemoteFeed:Feeds:US` and `:GB` with
  `Enabled` and `MaxDownloadBytes`.

### 5.2 Reference-country dropdown

`PopulateReferenceCountryCombo` offers `IT`, `EU` and the countries
already in the local catalogue. A country served only by a remote feed
never appears, so it can never be selected and its feed never
downloads. Fix: seed the list from `CatalogueFeedDescriptor.All` as
well. On selection of a country with an empty catalogue, the next
start (or an immediate refresh, D4) downloads it; the autocomplete
shows nothing until then, which the Settings page should state (new
UI string, in all five `strings.<lang>.json`).

### 5.3 Barcodes

- **US.** A US drug package carries a UPC-A or a GS1 GTIN that embeds
  the 10-digit NDC (GS1 US prefix 03: GTIN-12 = `3` + NDC10 + check
  digit; GTIN-14 = `003` + NDC10 + check digit) [INFERRED from the GS1
  US / FDA NDC convention; to verify against FDA guidance]. Unlike the
  AIC (`ANALYSIS-A2-BARCODE-SCAN.md` §2.3), the NDC can therefore be
  derived from the GTIN. `BarcodeParser` gains a UPC-A path (12 digits)
  and a rule: GTIN with prefix `003` (or UPC-A starting with `3`) →
  candidate NDC10, looked up only when the reference country is US.
  Many scanners send UPC-A as EAN-13 with a leading `0`, which the
  same rule covers.
- **UK.** GTINs are arbitrary; the mapping is dm+d's GTIN file. New
  table `reference_gtins (country, gtin, medicine_id)`, created by an
  idempotent boot patch in `CatalogueSchema` (CLAUDE.md §7), filled by
  the importer from `dmd-gtin.tsv`. The scan dialog looks up a GTIN
  there when national-code lookup fails. Phase 3, after the GB
  catalogue itself.

### 5.4 What stays Italy-only

Shortages, equivalents, regional services, the Codifa link and the
AIC check digit. `CatalogueFeedSelection.IsItaly` already gates them.

---

## 6. Decisions for the product owner

| # | Decision | Recommendation |
|---|----------|----------------|
| D1 | US distribution: openFDA bulk JSON or `ndctext.zip` | openFDA (explicit CC0, nested packages, SPL set id) |
| D2 | US scope: Rx + OTC; vaccines, kits, bulk ingredients | Rx + OTC finished products; vaccines kept; bulk and unfinished dropped |
| D3 | Embedded snapshots for US/GB | No: remote feed only, keeps the installer size and the SNOMED exposure down; fix §5.2 instead |
| D4 | Refresh on country change: at next start or immediately | Immediately, through the existing refresher, off the UI thread |
| D5 | US therapeutic equivalence from the Orange Book | Not now; revisit after US usage is known |
| D6 | dm+d key: AMPP id or GTIN | Decide after the NHS England answer (§4.4 point 1) |
| D7 | Proceed with GB at all | Only after the TRUD licence page for item 24 and the TRUD terms are read and recorded in `CATALOGUE-DATA.md` §7 |

---

## 7. Risks

| Risk | Mitigation |
|------|------------|
| openFDA or TRUD changes format | Script validates required fields and row floors; nothing is published on failure (existing `common.py` behaviour) |
| TRUD key revoked or account expired | GB feed workflow fails; clients keep the last import; failure visible in Actions |
| Users read US/GB data as clinical advice | Same wording as today: reference data for naming and stock, no clinical role; FDA non-endorsement sentence (§3.6) |
| Name collisions in the autocomplete (US package-level rows) | Same as Italy today; group by product in the UI later if needed |
| SNOMED CT licensing outside the UK | D3 + D6 + written answer from NHS England before release |
| US labels in English only | Expected; UI strings are already localised |

---

## 8. Phasing

0. **Verification on a GitHub runner** (this session cannot reach the
   hosts): fetch the openFDA `download.json`, one NDC bulk file and
   `ndctext.zip`; record field names, row counts, sizes. Read and save
   the licence text of openFDA and of TRUD item 24, and the GOV.UK
   category list page (columns, date). Update the `[UNCERTAIN]` marks
   of this document.
1. **US catalogue**: `fda_ndc.py` + workflow, `OpenFdaNdcParser`,
   descriptor, `NonEuCovered` + `Select` change (§5.1), dropdown fix
   (§5.2), notices, user guides (five languages), fixture of about 200
   rows, tests.
2. **US barcodes**: UPC-A and NDC-from-GTIN rule (§5.3).
3. **GB catalogue**, gated by D6/D7: `dmd.py` + workflow with
   `TRUD_API_KEY`, `DmdParser`, descriptor, notices, guides,
   `CATALOGUE-DATA.md` §7 rewritten as a refresh procedure.
4. **GB barcodes**: `reference_gtins` table and lookup.
5. Optional (D5): Orange Book TE list for the US.

Each phase is one PR; phases 1 and 3 are independent.

---

## 9. Sources

- FDA, Orange Book Data Files: <https://www.fda.gov/drugs/drug-approvals-and-databases/orange-book-data-files>;
  download instructions: <https://www.accessdata.fda.gov/drugsatfda_docs/ob/OrangeBookDataFileDownloadInstructions.pdf>
- FDA, National Drug Code Directory: <https://www.fda.gov/drugs/drug-approvals-and-databases/national-drug-code-directory>
- openFDA, NDC data and downloads: <https://open.fda.gov/data/ndc/>, <https://open.fda.gov/data/downloads/>
- openFDA, licence and terms: <https://open.fda.gov/license>, <https://open.fda.gov/terms>
- GOV.UK, Category lists following implementation of the Windsor Framework: <https://www.gov.uk/government/publications/category-lists-following-implementation-of-the-windsor-framework>
- MHRA, Windsor Framework explainer (PDF): <https://assets.publishing.service.gov.uk/media/673cc5cf7e8a3c98a090fe94/MHRA_Windsor_Framework_Explainer.pdf>
- NHSBSA, Release of dm+d files: <https://www.nhsbsa.nhs.uk/pharmacies-gp-practices-and-appliance-contractors/nhs-dictionary-medicines-and-devices-dmd/release-dmd-files>
- TRUD, NHSBSA dm+d releases: <https://isd.digital.nhs.uk/trud/user/guest/group/0/pack/6/subpack/24/releases>
- TRUD, UK Drug bonus files licence: <https://isd.digital.nhs.uk/trud/users/guest/filters/0/categories/8/items/639/licences>
- dm+d structure and TRUD API usage (open-source implementation): <https://github.com/wardle/dmd>
