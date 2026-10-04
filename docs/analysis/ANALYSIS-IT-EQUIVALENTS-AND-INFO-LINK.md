# ANALYSIS — Italian equivalent medicines and medicine information link

Status: §3 (Codifa link) and phases E1 and E2 of §2 (feed, equivalents
window, uses U1 to U3) implemented; E3 (U4, U5), E4 and U6 remain
proposals. Date: 2026-10-03.

Implementation notes:

- The equivalents window opens from the grid context menu and from an
  "Equivalent medicines" link in the information row of the edit dialog
  (next to leaflet, SPC and Codifa), instead of a separate button.
- U3 is shown in the equivalents window only; the low-stock warning is
  unchanged.
- U2: the shortage tooltip of a package in the list points to the
  equivalents window; every member of the group in shortage is marked
  in the window.
- The feed is not mirrored to `main/data/`: only releases that read the
  `feeds` branch know it.

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
| Classe A per principio attivo | `.../documents/20142/3815901/Classe_A_per_principio_attivo_<dd-mm-yyyy>.csv` (date in the name) | Irregular `[INFERRED]`: the file dated 31-05-2026 was listed as updated on 28/09/2026 | Every class A package with public price and equivalence group, patented ones included |
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
  keeps 9 digits. Once padded, every AIC passes
  `ItalianPharmacode.IsValidAic`.
- Each AIC appears once and in one group only; every group has at
  least two members. An index AIC → group is therefore unambiguous.
- The price column name carries the list date
  (`Prezzo Pubblico 15 settembre 2026`): match the column by prefix,
  and read the list date from it.
- Prices are text with comma decimals and a euro sign (`5,63 €`).
- `Differenza` = public price minus reference price, the amount the
  patient pays on top when the dispensed package costs more than the
  reference price. 2,731 rows have a non-zero difference.
- `Confezione di riferimento` describes the group in a normalised way
  (`40 UNITA' 100 MG - USO ORALE`): number of units, strength, route.
  527 rows (6%) count something else: doses (`120 DOSI ...`), volume
  (`500 ML ...`) or mass (`30 G ...`).
- `Nota` is free text attached to a package, not to a group: 1,324
  rows carry one, and in 10 groups members have different notes. The
  packages concerned have an asterisk in `Farmaco` (`AMARKOR*`). A note
  can restrict substitution inside the group, for example
  `*non sostituibile con Adalat Crono, Nifedipina Doc, ...` on some
  nifedipine packages of group 12A; others read
  `*esclusivamente per uso sottocutaneo` or `In distribuzione diretta
  da parte delle Regioni`. It must be shown verbatim next to its
  package and never be dropped.

Classe A file: 10,736 rows, 2,617 groups, 8,430 rows marked as in
the transparency list. It adds `Solo in lista di Regione` (regional
transparency lists differ from the national one).

### 2.3 What the list means, and what it does not

- Two packages in the same group are interchangeable for the SSN:
  same active ingredient, strength, pharmaceutical form, route and
  number of units, within the limits stated by the notes (§2.2). The
  pharmacist may substitute within the group unless the doctor marked
  the prescription as non-substitutable; the patient may refuse and
  pay the difference.
- A medicine outside the list is not "without equivalents": it may be
  patented, class C, or simply not listed. The UI must say "not in the
  AIFA transparency list", never "no equivalent exists".
- The list says nothing about excipients, allergies or tolerability.

### 2.4 Proposed architecture — copy the shortage feed

Same shape as §8 of `docs/CATALOGUE-DATA.md`, so no new mechanism:

| Item | Proposal |
|---|---|
| Script | `scripts/feeds/aifa_equivalents.py`: download, pad AIC, parse prices to cents, keep `Nota` verbatim, write JSON; sanity checks on row count against the previous run (as for the catalogue) |
| Workflow | `download_aifa_equivalents.yaml`, daily; publishes only a newer list date or a correction of the same date, never an older one (`docs/CATALOGUE-DATA.md` §9); own concurrency group |
| Published | `data/it/equivalents/equivalents-<yyyymmdd>.json` + `latest.json` on the `feeds` branch |
| Client | `EquivalenceRefresher` next to `ShortageRefresher`; Italy as reference country only; same settings (remote feeds on, automatic update check on); own size cap |
| Stored | `%LOCALAPPDATA%\MedReminder\catalogue\equivalents\equivalents-it.json`, shared by every profile; not in any profile database, not synced, not exported |
| Domain | `EquivalenceList` (country, list date, groups by code, index AIC → group); `EquivalenceGroup` (code, ingredient, reference description, ATC, reference price, members); `EquivalentPackage` (AIC, name, package, holder, public price, note) |
| Application | query `FindEquivalents(medicine)` returning the group or null |

Alternative rejected: add the prices to the catalogue snapshot
(`reference_medicines`). The catalogue is a large monthly import
(512 MiB cap), keyed on all packages; prices change on a different
cadence and come from a different file. Mixing them would force a
catalogue re-import for each price update and a schema change on the
reference database.

Sizing: the 2026-10-03 list serialised as compact JSON grouped as
above takes about 1.7 MB, within a 4 MiB cap like the shortage feed.

Phase 2: merge the Classe A file into the same JSON, to give a price
to every reimbursed package (single-member groups included). Its file
name changes with each release, so the script must find the current
link on the AIFA "Liste dei farmaci" page. The feeds already do this
for the French BDPM (`CATALOGUE-DATA.md` §6.2: scrape the download
page, fall back to a URL pattern), so it is an accepted pattern; the
risk is a layout change on the AIFA page, handled by the same retries
and red run.

### 2.5 Join with the user's data

- `Medicine.NationalCode`, used only when `ItalianPharmacode.IsValidAic`
  accepts it (the field can also hold an EMA, Spanish or French code).
  `LinkMedicineToReferenceUseCase` and `UpdateMedicine` write it
  together with `LinkedReferenceMedicineId`, so the reference id adds
  nothing; it is also a weak key into a catalogue whose rows are
  replaced on refresh.
- Otherwise nothing. No matching by name or active ingredient: a
  wrong match on strength or form is a safety problem, and the list
  is exact by design.

### 2.6 Uses

Ordered by value over effort.

| # | Use | Where | Notes |
|---|---|---|---|
| U1 | Equivalents view: group description, members sorted by public price, reference price, difference, each package's note verbatim, current package highlighted, source and list date | New `EquivalentsDialog`, opened from a button in `MedicineEditDialog` and from the grid context menu | Core of the request. The app has no medicine detail form; the edit dialog is already dense, so the list goes in its own dialog |
| U2 | Shortage + equivalents: for a medicine in the shortage list with `ShortageEntry.EquivalentAvailable`, mark the members of its group that are themselves in shortage | `EquivalentsDialog`; the shortage tooltip of the grid (`SupplyDetail`) points to it | Crosses two lists already local; answers "what can I ask my pharmacist for" |
| U3 | "You already have an equivalent at home": another medicine of the same profile, with stock > 0, whose AIC is in the same group | `EquivalentsDialog`, low-stock warning | The home-stock case of the request. Information only: stock stays per medicine. When either package has a note, show the note instead of a plain "equivalent" |
| U4 | Cost of therapy: price per unit (`public price / units`) × daily consumption → monthly and yearly cost; and the yearly extra paid over the reference price | `EquivalentsDialog` | Units come from `Confezione di riferimento`; `<n> UNITA'` covers 94% of rows; doses, ML and G (§2.2) need a mapping to the medicine's `Unit`, or no cost is shown |
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
  (a few groups, one whose members have different notes, one 7-digit
  AIC, one reference description in DOSI); Python tests for
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
  The check digit rejects 13 of the 300,193 AICs of the shipped
  snapshot (comment in `ItalianPharmacode`); those packages get no
  link, an accepted loss.
- Medicines without an AIC (user-authored, never linked, supplements)
  get no link; the menu item is disabled, the edit-dialog link hidden.
- Not tied to the reference country setting nor to the catalogue
  feature flag: an AIC identifies an Italian package whatever the
  setting, and the link needs no local data.

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
  "Leaflet" and "SPC".
- Today `_documentsRow` exists only when the catalogue is on and its
  country is Italy (constructor, `catalogueContext.Country.Value ==
  "IT"`). Per §3.2 the row must be created in every case; leaflet and
  SPC keep their Italian-catalogue condition, the Codifa link does not.
- The link follows the AIC currently in the dialog. Three paths set
  it today through `UpdateDocumentLinks`: selection of a catalogue row
  (`ApplyReference`, which sets `_linkedNationalCode`), unlink
  (`ClearReferenceLinkage`, also fired when the user edits the name or
  ingredient), and the opening of an existing medicine
  (`HydrateSeededDocumentsAsync`, catalogue lookup by `NationalCode`). On opening, the Codifa link is
  set from `NationalCode` directly, before and independently of that
  lookup, which only runs with an Italian catalogue.
- `_documentsRow.Visible` becomes true when any of the three links is
  available. Today the row hides when leaflet and SPC are both
  missing.
- Click handling reuses `OnDocumentLinkClicked` (open with the shell,
  mark visited, warning dialog on failure).

Main grid (`MainForm.BuildGridContextMenu`):

- New item "Open Codifa page", after "Stock history" and before the
  separator of "Deactivate", with `Mdl2Glyph.Glyphs.OpenInNewWindow`.
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
