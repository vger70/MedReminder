# EVOLUTION — Open evolution backlog

Working document listing candidate evolutions of MedReminder, prepared
in September 2026. It records options, tradeoffs and rejection
rationale so that future sessions and future maintainers do not
re-derive them from scratch. **It is not a commitment.** Individual
items become work only once explicitly approved and turned into a
dedicated analysis document (see `ANALYSIS-MULTI-USER.md` for the
pattern) or a GitHub issue.

**Split on 2026-09-25.** This file now holds only the open items.
Shipped items (A1, A3, A5, A6, C.3, C.3+, C.3++ Phase 1, website v1)
moved to `EVOLUTION-DONE.md`, rewritten to match what was
implemented. Section numbers are unchanged, so existing
`EVOLUTION.md §<n>` references elsewhere stay valid: a section that
moved keeps a one-line pointer here.

The two open non-goals from `ANALYSIS-MULTI-USER.md` §16 (profile
promote/demote and the consolidated admin view) remain deferred and
are **not** covered here — this document looks past the multi-user
baseline that landed with PRs #22–#26.

---

## 0. Reading conventions

Where a claim in this document could be wrong or is not verified
against a primary source, it is tagged:

- **[VERIFIED]** — established, publicly traceable knowledge.
- **[INFERRED]** — logical deduction from verified facts, flagged as
  inference.
- **[UNCERTAIN]** — no sufficient data at the time of writing; treat as
  a hypothesis, not a plan input.

Untagged claims are ordinary design opinion.

---

## 1. Scope

Included:

- **Group A** — low-friction extensions of the existing app.
- **Group C** — options for multi-device data movement
  (export/import, personal-cloud sync, end-to-end encrypted sync).
- **Item B.1** — mobile companion client.

Deliberately excluded from this document:

- **Group D** — integrations with national health-system services
  (electronic prescription, Fascicolo Sanitario Elettronico, pharmacy
  reservation). Access to these APIs by third-party consumer apps is
  today uncertain and region-dependent [UNCERTAIN]. When the picture
  stabilizes, they will be picked up in a dedicated analysis
  document, not here.
- **Dose-adherence tracking, clinical alerts, drug-interaction
  checks.** These would move MedReminder into the medical-device
  category (EU MDR 2017/745) and are outside the current product
  positioning stated in `CLAUDE.md` §1. Reopening them requires an
  explicit decision to become a CE-marked medical-device software.

---

## 2. Priority ordering

**DECIDED 2026-09-20 — sequence.** The product owner set the order
A6 → A5 → remaining items. As of 2026-09-25 A1, A3, A5, A6, C.3,
C.3+ and C.3++ Phase 1 have shipped (see `EVOLUTION-DONE.md`).

### 2.0 Remaining sequence

1. **A2 — AIC / barcode scan** (§3.2). Next Group A item in the
   decided sequence. Design in
   `docs/analysis/ANALYSIS-A2-BARCODE-SCAN.md` (USB HID scanner and
   webcam variants). No cross-item preconditions.
2. **B.1 — mobile companion client** (§7). Its precondition, C.3+,
   is met.
3. **C.3++ Phase 2 — native cloud providers** (§6). Only once B.1 is
   approved; the `IArchiveStorage` port it plugs into already exists.
4. **C.1 — end-to-end encrypted sync with a dedicated backend**
   (§8). Only if the product owner accepts turning the app into a
   service.

**C.2** (raw file-sync of the live SQLite database) stays **rejected**
— see §8.1.

Rules that still apply: do not ship B.1 without the C.3 / C.3+
foundation (met), and do not decide on C.1 before B.1. A2 is
independent of the multi-device track and can be reordered freely.

---

## 3. Group A — low-friction extensions

Common properties: each item is small, self-contained, does not
change the app's architectural posture (still local-first, single
Windows binary, not a medical device). Effort estimates are
grossly indicative and assume one developer familiar with the
codebase.

### 3.1 A1 — Complex therapy regimens

Shipped. See `EVOLUTION-DONE.md` §3.1.

### 3.2 A2 — AIC / barcode scan of medicine package

**Status.** Open. Next item in the decided sequence (§2.0).
Approved design: `docs/analysis/ANALYSIS-A2-BARCODE-SCAN.md`, which
covers both desktop variants: USB HID scanner (keyboard wedge) and
webcam. Where this section and the analysis disagree, the analysis
wins.

**Motivation.** Reduce data-entry errors and friction when adding a
medicine. The Italian AIC code (Autorizzazione all'Immissione in
Commercio) is printed on every package sold in Italy, as a Code 32
barcode, and uniquely identifies the medicinal product. The EU FMD
GS1 DataMatrix is being phased in for Italy (transition from
9 February 2025, mandatory from 9 February 2027).

**Design sketch.**

- On desktop, two input variants feeding one parser and one catalogue
  lookup:
  - **USB HID scanner** as keyboard input, captured in a dedicated
    field of a scan dialog. Default mode: no new dependency, no
    camera permission. A 1D scanner covers Code 32; DataMatrix needs
    a 2D imager.
  - **Webcam**, decoded in-process with ZXing.Net, started only on
    explicit request.
- On the mobile companion (see §7): the phone camera is the
  natural scanner; `ZXing.Net.Maui` or equivalent handles the
  decode.
- Once the AIC is captured, look it up in the local reference
  catalogue (see `ANALYSIS-DRUG-CATALOGUE.md` — the mechanism
  already exists) and populate the medicine record as a manual
  autocomplete pick does.

**Flows.** (a) add a new medicine by scanning its package; (b) restock
an existing medicine by scanning its package (medicine identified by
AIC, kind "new package", quantity from the last new-package movement;
no expiry/batch, which stock movements do not store).

**Phases.** DECIDED 2026-09-26, one PR each:

1. Shared core + USB HID scanner — flow (a).
2. Webcam — flow (a).
3. Flow (b) — only after flow (a) is complete and on explicit
   product-owner request. Not scheduled.

**Effort.** Phase 1 ~9–10 days, phase 2 ~6–7 days, phase 3 ~4–5 days.
Mobile path lands together with §7. [INFERRED]

**Risks.** Camera access adds a new permission surface on Windows;
handheld scanners are the safer default. HID scanners vary in suffix,
GS1 separator and keyboard-layout configuration; the parser rejects
garbled input by checksum. GTIN-only scans may not resolve to an AIC
once Code 32 disappears from Italian packs [UNCERTAIN].

### 3.3 A3 — Caregiver notifications

Shipped. See `EVOLUTION-DONE.md` §3.3.

### 3.4 A4 — Data export/import

Merged into C.3. See `EVOLUTION-DONE.md` §4.

### 3.5 A5 — Dose-time "remind me to take it" notification

Shipped. See `EVOLUTION-DONE.md` §3.5.

### 3.6 A6 — Donation / support UI

Shipped. See `EVOLUTION-DONE.md` §3.6.

---

## 4. C.3 — Manual export/import

Shipped. See `EVOLUTION-DONE.md` §4.

---

## 5. C.3+ — Backup to user-controlled cloud folder + explicit restore

Shipped. See `EVOLUTION-DONE.md` §5.

---

## 6. C.3++ — Native cloud provider integrations (Phase 2 onward)

**Status.** Phase 1 (`IArchiveStorage` + `LocalFolderArchiveStorage`)
shipped in PR #56 — see `EVOLUTION-DONE.md` §6. What remains is the
provider-specific work, deferred by decision.

**Authoritative analysis.**
`docs/analysis/ANALYSIS-C3PP-CLOUD-PROVIDERS.md` (§14 roadmap).

**Motivation.** C.3+ relies on a user-selected cloud-synced folder
and stays provider-independent: no OAuth, no SDK, no API quotas, no
lock-in. The driver for native backends is the future B.1 mobile
companion, where the Windows filesystem model does not map cleanly
to Android and iOS.

**Remaining phases** (from `ANALYSIS-C3PP` §14):

- **Phase 2 — when B.1 is approved.** `OneDriveArchiveStorage`
  (Microsoft Graph, MSAL token cache), `GoogleDriveArchiveStorage`,
  a `BackupSettings.CloudProvider` discriminator, provider selection
  and OAuth sign-in / sign-out in `SettingsDialog`, localization.
  Each backend must pass the existing `ArchiveStorageContractTests`.
- **Phase 3 — optional.** `DropboxArchiveStorage`; REST-only
  enterprise providers (SharePoint, Box, Nextcloud).
- **Phase 4 — re-evaluate on data.** iCloud on Windows stays on the
  synced-folder model unless Apple publishes a supported Windows SDK.

Priority among providers: OneDrive, then Google Drive, then Dropbox.
The `.mrz` archive format stays the interoperability contract.

**Mobile strategy.** The first B.1 release should use the platform
file picker (`ANALYSIS-C3PP` §6.1, Scenario A) rather than
provider-specific OAuth; native APIs can follow once the companion
has stabilized.

**Verdict.** Do not start Phase 2 before B.1 creates a concrete
requirement for it.

---

## 7. B.1 — Mobile companion client

### 7.1 Portable code already available

The projects `MedReminder.Domain` and `MedReminder.Application`
target `net10.0` with no Windows-specific dependencies. They are
reusable from a mobile host without modification. [VERIFIED against
`CLAUDE.md` §3]

### 7.2 What must be re-implemented per platform

`MedReminder.Infrastructure` targets `net10.0-windows` and hosts
platform-bound adapters:

| Current desktop adapter | Mobile substitute |
|---|---|
| DPAPI (`smtp.protected`) | iOS Keychain / Android Keystore via MAUI `SecureStorage` |
| Toast (WinRT) | Local notifications (MAUI `LocalNotification`) or push (APNs/FCM) |
| Tray icon | Not applicable |
| Backup on filesystem | Sandboxed app storage (`FileSystem.AppDataDirectory`) |
| Single-instance mutex | Not applicable — the OS enforces this |

EF Core with `Microsoft.EntityFrameworkCore.Sqlite` runs natively on
iOS and Android with no changes. MailKit runs on both.

### 7.3 UI framework choice

- **.NET MAUI** — Microsoft-official, XAML, iOS/Android/Windows/
  macOS. Best integration with the .NET ecosystem. Stability
  history through 2024 was mixed [INFERRED]; 2026 status not
  independently verified [UNCERTAIN].
- **Avalonia** — mobile production-ready from 11.x, strong desktop
  tradition. Smaller mobile ecosystem than MAUI.
- **Uno Platform** — WinUI 3 XAML across platforms, including web.
  Less widespread.

Recommended default: **MAUI**, on ecosystem grounds (documentation,
Visual Studio integration, sample density). Avalonia becomes the
better choice only if desktop Linux is also a target.

### 7.4 Effort

- Rough MVP (browse medicines, add/edit, low-stock notification,
  settings) with no sync: **2–4 developer-months**. [INFERRED —
  strongly dependent on developer experience with MAUI]
- Mobile UI is a **redesign**, not a port of the WinForms UI.
  Every screen must be re-thought for touch, scroll, stack
  navigation and system notifications.

### 7.5 Distribution

- **iOS**: Apple Developer Program ~99 USD/year; App Store review
  required; TestFlight for beta.
- **Android**: Google Play 25 USD one-off; more permissive review;
  APK sideload allowed.

Neither store rejects "medication reminder" apps as long as no
clinical claims are made — the existing disclaimer wording
(`CLAUDE.md` §1) is sufficient.

### 7.6 Localization

Reuses `assets/localization/strings.<lang>.json` unchanged if the
loader is a service exposed via a port from
`MedReminder.Application`. If it currently lives in
`MedReminder.UI` (WinForms-side), promote it to Application first.
This move should be part of the B.1 preparation, not a duplicate
translation effort.

### 7.7 Precondition

Do **not** ship B.1 without at least C.3+ in place. A mobile
client whose data does not connect to the desktop's data is a new
app, not a companion. **Met as of 2026-09-25:** C.3+ shipped, and
the `.mrz` format (`docs/EXPORT-FORMAT.md`) plus the
`IArchiveStorage` port (C.3++ Phase 1) are the contracts a
companion would read.

### 7.8 Verdict

Meaningful only when paired with C.3+ or C.1. As a standalone
effort, it is a large investment (months) for uncertain reward.

---

## 8. C.1 — End-to-end encrypted sync with dedicated backend

### 8.1 Rejection of C.2 (raw file-sync of the live SQLite DB)

Documented here rather than as a separate item because C.2 is a
tempting-looking non-option: it should be ruled out before C.1 is
considered.

- SQLite explicitly recommends **not** placing a live database on
  file-sync services (Dropbox, OneDrive, iCloud). The database
  file, its WAL sidecar and its SHM sidecar must land at the same
  logical time; sync services do not guarantee that ordering.
  [VERIFIED — SQLite FAQ, standing recommendation]
- Locking is filesystem-local. Two devices holding the same synced
  file cannot see each other's locks — concurrent writes produce
  data loss or corruption.
- File-sync conflict resolution produces `medreminder (conflicted
  copy).db`, which is meaningless to the average user.

C.2 is therefore rejected. Its only defensible variant is
**backup-of-a-snapshot on a cloud folder**, which is C.3+ (§5) —
not sync.

### 8.2 C.1 motivation

The only option that both scales to real multi-device operation
and respects the sensitivity of medical data. Its cost is the
switch from "distribute a binary" to "operate a service".

### 8.3 Architecture

Zero-knowledge model:

1. Master key derived from the user's passphrase via **Argon2id**
   (OWASP current recommendation as of writing). [VERIFIED at
   publication time]
2. Per-record symmetric key: **ChaCha20-Poly1305** or AES-GCM,
   sourced via **libsodium** (`NSec` is a mature .NET binding).
3. Server stores only: `user_id`, `record_id`,
   `encrypted_blob`, `timestamp`, `operation_type`. No content
   in cleartext.
4. Sync model: **append-only operation log** rather than snapshot
   merge. Each device publishes its own operations, pulls the
   others, replays locally. Simpler and less conflict-prone.

### 8.4 Multi-device bootstrap

Since MedReminder is single-user per account, no Signal-style
device pairing is required. The user re-enters the same passphrase
on the second device → same derived key → the server-stored blobs
decrypt. Force a printable **recovery kit** (mnemonic phrase) at
first-device setup to mitigate passphrase loss.

### 8.5 Backend

- **Stack**: ASP.NET Core (coherent with the rest), Postgres for
  the blob store, email+password auth **separate** from the E2E
  passphrase (the passphrase must never reach the server).
- **Hosting**: EU-based (Hetzner, Fly.io eu-region, DigitalOcean
  Frankfurt) for GDPR default. Raw cost order-of-magnitude
  10–30 EUR/month for a small user base. [INFERRED from 2024–2025
  price lists]
- **Backups**: encrypted blobs only, so off-site backups carry no
  additional privacy risk.

### 8.6 Non-technical cost

The reason C.1 is rated last:

1. **Perpetual operation** — TLS renewals, dependency patches,
   monitoring, incident response, on-call. A running service
   cannot be "shipped and forgotten".
2. **Recoverability** — passphrase loss = data loss. Intrinsic
   to zero-knowledge, not a bug. Requires prominent
   onboarding UX.
3. **Business model change** — from free local app to service
   with recurring cost. Consider **self-host + optional hosted
   service** (the Bitwarden/Vaultwarden and Nextcloud pattern);
   it is the pattern that best preserves the local-first DNA of
   MedReminder.
4. **GDPR posture** — ciphertext blobs tied to identifiable
   `user_id` are still personal data under art. 4 GDPR. If the
   underlying (encrypted) content is health-related, art. 9 still
   applies to the processing regardless of encryption.
   [INFERRED — the cautious legal reading; obtain qualified
   advice before production]

### 8.7 Prior art

- **Bitwarden** — open source, documented key-derivation and
  blob-storage architecture. Recommended primary reference.
- **Standard Notes** — similar zero-knowledge model, well
  documented.
- **Peer-to-cloud** synchronization via OneDrive/GoogleDrive

Study these before designing anything from scratch.

### 8.8 Verdict

Warranted only if the product owner accepts the shift from
"desktop tool" to "small service" and the perpetual cost that
comes with it. Do not enter unless that decision has been made
explicitly.

---

## 9. Explicitly excluded — and why

### 9.1 Group D — national health-system integrations

Kept out of this document because access to Fascicolo Sanitario
Elettronico, electronic prescription systems and pharmacy
reservation APIs by third-party consumer software is today
uncertain, region-fragmented in Italy, and typically mediated by
credentialed professional roles. [UNCERTAIN — status as of writing]
When the picture stabilizes, this becomes its own analysis
document rather than a section here.

### 9.2 Medical-device functions

Adherence tracking ("did you take the 08:00 dose?"), clinical
alerts, drug-interaction checks and dose-safety warnings all fall
under EU MDR 2017/745 depending on their stated intended use.
[INFERRED — MDR classification depends on the declared intended
use]. Adding them turns MedReminder into a medical-device software
requiring CE marking. Not a coding decision — a product-strategy
decision that must precede any implementation attempt.

---

## 10. Public presentation website

v1 shipped on 2026-09-25 in `vger70/medreminder-website`. See
`EVOLUTION-DONE.md` §10 for what was built and how it deviates from
the design. A content refresh to align the site with the current
application is prepared in
`docs/prompt/PROMPT-WEBSITE-CONTENT-REFRESH.md`.

---

## 11. Change log for this document

- 2026-09-19 — initial draft. Group A, C.3, C.3+, B.1, C.1
  documented. C.2 rejected with rationale. Group D and
  medical-device features explicitly excluded.
- 2026-09-20 — added A5 (dose-time "remind me to take it"
  notification). §2 priority ordering updated with the A1 → A5
  precondition.
- 2026-09-20 — added A6 (donation / support UI) from the draft in
  `docs/DONATION-SUPPORT-FEATURE.md`. Classified as a Group A
  item (low-friction extension, UI + configuration only, no
  backend, no schema patch). §2 priority ordering rewritten with
  an explicit inside-Group-A cost/benefit sequence:
  A6 → A2 → A3 → A1 → A5, with the A1 → A5 dependency preserved.
- 2026-09-20 — recorded the product owner's decided implementation
  sequence in a new §2.0: **A6 → A5 as the top two priorities**,
  followed by all remaining planned items (A2 → A3 → C.3 → C.3+ →
  B.1 → C.1; A1 already [DONE]). The A1 → A5 hard dependency is
  satisfied because A1 is [DONE]. The original cost-ordered
  reasoning in §2 is retained but is now subordinate to §2.0.
- 2026-09-20 — §3.5 and §3.6 reconciled with their now-approved
  analysis documents. §3.5: added two "Correction" notes flagging
  that the deduplication hints in the original sketch
  (`MedicationScheduleHistory` table pattern; "share the
  deduplication history table") are wrong / superseded by
  `ANALYSIS-A5` §3.2 and §4.6, which key dose reminders on
  `(MedicineId, SlotKey, LocalDate)` in a dedicated
  `DoseReminderEvents` table and keep the low-stock and dose paths
  on separate tables. §3.6: added an "Authoritative analysis" note
  that the "Help menu" entry point is not documented in the
  `ANALYSIS.md` MVP and must be confirmed against the tree
  (`ANALYSIS-A6` §9.1). No priority-ordering change.
- 2026-09-21 — marked A5 (§3.5) and A6 (§3.6) as **[DONE]**.
  Combined with the pre-existing A1 (§3.1), every Group A item that
  §2.0 lists as a top priority has now shipped. §2.0 items 1 and 2,
  the "Inside Group A" cost-ordered list items 1 and 5, and the two
  section headers were flagged accordingly. The remaining Group A
  items (A2, A3) and the multi-device track (C.3 → C.3+ → B.1 → C.1)
  are unchanged.
- 2026-09-23 — added §10 (Public presentation website). Captures the
  motivation, design sketch (Hugo, Cloudflare Pages, five-language
  static site, no backend), effort estimate, open decisions, and pointers
  to the authoritative analysis (`ANALYSIS-WEBSITE.md`) and
  implementation prompt (`PROMPT-WEBSITE-IMPLEMENTATION.md`).
- 2026-09-25 — split into `EVOLUTION.md` (open items) and
  `EVOLUTION-DONE.md` (shipped items, rewritten to match the
  implementation). Marked C.3, C.3+, C.3++ Phase 1 and website v1
  as shipped. §2 rewritten around the remaining sequence
  A2 → B.1 → C.3++ Phase 2 → C.1. §6 narrowed to the deferred
  native-provider phases. §7.7 records that the C.3+ precondition
  is met. Section numbers kept, with pointers for moved sections.
- 2026-09-25 — §2.0 and §3.2: A2 design now points to
  `ANALYSIS-A2-BARCODE-SCAN.md` (renamed from
  `ANALYSIS-A2-BARCODE-WEBCAM.md`), which covers both the USB HID
  scanner and the webcam variant. §3.2 design sketch, effort and risks
  updated accordingly; Code 32 named as the Italian AIC barcode and
  the Italian FMD DataMatrix timeline added.
- 2026-09-26 — §3.2: recorded the A2 flows (a: new medicine, b:
  restock) and the decided phases HID → webcam → flow (b), the last
  deferred until flow (a) is complete and the product owner requests
  it. Effort split per phase.
