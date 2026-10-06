# Development status — 2026-10-05

Snapshot of what has shipped and what remains open, taken at
`main` = v2.16.0 plus PR #161 (commit `c6f1f58`), with only draft PR #106 open. Sources: `CHANGE_LOG.md`,
`docs/EVOLUTION.md`, `docs/EVOLUTION-DONE.md`,
`docs/notes/EVOLUTION-PROPOSALS.md`,
`docs/analysis/ANALYSIS-B1-MOBILE-SYNC.md`, the GitHub tracker.
Where this file and those documents disagree, they win; this file is
not maintained as a living document.

Updated on 2026-09-28 after #99, #101–#103 and #105 (Phase 4c, sync for every
profile, P8 residue closed, Google Cloud project published): §2.4,
§3.1, §4.

Reviewed on 2026-09-28 at v2.9.1, after #100, #104, #106 (draft),
#107 and #108 (therapy card PDF, text size): all sections.

Reviewed again on 2026-09-28 at v2.10.0, after #109–#112 (A2 webcam
and restock by scan shipped, multi-user G/I design): all sections.

Reviewed again on 2026-10-01 at v2.12.0, after #113–#155 (remote
catalogue feeds, household of devices with a master device, UI
modernisation, database-query logging): all sections.

Updated on 2026-10-02 after #160 and #162–#170 (the eight proposals of
`docs/notes/EVOLUTION-PROPOSALS-2.md`, on `main` after v2.12.1, not yet
released): §2.9, §3.3, §4.

Reviewed again on 2026-10-03 at v2.14.0, after #158, #159 and
#171–#177 (releases v2.12.1 to v2.14.0, signed release script, feeds
branch, stock that follows the dose times) and with #178 open: all
sections.

Updated on 2026-10-05 at `c6f1f58` (v2.16.0 plus #161), after #178–#200
(releases v2.14.1 to v2.16.0, package expiry, AIFA equivalents list,
repeatable prescriptions, regional prescription services, Store MSI):
header, §1, §2.11, §2.12, §3.3, §3.4, §3.5, §4. The counts in §1 were
taken at `c6f1f58`.

Updated on 2026-10-05 after the Android spike runs (S1–S4, draft
#106): §3.1, §4.

Updated on 2026-10-06: Android plan revised for a standalone app with
every current feature (`ANALYSIS-B1-ANDROID-PLAN.md`): §3.1, §4.

Tags: **[INFERRED]** for deductions, **[UNCERTAIN]** for claims not
verified against the tree or the tracker.

---

## 1. Codebase at a glance

| Item | Value |
|---|---|
| Latest release | v2.16.0 (2026-10-04); v2.14.1, v2.14.2 and v2.15.0 on 2026-10-03; v2.14.0 on 2026-10-03 |
| Projects | Domain, Application, Infrastructure.Portable (`net10.0`); Infrastructure (`net10.0-windows`); UI (WinForms, `net10.0-windows10.0.19041.0`); DataImporter |
| Source files | 542 `.cs` under `src/` |
| Test projects | 6 (one per project, plus UI and DataImporter); 252 `.cs` files, 1537 `[Fact]`/`[Theory]` attributes |
| Open pull requests | #106 — Android spikes S1–S4 (draft, not to be merged) |
| Open issues | #11 — Simplified Chinese localization (catalogue search disabled) |
| UI languages | en, it, fr, es, de |

---

## 2. Shipped

### 2.1 MVP and multi-user baseline

- Medicine stock, schedules, automatic consumption, low-stock and
  prescription reminders (toast, email via MailKit), tray, single
  instance.
- Profiles with administrator / standard roles, PIN, per-profile
  database and settings (PRs #22–#26).
- Reference drug catalogue with autocomplete (Italian data, see
  `docs/CATALOGUE-DATA.md`).
- Update check against GitHub releases (notice only, the app is never
  downloaded). Since v2.11.0 the same setting also refreshes the
  reference catalogues from the remote feeds (§2.6).

### 2.2 Evolution items (`EVOLUTION-DONE.md`)

| Item | Content |
|---|---|
| A1 | Complex regimens, including stepped taper |
| A3 | Caregiver notifications |
| A5 | Dose-time "remind me to take it" notification |
| A6 | Donation / support UI |
| C.3 | Encrypted `.mrz` export / import (`docs/EXPORT-FORMAT.md`) |
| C.3+ | Automatic backup to a user-chosen cloud folder, explicit restore, all profiles |
| C.3++ Phase 1 | `IArchiveStorage` + `LocalFolderArchiveStorage` (PR #56) |
| Website v1 | `vger70/medreminder-website`, live since 2026-09-25 |
| Multi-user G | Profile role change (Manage profiles → Change role…), shipped with household step H2a (#117, merged with #129); item I (all-profiles view) not planned |

### 2.3 Proposals from `EVOLUTION-PROPOSALS.md`

| Rank | Proposal | PR |
|---|---|---|
| 2 | Per-profile text size (Normal / Large / Extra large), display scaling, high contrast | #108 |
| 3 | A2 phase 1 — barcode scan with a USB HID scanner (Code 32 / DataMatrix parser, catalogue lookup) | #72 |
| 3 | A2 phase 2 — barcode scan with the webcam (Windows.Media.Capture, ZXing.Net) | #110 |
| 3 | A2 phase 3 — restock by scan: Stock → Restock from barcode | #111 |
| 4 | Prescription request draft for the doctor | #74 |
| 6 | Guided stock count with gap display | #73 |
| 8 | Read-only therapy timeline | #75 |
| 9 | Printable therapy card: table layout, PDF via Microsoft Print to PDF | #107 |

### 2.4 B.1 — synchronization track (desktop side)

| Phase | Content | PR | Release |
|---|---|---|---|
| 0 (partial) | S9 convergence prototype (10 000 seeds); S6 OneDrive and S7 Google Drive on Windows | #77 (reverted #78), #90 / #92 drafts not merged | — |
| 1 | `MedReminder.Infrastructure.Portable` (`net10.0`), `IArchiveReader`, overview loader in Application | #79 | v2.7.0 |
| 2a–2d | Every UI write through a use case; ledger schema; `LedgerDeriver`; ledger derived from facts; fact retraction; epoch fact id | #80–#84 | v2.7.0 |
| 3a–3d | HLC and operation log; apply with LWW and conflicts; count re-evaluation by HLC; encrypted segments, group key, checkpoints, compaction; folder transport; `SyncHostedService`; Tools → Sync… | #85–#89 | v2.8.0 |
| 4a | OneDrive sync transport and cloud backups (Graph REST, MSAL, DPAPI token cache) | #91, #93, #94 | v2.8.0 |
| 4b | Google Drive sync transport and cloud backups (Drive REST v3, loopback + PKCE) | #95 | v2.8.0 |
| 4c | Pairing codes and QR, key rotation, device removal, rekey with carry-over; sync available to every profile | #99, #101–#103 | v2.9.0 |
| P8 residue | Profile display name and notification recipients replicated (`ProfileSettingChanged`, operation schema 3) | #105 | v2.9.0 |

Result: two Windows PCs of the same user stay in sync through a shared
folder, OneDrive or Google Drive, end-to-end encrypted, without a
backend. Public format: `docs/SYNC-FORMAT.md`; manual exit check:
`docs/SYNC-TWO-PC-CHECKLIST.md`.

### 2.5 Changes v2.9.0 – v2.10.0

- v2.9.0: B.1 Phase 4c and P8 (§2.4); toasts and the dose-reminder
  email follow the application language, not the Windows language
  (#100).
- v2.9.1: therapy card printed as a paginated table and saved as PDF
  through "Microsoft Print to PDF" (#107); per-profile text size,
  display-DPI scaling and high-contrast colours (#108).
- v2.10.0: barcode scan with the webcam (#110); Stock → Restock from
  barcode, which identifies the medicine by its AIC code and pre-fills
  the quantity of its last new package (#111). With A2 phase 3, every
  A2 phase has shipped.
- Manual acceptance of the webcam (`ANALYSIS-A2-BARCODE-SCAN.md`
  §9.5): camera start, stop and error states work; decoding a real
  pack (item 7) is not confirmed, the test webcam's resolution was too
  low `[UNCERTAIN]`. Whether the restock checks of §5C.5 were run
  before the release cannot be verified from this repository
  `[UNCERTAIN]`.

### 2.6 Remote catalogue feeds (v2.11.0)

| PR | Content |
|---|---|
| #130, #131 | Italian catalogue refreshed at startup from the monthly AIFA feed published in this repository (`data/it/`) |
| #134, #135 | Same for EU (EMA EPAR), Spain (AEMPS) and France (BDPM): the reference country's feed and the EU feed |

Feeds are built by the `download_*.yaml` workflows into
`data/<country>/` (`docs/CATALOGUE-DATA.md`). The app downloads a
feed only when it is newer than the profile's catalogue, under the
startup update-check setting; data only, nothing is executed
(`docs/ANALYSIS.md` §9.5).

### 2.7 Household of devices with a master device (v2.12.0)

Steps H0–H5 of `analysis/ANALYSIS-HOUSEHOLD-MASTER-DEVICE.md`: H0 in
#115 (on `main`, v2.11.0), H1–H5 in #116–#128 on the integration branch
`feature/master-slave`, merged into `main` by #129 on 2026-09-30 after
the manual tests (`analysis/HOUSEHOLD-MANUAL-TESTS.md`).

| Step | Content | PR |
|---|---|---|
| H0 | Join waits for its own device record; join by passphrase asks which group | #115 |
| H1 | One low-stock email per sync group (`EmailNotificationSent`, profile schema 4) | #116 |
| H2 | Local household store; role change; installation settings as household state | #117, #118 |
| H3 | Household group on a storage, device keys and recovery escrow, join with an `mrpair2` code, Tools → Installation…, first-run join | #119–#123 |
| H4 | Master device: email and cloud backup on the master only, handover wizard, every profile checked on the master | #124–#126 |
| H5 | Device removal with new installation and profile keys | #127, #128 |

Fixes on the integration branch before the merge: #136 (pairing code
shown on request), #138 (unchanged recipients of a legacy profile
group); #141 merged v2.11.0 into it.

Every device of a sync group needs v2.12.0 or later (profile
operation schemas 4 and 5). Tools → Sync… is admin-only.

### 2.8 UI modernisation (v2.12.0)

Plan in `analysis/ANALYSIS-UI-MODERNIZATION.md` (§6b status: every
step done).

| PR | Content |
|---|---|
| #143, #144 | Phase 0 review; theme layer (light, dark, high-contrast palettes) and per-profile appearance setting |
| #145 | Dark-mode and layout defects of the screenshot baseline |
| #146 | Main window: summary cards, navigation pane, toolbar with search (Ctrl+F) |
| #147 | Settings: section list instead of tabs, resizable window |
| #148–#150 | Dialog template (`DialogLayout`), inline errors, confirmations in the app language |
| #151, #152, #154 | Documentation; dark text box borders; own message and choice dialogs, section lists in Sync and Installation |

Also in v2.12.0: Settings → General → Log database queries
(diagnostics), administrators only (#153). The user guides were
rewritten around tasks and the household features (#142).

### 2.9 Proposals from `EVOLUTION-PROPOSALS-2.md` (v2.13.0)

Second round of proposals (#160), benchmarked against similar apps and
implemented in the recommended order 1 → 5 → 2 → 4 → 3, then 6, 7, 8.
Each item has a status paragraph in the proposals note.

| Rank | Proposal | PR | Sync / format impact |
|---|---|---|---|
| 1 | Second low-stock warning at half of the warning threshold | #162 | `Stage` on notification events and sent emails; `EmailNotificationSent` version 6 for a second-stage email; image schema 4 |
| 5 | Supply planner for a chosen period (Therapy → Plan supply…) | #163 | none |
| 2 | Prescription lifecycle: requested, issued, collected; reminder before "valid until" (Therapy → Prescriptions…) | #164 | `PrescriptionChanged`, operation schema 7; image schema 5; export field `prescriptions` |
| 4 | Actions in Windows notifications: open the medicine or the list, prepare the prescription request, snooze a dose reminder by 15 minutes | #165 | none |
| 3 | AIFA shortage list: Supply column, notice once per shortage (Italy) | #166, #167 | none (reference data under `data/it/shortages/`); #167 keeps feed files byte for byte (`.gitattributes`) |
| 6 | Administrative deadlines (therapeutic plan, exemption renewal, check-up), optional recurrence (Therapy → Administrative deadlines…) | #168 | `DeadlineChanged`, operation schema 8; image schema 6; export field `deadlines` |
| 7 | Calendar export (`.ics`, Therapy → Export to calendar…); run-out date attached to low-stock emails | #169 | none |
| 8 | Caregiver: copies per kind of email, weekly stock summary | #170 | replicated profile settings `CaregiverEmails`, `CaregiverDigest`, `CaregiverDigestSentOn` (no schema bump); additive export fields |

Every device of a sync group must run a build with operation schema 8
and image schema 6 before prescriptions or deadlines are used; an older
app stops at the first operation it cannot read (R7). Released in
v2.13.0. The Windows-only test projects (`Infrastructure.Tests`,
`UI.Tests`) were run by the maintainer before v2.14.0.

### 2.10 Changes v2.12.1 – v2.13.1

| PR | Content | Release |
|---|---|---|
| #137, #158 | Local Certum-signed release build in `release.ps1`; `publish-signed-release.ps1` chains the GitHub release, the CI wait, the signed build with `signtool verify` and the upload of the signed assets | v2.12.1 |
| #159 | Remote catalogue feeds checked once a day while the app stays open | v2.12.1 |
| #171 | Status snapshot of the second round of proposals | — |
| #172–#174 | Feed workflows moved off the top of the hour; feeds published on a single-commit `feeds` branch instead of `main`'s history; AIFA shortage list fetched daily | v2.13.0 |
| #175, #176 | A maximized main window goes to the tray on the first close; the main window reopens at its last size, position and state, per profile; all data sources listed in About; text box borders no longer flicker | v2.13.1 |

### 2.11 Stock that follows the dose times; as-needed doses (v2.14.0)

Analysis and status: `analysis/ANALYSIS-INTRADAY-CONSUMPTION.md`;
summary in `EVOLUTION-DONE.md` §12.5.

| PR | Content | Sync / format impact |
|---|---|---|
| #177 | As-needed slots never consumed automatically, no reminder; under PRN the slots consume nothing; "Extra dose as needed" in the intake dialog; existing "As needed" slots corrected once from today | `IntakeRecorded.IsExtra` and `SlotValue.IsAsNeeded`, operation schema 9; image schema 7; additive export fields `isAsNeeded`, `isExtra` |
| #177 | Therapy → Dose times…: editable and user-defined time-of-day presets, times of medicines without slots; slots keep their preset (`PresetId`) | device-local, not replicated; `SlotValue.PresetId` display only (no version); additive export fields |
| #177 | Main list: stock after today's doses whose time has passed, refreshed every minute; run-out date, coverage, recorded stock and low-stock monitor keep the start-of-day stock | none |
| #178 | Days left counted from the stock shown; user guides and architecture documents updated (v2.14.1) | none |
| #194 | Review findings: PRN switch and backfill keep slots, imported archives keep the user's choices, main list reload and catch-up fixes (v2.16.0) | none |

Every device of a sync group must run v2.14.0 before anyone records an
extra intake or flags an as-needed slot; an older app stops at the
first operation of schema 9 (R7). Past as-needed consumption is not
given back: one stock count per affected medicine recovers it. The
maintainer ran the Windows-only test projects on #177 before v2.14.0;
whether they ran on #178 before v2.14.1 cannot be verified from this
repository `[UNCERTAIN]`.

### 2.12 Changes v2.14.1 – v2.16.0 and after

Sources: the `CHANGE_LOG.md` entries of each PR.

| PR | Content | Release |
|---|---|---|
| #178 | Days left counted from the stock shown (§2.11) | v2.14.1 |
| #179, #180 | Dose times order, slots lost in Advanced mode, start date not saved; slots place the quantity of advanced schedules (#180, stacked on #179) | v2.14.2 |
| #181, #182 | Date pickers with static Segoe UI; slot presets ordered by time of day, resizable navigation pane | v2.14.2 |
| #183–#187 | Package expiry (proposal 1 of `EVOLUTION-PROPOSALS.md`): analysis, packages with expiry and in-use period, package list, expiry notices with lead days, Stock → Expiring packages | v2.15.0 |
| #188 | Export snapshot test isolated in its own scratch folder | v2.15.0 |
| #189, #190 | AIFA equivalent medicines list (transparency list feed, Equivalent medicines window) and Codifa info link, Italy only | v2.16.0 |
| #191 | SMTP provider section in the user guides | v2.16.0 |
| #192, #193 | Review fixes of the daily catalogue check; dated-list feeds hardened, feed workflows publish concurrently | v2.16.0 |
| #195, #196, #198, #200 | Implementation prompts: repeatable prescriptions, regional prescription services, guided setup (corrected by #200) | v2.16.0 |
| #197, #199 | Repeatable prescriptions with several dispensations (`DispensationChanged`, operation schema 12 for repeatable prescriptions only, image schema 9, export `schemaVersion` 3 when present); regional prescription service link and "Paste NRE" (#199, stacked on #197) | v2.16.0 |
| #161 | Self-contained MSI for the Microsoft Store (`dotnet-desktop.yml`, `release.ps1`, `publish-signed-release.ps1`, `store` branch on GitHub Pages); MSIX scaffolding removed | not released |

Every device of a sync group must run v2.16.0 before anyone records a
repeatable prescription (operation schema 12).

---

## 3. Open work

### 3.1 B.1 — remaining phases (decided sequence, item 1)

| Phase | Content | Effort `[INFERRED]` | Blocking inputs |
|---|---|---|---|
| 5 (Android: M0–M5 of `ANALYSIS-B1-ANDROID-PLAN.md`, proposed) | Standalone Android app with every applicable desktop feature, guided setup included: M0 portability refactor 2 and notification planner; M1 standalone core (first release); M2 cloud backup, sync, household, profiles, roles and PIN; M3 prescriptions, planning, views; M4 catalogue, scan, Italian services; M5 email and support | 110–160 d | D4, D11, D13, DA1–DA5; Play Console account (spikes S1–S5 and S8 done) |
| 6 | iOS | 15–25 d | Phase 5; macOS host; Apple Developer Program; S1, S3, S5 on iOS |
| 7 | iOS feature parity (for Android absorbed by M2–M5); the state-hash check is not implemented on any device and stays a B.1 item | not re-estimated | Phase 6; D14 |

Household step H6 (household creation and join on the phone, phone as
master, QR decode on the PC webcam) belongs to Phases 5 and 7, plus
5–8 d (`ANALYSIS-HOUSEHOLD-MASTER-DEVICE.md` §13).

Open prerequisites and debts inside B.1:

- **Spikes**: S1–S4 pass on Android 13, 14 and 16 with the Release
  defaults; S2 takes at most 2.0 s on the low-end phone (Galaxy A32 4G,
  3.6 GB); full trimming breaks reflection-based JSON and EF Core,
  so the client keeps `TrimMode=partial` (B.1 analysis §18.0–§18.4; tool
  in draft PR #106, not to be merged). S5 passes on Android 16: exact
  alarms fire within 4 s, also after a reboot, and need
  `SCHEDULE_EXACT_ALARM` granted by the user (§18.5). S8: the
  15-minute WorkManager job runs every 1 to 4 hours, so background sync
  is best effort (§18.8). S6 and S7 lack their Android half.
- **Decisions open**: D4 (notification defaults per device), D11
  (strip-target exclusion for mobile; S4 passed, recommendation:
  reject), D13 (minimum OS versions), D14 (donation links on iOS).
- **Known sync limit closed** (shared by OneDrive and Google Drive): a
  device that joined while the listing lagged, while another device
  compacted, ended in `RebuildRequired`. The join now waits until the
  provider lists its record and picks another image if segments were
  deleted meanwhile (B.1 analysis §20, 2026-09-28).
- **P15**: store accounts and macOS build host are product-owner
  actions.

Total remaining B.1 effort, phases 5–7 only: about 75–115
developer-days `[INFERRED — from the §13.1 estimates]`.

### 3.2 Other decided items

| Item | Status | Effort `[INFERRED]` |
|---|---|---|
| C.1 — hosted relay | Optional extra sync transport; only if the product owner accepts operating a service | months + running cost |
| C.3++ Phase 3 — Dropbox, enterprise REST providers | Optional | not estimated |

### 3.3 Proposals with an implementation prompt ready

- Guided setup after the first start (item M2 of the evolution plan of
  2026-10-04, kept outside this repository):
  `docs/prompt/PROMPT-GUIDED-SETUP.md` (#198, corrected by #200). Not
  implemented.

Every proposal of `EVOLUTION-PROPOSALS-2.md` §3 has shipped (§2.9); its
§4 and §5 (re-assessed backlog items, proposals not recommended) remain
notes. `PROMPT-REPEATABLE-PRESCRIPTION.md` and
`PROMPT-REGIONAL-PRESCRIPTION-SERVICES.md` are implemented (#197, #199)
but still in `docs/prompt/`, not yet moved to `Completed/`. The two
website prompts are covered in §3.5. The others are in `docs/prompt/Completed/`, including
the multi-user roles prompt (item G shipped with household step H2a;
item I was not built) and the two remote-feed prompts.

### 3.4 Proposals without design

From `EVOLUTION-PROPOSALS.md`, not started (proposal 1, package
expiry tracking, shipped in v2.15.0, §2.12): 5 weekly
pill-organizer preparation (interaction with A5 needs a decision),
10–16 and 18–19 (storage location, shared household stock, database
encryption at rest, text-to-speech, cost tracking, Windows 11 widget,
WebDAV target, command palette, CLI). The household of devices (§2.7)
shares profiles and settings between PCs, not the stock of one
medicine between profiles: proposal 11 is still open.

Left out of the dose-time feature (§2.11, `EVOLUTION-DONE.md` §12.5):
minimum-quantity alerts for as-needed medicines (they have no
forecast, so no low-stock warning), time-of-day presets synchronized
between devices, dose reminders at preset times, per-slot intake
tracking.

### 3.5 Other open items

- **Automatic update** (`docs/AUTO_UPDATE.md`): design proposal, not
  implemented.
- **Code signing**: done. The signed local build (#137) and the release
  script (#158) are on `main`, and the published executables are
  signed (Certum, confirmed by the maintainer on 2026-10-03). CI still
  publishes unsigned packages that the script replaces with the signed
  ones (`docs/PACKAGING.md` §25); signing in CI is open. `README.md` and
  the user guides now describe signed binaries (#178).
- **Microsoft Store**: the self-contained MSI (#161) is on `main`, not yet
  released; the Partner Center submission is a product-owner action
  (`docs/PACKAGING.md` §26).
- **UI, known limitations** (`ANALYSIS-UI-MODERNIZATION.md` §6b): in
  dark mode the date and time pickers keep a white field (a dark picker
  needs a replacement control); the Windows MessageBox remains on the
  start-up and crash paths; the extra row height of Name and Active
  ingredient in the medicine editor at 150 % + Large is still open.
- **Issue #11**: Simplified Chinese localization.
- **Website**: `PROMPT-WEBSITE-CONTENT-REFRESH.md` is live on the site;
  whether `PROMPT-WEBSITE-V2.6-REFRESH.md` has been applied cannot be
  verified from this repository `[UNCERTAIN]`. Neither covers v2.7–v2.14
  (sync, cloud providers, therapy card PDF, text size, webcam scan,
  restock by scan, remote catalogue feeds, shared installation and
  master device, dark mode and the new main window, the
  `EVOLUTION-PROPOSALS-2` features, stock that follows the dose times
  and as-needed doses) `[INFERRED]`.
- **Multi-user I** (`ANALYSIS-MULTI-USER-ROLES-OVERVIEW.md`): a
  read-only "All profiles" stock view for an admin. Not planned. Item G
  (role change) shipped (§2.2).

### 3.6 Excluded by decision

- C.2 (live SQLite on a synced folder): rejected.
- Group D (national health-system integrations) and medical-device
  functions (adherence tracking, clinical alerts, interaction
  checks): out of scope under EU MDR 2017/745 positioning.

---

## 4. Suggested next steps `[INFERRED]`

0. Release #161 (Store MSI) and submit the MSI in Partner Center
   (`docs/PACKAGING.md` §26). Move the two implemented prompts to
   `docs/prompt/Completed/`. Implement the guided setup
   (`PROMPT-GUIDED-SETUP.md`).

The household feature shipped after its manual tests (§2.7). Every
device of a sync group or installation must run v2.12.0 or later
before anyone uses the household features (older apps cannot read
profile operation schemas 4 and 5).

1. Desktop: confirm webcam decoding on a real pack with a webcam of
   sufficient resolution (A2 checklist item 7).
2. UI: replace the date and time pickers if a fully dark mode is
   wanted (§3.5).
3. Website content refresh for v2.7–v2.16; the screenshots in
   particular predate the new main window and dark mode.
4. Mobile: the Android spikes are done (S1–S5, S8). Approve the
   standalone Android plan (`ANALYSIS-B1-ANDROID-PLAN.md`), then decide
   D11, D13 and DA1–DA5 (DA5 fixes the application id) before M1, D4
   before M2. Independent
   of items 1–3.
