# Implementation prompt — Link to the regional prescription services

Briefing for the Claude Code session that implements item A4 of the
evolution plan prepared on 2026-10-04 (competitor benchmark and
cost/benefit plan, kept outside this repository). Read it fully, then
read the referenced files before changing code.

---

## 1. Goal

From the prescription windows, open in one click the service of the
profile's Italian region where the issued electronic prescriptions are
shown (a web portal in the browser, or the regional app on the phone
through a QR code), and bring the prescription code (NRE) back into
MedReminder without retyping it.

MedReminder hands over and receives only what the user pastes or scans.
It never signs in for the user, never embeds a browser, never sees SPID,
CIE or TS-CNS credentials, tokens or data of the health record
(Fascicolo Sanitario Elettronico). Direct integration with the health
record stays excluded (`docs/EVOLUTION.md` §9.1).

Why: the regional services hold the real prescription data, and today
the user copies it by hand. Example verified on 2026-10-04: the Lazio
health record opens from the Salute Lazio portal with SPID, CIE or
TS-CNS and shows the prescriptions; the SaluteLazio app (LAZIOcrea)
accepts SPID and CIE and supports family delegation.

Target effort: 4–6 days **[INFERRED]**. Release it with the repeatable
prescription (`PROMPT-REPEATABLE-PRESCRIPTION.md`) if both are ready,
since both change the prescription windows; it does not depend on it.

## 2. Context to read first

- `CLAUDE.md` — §5 (runtime data), §7 (no codes or medical data in
  logs, database gate).
- `docs/CATALOGUE-DATA.md` — the remote feeds, the `feeds` branch and
  the dated-list format (§1.1, §8, §9).
- `docs/SYNC-FORMAT.md` — replicated profile settings.
- `docs/EXPORT-FORMAT.md` — profile settings fields and §5 (additive
  fields).
- Feed side: `scripts/feeds/common.py` (`publish_dated_list`),
  `scripts/feeds/aifa_shortages.py` and `aifa_equivalents.py` as
  examples, `scripts/feeds/feeds_branch.sh`,
  `.github/workflows/download_aifa_shortages.yaml`,
  `.github/workflows/scripts_tests.yaml`, `scripts/feeds/tests/`.
- Application: `src/MedReminder.Application/Catalogue/DatedListFeed.cs`
  (definition, transport, store, refresh shared by the shortage and
  transparency lists), `ShortageFeed.cs`, `EquivalenceFeed.cs`,
  `MedicineInfoLink.cs`, `BarcodeParser.cs`;
  `UseCases/ProfileSettingsUseCases.cs`; `Abstractions/UserSettings.cs`
  (`ReferenceCountry` is an installation setting, not a profile one).
- Domain: `src/MedReminder.Domain/Sync/SyncOperationBodies.cs`
  (`ProfileSetting`: a new name needs no schema bump, an older app keeps
  it without projecting it); `Domain/Prescriptions/PrescriptionRules.cs`
  (`MaxCodeLength`).
- UI: `src/MedReminder.UI/Forms/PrescriptionsDialog.cs`,
  `PrescriptionEditDialog.cs`, `PrescriptionRequestDialog.cs`,
  `SettingsDialog*.cs`, `SyncPairingDialog.cs` (QR with `QRCoder`),
  `AboutDialog.cs` (opening a URL with `UseShellExecute`),
  `BarcodeScanDialog.cs`, `Camera/FrameBarcodeDecoder.cs`.

## 3. Design

### 3.1 Reference data: the regional services list

- One JSON list for Italy, published on the `feeds` branch under
  `it/regional-services/` with `publish_dated_list` (prefix
  `regional-services-`, list date = the date of the publish), so the app
  reuses `DatedListFeed` with a new definition (`RegionalServicesFeed`)
  instead of a new transport.
- Source maintained by hand on `main`, for example
  `scripts/feeds/regional_services_it.json`, and published by a small
  script (`scripts/feeds/regional_services.py`) that validates it.
  A workflow publishes it when that file changes on `main`, and once a
  month checks every URL (HTTP status only) and opens an issue listing
  the ones that fail; it never edits the list on its own.
- One entry per region or autonomous province (21), keyed by the ISTAT
  region code:

  ```json
  {
    "regionCode": "12",
    "region": "Lazio",
    "service": "Salute Lazio",
    "webUrl": "https://www.salutelazio.it/scarica-il-tuo-referto",
    "iosAppUrl": "https://apps.apple.com/it/app/salutelazio/id1201847471",
    "androidAppUrl": null,
    "signIn": ["SPID", "CIE", "TS-CNS"],
    "showsPrescriptions": true,
    "familyDelegation": true,
    "verifiedOn": "2026-10-04"
  }
  ```

  The validator refuses: a missing or duplicate `regionCode`, a URL that
  is not `https`, an unknown `signIn` value, a `verifiedOn` in the
  future. An entry may have `webUrl` only.
- Initial content: Veneto (Sanità km zero Ricette) and Lazio (Salute
  Lazio) are verified; Lombardia, Campania, Toscana and Sardegna have
  candidate services from the Italian App Store Medicine chart of
  2026-10-04 and must be checked; the other regions are compiled during
  the work. An entry whose prescriptions are not verified has
  `showsPrescriptions: false`; never publish a guessed URL.
- A copy of the list ships with the application for the first start
  and for an installation that never downloads feeds; the downloaded
  list wins when newer. Store it once for every profile under
  `%LOCALAPPDATA%\MedReminder\catalogue\regional-services\`, as the
  shortage and transparency lists are stored. Update `CLAUDE.md` §5 and
  `docs/CATALOGUE-DATA.md`.

### 3.2 Domain and Application

- Domain: `RegionalHealthService` (the entry above) and `NreCode`
  (`TryParse`: drop spaces and dashes, upper case, exactly 15 letters
  or digits). No check digit: no public specification was found
  **[UNCERTAIN]**; do not invent one.
- Application: `RegionalServicesFeed : DatedListFeedDefinition<…>`,
  registered with the same refresh and the same update-check setting as
  the shortage list; `RegionalServiceForProfileQuery` returns the entry
  for the profile's region, or none when the reference country is not
  Italy, the region is empty or the list has no entry.
- Profile setting `ProfileSetting.Region` (ISTAT code, "" when not set),
  saved through `SaveProfileSettings` with the existing validation
  style (refuse a code not in the 21). Replicated as `CaregiverEmails`
  is: no operation schema bump. Additive export field; update
  `docs/EXPORT-FORMAT.md` and `docs/SYNC-FORMAT.md` (setting names).
- Opening a link goes through one helper (`SafeLinkLauncher` or a
  method next to the existing URL opening): only `https`, only a host
  present in the current list, no query parameters added, opened with
  the default browser. Refuse anything else and log the refusal without
  the URL path.

### 3.3 UI and user experience

1. **First use.** In Therapy → Prescriptions… and in the prescription
   request dialog, a button "Regional prescription service". With no
   region on the profile, the click opens a small dialog with the 21
   regions and saves the choice. The field is also in Settings, in the
   section that holds the profile's notification addresses. Hidden when
   the reference country is not Italy.
2. **Two choices on the button** (split button or a small menu):
   "Open in browser" opens `webUrl`; "Open on phone" shows a QR code of
   the app link (or of `webUrl` when no app link exists), rendered with
   `QRCoder` as in `SyncPairingDialog`, with a line saying to scan it
   with the phone camera.
3. **Information line** under the button: service name, sign-in methods
   from `signIn` ("sign in with SPID, CIE or TS-CNS"), and "MedReminder
   does not see your credentials or your health record". When
   `familyDelegation` is true, add that a caregiver signs in with their
   own credentials and a delegation set up on the regional service.
   When `showsPrescriptions` is false, say the service is not verified
   for prescriptions yet.
4. **Bringing the code back.** In the prescription edit dialog, a
   "Paste NRE" button next to Code: it reads the clipboard only when
   clicked, accepts the text when `NreCode.TryParse` succeeds, writes
   the normalised code, and pre-fills the issue date with today when it
   is empty. Otherwise an inline error under the field, in the style of
   `DialogLayout`.
5. **Scanning the paper slip (optional, last step).** The NRE is printed
   under two barcodes whose characters together make the 15. Their
   symbology is not verified **[UNCERTAIN]**: check it on a real slip
   first. If the webcam decoder or a USB scanner reads them, accept the
   two parts into the Code field in either order only when they join
   into a valid `NreCode`, and keep them out of the AIC path of
   `BarcodeParser`. If they cannot be read reliably, leave scanning out
   and say so in the PR.
6. **No region entry.** The button stays, with a message pointing the
   user to the health record portal of their region; no link is
   guessed.

All new strings in the five `assets/localization/strings.<lang>.json`;
the five `docs/USER_GUIDE.<lang>.md` get a short section on the button,
the QR code and "Paste NRE". Wording describes opening a service, never
importing prescriptions.

## 4. Constraints

- No embedded browser (WebView2 stays for the help only), no sign-in,
  no tokens, no reading of the regional service's pages.
- The NRE is never logged (as for the prescription code today); log
  lines name the region code only ("Regional service opened: 12").
- No write outside `%LOCALAPPDATA%\MedReminder\`; respect the database
  gate.
- The list is data only, nothing is executed (`docs/ANALYSIS.md` §9.5).
- Not a medical device: the feature is a link and a code field.

## 5. Workflow

- Ask before creating the branch; proposed name
  `feature/regional-prescription-services`. Open the PR after the first
  commit, prepend a `CHANGE_LOG.md` entry, ask the user to run
  `dotnet build` and `dotnet test` before committing source code.
- Suggested commit order: feed source, validator, publisher, workflow
  and Python tests; Domain (`NreCode`, entry) with tests; feed
  definition, store, query and shipped copy; profile setting, export
  and documents; UI; scanning if verified; user guides.

## 6. Verification

- Python: validator accepts the sample, refuses each invalid case; the
  publish writes `latest.json` with size and SHA-256 like the other
  dated lists (`scripts/feeds/tests/`).
- Domain: `NreCode.TryParse` truth table (spaces, dashes, lower case,
  14 and 16 characters, non-alphanumeric).
- Application: query returns nothing outside Italy, with an empty
  region, or with no entry; the downloaded list wins over the shipped
  copy only when newer; the region setting replicates between two
  devices and an older app keeps it without projecting it; export round
  trip with and without the field.
- Link helper: refuses `http`, a host not in the list, a non-URL value.
- Manual check in the running app: choose Lazio, open in browser, sign
  in with SPID on the portal, copy an NRE, paste it with "Paste NRE";
  open the QR code and scan it with a phone; switch the reference
  country away from Italy and check the button is gone.
