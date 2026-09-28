# Development status — 2026-09-28

Snapshot of what has shipped and what remains open, taken at
`main` = v2.9.1 (commit `087c25d`). Sources: `CHANGE_LOG.md`,
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

Tags: **[INFERRED]** for deductions, **[UNCERTAIN]** for claims not
verified against the tree or the tracker.

---

## 1. Codebase at a glance

| Item | Value |
|---|---|
| Latest release | v2.9.1 (2026-09-28) |
| Projects | Domain, Application, Infrastructure.Portable (`net10.0`); Infrastructure (`net10.0-windows`); UI (WinForms); DataImporter |
| Source files | 365 `.cs` under `src/` |
| Test projects | 6 (one per project, plus UI and DataImporter); 166 `.cs` files, 910 `[Fact]`/`[Theory]` attributes |
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
- Update check against GitHub releases (notice only, no download).

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

### 2.3 Proposals from `EVOLUTION-PROPOSALS.md`

| Rank | Proposal | PR |
|---|---|---|
| 2 | Per-profile text size (Normal / Large / Extra large), display scaling, high contrast | #108 |
| 3 | A2 phase 1 — barcode scan with a USB HID scanner (Code 32 / DataMatrix parser, catalogue lookup) | #72 |
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

### 2.5 Latest changes (v2.9.0, v2.9.1)

- v2.9.0: B.1 Phase 4c and P8 (§2.4); toasts and the dose-reminder
  email follow the application language, not the Windows language
  (#100).
- v2.9.1: therapy card printed as a paginated table and saved as PDF
  through "Microsoft Print to PDF" (#107); per-profile text size,
  display-DPI scaling and high-contrast colours (#108).

---

## 3. Open work

### 3.1 B.1 — remaining phases (decided sequence, item 1)

| Phase | Content | Effort `[INFERRED]` | Blocking inputs |
|---|---|---|---|
| 5 | Android full client (MAUI): screens, pairing scanner, provider sign-in, notification planner, WorkManager sync, secure storage, app lock, CI job, Play internal track | 40–60 d | spikes S1, S3 (and S2, S4, S5, S8); D4, D11, D13; Play Console account |
| 6 | iOS | 15–25 d | Phase 5; macOS host; Apple Developer Program; S1, S3, S5 on iOS |
| 7 | Feature parity on mobile (timeline, prescription request, catalogue and camera scan, mail device, `.mrz` export, PDF share, state-hash check) | 20–30 d | Phase 5 / 6; D14 |

Open prerequisites and debts inside B.1:

- **Spikes not run**: S1 AES-GCM on Android, S2 Argon2id cost on a
  low-end phone, S3 EF Core SQLite with trimming / AOT, S4
  `StripReleaseDebugArtifacts` on an Android build, S5 local
  notifications, S8 background sync. S6 and S7 lack their Android half.
  The spike app for S1–S4 is in draft PR #106 (not built yet, not to be
  merged; only its results go into §18 of the B.1 analysis).
- **Decisions open**: D4 (notification defaults per device), D11
  (strip-target exclusion for mobile, depends on S4), D13 (minimum OS
  versions), D14 (donation links on iOS).
- **Known sync limit** (shared by OneDrive and Google Drive): a device
  that joins while the listing lags, while another device compacts,
  ends in `RebuildRequired`; "Rebuild from the group" repairs it. A
  fix needs the join to wait until its own device record is listed.
- **P15**: store accounts and macOS build host are product-owner
  actions.

Total remaining B.1 effort, phases 5–7 only: about 75–115
developer-days `[INFERRED — from the §13.1 estimates]`.

### 3.2 Other decided items

| Item | Status | Effort `[INFERRED]` |
|---|---|---|
| A2 phase 2 — webcam scan (ZXing.Net) | Implemented, not released. Manual acceptance (`ANALYSIS-A2-BARCODE-SCAN.md` §9.5): camera start, stop and error states work; decoding (item 7) not confirmed, the test webcam's resolution was too low to read the code | — |
| A2 phase 3 — restock by scan (flow b) | Planned after phase 2 (product owner, 2026-09-28) | 4–5 d |
| C.1 — hosted relay | Optional extra sync transport; only if the product owner accepts operating a service | months + running cost |
| C.3++ Phase 3 — Dropbox, enterprise REST providers | Optional | not estimated |

### 3.3 Proposals with an implementation prompt ready

None. The last two (large text mode, medication card PDF) shipped in
v2.9.1; their prompts are in `docs/prompt/Completed/`.

### 3.4 Proposals without design

From `EVOLUTION-PROPOSALS.md`, not started: 1 package expiry tracking
(domain change: stock is one quantity per medicine), 5 weekly
pill-organizer preparation (interaction with A5 needs a decision),
10–16 and 18–19 (storage location, shared household stock, database
encryption at rest, text-to-speech, cost tracking, Windows 11 widget,
WebDAV target, command palette, CLI).

### 3.5 Other open items

- **Automatic update** (`docs/AUTO_UPDATE.md`): design proposal, not
  implemented.
- **Issue #11**: Simplified Chinese localization.
- **Website**: `PROMPT-WEBSITE-CONTENT-REFRESH.md` is live on the site;
  whether `PROMPT-WEBSITE-V2.6-REFRESH.md` has been applied cannot be
  verified from this repository `[UNCERTAIN]`. Neither covers v2.7–v2.9
  (sync, cloud providers, therapy card PDF, text size) `[INFERRED]`.
- **Multi-user non-goals** (`ANALYSIS-MULTI-USER.md` §16): profile
  promote / demote and the consolidated admin view. Not planned; to be
  picked up only if a concrete need emerges (product owner,
  2026-09-28).

### 3.6 Excluded by decision

- C.2 (live SQLite on a synced folder): rejected.
- Group D (national health-system integrations) and medical-device
  functions (adherence tracking, clinical alerts, interaction
  checks): out of scope under EU MDR 2017/745 positioning.

---

## 4. Suggested next steps `[INFERRED]`

Phase 4c and P8 shipped in v2.9.0. Whether
`docs/SYNC-TWO-PC-CHECKLIST.md` (K1–K11, P1–P4) was run before that
release cannot be verified from this repository `[UNCERTAIN]`. Every
device must run v2.9.0 or later before anyone changes a group key or
records a replicated profile setting (an app ≤ 2.8.x cannot follow).

1. Desktop: A2 phase 2 (webcam), then A2 phase 3 (restock by scan),
   one PR each (`ANALYSIS-A2-BARCODE-SCAN.md` §1.6).
2. Mobile: build and run the S1–S4 spike app of draft PR #106 on
   Android before committing to Phase 5; S1 and S3 are the go / no-go
   risks for MAUI. Independent of item 1.
3. Website content refresh for v2.7–v2.9.
