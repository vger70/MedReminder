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

Tags:

- `[VERIFIED]` checked against the tree at `c6f1f58`, or against
  public source code that consumes the data: FDA's own openFDA
  pipeline (`FDA/openfda`, `openfda/ndc/pipeline.py`,
  `schemas/ndc_mapping.json`) and the dm+d importer `wardle/dmd`
  (`src/com/eldrix/dmd/import.clj`, `download.clj`, README) with its
  TRUD client `wardle/trud`. Read on 2026-10-05 (§11).
- `[SEARCH]` found through a web search on 2026-10-05, page not
  fetched: this session's egress proxy blocks `www.fda.gov`,
  `open.fda.gov`, `www.accessdata.fda.gov`, `www.gov.uk`,
  `isd.digital.nhs.uk`, `www.nhsbsa.nhs.uk` and `dailymed.nlm.nih.gov`.
- `[OWNER]` supplied by the product owner; `[INFERRED]`;
  `[UNCERTAIN]`.

---

## 1. Summary

- **US: use the NDC Directory (S2) as the catalogue, not the Orange
  Book (S1).** The Orange Book lists only drugs approved under an NDA
  or ANDA, keyed on application and product number, with no package
  code. It leaves out OTC monograph drugs and, since March 2020,
  biologics such as insulins, which moved to the Purple Book [INFERRED
  from the scope of the publication and the 2020 BLA transition; not
  re-checked this session]. The NDC Directory lists every marketed
  listing, prescription and OTC, including biologics, down to the
  package NDC printed in the barcode. The Orange Book is useful only
  for its therapeutic-equivalence (TE) codes, the US counterpart of
  the Italian equivalents list; patents and exclusivity have no use in
  a reminder app.
- **UK: use dm+d (S4), not the MHRA category lists (S3).** The
  category lists assign each licensed product to Windsor Framework
  Category 1 or 2. Together they may cover every licensed product
  [INFERRED], but they are a regulatory classification (PL number,
  name, category) without form, strength, pack or GTIN [SEARCH,
  columns not verified]. dm+d has all of them, weekly.
- **dm+d licence: the project's earlier conclusion is wrong.**
  `ANALYSIS-DRUG-CATALOGUE.md` §3.5 and `CATALOGUE-DATA.md` §7 state
  that dm+d "does not allow silent redistribution inside a third-party
  product binary". The NHSBSA dm+d release (TRUD item 24) is published
  under the Open Government Licence v3.0 ([SEARCH], and stated in the
  `wardle/dmd` README [VERIFIED]); so is the supplementary item 25
  that carries the ATC mapping [SEARCH]. Open points: the TRUD account
  terms and the SNOMED CT nature of dm+d identifiers (§4.4).
- **ATC: available for GB, not for US.** An earlier draft of this
  document said dm+d has no ATC under an open licence. Wrong: the
  supplementary item 25 maps VMPs to ATC and BNF codes under OGL
  [SEARCH]; NHSBSA notes it "is not an officially endorsed dm+d
  product" and covers products prescribed in primary care [SEARCH].
  The NDC Directory has no ATC; it carries FDA pharmacologic classes
  (`pharm_class`) instead [VERIFIED].
- **The NDC changes format on 2033-03-07**: every NDC becomes 12
  digits, 6-4-2, existing 10-digit NDCs converted by left-padding
  with zeros; 10-digit labelling tolerated until March 2036 (final
  rule published 2026-03-05) [SEARCH]. The US key and barcode rule
  must be designed for both formats now (§3.2, §5.3).
- **Client gaps to close first** (§5): `US` not flagged as outside
  EMA coverage; EU always fetched with the reference country; the
  reference-country dropdown offers only countries already in the
  local catalogue; no GTIN → catalogue lookup; the withdrawn badge
  recognises Italian wording only.

---

## 2. Current state [VERIFIED]

| Area | What exists | Impact on US/UK |
|------|-------------|-----------------|
| Parsers | `IReferenceSnapshotParser` per country, yielding `ReferenceMedicineRow` (country, national code, name, form, dosage, MAH, status, dispensing regime, leaflet/SPC links, ingredients with optional ATC) | Two new parsers; the row shape fits both sources (§3.3, §4.3) |
| Schema | `reference_medicines` unique on `(country, national_code)`; no GTIN column (`CatalogueSchema.cs`) | Barcode support needs a GTIN rule (US) or a GTIN table (UK), §5.3 |
| Feeds | `CatalogueFeedDescriptor.All = [IT, EU, ES, FR]`; `CatalogueFeedSelection.Select` keeps every supranational feed, so EU is always fetched | Two descriptors; EU must not be fetched for US/GB (§5.1) |
| Country profile | `StaticCountryProfileProvider.NonEuCovered = { "GB", "UK" }` | `US` missing: a US user would see EMA rows (§5.1) |
| Embedded import | `CatalogueRefreshHostedService.ImportOrder = { IT, EU, ES, FR }` | Decide whether US/GB ship embedded (§6, D3) |
| Settings | `PopulateReferenceCountryCombo` offers `IT`, `EU` plus `ListAvailableCountriesAsync` (distinct `country` in `reference_medicines`) | Without an embedded snapshot, US/GB can never be selected (§5.2) |
| Withdrawn badge | `MedicineAutocompleteBox.WithdrawnMarkers = { "sospesa", "ritirat", "revocata" }` | US/GB statuses (English) never badged (§5.4) |
| Barcodes | `BarcodeParser`: GS1 DataMatrix (AI 01) → GTIN, Code 32 / 9 digits → AIC, EAN-13 → GTIN; a 12-digit UPC-A payload is `Unrecognized` | US UPC-A not read; no GTIN → catalogue lookup (§5.3) |
| Document links | `MedicineEditDialog.IsSafeAifaUrl` allows HTTPS on `aifa.gov.it` / `agenziafarmaco.gov.it` only | A DailyMed leaflet link needs the host added (§3.5) |
| Dated lists | Shortages, equivalents, regional services are Italy-only (`CatalogueFeedSelection.IsItaly`) | Orange Book TE codes would be a new dated list (§3.4) |

---

## 3. United States

### 3.1 S1 — Orange Book

- **Content** [SEARCH]: one ZIP, three ASCII files, `~` delimited,
  updated monthly. `products.txt` header starts
  `Ingredient~DF;Route~Trade_Name~Applicant~Strength~Appl_Type~Appl_No~Product_No~TE_Code~…`
  (the rest — approval date, RLD, RS, type Rx/OTC/DISCN, applicant
  full name — to confirm on a runner [UNCERTAIN]). Multiple
  ingredients are `;`-separated in one field.
- **Coverage**: NDA and ANDA products (§1). No package level.
- **Identifier**: application number + product number. It cannot be
  matched to a scanned box.
- **Licence** [OWNER]: US Government work, no copyright in the US.
  Outside the US the status of US federal works is not uniform
  [UNCERTAIN]. Not an issue if only the TE codes are ever used (§3.4).
- **Verdict**: not a catalogue source. Candidate for an optional
  "US equivalents" list (§3.4).

### 3.2 S2 — NDC Directory

Two distributions of the same data. openFDA builds its JSON from the
FDA text files [VERIFIED: `pipeline.py` downloads
`https://www.accessdata.fda.gov/cder/ndctext.zip` and
`.../ndc_unfinished.zip`]:

| Distribution | Format | Licence |
|--------------|--------|---------|
| `ndctext.zip` (finished drugs) | `product.txt` + `package.txt`, tab-delimited, UTF-8 (openFDA patches some invalid UTF-8 in `product.txt`) [VERIFIED] | US Government work [OWNER] |
| openFDA bulk, endpoint `drug/ndc`, listed in `https://api.fda.gov/download.json` | zipped JSON, about 27 MB [SEARCH]; one object per product, nested `packaging[]`, an `openfda` annotation block; finished and unfinished merged, flagged by `finished` [VERIFIED] | CC0 1.0 "unless otherwise noted", no exemption listed [VERIFIED: licence page supplied by the owner, §9.1] |

- **Update frequency**: daily [SEARCH]. The feed stays weekly like the
  others.
- **Recommendation: openFDA bulk JSON** (D1). Explicit worldwide CC0
  waiver, packages already nested, `openfda.spl_set_id` for the
  DailyMed link. Fallback: `ndctext.zip`, join on `PRODUCTID`.
- **Fields** [VERIFIED: `NDCProduct2JSONMapper`,
  `NDCPackage2JSONMapper`, `ndc_mapping.json`]:

  | `ReferenceMedicineRow` | openFDA field (FDA text column) |
  |------------------------|---------------------------------|
  | `NationalCode` | `packaging[].package_ndc` (`NDCPACKAGECODE`), normalised (below) |
  | `CommercialName` | `brand_name` (`PROPRIETARYNAME` + `PROPRIETARYNAMESUFFIX`, already joined by openFDA); fallback `generic_name` (`NONPROPRIETARYNAME`) |
  | `PharmaceuticalForm` | `dosage_form` (`DOSAGEFORMNAME`) |
  | `Dosage` | `active_ingredients[].strength` (numerator strength + unit, joined by openFDA) |
  | `MarketingAuthorisationHolder` | `labeler_name` (`LABELERNAME`) |
  | `MarketingStatus` | `marketing_category` (`MARKETINGCATEGORYNAME`); `marketing_end_date` on product or package → ended |
  | `DispensingRegime` | `product_type` (`PRODUCTTYPENAME`: prescription / OTC) + `dea_schedule` (`DEASCHEDULE`) |
  | `LinkLeaflet` | DailyMed URL from `openfda.spl_set_id` (§3.5) |
  | ingredients | `active_ingredients[].name` (`SUBSTANCENAME`), ATC `null` |

  Also available: `packaging[].description` (pack text, for example
  quantity and container), `packaging[].sample` (boolean),
  `application_number` (`APPLICATIONNUMBER`, the join key to the
  Orange Book), `pharm_class`, and `openfda.upc` [VERIFIED in the
  mapping; content and coverage not checked].
- **The `openfda` block is an annotation**: present only when openFDA
  matched the product to its SPL/RxNorm harmonisation
  (`annotate.py`) [VERIFIED]. A missing `spl_set_id` only means no
  leaflet link.
- **Filters**: `finished = true`; `product_type` prescription or OTC
  human drug (exact strings to record on the runner [UNCERTAIN]);
  drop `packaging[].sample = true`; vaccines kept (D2). An earlier
  draft mentioned an `NDC_EXCLUDE_FLAG`: openFDA does not map any such
  column [VERIFIED], so it is not used.
- **Granularity: one row per package NDC**, as Italy (one row per AIC
  package). It is what the box carries and what a barcode resolves to
  (§5.3).
- **NDC key.** Today NDCs are 10 digits in three segments (4-4-2,
  5-3-2, 5-4-1); from 2033-03-07 they are 12 digits (6-4-2), old ones
  converted by left-padding with zeros [SEARCH]. Store
  `NationalCode` in the canonical 12-digit 6-4-2 form (each segment
  left-padded), which is unique, stable across the 2033 change, and
  displayable; keep the published hyphenated form in the pack text.
  The script fails if two packages map to the same canonical key.
  The 10-digit unsegmented form needed by the barcode rule is derived
  at import (§5.3).
- **Size** [UNCERTAIN]: on the order of 100 000 package rows; the
  script emits a compact TSV, expected around the Italian archive's
  5 MB.

### 3.3 Parser and feed

- `OpenFdaNdcParser` (`SupportedCountries = { US }`) reads one TSV
  produced by the feed script, not the raw JSON: the transformation
  and shape checks run on the runner (as `ema.py` turns XLSX into
  CSV). Archive `fda-ndc-<yyyymm>.zip`, entry `fda-ndc.tsv`.
- `scripts/feeds/fda_ndc.py`, workflow `download_fda_ndc.yaml`, on
  `common.py`: download, JSON → TSV, validation (required fields, row
  floor from the first runner measurement, ≥ 90% of the previous run),
  publish `data/us/`.

### 3.4 Optional: therapeutic equivalence from the Orange Book

`products.txt` gives, per approved product, a TE code (for example
`AB`). A list "products with the same ingredient, form, route,
strength and an `A*` TE code" is the US analogue of the AIFA
transparency list. The join to NDC rows goes through the application
number, present on both sides (`Appl_Type` + `Appl_No` vs
`application_number`, for example `ANDA071234`) [VERIFIED on the NDC
side; Orange Book side per §3.1].

Not proposed for the first phase: it extends the product suggestion
stance to a second country, accepted for Italy only after an explicit
owner decision (`ANALYSIS-IT-EQUIVALENTS-AND-INFO-LINK.md` §2.6).
Decision D5.

### 3.5 Information link

DailyMed serves the label at
`https://dailymed.nlm.nih.gov/dailymed/drugInfo.cfm?setid=<SPL set id>`,
the set id being a UUID [SEARCH]. The parser fills `LinkLeaflet` only
when the set id parses as a GUID, so nothing read from data reaches the
shell (rule of `MedicineInfoLink`), and `IsSafeAifaUrl` (renamed, for
example `IsSafeDocumentUrl`) gains `dailymed.nlm.nih.gov`.

### 3.6 Attribution

`THIRD-PARTY-NOTICES.md` and the About dialog: "Contains data from the
U.S. Food and Drug Administration (openFDA), CC0 1.0. Not endorsed by
FDA." No FDA logo: FDA marks must not be used with third-party
products or in a way that implies endorsement [SEARCH]. User guides
must not suggest that FDA approves or reviews MedReminder.

---

## 4. United Kingdom

### 4.1 S3 — MHRA category lists

- **What they are** [SEARCH]: two lists ("Category 1 list of
  products", "Category 2 list of products") published from 20
  December 2024, last updated 2 March 2026, each update replacing the
  previous lists. Category 1: products formerly authorised through the
  EU centralised procedure and their generics, hybrids and
  biosimilars; Category 2: all others. From 1 January 2025 both are
  licensed UK-wide by MHRA.
- **Premise check.** The owner's note describes them as "all
  medicines authorised in the UK". Since every licensed product falls
  in one category, the union may indeed be complete [INFERRED]. The
  columns reported are PL number, product name and category [SEARCH,
  weak: from a search summary, not from the files]. The format (CSV or
  spreadsheet) could not be checked [UNCERTAIN].
- **Licence**: OGL v3.0 for GOV.UK content [OWNER].
- **Verdict**: not a catalogue source: no form, strength, pack,
  ingredients or GTIN, irregular updates, and no shared key with dm+d
  (dm+d carries no PL number [INFERRED from the AMP fields in §4.3]).
  Not proposed.

### 4.2 S4 — NHSBSA dm+d

- **Content** [VERIFIED: `import.clj`]: weekly XML release, files
  `f_<class><n>_<n><ddMMyy>.xml` for LOOKUP, INGREDIENT, VTM, VMP,
  AMP, VMPP, AMPP, GTIN; the GTIN file sits in a nested ZIP of the
  main release. Five classes: VTM (therapeutic moiety), VMP (generic
  product), AMP (branded product), VMPP (generic pack), AMPP (branded
  pack). GTIN → AMPP is one-to-one in practice (data-quality report in
  the `wardle/dmd` README) [VERIFIED]. Devices and appliances are in
  the same dictionary.
- **Supplementary release** (TRUD item 25) [VERIFIED: `download.clj`
  fetches items 24 and 25]: BNF and ATC mapping at VMP level (file
  type `BNF`), history, VTM ingredients. Licence OGL [SEARCH].
- **Distribution**: TRUD. Free account, subscription to the items,
  account API key. API:
  `https://isd.digital.nhs.uk/trud/api/v1/keys/<api key>/items/<item>/releases?latest`
  [VERIFIED: `wardle/trud`]. The key travels in the URL path, so the
  script must never print request URLs or exception messages that
  contain them.
- **Licence**: OGL v3.0 for items 24 and 25 [SEARCH; `wardle/dmd`
  README: "published by the NHS Business Services Authority under an
  Open Government Licence" VERIFIED]. To confirm by reading the
  licence page of each item on TRUD. The UK Drug bonus files and the
  SNOMED CT UK Drug Extension are under the SNOMED CT UK Affiliate
  Licence [SEARCH] and are **not** used.

### 4.3 Mapping

One row per AMPP (branded pack), the level that carries the GTIN, the
legal category and the discontinued flag. Element names
[VERIFIED: `import.clj`]; code values [UNCERTAIN unless stated]:

| `ReferenceMedicineRow` | dm+d |
|------------------------|------|
| `NationalCode` | AMPP `APPID` — see §4.4 |
| `CommercialName` | AMPP `NM` |
| `PharmaceuticalForm` | VMP drug form `FORMCD` → LOOKUP |
| `Dosage` | VMP ingredients `STRNT_NMRTR_VAL`/`STRNT_NMRTR_UOMCD` over `STRNT_DNMTR_VAL`/`STRNT_DNMTR_UOMCD` |
| `MarketingAuthorisationHolder` | AMP `SUPPCD` → LOOKUP |
| `MarketingStatus` | AMPP `DISCCD` (discontinued), AMP `AVAIL_RESTRICTCD`; records with `INVALID` dropped |
| `DispensingRegime` | AMPP `LEGAL_CATCD` → LOOKUP (POM, P, GSL…); VMP controlled-drug `CATCD` |
| ingredients | VMP ingredients `ISID` → INGREDIENT `NM`; ATC from item 25 (VMP level), else `null` |

Filters:

- `INVALID` records out.
- Devices and appliances out. `LIC_AUTHCD` cannot tell medicines
  from appliances (dm+d Implementation Guide) [SEARCH]; use instead
  "VMP has at least one ingredient and the AMPP has no
  `APPLIANCE_PACK_INFO`" [INFERRED; measure on a release].
- Unlicensed products (`LIC_AUTHCD` 0; 1–2 licensed, 3 unknown, 4
  traditional herbal [SEARCH]): kept, as specials are real prescribed
  items; decision D8.
- `CountryCode` `GB` (already in `NonEuCovered`). Northern Ireland:
  UK-wide licensing since 2025, so one `GB` catalogue serves the UK
  [INFERRED from §4.1].

### 4.4 Open points before any code

1. **SNOMED CT identifiers.** dm+d codes are SNOMED CT identifiers
   [VERIFIED: `wardle/dmd` README, "dm+d codes are actually SNOMED
   identifiers"]. MedReminder is distributed worldwide. Whether
   shipping those ids to users outside the UK, as opaque keys, falls
   under SNOMED International's affiliate rules is a legal question.
   Options (D6):
   - fetch the GB feed only when the reference country is GB (the rule
     of `CatalogueFeedSelection`) and never embed it, so only
     UK-configured installs download it;
   - use the AMPP's GTIN as `NationalCode` and drop dm+d ids from the
     published archive; AMPPs without a GTIN then need another key or
     are dropped [INFERRED].
   Ask NHS England (`information.standards@nhs.net`, the dm+d contact
   [SEARCH]) before the first release.
2. **TRUD account terms.** The OGL governs the data, the TRUD service
   terms the account. Publishing a derived archive on the public
   `feeds` branch is redistribution: allowed by the OGL, to check
   against the TRUD terms [UNCERTAIN].
3. **Secret handling.** `TRUD_API_KEY` as a repository secret, used
   only by `download_dmd.yaml`; masked by Actions in logs, but the
   script must still avoid echoing URLs (§4.2). Never on
   `pull_request` from forks.
4. **Size** [UNCERTAIN]: tens of MB compressed. Stream-parse
   (`xml.etree.ElementTree.iterparse`), publish only the TSV.

### 4.5 Parser and feed

`DmdParser` (`SupportedCountries = { GB }`) reads `dmd.tsv` (and
`dmd-gtin.tsv`, §5.3) from `nhs-dmd-<yyyymm>.zip`.
`scripts/feeds/dmd.py`, workflow `download_dmd.yaml`: TRUD API → latest
releases of items 24 and 25 → iterparse → TSV → validation →
`data/gb/`.

---

## 5. Client changes

### 5.1 Country profile and feed selection

- Add `US` to `StaticCountryProfileProvider.NonEuCovered`; the set
  means "not covered by EMA centralised authorisations".
- `CatalogueFeedSelection.Select`: keep EU only when
  `GetProfile(reference).IncludesEuCentralised`. Saves one download
  per start for US/GB users.
- `CatalogueFeedDescriptor`: `UnitedStates` (`fda-ndc`,
  `["fda-ndc.tsv"]`) and `UnitedKingdom` (`nhs-dmd`, `["dmd.tsv"]`),
  uncompressed cap 256 MiB each; `All` gains both, after FR.
- `appsettings.json`: `Catalogue:RemoteFeed:Feeds:US` and `:GB`.
- `Ui.SettingsDialog.Tooltip.CheckUpdates` says the app downloads
  "the medicine catalogue of the reference country and the EU one";
  with US/GB that is no longer always true. Reword in all five
  `strings.<lang>.json` [VERIFIED: current English text].

### 5.2 Reference-country dropdown

Seed `PopulateReferenceCountryCombo` with `CatalogueFeedDescriptor.All`
as well, so a feed-only country can be chosen. Until its first
download the autocomplete is empty; Settings states it (new UI string
in all five `strings.<lang>.json`). Refresh on change: D4.

### 5.3 Barcodes

- **US, current 10-digit NDC** [SEARCH]: the retail barcode is UPC-A
  `3` + NDC10 + check digit, or EAN-13 `03` + NDC10 + check digit; the
  GS1 company prefix is `03` + labeler code. GTIN-14 in a DSCSA
  DataMatrix: `003` + NDC10 + check digit [INFERRED: same GTIN
  left-padded]. The NDC10 can therefore be read from the GTIN, unlike
  the AIC (`ANALYSIS-A2-BARCODE-SCAN.md` §2.3), but without hyphens:
  the segmentation is unknown, so the lookup needs a column (or an
  index) holding the unsegmented 10 digits, filled at import.
  `BarcodeParser` gains a UPC-A path (12 digits), and the scan lookup,
  for reference country US, maps a GTIN starting `003` to NDC10.
- **US, 12-digit NDC (2033)**: the DataMatrix encodes the 12-digit
  NDC [SEARCH]; how it sits in a GTIN-14 is not settled in what was
  found [UNCERTAIN]. The canonical 6-4-2 key (§3.2) keeps the
  catalogue side ready; the barcode rule is revisited before 2033.
- **UK**: GTINs are arbitrary, the mapping is dm+d's GTIN file. New
  table `reference_gtins (country, gtin, medicine_id)`, created by an
  idempotent boot patch in `CatalogueSchema` (CLAUDE.md §7), filled
  from `dmd-gtin.tsv`. The scan dialog looks there when the
  national-code lookup fails. Same table can hold the US unsegmented
  NDC10 instead of a dedicated column.

### 5.4 Withdrawn badge

`WithdrawnMarkers` holds Italian words only. Parsers for US/GB should
map their status to a small fixed vocabulary, and the badge should test
that vocabulary instead of free text. The same gap exists today for
EU, ES and FR [VERIFIED]: `EmaEparParser`, `AempsCimaParser` and
`AnsmBdpmParser` store the source status verbatim (EMA "Medicine
status", AEMPS "Estado", BDPM "Statut administratif"), none of which
contains an Italian marker. Fixing it for those countries is a
separate change.

### 5.5 What stays Italy-only

Shortages, equivalents, regional services, the Codifa link and the AIC
check digit (`CatalogueFeedSelection.IsItaly`).

---

## 6. Decisions for the product owner

| # | Decision | Recommendation |
|---|----------|----------------|
| D1 | US distribution: openFDA bulk JSON or `ndctext.zip` | openFDA (explicit CC0, nested packages, SPL set id) |
| D2 | US scope | Finished human prescription + OTC drugs, vaccines included, samples excluded |
| D3 | Embedded snapshots for US/GB | No: remote feed only; installer size, SNOMED exposure; fix §5.2 instead |
| D4 | Refresh on country change: next start or immediately | Immediately, through the existing refresher, off the UI thread |
| D5 | US therapeutic equivalence from the Orange Book | Not now |
| D6 | dm+d key: AMPP id or GTIN | After the NHS England answer (§4.4 point 1) |
| D7 | Proceed with GB | Only after the TRUD licence pages of items 24 and 25 and the TRUD terms are read and recorded in `CATALOGUE-DATA.md` §7. The OGL v3.0 text itself is reviewed (§9.2) and does not block |
| D8 | GB unlicensed products (`LIC_AUTHCD` 0) | Keep, with a status the UI can show |

---

## 7. Risks

| Risk | Mitigation |
|------|------------|
| openFDA or TRUD changes format | Script validates fields and row floors; nothing published on failure (`common.py`) |
| TRUD key revoked or account lapsed | GB workflow fails; clients keep the last import |
| TRUD key leaked through a logged URL | No URL logging in `dmd.py`; Actions masking as second line |
| Users read US/GB data as clinical advice | Same wording as today; FDA non-endorsement sentence (§3.6) |
| Package-level near-duplicates in the autocomplete (US) | As Italy today; product grouping later if needed |
| SNOMED CT licensing outside the UK | D3 + D6 + written answer from NHS England |
| 2033 NDC format change | Canonical 6-4-2 key now; barcode rule revisited before 2033 |
| ATC for GB covers primary-care products only, "not officially endorsed" | ATC optional per row, as already in the schema |

---

## 8. Phasing

0. **Runner verification** (hosts unreachable from this session):
   fetch `download.json`, the NDC bulk file and `ndctext.zip`; record
   `product_type` and `marketing_category` values, row counts, sizes,
   duplicate canonical keys. Read and save the openFDA terms of
   service, the TRUD licence pages of items 24 and 25 and the TRUD
   terms, and one GOV.UK category list (columns). The openFDA licence
   page and the OGL v3.0 text are done (§9). Clear the remaining
   `[UNCERTAIN]` marks.
1. **US catalogue**: `fda_ndc.py` + workflow, `OpenFdaNdcParser`,
   descriptor, §5.1, §5.2, status vocabulary (§5.4), notices, user
   guides (five languages), fixture of about 200 rows, tests.
2. **US barcodes**: UPC-A path and NDC10 lookup (§5.3).
3. **GB catalogue**, gated by D6/D7: `dmd.py` + workflow with
   `TRUD_API_KEY`, `DmdParser` with ATC from item 25, descriptor,
   notices, guides, `CATALOGUE-DATA.md` §7 rewritten as a refresh
   procedure and `ANALYSIS-DRUG-CATALOGUE.md` §3.5 corrected.
4. **GB barcodes**: `reference_gtins` and lookup.
5. Optional (D5): Orange Book TE list.

One PR per phase; phases 1 and 3 are independent.

---

## 9. Licence texts supplied by the owner (2026-10-05)

The owner supplied two browser printouts, both dated 05/10/26 10:03–10:04
and image-only (no text layer): `licenza-openFDA.pdf` (3 pages) and
`NHS-TRUD-Licence.pdf` (4 pages). Read page by page.

### 9.1 openFDA — "Data Licensing" (`https://open.fda.gov/license/`)

Page last modified 27 May 2014. Relevant text:

- "Use of the data made available via openFDA is generally
  unrestricted"; the *service* is subject to the openFDA terms of
  service and to "any relevant sections of the FDA Website Policies".
- "Unless otherwise noted, the content, data, documentation, code,
  and related materials on openFDA is public domain and made available
  with a Creative Commons CC0 1.0 Universal dedication … waiving all
  rights to the work worldwide under copyright law, including all
  related and neighboring rights … You can copy, modify, distribute
  and perform the work, even for commercial purposes, all without
  asking permission."
- CC0 considerations: patent and trademark rights are not affected;
  no warranty, liability disclaimed; "When using or citing the work,
  you should not imply endorsement by the author or the affirmer."
- A GMDN paragraph: GMDN content (medical-device nomenclature) may not
  be used for commercial services, alternative categorisation, mapping
  or AI training without a licence from The GMDN Agency.
- "Exemptions": data not covered by these terms will be listed on the
  page; none is listed.

Assessment:

1. **The NDC dataset is covered by CC0.** It is "data on openFDA" and
   is not listed under the exemptions. Redistribution in the `feeds`
   branch and in the app is allowed without attribution; attribution
   stays as good practice (§3.6).
2. **The GMDN restriction does not apply.** GMDN terms belong to the
   device datasets (GUDID); the NDC mapping has no GMDN field
   [VERIFIED: `ndc_mapping.json`]. The feed script must not read any
   other openFDA endpoint without re-checking this point.
3. **The CC0 covers the openFDA copy, not `ndctext.zip`** on
   `accessdata.fda.gov`, which is outside openFDA. That file relies on
   the US public-domain status of federal works only. A further reason
   for D1 (openFDA bulk).
4. **Trademarks.** Brand names in the NDC data stay trademarks of
   their owners; CC0 does not license them. Showing them to identify
   the product the user owns is the same use the app already makes of
   AIFA, EMA, AEMPS and BDPM names [INFERRED; not legal advice].
5. **Endorsement.** No wording, logo or icon may suggest FDA endorses
   MedReminder (CC0 consideration and §3.6).
6. **Gaps.** The printout's page 2 starts under the site's fixed
   header, so a few lines before the GMDN paragraph (probably its
   heading) are hidden. The openFDA *Terms of Service* and the FDA
   Website Policies were not supplied; they govern the download
   service (for example rate limits), not the data. The bulk download
   is a handful of files a week, well within any plausible limit
   [UNCERTAIN until the terms are read].

### 9.2 "NHS-TRUD-Licence.pdf" — Open Government Licence v3.0

The file is the generic OGL v3.0 page of The National Archives
(`https://www.nationalarchives.gov.uk/doc/open-government-licence/version/3/`),
**not** the TRUD licence page of the dm+d items nor the TRUD terms of
use. It establishes what OGL v3.0 allows; it does not establish that
dm+d is under OGL v3.0 (still [SEARCH] plus the `wardle/dmd` README),
nor which attribution statement NHSBSA requires.

What OGL v3.0 grants and requires, applied to dm+d:

| OGL v3.0 clause | Effect on MedReminder |
|-----------------|-----------------------|
| Worldwide, royalty-free, perpetual, non-exclusive licence | Covers users outside the UK |
| Free to copy, publish, distribute, adapt, and exploit commercially, "by including it in your own product or application" | Publishing the derived TSV on the `feeds` branch and importing it in the app are allowed |
| Must acknowledge the source with the attribution statement specified by the Information Provider, else "Contains public sector information licensed under the Open Government Licence v3.0.", and link to the licence where possible | `THIRD-PARTY-NOTICES.md`, About dialog, `feeds` README; the exact NHSBSA statement to be copied from the TRUD item page |
| Several providers: a URI to a page listing the attributions is enough | `THIRD-PARTY-NOTICES.md` can be that page |
| Rights "end automatically" if the conditions are not met | Attribution must ship in the same release that ships the GB feed |
| Exemption: personal data | dm+d lists products and suppliers (organisations); nothing to exclude [INFERRED] |
| Exemption: "third party rights the Information Provider is not authorised to license" | **SNOMED CT.** dm+d codes are SNOMED CT identifiers owned by SNOMED International; OGL cannot license them if NHSBSA is not authorised to. Confirms §4.4 point 1 and D6 as a real gate |
| Exemption: "other intellectual property rights, including patents, trade marks" | Brand names: same position as §9.1 point 4. "SNOMED CT" itself is a registered trade mark [UNCERTAIN on registration details] |
| Exemption: public-sector logos and crests | No NHS logo in the app or the guides |
| Non-endorsement: no use suggesting official status or endorsement | No "NHS-approved" wording |
| No warranty; "does not guarantee the continued supply of the Information" | Clients keep the last import when the feed stops (current behaviour) |
| Governed by the law of the Information Provider's jurisdiction | England and Wales for NHSBSA [INFERRED] |
| Compatible with CC BY 4.0 and ODC-BY | No conflict with the Apache-2.0 code licence: the data keeps its own licence, stated per dataset in `THIRD-PARTY-NOTICES.md`, as for AIFA (CC BY 4.0) |

Outcome:

- **OGL v3.0 does not block GB.** It is at least as permissive as the
  licences already accepted for IT (CC BY 4.0) and FR (Etalab 2.0).
- **Still missing for D7:** the licence page of TRUD items 24 and 25
  (to confirm OGL v3.0 and read the NHSBSA attribution statement and
  any SNOMED note), and the TRUD terms of use (account, API key,
  redistribution of downloaded files). Both are visible only after
  login on TRUD.
- **D6 is now the main gate**, by the OGL's own third-party-rights
  exemption.

---

## 10. Corrections to the first draft of this document

| Claim in the first draft | Now |
|--------------------------|-----|
| No open ATC for GB (only in SNOMED-licensed bonus files) | Wrong: dm+d supplementary (item 25) maps ATC under OGL |
| US packages filtered on `ndc_exclude_flag` | Not mapped by openFDA; dropped. Filters are `finished`, `product_type`, `sample` |
| NDC key: hyphenated 10-digit form | Canonical 12-digit 6-4-2, because of the 2033 rule |
| MHRA lists likely partial | May be complete (two categories partition all licensed products); rejected for missing fields instead |
| Device filter via a dm+d flag | `LIC_AUTHCD` does not separate devices; rule based on ingredients / appliance pack info |
| Withdrawn badge not considered | Italian-only markers; gap added (§5.4), also present for EU/ES/FR |

---

## 11. Sources

Verified (read on 2026-10-05):

- openFDA NDC pipeline: <https://raw.githubusercontent.com/FDA/openfda/master/openfda/ndc/pipeline.py>,
  mapping <https://raw.githubusercontent.com/FDA/openfda/master/schemas/ndc_mapping.json>,
  annotation <https://raw.githubusercontent.com/FDA/openfda/master/openfda/ndc/annotate.py>
- open.fda.gov repository licence: <https://raw.githubusercontent.com/FDA/open.fda.gov/master/COPYING.txt>
- dm+d importer: <https://github.com/wardle/dmd> (README, `src/com/eldrix/dmd/import.clj`, `download.clj`);
  TRUD client: <https://github.com/wardle/trud> (`src/com/eldrix/trud/impl/release.clj`)

Search results only (pages not fetched):

- FDA Orange Book data files: <https://www.fda.gov/drugs/drug-approvals-and-databases/orange-book-data-files>,
  <https://www.accessdata.fda.gov/drugsatfda_docs/ob/OrangeBookDataFileDownloadInstructions.pdf>
- FDA NDC Directory: <https://www.fda.gov/drugs/drug-approvals-and-databases/national-drug-code-directory>;
  openFDA: <https://open.fda.gov/data/ndc/>, <https://open.fda.gov/data/downloads/>,
  <https://open.fda.gov/license>, <https://open.fda.gov/terms>
- 12-digit NDC final rule: <https://www.thefdalawblog.com/2026/03/6-4-2-blastoff-fdas-new-ndc-format-coming-in-2033/>,
  <https://www.faegredrinker.com/en/insights/publications/2026/3/fda-finalizes-rule-requiring-12-digit-national-drug-code-ndc>,
  <https://www.raps.org/resource/fda-issues-long-awaited-final-ndc-rule.html>
- NDC in barcodes: <https://www.rxtrace.com/2012/01/depicting-an-ndc-within-a-gtin.html/>,
  <https://en.wikipedia.org/wiki/National_drug_code>
- DailyMed web services: <https://www.dailymed.nlm.nih.gov/dailymed/webservices-help/v2/spls_setid_api.cfm>
- MHRA category lists: <https://www.gov.uk/government/publications/category-lists-following-implementation-of-the-windsor-framework>;
  explainer <https://assets.publishing.service.gov.uk/media/673cc5cf7e8a3c98a090fe94/MHRA_Windsor_Framework_Explainer.pdf>
- NHSBSA dm+d: <https://www.nhsbsa.nhs.uk/pharmacies-gp-practices-and-appliance-contractors/nhs-dictionary-medicines-and-devices-dmd/release-dmd-files>;
  TRUD item 24 <https://isd.digital.nhs.uk/trud/user/guest/group/0/pack/6/subpack/24/releases>;
  item 25 licence <https://isd.digital.nhs.uk/trud/users/guest/filters/0/categories/6/items/25/licences>;
  UK Drug bonus files licence <https://isd.digital.nhs.uk/trud/users/guest/filters/0/categories/8/items/639/licences>
- dm+d Implementation Guide (Primary Care) v2.0: <https://www.nhsbsa.nhs.uk/sites/default/files/2020-11/dm+d%20Implementation%20Guide%20(Primary%20Care)%20v2.0.pdf>
