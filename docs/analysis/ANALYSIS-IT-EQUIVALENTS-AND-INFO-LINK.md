# ANALYSIS — Italian equivalent medicines and medicine information link

Status: analysis only, nothing implemented. Date: 2026-10-03.

Two requests from the product owner:

1. Add the AIFA list of equivalent medicines, which carries package
   prices, so the user can look up the equivalents of a prescribed
   medicine or of a medicine in the home stock (§2).
2. Open the Codifa page of an Italian medicine, built from its AIC,
   from the medicine edit dialog and from the context menu of the main
   grid (§3).

Out of scope: a catalogue of non-medicinal products (food supplements
such as *Psyllogel Fibra*, medical devices). No open, machine-readable
and up-to-date Italian database exists for them (the Ministry of
Health supplement register is a web search form only), so no code is
planned. Such products can still be entered as medicines without a
catalogue link.

Tags follow the convention of the other analysis documents:
`[VERIFIED]` (checked on the source on the date above), `[INFERRED]`,
`[UNCERTAIN]`.

---

## 1. What already exists

| Item | Where | Relevance |
|---|---|---|
| Italian reference catalogue (AIFA `confezioni_fornitura.csv` + `PA_confezioni.csv`), keyed on the 9-digit AIC | `AifaSnapshotParser`, `docs/CATALOGUE-DATA.md` §2 | Join key for every Italian list below |
| `Medicine.NationalCode`, `Medicine.AtcCode`, `Medicine.LinkedReferenceMedicineId` | `src/MedReminder.Domain/Medicines/Medicine.cs` | A linked medicine already carries its AIC |
| AIFA shortage list: feed script, `feeds` branch, `ShortageRefresher`, JSON store shared by all profiles, not synced, not exported | `docs/CATALOGUE-DATA.md` §8, `ShortageList` | Template to copy for a second AIFA list |
| Shortage wording policy: "No product is named or suggested, to stay clear of any clinical role" | `docs/notes/EVOLUTION-PROPOSALS-2.md` §3.3 | Request 1 changes this stance; see §2.6 |
| Leaflet and SPC links in the edit dialog (`_documentsRow`, `UpdateDocumentLinks`, `OnDocumentLinkClicked`, host allow-list `IsSafeAifaUrl`) | `src/MedReminder.UI/Forms/MedicineEditDialog.cs` | Home of the Codifa link (§3) |
| Grid context menu (`BuildGridContextMenu`) | `src/MedReminder.UI/Forms/MainForm.cs` | Second entry point of the Codifa link |
| AIC validation with check digit (`ItalianPharmacode.IsValidAic`) | `src/MedReminder.Application/Catalogue/ItalianPharmacode.cs` | Guards the code put in the URL |

---

## 2. Request 1 — equivalent medicines and prices

### 2.1 Sources `[VERIFIED]`

AIFA, page "Liste dei farmaci" (open data, CSV, `;` separator,
Windows-1252, CRLF):

| List | URL | Cadence | Content |
|---|---|---|---|
| Lista di trasparenza (equivalent medicines) | `https://www.aifa.gov.it/documents/20142/825643/Lista_farmaci_equivalenti.csv` (stable name) | Monthly, around the 15th | Off-patent class A medicines with at least one equivalent, grouped, with reference price and public price |
| Classe A per principio attivo | `.../documents/20142/3815901/Classe_A_per_principio_attivo_<dd-mm-yyyy>.csv` (date in the name) | Irregular, several times a year | Every class A package with public price and equivalence group, patented ones included |
| Classe H per principio attivo | same folder | as above | Hospital class; little use at home |

Class C (most OTC and SOP medicines) has a free price and no public
price list. These medicines get no price.

Licence: the AIFA open-data page states CC BY 4.0, already used for
the shortage list. `[INFERRED]` that the same applies to these
files; confirm on the open-data page before shipping, and cite as
"AIFA, Lista di trasparenza of <date>".

### 2.2 Lista di trasparenza — file facts (download of 2026-10-03)

Header:

```
Principio attivo;Confezione di riferimento;ATC;AIC;Farmaco;Confezione;Ditta;
Prezzo riferimento SSN;Prezzo Pubblico 15 settembre 2026;Differenza;Nota;
Codice gruppo equivalenza
```

- 8,560 rows, 1,010 equivalence groups, 376 active ingredients.
- `AIC` lost its leading zeros (7 or 8 digits): it must be left-padded
  to 9 digits before the join with the catalogue. The Classe A file
  keeps 9 digits.
- The price column name carries the list date
  (`Prezzo Pubblico 15 settembre 2026`): match the column by prefix,
  and read the list date from it.
- Prices are text with comma decimals and a euro sign (`5,63 €`).
- `Differenza` = public price minus reference price, the amount the
  patient pays on top when the dispensed package costs more than the
  reference price. 2,731 rows have a non-zero difference.
- `Confezione di riferimento` describes the group in a normalised way
  (`40 UNITA' 100 MG - USO ORALE`): number of units, strength, route.
- `Nota` is free text and can be clinically relevant, for example
  `*non sostituibile con Adalat Crono, Nifedipina Doc, ...`,
  `*esclusivamente per uso sottocutaneo`, `In distribuzione diretta da
  parte delle Regioni`. It must be shown verbatim next to the group
  and never be dropped.

Classe A file: 10,736 rows, 2,617 groups, 8,430 rows marked as in
the transparency list. It adds `Solo in lista di Regione` (regional
transparency lists differ from the national one).

### 2.3 What the list means, and what it does not

- Two packages in the same group are interchangeable for the SSN:
  same active ingredient, strength, pharmaceutical form, route and
  number of units. The pharmacist may substitute within the group
  unless the doctor marked the prescription as non-substitutable.
- A medicine outside the list is not "without equivalents": it may be
  patented, class C, or simply not listed. The UI must say "not in the
  AIFA transparency list", never "no equivalent exists".
- The list says nothing about excipients, allergies or tolerability.

### 2.4 Proposed architecture — copy the shortage feed

Same shape as §8 of `docs/CATALOGUE-DATA.md`, so no new mechanism:

| Item | Proposal |
|---|---|
| Script | `scripts/feeds/aifa_equivalents.py`: download, pad AIC, parse prices to cents, keep `Nota` verbatim, write JSON; sanity checks on row count against the previous run (as for the catalogue) |
| Workflow | `download_aifa_equivalents.yaml`, daily, exits without changes when the list date is unchanged; concurrency group `catalogue-feeds-publish` |
| Published | `data/it/equivalents/equivalents-<yyyymmdd>.json` + `latest.json` on the `feeds` branch |
| Client | `EquivalenceRefresher` next to `ShortageRefresher`; Italy as reference country only; same settings (remote feeds on, automatic update check on); own size cap |
| Stored | `%LOCALAPPDATA%\MedReminder\catalogue\equivalents\equivalents-it.json`, shared by every profile; not in any profile database, not synced, not exported |
| Domain | `EquivalenceList` (country, list date, groups by code, index AIC → group); `EquivalenceGroup` (code, ingredient, reference description, ATC, reference price, note, members); `EquivalentPackage` (AIC, name, package, holder, public price) |
| Application | query `FindEquivalents(medicine)` returning the group or null |

Alternative rejected: add the prices to the catalogue snapshot
(`reference_medicines`). The catalogue is a large monthly import
(512 MiB cap), keyed on all packages; prices change on a different
cadence and come from a different file. Mixing them would force a
catalogue re-import for each price update and a schema change on the
reference database.

Sizing: about 8,600 rows → JSON of roughly 1–1.5 MB uncompressed
`[INFERRED]`, comparable to the shortage file cap (4 MiB).

Phase 2: merge the Classe A file into the same JSON, to give a price
to every reimbursed package (single-member groups included). Its file
name changes with each release, so the script must find the current
link on the AIFA page; allowed only if the link is read from a stable
listing, per the "no HTML scraping" rule of `CATALOGUE-DATA.md` §7.
`[UNCERTAIN]` whether a stable index exists.

### 2.5 Join with the user's data

1. `Medicine.NationalCode` (9-digit AIC) when present.
2. Otherwise the AIC of `LinkedReferenceMedicineId` in the catalogue.
3. Otherwise nothing. No matching by name or active ingredient: a
   wrong match on strength or form is a safety problem, and the list
   is exact by design.

### 2.6 Uses

Ordered by value over effort.

| # | Use | Where | Notes |
|---|---|---|---|
| U1 | "Equivalents (AIFA)" section in the medicine detail: group description, members sorted by public price, reference price, difference, `Nota` verbatim, current package highlighted, source and list date | `MedicineEditDialog` / medicine detail | Core of the request |
| U2 | Shortage + equivalents: for a medicine in the shortage list with `Equivalente = Sì`, show the members of its group that are not themselves in shortage | Shortage detail | Crosses two lists already local; answers "what can I ask my pharmacist for" |
| U3 | "You already have an equivalent at home": another medicine of the same profile, with stock > 0, whose AIC is in the same group | Medicine detail, low-stock warning | The home-stock case of the request. Information only: stock stays per medicine |
| U4 | Cost of therapy: price per unit (`public price / units`) × daily consumption → monthly and yearly cost; and the yearly extra paid over the reference price | Medicine detail, overview | Units come from `Confezione di riferimento`; needs a parser for `<n> UNITA'` and a fallback when absent |
| U5 | Prescription view: reference price and difference for each prescribed package | Prescriptions | Regional ticket excluded (varies by region and exemption) |
| U6 | Barcode scan of a package that is an equivalent of a profile medicine: offer to open that medicine | Barcode dialog | Adding the scanned pack as stock of the other medicine changes stock semantics; out of scope here |

Wording and safety (non-medical device):

- Factual only: "Equivalent according to the AIFA transparency list
  of <date>". No ranking label like "cheapest, switch", no automatic
  suggestion to change medicine.
- Fixed text: "Substitution is decided by your doctor and pharmacist.
  The list does not consider excipients or allergies."
- When the prescription is non-substitutable (not modelled today) the
  information stays informative.
- U1 names products, unlike the shortage feature (§1). It is still
  the publication of a public AIFA list, not a clinical choice, but
  the decision to name products should be recorded in the change log
  and in `EVOLUTION-PROPOSALS-2.md` §3.3.

### 2.7 Cross-cutting

- Localisation: new UI keys in all five `strings.<lang>.json`; data
  stays Italian (shown only with Italy as reference country).
- Household: the list is shared non-profile data; each device
  downloads it. Nothing to sync.
- Logs: list date and row count only.
- Tests: reduced fixture `tests/fixtures/catalogue/aifa-equivalents-sample.csv`
  (a few groups, one with `Nota`, one 7-digit AIC); Python tests for
  the script; domain tests for `EquivalenceList`; application tests
  for the join order of §2.5.

### 2.8 Effort `[INFERRED]`

| Phase | Content | Effort |
|---|---|---|
| E1 | Script, workflow, feed client, store, domain, U1 | 1–1.5 weeks |
| E2 | U2 + U3 | 3–4 days |
| E3 | U4 + U5 (unit parsing, cost display) | 1 week |
| E4 | Classe A merge (if a stable link exists) | 2–3 days |

---

## 3. Request 2 — Codifa link for Italian medicines

### 3.1 Source

Codifa publishes one page per Italian package at
`https://codifa.it/farmaci/dettaglio/<AIC>`, with the 9-digit AIC,
e.g. `https://codifa.it/farmaci/dettaglio/038253035` (GASTROLOC).
`[VERIFIED]` the URL answers HTTP 200. The page is rendered in the
browser: the server answers 200 even for a code that does not exist
(`000000000`), so the app cannot check in advance whether Codifa
knows a code, and must not try (no request from the app).

Codifa is a private service, not an AIFA open-data source. The app
only opens a public page in the user's browser on a user click: no
download, no scraping, no data stored. `[UNCERTAIN]` whether Codifa's
terms say anything about deep links; read them once before shipping.

What the user gains over the existing AIFA leaflet and SPC links: one
readable page per package (composition, class, dispensing regime,
price where known, equivalents), and a link that works for every
medicine with an AIC, also when the catalogue row has no leaflet URL.

### 3.2 When the link is offered

- Only when the AIC is valid: `ItalianPharmacode.IsValidAic` (9 digits
  and check digit). This also excludes non-Italian national codes
  (EMA product numbers, Spanish and French codes), which can sit in
  the same `NationalCode` field, without a separate country check.
- Medicines without an AIC (user-authored, never linked, supplements)
  get no link; the menu item is disabled, the edit-dialog link hidden.
- Not tied to the reference country setting: an AIC identifies an
  Italian package whatever the setting.

### 3.3 Design

One builder in the Application layer, next to `ItalianPharmacode`:

```csharp
// Public information page of an Italian package on Codifa, or null
// when the code is not a valid AIC.
public static class MedicineInfoLink
{
    private const string CodifaDetailBase = "https://codifa.it/farmaci/dettaglio/";

    public static Uri? ForNationalCode(string? nationalCode) =>
        nationalCode is { } code && ItalianPharmacode.IsValidAic(code.Trim())
            ? new Uri(CodifaDetailBase + code.Trim())
            : null;
}
```

- The base URL is a constant, not a setting: one place to change if
  Codifa moves its pages.
- The URL is built only from a validated 9-digit code, so nothing
  else can reach `Process.Start`. `IsSafeAifaUrl` stays as it is; the
  Codifa link does not go through it because it is never read from
  data.

Edit dialog (`MedicineEditDialog`):

- A third `LinkLabel` "Codifa page" in `_documentsRow`, next to
  "Leaflet" and "SPC". It follows the AIC currently in the dialog:
  the medicine's `NationalCode` when editing, the selected catalogue
  row when the user links a medicine in the dialog
  (`UpdateDocumentLinks` call sites at the reference-selection and
  reset paths), nothing after an unlink.
- `_documentsRow.Visible` becomes true when any of the three links is
  available. Today the row hides when leaflet and SPC are both
  missing.
- Click handling reuses `OnDocumentLinkClicked` (open with the shell,
  mark visited, warning dialog on failure).

Main grid (`MainForm.BuildGridContextMenu`):

- New item "Open Codifa page", after "Stock history" and before the
  separator of "Deactivate", with a globe or link glyph.
- `MedicineListItem` gains `NationalCode` (filled in
  `MedicineOverviewLoader`, which already reads it for shortages).
- In `menu.Opening`, enable the item only when the selected row has a
  link (`MedicineInfoLink.ForNationalCode(row.NationalCode) is not null`).
- Click opens the URL with `Process.Start(new ProcessStartInfo(url)
  { UseShellExecute = true })`, same error handling as the dialog.

Optional, same menu: "Leaflet (AIFA)" and "SPC (AIFA)" items. They
need a catalogue lookup per click through the linked reference row;
left out unless asked.

### 3.4 Cross-cutting

- Localisation: keys `Ui.MedicineEditDialog.Documents.Codifa` and
  `Ui.MainForm.Menu.Therapy.OpenCodifa` in all five
  `strings.<lang>.json`; "Codifa" is a proper name and stays
  untranslated.
- Privacy: opening the page sends the AIC to Codifa through the
  user's browser, on the user's click. No PII, nothing logged beyond
  what the dialogs already log.
- No schema change, no sync or export change (`NationalCode` is
  already stored, synced and exported).
- Tests: unit tests for `MedicineInfoLink` (valid AIC, wrong check
  digit, 8 digits, letters, surrounding spaces, null, EMA code).
  UI wiring checked by hand.
- User guides: one line in the catalogue section of
  `docs/USER_GUIDE.*.md` (the leaflet and SPC links are not described
  there yet; describe the three links together).

Effort `[INFERRED]`: 1 day. Independent of §2; can ship first.

---

## 4. Recommendation

1. Codifa link (§3) first: one day of work, no data to maintain, and
   it already shows a package's equivalents and price on the web.
2. E1 (transparency list feed + equivalents section), then E2. It
   reuses the shortage feed design end to end and adds what a web
   page cannot: offline data crossed with the profile (shortages, home
   stock).
3. Keep E3 and E4 as later proposals.

Open points for the product owner:

- Accept naming products in the UI (§2.6), a change from the shortage
  feature policy.
- Whether cost of therapy (U4) is wanted, since it needs unit parsing
  that the list does not always make explicit.
- Whether the grid context menu should also offer the AIFA leaflet and
  SPC (§3.3, optional).

## Sources

- AIFA, Liste dei farmaci: <https://www.aifa.gov.it/en/liste-dei-farmaci>
- AIFA, monthly transparency-list update notice: <https://www.aifa.gov.it/en/-/aifa-pubblica-l-aggiornamento-mensile-delle-liste-di-trasparenza>
- Codifa, example package page: <https://codifa.it/farmaci/dettaglio/038253035>
- Ministry of Health open data (checked for the out-of-scope supplement register): <https://www.dati.salute.gov.it/>
