# Change Log

All notable changes to MedReminder are recorded here, grouped by pull
request. Each PR gets a single entry, added when the PR is opened and
updated only if the PR's scope changes materially before it merges.

## How this file is maintained

- Every time a new pull request is created for this repository, an
  entry is prepended below in reverse-chronological order (newest
  first).
- The entry title is `## PR #<number> — <one-line summary>` and links
  back to the PR on GitHub.
- The body lists the observable changes as terse bullet points,
  focused on **what changed** and **why**, not on implementation
  detail. Reference the affected paths when it helps a future reader
  locate the change.
- If a PR is later closed without merging, mark the entry as
  `**Status:** closed (not merged)` — do not delete it.
- Once a PR merges, mark it as `**Status:** merged (<merge-date>)`
  under the title.
- Do not squash entries across releases: this log tracks pull
  requests, not versions. Release-level history belongs in the GitHub
  Releases page.

Format loosely inspired by [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
with the classification adapted to per-PR granularity: **Added**,
**Changed**, **Deprecated**, **Removed**, **Fixed**, **Security**,
**Docs**, **Build**.

---

## PR #19 — Reference catalogue: EU centralised authorisations (EPAR) (M3)

Link: [vger70/MedReminder#19](https://github.com/vger70/MedReminder/pull/19)
**Status:** open
Branch: `M3-drug-reference-catalogue`

Implements **M3** of the drug reference catalogue described in
[`docs/ANALYSIS-DRUG-CATALOGUE.md`](docs/ANALYSIS-DRUG-CATALOGUE.md)
§3.4 — the supranational `EU` catalogue. Instead of the Article 57
dataset the analysis mentions, this ships the EMA EPAR (European
public assessment reports) dataset, which is the file that
concretely materialises the "EU-centralised authorisations" concept
the design targets. Article 57 (pan-EEA per-country
authorisations) stays out of scope; the deviation is documented in
the PR body and in `THIRD-PARTY-NOTICES.md`.

### Added

- First real EMA snapshot embedded in the Infrastructure assembly:
  `src/MedReminder.Infrastructure/Assets/Catalogue/eu/ema-epar-202609.zip`
  (2.1 MB uncompressed, 486 KB compressed). Picked up automatically
  by a new `<EmbeddedResource>` glob and served under
  `MedReminder.Infrastructure.Assets.Catalogue.eu.ema-epar-202609.zip`
  — mirrors the existing IT convention, no changes to
  `EmbeddedSnapshotProvider` needed.
- `EmaEparParser` (`src/MedReminder.Infrastructure/Catalogue/Parsers/`):
  `IReferenceSnapshotParser` for country `EU`, reads a single-CSV ZIP
  entry (`ema-epar.csv`) at the archive root, matches EPAR header
  columns case-insensitively, filters `Category != 'Human'` (395
  veterinary rows dropped from the full 2 734-row export). Every
  emitted row carries `country = 'EU'` via
  `CountryCode.Parse("European Union")` — the long form never
  reaches the DB. `NationalCode` = EMA product number
  (e.g. `EMEA/H/C/004556`), guaranteed unique per row so the
  `UNIQUE (country, national_code)` constraint stays trivially
  satisfied without any synthetic derivation. Active substances
  split on `;` only (comma stays inside a single substance
  description); the row-level ATC is copied onto every ingredient
  of the row, same convention as `AifaSnapshotParser`. Registered
  next to `AifaSnapshotParser` in
  `InfrastructureServiceCollectionExtensions`.
- Extended `CatalogueRefreshHostedService` to iterate over
  `{ IT, EU }` instead of importing IT only. Each country runs in
  its own transaction (owned by the importer); a per-country
  `try/catch` around the import ensures a broken snapshot for one
  country never blocks the other.
- Curated fixture `tests/fixtures/catalogue/ema-epar-sample.csv`
  (73 rows: 70 Human + 3 Veterinary) covering every non-veterinary
  `Medicine status` EMA emits (`Authorised`, `Withdrawn`, `Refused`,
  `Suspended`, `Lapsed`, `Application withdrawn`, `Expired`,
  `Revoked`, `Opinion`), plus multi-substance rows (DuoPlavin,
  Symtuza, Qdenga) to exercise the `;` splitter, Gardasil 9 for the
  "comma-inside-a-single-substance" case, three no-ATC rows
  (Camcevi, Myqorzo, Vafseo), and a handful of well-known brands so
  assertions stay readable.
- New `EmaEparParserTests` (11 tests) exercising the fixture:
  row-count invariants, EU-country invariant on every yielded row,
  `;` vs `,` splitter behaviour, `EMEA/H/...` uniqueness, status
  pass-through, no-ATC handling, `Medicine URL` mirrored onto both
  leaflet + SPC links, non-seekable-stream buffering.
- `CsvReferenceCatalogueImporterTests` M3 additions:
  IT-then-EU import populates both countries with no dedup
  (168 + 70 = 238 rows visible with `country IN ('IT','EU')`),
  EU-only import leaves IT rows alone, a newer EU snapshot never
  touches IT rows at their older `snapshot_version`, no row ever
  lands with the `'European Union'` long form in `country`.
- `SqliteReferenceCatalogueQueryServiceTests` M3 additions:
  cross-country search after loading the EU fixture on top of the
  Italian one — scope `{ IT, EU }` returns Symtuza (an EU-only
  medicine that never appears in the Italian fixture); scope
  `{ EU }` returns only EU rows; `ListAvailableCountriesAsync`
  surfaces both `IT` and `EU`.

### Changed

- `about.dataSources.emaArticle57` value updated in all 5
  dictionaries (`en/it/fr/es/de`) from the M2 "reserved for a
  future release" placeholder to the real EPAR attribution line.
  The **key name** is left unchanged for parity stability — it
  is an internal identifier, the user-visible text is what
  changed. Parity holds at 348 keys per dictionary;
  `DictionaryParityTests` stays green.
- `THIRD-PARTY-NOTICES.md`: replaced the EMA placeholder section
  with the real attribution — EPAR dataset, EMA legal notice
  (Commission decision 2011/833/EU on reuse of Commission
  documents), snapshot path, applied filter (`Category = 'Human'`),
  and an explicit note that EPAR ≠ Article 57.

### Docs

- `docs/CATALOGUE-DATA.md`: new §3 "Refresh procedure (EU — EMA
  EPAR)" covering the XLSX-to-CSV conversion, the ZIP layout
  (single `ema-epar.csv` at archive root), the naming convention
  (`ema-epar-<yyyymm>.zip`), the drop path and the boot
  verification. §1 table gains the EU row; §5 renames from "other
  countries" and drops EMA from the pending list. Fixture-regen
  notes for AIFA move to §4.1, EPAR gets §4.2 with the exact
  status buckets the fixture must cover.
- `docs/USER_GUIDE.{en,it,fr,es,de}.md`: "Reference catalogue
  (Italy)" section becomes "Reference catalogue (Italy + EU)"
  with a new "EU centrally authorised medicines" subsection
  explaining what EPAR is, that the autocomplete shows EU rows
  automatically when the reference country is any EU member
  (default IT), and that setting reference country to `EU`
  narrows the list to EU-only. Each guide in its own language.

### Deviations from `docs/ANALYSIS-DRUG-CATALOGUE.md`

- **EPAR instead of Article 57.** The design doc §1.3 lists "EMA
  Article 57" as the source for the `EU` country. The public
  Article 57 dump is actually the pan-EEA per-country
  authorisation register (~160 000 rows, one per (product ×
  authorisation country)) and has no explicit "centralised" flag —
  importing all of it as `country = 'EU'` would mis-label 160k
  national authorisations as supranational. EPAR is the concrete
  dataset that matches "EU-centralised medicines valid across the
  EU/EEA" (~2 700 rows, 1 568 currently Authorised), and it also
  ships ATC, MAH and the EMA product number as structured columns.
  File names, parser class and doc language use "EPAR" throughout;
  the localisation key `about.dataSources.emaArticle57` is kept
  as-is for compatibility with the M2 dictionaries.
- **Category = 'Human' filter.** Veterinary rows (395 of 2 734)
  are skipped at parse time — MedReminder targets human medicine
  reminders, and mixing veterinary products into the autocomplete
  would confuse users. Recorded as `Skipped` in `ImportReport`.

---

## PR #18 — Reference catalogue: foundations + AIFA autocomplete (Italy) (M1 + M2)

Link: [vger70/MedReminder#18](https://github.com/vger70/MedReminder/pull/18)
**Status:** open
Branch: `claude/sleepy-turing-s6fwzy`

### M2 — Autocomplete Italy (`src/MedReminder.UI` + snapshot embedded)

#### Added

- First real AIFA snapshot embedded in the Infrastructure assembly:
  `src/MedReminder.Infrastructure/Assets/Catalogue/it/aifa-202609.zip`
  (94 MB uncompressed, ~5 MB compressed), picked up automatically by
  a `<EmbeddedResource>` glob and served under
  `MedReminder.Infrastructure.Assets.Catalogue.it.aifa-202609.zip`.
- `MedicineAutocompleteBox` WinForms control
  (`src/MedReminder.UI/Controls/`): text input + owner-drawn ListBox
  with 150 ms debounce on keystrokes, hard 20-row limit per query,
  row template `commercial_name — active_ingredient — dosage`, red
  circle badge on rows whose AIFA `STATO_AMMINISTRATIVO` contains
  `sospesa`, `ritirat` or `revocata` (case-insensitive substring —
  free-text column, no enum assumed), horizontal scrollbar with
  measured `HorizontalExtent`, dropdown anchored at the form's left
  edge and spanning the full dialog width.
- `CatalogueRefreshHostedService` (`src/MedReminder.UI/Hosting/`):
  boot-time importer that runs on a background thread only when
  `Catalogue:Enabled == true`, opens the embedded snapshot via
  `EmbeddedSnapshotProvider` and short-circuits when the recorded
  `snapshot_version` matches. Registered conditionally in
  `Program.BuildHost`.
- `MedicineEditDialog` hosts two `MedicineAutocompleteBox` instances
  — one on "Commercial name" and one on "Active ingredient".
  Picking a catalogue row on either side fills the sibling text
  field plus `Package` (from AIFA `DESCRIZIONE`), `Unit` (mapped
  from AIFA `FORMA` to a localised label via a 13-entry stem table,
  falling back to raw FORMA when nothing matches), and an extended
  commercial name of the shape `<CommercialName> <strength> <form>`
  where strength is extracted from `DESCRIZIONE` with a
  conservative regex (`mg / g / mcg / µg / ug / ml / l / ui / iu / %`).
  Free-text edits clear the reference linkage so a manually-typed
  entry saves unlinked.
- Settings dialog — General tab: new "Reference country" dropdown
  populated from `IReferenceCatalogueQueryService.ListAvailableCountriesAsync`
  plus the fixed `IT` and `EU` options. Save writes Language +
  ReferenceCountry atomically to `user.settings.json`.
- Root `THIRD-PARTY-NOTICES.md` listing the AIFA attribution
  (CC BY 4.0) and a placeholder for EMA Article 57 (M3). The two
  `about.dataSources.*` strings are surfaced in the About dialog.
- `docs/CATALOGUE-DATA.md`: monthly AIFA-refresh procedure — where
  to download, how to build the ZIP, where to drop it, how to
  regenerate the reduced fixture.
- Seven new unit keys in all five dictionaries (`Granules`,
  `Drops`, `Suppositories`, `Patches`, `Grams`, `Puffs`,
  `Ampoules`) — the medicine form's unit combo grew from 6 to 13
  options and picks up AIFA FORMA values automatically. Ten new
  M2 keys (`medicine.field.*`, `medicine.autocomplete.*`,
  `settings.referenceCountry.*`, `about.dataSources.*`). Parity
  verified across the 5 dictionaries (348 keys each).

#### Changed

- `UserSettings` gains `ReferenceCountry` (default `"IT"`).
  `user.settings.json` is now loaded with `reloadOnChange: true`
  so a country change takes effect at the next medicine-form open
  without a restart.
- `IReferenceCatalogueQueryService` gains
  `ListAvailableCountriesAsync` — one method, needed by the
  Settings dropdown.
- `AddMedicineCommand` and `UpdateMedicineCommand` gain trailing
  optional catalogue-linkage parameters (`NationalCode`,
  `AtcCode`, `LinkedReferenceMedicineId` on Add; a `CatalogueLink`
  block on Update). Existing named-arg callers stay
  source-compatible; the two use-case implementations copy the
  linkage into the Medicine entity when set.
- `CatalogueFeatureOptions.Enabled` defaults to `true` in
  `appsettings.json`, activating the feature end-to-end.
- `MedicineEditDialog` width bumped from 620 to 880 px so the
  autocomplete dropdown has enough room for long AIFA rows without
  heavy horizontal scrolling; German `Unit.Vials` translation fixed
  from "Ampullen" (semantically ambiguous) to "Fläschchen", freeing
  "Ampullen" for the new `Ampoules` key that maps AIFA "fiale".

#### Docs

- `USER_GUIDE.{en,it,fr,es,de}.md`: new "Reference catalogue
  (Italy)" section covering autocomplete behaviour, the free-text
  fallback, the reference-country setting, and the AIFA source with
  its CC BY 4.0 licence — each guide in its own language.

---

### M1 — Foundations (`src/MedReminder.Domain` + `Application` + `Infrastructure`)

#### Added

- Domain (`net10.0`): `CountryCode` value object (normalises
  `European Union` → `EU`, validates ISO 3166-1 alpha-2) and
  `AtcCode` value object (7-character WHO ATC pattern), plus the
  `ReferenceMedicine` and `ReferenceActiveIngredient` read models
  under `src/MedReminder.Domain/Catalogue/`.
- Application (`net10.0`): `IReferenceCatalogueQueryService`,
  `IReferenceCatalogueImporter`, `ImportReport`,
  `SearchCatalogueUseCase`, `LinkMedicineToReferenceUseCase`,
  `ICountryProfileProvider` / `StaticCountryProfileProvider` (the
  sole owner of the "national ∪ EU" filter — default `true`;
  explicitly `false` for `GB` / `UK` per §12 point 7) and
  `CatalogueFeatureOptions` (off by default).
- Infrastructure (`net10.0-windows`): `AifaSnapshotParser` reads
  `confezioni_fornitura.csv` joined on `CODICE_AIC` with
  `PA_confezioni.csv` inside a ZIP archive, skipping
  `TIPO_PROCEDURA = 'Omeopatico'` and `PRINCIPIO_ATTIVO = 'N.D.'`;
  every row lands with `country = 'IT'`; `dispensing_regime` /
  `link_leaflet` / `link_spc` are mapped from `FORNITURA` /
  `LINK_FI` / `LINK_RCP`; 9-digit AIC leading zeros preserved.
- `CsvReferenceCatalogueImporter` (transactional replace,
  short-circuits on same recorded `snapshot_version`),
  `SqliteReferenceCatalogueQueryService` (raw-SQL adapter hitting
  the indexed `_norm` columns), `CatalogueTextNormalizer` (shared
  lowercase + diacritics stripping) and an `EmbeddedSnapshotProvider`
  stub (M2 will ship the first real snapshot).
- DI wiring for the catalogue ports; feature flag registered off by
  default, so no runtime behaviour changes.
- Tests: 35 new Domain tests (`CountryCode`, `AtcCode`), 25 new
  Application tests (fake-port union semantics for
  `SearchCatalogueUseCase`, `StaticCountryProfileProvider`,
  `LinkMedicineToReferenceUseCase`) and five new Infrastructure
  test files (`CatalogueSchemaTests`, `AifaSnapshotParserTests`,
  `CsvReferenceCatalogueImporterTests`,
  `SqliteReferenceCatalogueQueryServiceTests`, `CatalogueFixtures`)
  exercising the M0 fixture (168 kept / 29 Omeopatico skipped,
  idempotent replay, newer-version replace, Aspirina M2M).

#### Changed

- `Medicine` gains three optional catalogue fields — `NationalCode`,
  `AtcCode`, `LinkedReferenceMedicineId` — surfaced on the entity
  and mapped in `MedicineConfiguration` for fresh DBs.
- `DatabaseInitializer.InitializeAsync` now applies the catalogue
  DDL unconditionally on every boot (additive, idempotent — no
  `EnsureCreated` shortcut for the catalogue tables per §2.4) and
  adds the three Medicine columns to pre-existing DBs via
  `AddColumnIfMissingAsync`.
- `MedReminder.Infrastructure.Tests.csproj` copies the
  `tests/fixtures/catalogue/*.csv` files as content to the test
  output directory.

#### Docs

- No changes to `docs/ANALYSIS-DRUG-CATALOGUE.md`. Four
  M1-time deviations flagged in the PR description
  (fixture uses ASPIRINA not Augmentin, port carries a pre-computed
  `countryScope`, AIFA parser copies `CODICE_ATC` onto every
  ingredient of a row, catalogue table ids stored as `TEXT` to
  match how EF stores Guids elsewhere).

---

## PR #13 — Add drug reference catalogue design analysis (with M0 findings)

Link: [vger70/MedReminder#13](https://github.com/vger70/MedReminder/pull/13)
**Status:** open
Branch: `claude/database-principi-attivi-gl3rnw`

### Docs

- `docs/ANALYSIS-DRUG-CATALOGUE.md` (new): engineering plan for
  the future reference catalogue of medicinal products (commercial
  name ↔ active ingredient ↔ ATC), country-aware from the start.
  Covers scope, Clean-Architecture impact on the four projects,
  Domain / Application / Infrastructure additions, SQLite schema
  sketch, snapshot layout and attribution obligations for AIFA
  (CC BY 4.0) and EMA Article 57, milestones M0–M5, testing
  strategy, localisation notes for the five shipped languages
  (`de`, `en`, `es`, `fr`, `it`), risks, effort estimate and the
  seven §12 decisions with their current status.
- §3.1 M0 marked as completed. Full field mapping and
  cardinalities are recorded in the M0 comment on issue
  [#9](https://github.com/vger70/MedReminder/issues/9#issuecomment-5718752090):
  85,697 imported packages, 9,619 commercial names,
  5,750 active ingredients, 2,269 ATC codes after applying the
  two documented import filters.
- §2.4 schema: three new optional columns on `reference_medicines`
  — `dispensing_regime` (from AIFA `FORNITURA`), `link_leaflet`
  (from `LINK_FI`), `link_spc` (from `LINK_RCP`).
- §3.2 M1: documented two AIFA import filters — skip
  `TIPO_PROCEDURA = 'Omeopatico'` (74k rows, 46%) and skip
  `PRINCIPIO_ATTIVO = 'N.D.'` in `PA_confezioni` (53k rows).
- §7 Risks: replaced the speculative snapshot-size row with the
  measured baseline (gzipped snapshot ~17–22 MB, SQLite growth
  ~60–80 MB).
- §12.1 marked Resolved with the resolved dataset choice
  (`confezioni_fornitura.csv` joined with `PA_confezioni.csv`).
- Follow-up to the discovery notes in
  [#9](https://github.com/vger70/MedReminder/issues/9).
- No source code changes; no runtime behaviour changes.

### Added

- `tests/fixtures/catalogue/aifa-confezioni-sample.csv`,
  `aifa-pa-sample.csv`, `aifa-atc-sample.csv` — 198 + 253 + 76
  rows stratified from real AIFA open data. Cover `Sospesa`
  status, `Procedura Centralizzata` (EU-authorised), OTC,
  hospital-only, well-known brands (Augmentin, Aspirina,
  Tachipirina, Cardura, Eutirox, Coumadin, Zoloft, …), common
  active ingredients (paracetamolo, ibuprofene, metformina,
  olmesartan, atorvastatina, simvastatina, omeprazolo,
  amoxicillina, ramipril, bisoprololo), and 37 multi-ingredient
  combinations so the M2M table is exercised. Consumed by M1
  integration tests once M1 lands.

## PR #8 — Bump WebView2 to 1.0.4191.47; drop unused WPF reference

Link: [vger70/MedReminder#8](https://github.com/vger70/MedReminder/pull/8)
**Status:** open
Branch: `webview2-strip-wpf-ref`

### Changed

- `Microsoft.Web.WebView2` bumped from `1.0.2792.45` to
  `1.0.4191.47`.

### Build

- New MSBuild target `RemoveUnusedWebView2Wpf` in
  `src/MedReminder.UI/MedReminder.UI.csproj`, running
  `AfterTargets="ResolveAssemblyReferences"`, removes the unused
  `Microsoft.Web.WebView2.Wpf` reference and its copy-local entry
  from the WinForms-only host. This suppresses the `MSB3277`
  warning introduced by the new package version (its WPF assembly
  requires `WindowsBase 5.0.0.0`, unified against .NET 10's
  `WindowsBase 4.0.0.0`) and removes the dead ~50 KB DLL from the
  published output. `HelpViewerForm` and the native
  `WebView2Loader.dll` under `runtimes/` are unaffected.

### Fixed

- `MSB3277` warning about conflicting `WindowsBase` versions that
  appeared on every build after the WebView2 bump.
## PR #7 — Document SmartScreen warning; add French and Spanish user guides

Link: [vger70/MedReminder#7](https://github.com/vger70/MedReminder/pull/7)
**Status:** open
Branch: `smartscreen-advice`
(previously `claude/jolly-mccarthy-xk8lc4`; renamed after first push)

### Added

- `docs/USER_GUIDE.fr.md` and `docs/USER_GUIDE.es.md`: native user
  guides for the French and Spanish UI locales, matching the
  content of `USER_GUIDE.en.md`. `HelpViewerForm` already resolves
  `USER_GUIDE.<lang>.md` dynamically — only the csproj wiring
  changed (`Content` + `EmbeddedResource`).

### Docs

- `README.md`: new "Windows SmartScreen warning on first run"
  section after the Download block, explaining the SmartScreen
  dialog and UAC "Unknown Publisher" prompt triggered by the
  intentionally unsigned release, with the two-click bypass steps.
- `docs/USER_GUIDE.en.md`, `docs/USER_GUIDE.it.md`,
  `docs/USER_GUIDE.fr.md`, `docs/USER_GUIDE.es.md`: matching
  "SmartScreen on first launch" subsection under *First start* /
  *Primo avvio* / *Premier démarrage* / *Primer inicio*, using the
  localized Windows dialog labels for each language.
- `README.md`: drop the obsolete "guide localized in EN and IT
  only" known limitation now that all four shipped languages have
  a native guide.
- `CLAUDE.md`: extend the English-only exceptions and the
  repository layout section to list all four shipped user guides.

### Build

- `src/MedReminder.UI/MedReminder.UI.csproj`: register the two new
  guides as `Content` (copied to `bin/localization/`) and
  `EmbeddedResource` (single-file publish fallback). Comment on
  the block updated to reflect the four supported languages.

---

## PR #5 — Add CLAUDE.md, English-only docs, strip .pdb/.xml in Release

Link: [vger70/MedReminder#5](https://github.com/vger70/MedReminder/pull/5)
**Status:** open
Branch: `claude/translate-in-english`
(previously `claude/compassionate-pasteur-qmmt3h`; renamed after
opening)

### Added

- `CLAUDE.md` at the repository root, with repository conventions,
  the English-only language policy (exception: `USER_GUIDE.it.md`
  and the JSON translation dictionaries under
  `assets/localization/`), build / test / publish commands, branch
  policy, data locations and pointers to CHANGE_LOG.md maintenance.
- `CHANGE_LOG.md` (this file) with per-PR entries and maintenance
  rules.

### Changed

- `docs/ANALYSIS.md`, `docs/ANALYSIS-MULTI-USER.md` and
  `packaging/msix/Assets/README.md` translated from Italian to
  English. `USER_GUIDE.en.md` and `USER_GUIDE.it.md` intentionally
  left as-is — they are shipped to the end user in each locale.
- Every Italian comment, log message, exception message and
  hardcoded fallback string in the C# source (Domain, Application,
  Infrastructure, UI and test projects) translated to English.
  Identifiers and public APIs are untouched; hardcoded fallback
  strings in `NotificationTexts` and `TherapyReport` now default to
  English (matching the app's default locale) — used only when no
  `ILocalizationService` is registered.
- Italian comments in the ancillary config files translated to
  English: `.csproj`, `.pubxml` publish profiles, WiX `Product.wxs`
  and `MedReminder.wixproj`, MSIX `Package.appxmanifest`,
  `MedReminder.mapping.txt` and `priconfig.xml`, packaging scripts
  (`build-installer.ps1`, `make-selfsigned-cert.ps1`,
  `sign-artifact.ps1`), the GitHub Actions workflow, WiX
  `License.rtf` and `assets/build/generate_icon.py`.

### Build

- New MSBuild target `StripReleaseDebugArtifacts` in
  `Directory.Build.props` that runs after `Build` and `Publish` when
  `$(Configuration) == Release`, deleting `*.pdb` and `*.xml` from
  `$(OutputPath)` and `$(PublishDir)`. Test projects are excluded.
  This keeps the shipped ZIP / MSI / MSIX free of debug symbols and
  documentation dumps without changing the CI workflow (which already
  builds `-c Release`).
- The GitHub Actions release workflow
  (`.github/workflows/dotnet-desktop.yml`) now also builds the WiX
  MSI installer from the same self-contained publish output and
  attaches `MedReminder-win-x64.msi` alongside the existing ZIP to
  every GitHub Release. The MSI version tracks the pushed `vX.Y.Z`
  tag. Italian strings introduced by the MSI step
  (`throw` messages) are translated to English inline with the
  rest of the workflow.

### Docs

- `README.md` license section updated from "MIT" to "Apache 2.0"
  (matching the `LICENSE` file).
