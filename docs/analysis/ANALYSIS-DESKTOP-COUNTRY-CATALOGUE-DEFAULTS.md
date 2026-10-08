# ANALYSIS — Desktop: country and catalogue defaults (DA3 integration)

Design document, **prior to implementation**. It brings the desktop in
line with decision DA3 of `ANALYSIS-B1-ANDROID-PLAN.md` (§4.4, revised
2026-10-08), so that a profile behaves the same on the desktop and on
the phone.

Reading conventions: `[VERIFIED]` (checked against the tree at `main`
`fc347bd`), `[INFERRED]`, `[UNCERTAIN]`. Untagged statements are design
proposals.

---

## 1. Requirement (DA3)

| # | Requirement |
|---|---|
| R1 | UI language: the system language when it is one of the five supported languages (it, en, fr, es, de), English otherwise; user-changeable; independent of the country |
| R2 | Reference country: preselected from the system region and confirmed or changed by the user; nothing preselected when the region is unknown |
| R3 | Catalogue from the reference country: the national catalogue when one exists (IT, ES, FR, US); otherwise the EMA (EU) catalogue when the country is in the EU/EEA, where EMA centralised authorisations are valid; otherwise no catalogue |
| R4 | With no catalogue, medicines are entered manually, no catalogue feed is downloaded, and the user may still pick a catalogue explicitly |
| R5 | The search scope of a national catalogue stays as today: national plus EU for IT, ES, FR; national only for US |
| R6 | Existing installations keep their current reference country and catalogue; the defaults apply to new installations and to an explicit country change |

---

## 2. Current desktop behavior

| Topic | Today | Against DA3 |
|---|---|---|
| UI language at first run | `Program.ApplySystemLanguageOnFirstRun` adopts the Windows UI language if supported, English otherwise; `UserSettings.Language` defaults to `en` `[VERIFIED]` | Matches R1; no change |
| Reference country default | `UserSettings.ReferenceCountry` and `ExportPayload.ReferenceCountry` default to `IT`; no detection from the system region; the first-run wizard does not ask for it `[VERIFIED — FirstRunWizardForm has no country field]` | Differs from R2 |
| Country choices | Settings → General lists `IT`, `EU`, every feed country (`ES`, `FR`, `US`) and the countries present in the catalogue database (`SettingsDialog.General.PopulateReferenceCountryCombo`) `[VERIFIED]` | No way to state another country, such as DE or JP |
| Catalogue for a country | `StaticCountryProfileProvider`: any country not in {GB, UK, US} also searches EU; `CatalogueFeedSelection`: a country without a feed selects EU only `[VERIFIED]` | A non-EU/EEA country (for example JP) gets EMA, against R3 |
| No catalogue | Only the global feature flag `CatalogueFeatureOptions.Enabled`; when off, the medicine form runs in plain-text mode (`MainForm.BuildCatalogueContext` returns null) `[VERIFIED]` | No per-installation "no catalogue" state (R4) |
| Replication | `ReferenceCountry` is an installation setting replicated through the household (`HouseholdSetting.ReferenceCountry`, `InstallationSettingsMap`, `HouseholdProjection`) and part of the `.mrz` payload `[VERIFIED]` | Any new state must travel the same way |
| Italy-only lists | Shortages, equivalents and regional services refresh only with `IT` as reference country (`CatalogueFeedSelection.Includes*`) `[VERIFIED]` | Unchanged |

---

## 3. Proposed changes

### 3.1 Shared rule in Application

A pure `CatalogueDefaults` function in `MedReminder.Application/Catalogue`
(portable, used by the desktop and the Android app):

- input: reference country (ISO 3166-1 alpha-2, or none);
- output: reference catalogue (`IT`, `ES`, `FR`, `US`, `EU` or none);
- rules R3 and R5; the EU/EEA list (27 EU members plus Iceland,
  Liechtenstein and Norway) is a static set next to
  `StaticCountryProfileProvider`.

Unit tests cover each national country, an EU/EEA country without a
national catalogue (for example DE), a non-EU/EEA country (for example
JP, CH, GB) and the unknown case.

### 3.2 Stored state

The reference country stays in `ReferenceCountry`. The catalogue choice
needs a separate value, because "country DE, catalogue EU" and "country
JP, no catalogue" cannot be expressed with `ReferenceCountry` alone
while it also drives the Italy-only lists. Proposal: a new installation
setting `ReferenceCatalogue` (`IT`, `ES`, `FR`, `US`, `EU`, `none`),
replicated as a household setting and written to the `.mrz` payload.

Compatibility `[INFERRED]`: a device that does not know
`ReferenceCatalogue` keeps today's behavior from `ReferenceCountry`
(EU for an unlisted country). The household design records that
desktops keeping an unknown setting name without error is still to be
verified (`ANALYSIS-B1-ANDROID-PLAN.md` §4.8, family grant); this must
be verified before the setting ships, and `docs/SYNC-FORMAT.md` and
`docs/EXPORT-FORMAT.md` updated.

### 3.3 First run and settings

- First run: after the name and PIN, a country step preselected from
  the Windows region (`RegionInfo.CurrentRegion`); the catalogue shown
  as derived from it, changeable. When the region is unknown or
  invariant, nothing is preselected and the user may skip the step
  (no catalogue).
- Settings → General: the country list holds every ISO country (by
  localized name), not only the catalogue codes; a separate catalogue
  choice shows the derived default and allows any available catalogue
  or none. A country change applies the derived catalogue after
  confirmation; it never changes the UI language.
- With catalogue `none`, the medicine form uses the existing plain-text
  mode and the catalogue refresh downloads nothing.

### 3.4 Existing installations

On upgrade, `ReferenceCatalogue` is derived once from the stored
`ReferenceCountry` with today's rules (so an `IT` installation keeps IT
plus EU); nothing changes for the user until the country is changed
(R6).

### 3.5 Localization and documentation

New UI strings in the five dictionaries; user guides (five languages)
describe the country step and the "no catalogue" option.

---

## 4. Decisions

| # | Decision | Options | Resolution |
|---|---|---|---|
| DD1 | Storage of the catalogue choice | New `ReferenceCatalogue` setting; sentinel value in `ReferenceCountry` | **Decided 2026-10-08**: new setting (§3.2) |
| DD2 | Countries offered | Every ISO country; catalogue countries plus EU/EEA | **Decided 2026-10-08**: every ISO country, so that R3 can return "none" |
| DD3 | Area that selects EMA | EU/EEA (EMA centralised authorisations); euro area | **Decided 2026-10-08**: EU/EEA (27 EU members plus Iceland, Liechtenstein, Norway), not the euro area |
| DD4 | Country step at first run | Mandatory; optional with skip | **Decided 2026-10-08**: optional; skipping means no catalogue |

---

## 5. Plan

- Effort: 3–5 developer-days `[INFERRED]`, including the compatibility
  check of §3.2.
- Order: §3.1 before Android M4 (the Android app consumes the same
  rule); the desktop UI (§3.3) can ship with or after it.
- Tests: `CatalogueDefaults` unit tests; household projection test for
  the new setting; export/import round trip; manual check of first run
  with an Italian, German and Japanese Windows region.

---

## 6. Change log for this document

- 2026-10-08 — First version, from the DA3 revision of the Android plan.
- 2026-10-08 — DD1–DD4 decided by the product owner.
