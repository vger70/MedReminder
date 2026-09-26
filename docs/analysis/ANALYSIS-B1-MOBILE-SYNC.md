# ANALYSIS — B.1: Mobile client with desktop synchronization

Design document, **prior** to implementation. Corresponds to
`EVOLUTION.md` §7 (item B.1). Revision 2: synchronization between the
mobile app and the desktop is a **mandatory** part of the end result,
not an optional later phase. Delivery is phased; the end state is a
complete, bidirectional, multi-device product.

This revision absorbs three items that `EVOLUTION.md` kept separate:

- **C.3++ Phase 2** (native cloud providers, `EVOLUTION.md` §6): the
  sync transport needs provider APIs on the phone, so it becomes part of
  B.1.
- **The functional goal of C.1** (`EVOLUTION.md` §8): real multi-device
  operation with end-to-end encryption. B.1 reaches it **without a
  backend service**: the user's own cloud storage is the only
  transport. C.1 as "operate a service" stays out of scope; the design
  keeps a transport port so a backend could be added later without
  touching the sync model (§5.9).
- **The A2 mobile path** (`EVOLUTION.md` §3.2): barcode scan with the
  phone camera, in the completion phase.

C.2 (sharing the live SQLite file through a synced folder) stays
rejected; §3.2 restates why.

Where this document and `EVOLUTION.md` §6–§8 disagree, this document
wins. §17 lists the corrections to apply to other documents.

Epistemic classification: `[VERIFIED]` (checked against the current
tree, or against a publicly traceable primary source), `[INFERRED]`
(deduction from verified facts), `[UNCERTAIN]` (hypothesis pending
confirmation, usually by a Phase 0 spike). Untagged statements are
design decisions proposed by this document.

> The "Decisions still to confirm" section (§16) is the only zone of
> open choice. Every phase in §13 names the decisions and spikes it
> depends on; a phase does not start while one of them is open.

---

## 1. Scope

### 1.1 Problem

MedReminder runs only on the Windows PC that holds the profile
database. Away from the PC the user cannot check stock, record a new
package or an intake, or receive a dose reminder unless email is
configured. The desktop can already write encrypted `.mrz` snapshots to
a cloud-synchronized folder (C.3+), but these are backups: restoring
one overwrites the target profile (`IImportService`, overwrite-only)
`[VERIFIED]`.

### 1.2 End-state goal

1. A mobile app (Android, then iOS) that is a **full client** of a
   profile: it reads and writes the same data the desktop manages
   (§10 lists feature parity and the justified exclusions).
2. **Bidirectional, automatic, end-to-end encrypted synchronization**
   between any number of devices of the same profile: desktop ↔ phone,
   phone ↔ phone, desktop ↔ desktop.
3. **Offline-first**: every device works fully offline and converges
   when it reconnects, with no data loss and no silent overwrite.
4. **No backend operated by the project.** Transport through the
   user's own cloud storage (OneDrive, Google Drive; a synced local
   folder for desktop-only setups). The cloud provider sees only
   ciphertext.
5. Deterministic conflict handling: every conflict resolves the same
   way on every device, and the ones that need human judgment are
   shown in a conflict review list.

### 1.3 What B.1 is NOT

- **Not a shared database file.** Each device keeps its own SQLite
  database; devices exchange encrypted operations (§3.2).
- **Not a hosted service.** No server, no account system of our own, no
  recurring cost (§5.9 keeps the door open).
- **Not a medical device.** No adherence scoring, clinical alerts or
  interaction checks (`EVOLUTION.md` §9.2). Intake records remain a
  stock-keeping aid, as on desktop.
- **Not a port of the WinForms UI.** The mobile UI is a redesign for
  touch (`EVOLUTION.md` §7.4).
- **Not sync of the profile registry, PINs, roles, SMTP settings or
  backup settings.** These are per-installation (§4.6).

---

## 2. Preconditions — current state

| # | Precondition | State | Evidence / closing phase |
|---|---|---|---|
| P1 | Public, versioned archive contract (used for bootstrap snapshots) | **Met** | `docs/EXPORT-FORMAT.md`; `ExportFormat` `[VERIFIED]` |
| P2 | Argon2id + AES-GCM primitives behind a port | **Met** | `IArchiveCipher`, `ArchiveCipher` `[VERIFIED]` |
| P3 | Storage port for remote files | **Partly met** | `IArchiveStorage` (upload, download, list, delete) `[VERIFIED]`; sync needs conditional writes and prefix listing (§5.8). Phase 3 (port and local folder), Phase 4 (providers) |
| P4 | Domain and Application portable (`net10.0`) | **Met** | csproj targets `[VERIFIED]` |
| P5 | Persistence usable outside Windows | **Not met** | EF Core model, repositories, `DatabaseInitializer` live in `MedReminder.Infrastructure` (`net10.0-windows`, `UseWindowsForms`) `[VERIFIED]`. Phase 1 |
| P6 | Archive read path usable outside Windows | **Not met** | `ImportService`, `ExportService`, `CloudRestoreService` are `[SupportedOSPlatform("windows")]`; `ImportService` uses DPAPI through `ICredentialProtector` `[VERIFIED]`. Phase 1 |
| P7 | View models outside WinForms | **Not met** | `MedicineOverviewLoader` is `internal` in `MedReminder.UI/Presentation` `[VERIFIED]`. Phase 1 |
| P8 | Every data write goes through an Application use case | **Not met** | `MainForm` writes directly (for example `medicine.IsActive = false`, `MainForm.cs` ~l.1204) `[VERIFIED]`. Phase 2 audit |
| P9 | Stock ledger is a deterministic function of user facts | **Not met** | Automatic consumption, backdated-intake reversals, stock-count corrections and `StockEpoch` are computed locally and depend on execution order (§3.4) `[VERIFIED — ANALYSIS.md §4.4]`. Phase 2 |
| P10 | Stable GUID identity on every replicated entity | **Met** | All entities use `Guid Id` generated at creation `[VERIFIED — Domain entities]` |
| P11 | Medicines are never hard-deleted | **Met** | Deactivation via `IsActive` `[VERIFIED]`; slots are replaced as a set by `UpdateMedicine` (`DeleteForMedicineAsync` + add) `[VERIFIED]` |
| P12 | AES-GCM on mobile | iOS 13+ on .NET 9+ **met**; Android `[UNCERTAIN]` | dotnet/runtime #91523 `[VERIFIED]`; spike S1 |
| P13 | EF Core SQLite on Android / iOS AOT | `[UNCERTAIN]` | Spike S3 |
| P14 | OAuth app registrations (Microsoft Entra public client, Google Cloud OAuth client) | **Not met** | Guide exists: `docs/notes/AZURE-ENTRA-PUBLIC-CLIENT-APPLICATION-GUIDE.md` `[VERIFIED]`. Phase 0 |
| P15 | Build hosts and store accounts (macOS for iOS, Apple Developer Program, Google Play) | **Not met** | Product-owner action; Phase 0 / 6 |
| P16 | Product-owner decisions D1–D15 | **Open** | §16 |

Consequence: two desktop-side refactors (Phase 1 portability, Phase 2
ledger derivation) precede any sync or mobile code. They are the
foundation; skipping them makes convergence impossible.

---

## 3. Synchronization model

### 3.1 Requirements

| # | Requirement |
|---|---|
| R1 | Every device can read and write while offline |
| R2 | All devices that have received the same set of operations have identical replicated state (strong eventual consistency). Derived rows are a pure function of replicated state **and the local date**, so they are identical on devices that share the same date (§4.3) |
| R3 | No operation is lost or applied twice, whatever the delivery order, duplication or delay. Only exception: pending operations discarded by an explicit, user-confirmed generation reset (§5.7) |
| R4 | The transport stores only ciphertext; file names and sizes leak no medical content |
| R5 | No file in the remote storage has more than one writer (sync clients cannot create conflict copies) |
| R6 | Stock numbers after convergence are those a single device would compute from the merged facts |
| R7 | A device running an older app version never applies operations it does not understand |
| R8 | Remote storage grows bounded (compaction) |

### 3.2 Why not a shared database file (C.2)

Restated from `EVOLUTION.md` §8.1 and extended for mobile:

- File-sync services copy whole files asynchronously; the `.db`,
  `-wal` and `-shm` files (WAL is on, `ANALYSIS.md` §8.1) can arrive
  out of order and yield a corrupt or inconsistent database
  `[VERIFIED — SQLite documentation, standing recommendation]`.
- SQLite locks are local to one filesystem; two devices cannot see each
  other's locks. Concurrent writes end in a lost version or a
  "conflicted copy" (violates R3, R5).
- The desktop writes continuously while in the tray (catch-up,
  notification and dose-reminder bookkeeping) `[VERIFIED — ANALYSIS.md
  §6]`, so there is no safe window for another writer.
- On a phone, a cloud folder reached through the platform picker is a
  content stream, not a file SQLite can lock and seek `[INFERRED]`.
- The live database is plaintext; R4 forbids putting it in the cloud.
- `CLAUDE.md` §7: one process owns each profile database.

### 3.3 Chosen model: replicated operation log

Each device keeps its own database. Every user write produces one or
more **operations** that are stored locally, applied locally, and
published to the remote storage in **per-device, append-only, encrypted
segments**. Every device downloads the other devices' segments and
applies their operations. Merge rules (§4) are commutative, associative
and idempotent, so the result does not depend on arrival order (R2,
R3). Each remote file has exactly one writer, its device (R5).

This is the operation-log model of `EVOLUTION.md` §8.3 with the
user's cloud storage as transport instead of a server.

### 3.4 The ledger problem and its solution

Today's stock ledger mixes user facts with values computed by the
device at a given moment `[VERIFIED — ANALYSIS.md §4.4, use cases]`:

| Row / value | Written by | Depends on |
|---|---|---|
| Automatic `Consumption` | `ConsumptionCatchUp` | Schedule, suspensions and intakes **known when the catch-up ran** |
| Intake `Consumption` | `RegisterIntake` | The intake |
| Backdated-intake reversal (`PositiveCorrection`) | `RegisterIntake` | Whether the automatic consumption **had already been materialized** |
| Stock-count correction | `ReconcileStock` | Expected stock **computed on this device** at count time |
| `Medicine.StockEpoch`, `StockMovement.StockEpoch` | `AddStock`, `ReconcileStock` | Local counter incremented in place |
| Current schedule fields on `Medicine` | `ChangeMedicationSchedule` | Latest local schedule row |

Replicating these rows as they are would break R2 and R6. Examples:
two devices materialize the same day's automatic consumption and stock
drops twice; two devices record a new package offline and both set
`StockEpoch = 5`; a stock count taken on the phone is replayed on the
desktop as a delta computed against a stale expected stock.

**Solution: split replicated facts from derived rows.**

- **Facts** are what a user asserts: "a new package of 28 arrived at
  T", "I took 1 tablet on day D", "I counted 40 tablets at T", "from
  day D the dose is X". Facts are replicated.
- **Derived rows** are computed from facts by a pure, deterministic
  function (`LedgerDeriver`, Domain). They are never replicated. Every
  device recomputes them after merging, and therefore gets the same
  result.

The stock count becomes a fact, `StockCount(id, medicineId,
countedAt, countDay, countedQuantity, takenToday)`: an **anchor**.
`takenToday` is the user-entered quantity already taken on the count
day, an input of today's `ReconcileStock` command `[VERIFIED —
ReconcileStockCommand.TakenToday]`. The derived correction is computed
with the **existing** `ReconcileStock` formula (moved to Domain
unchanged), evaluated on the merged ledger as of `countedAt`, so a
count taken on the phone is correct against whatever the desktop
recorded before it, and facts dated after the count apply on top of
it.

### 3.5 Genesis cutoff: preserving existing data

Rebuilding the entire history of an existing profile with the new
derivation could change past numbers (for example, a retroactive
schedule change that the catch-up never re-applied to days already
materialized). To keep existing data unchanged:

- The Phase 2 boot patch, on each existing profile database, marks
  every stock movement existing at that moment, including historical
  consumption and reversals, as a **frozen fact** (origin `Legacy`) and
  stores the profile's **cutoff day** C (the day before the patch). A
  profile created after the patch has no legacy rows and C = the day
  before its `StartDate`.
- `LedgerDeriver` produces derived rows only for days after C. Up to C
  the ledger is exactly today's.
- When sync is enabled later, the **genesis** snapshot (§5.5) carries
  the frozen facts and C unchanged; enabling sync does not move C.
- Consequence: a change dated on or before C does not rewrite frozen
  days, which is today's behavior. After C, retroactive changes are
  re-derived consistently (D6), with or without sync.

---

## 4. Data classification and merge rules

### 4.1 Hybrid logical clock

Every operation carries an HLC timestamp `(physicalMs, counter,
deviceId)` with a total order (ties broken by `deviceId`). HLC keeps
causality when device clocks drift and gives every device the same
order for last-writer-wins decisions. A received timestamp more than
24 h ahead of local time is still applied (convergence wins) but the
receiving device shows a clock warning naming the peer device (§7.4).

### 4.2 Classification

| Data | Class | Merge rule |
|---|---|---|
| `Medicine` scalar fields (name, ingredient, package, unit, threshold, doctor, notes, channels, `RemindOnDose`, catalogue link, end date) | Mutable record | Last-writer-wins **per field** by HLC |
| `Medicine.IsActive` | Activity history | Each deactivation / reactivation is a dated fact; the current value is the latest by HLC. The history is needed by the derivation (§4.3, D15) |
| `Medicine.StartDate` | Immutable | Set at creation; `UpdateMedicine` does not change it `[VERIFIED]` |
| `Medicine` current schedule summary (`DosePerAdministration`, `AdministrationsPerDay`) | Derived | From the most recently **recorded** schedule row (highest HLC), whatever its `EffectiveFrom`: `ChangeMedicationSchedule` sets these fields from the new row even when it takes effect in the future `[VERIFIED]` |
| `Medicine.StockEpoch`, `StockMovement.StockEpoch` | Derived | §4.4 |
| Administration slots of a medicine | Set-valued register | LWW on the **whole set** per medicine (matches `UpdateMedicine`, which replaces the set) |
| `MedicationScheduleHistory` row | Keyed fact | Key `(MedicineId, EffectiveFrom)`; same key twice (one device or two) → higher HLC wins. The losing row enters the conflict list only when the two rows come from different devices. Behavior change, see §17 |
| `MedicationSuspension` | Mutable record | Creation is a fact; `EndDate`, `Reason` LWW per field; overlaps resolved per §4.5 |
| Stock entry facts (`NewPackage`, `ManualAdd`, `PositiveCorrection` from `AddStock`, `NegativeCorrection` from `AdjustStockDown`) | Append-only fact | Union by `Id` |
| `StockCount` (new) | Append-only fact | Union by `Id`; anchor for derivation |
| `MedicationIntake` | Mutable record | Creation is a fact; `Status`, `ActualAt`, `Notes` LWW per field |
| Legacy rows at genesis | Frozen fact | Immutable |
| Automatic and intake `Consumption`, backdated reversals, count corrections | Derived | `LedgerDeriver` |
| Per-profile notification settings (`ToAddress`, `CaregiverAddress`, `DoctorAddress`) | Mutable record | LWW per field |
| Profile display name | Mutable record | LWW operation, encrypted like every other operation; applied to the local registry entry of the joined profile. Never in `group.json` or in paths |
| `NotificationEvent`, `DoseReminderEvent` | Device-local | Not replicated: they record what **this** device has notified |
| Device notification preferences, language, sync schedule | Device-local | Not replicated |
| Profile registry, PIN, role, SMTP, backup settings | Installation-local | Not replicated (§4.6) |

Deletion: medicines are deactivated, never deleted (P11). The only
deletions are slot-set replacement (covered by the set register) and
the user deleting a mistaken fact (stock entry, count, intake,
suspension). That becomes a **retraction** operation: a tombstone keyed
by the fact `Id`, which wins over the fact whatever the order of
arrival. Today the UI does not delete these facts; retraction is an
addition (D8). `Legacy` facts cannot be retracted: a mistake before the
cutoff is fixed with a correction, as today.

### 4.3 `LedgerDeriver`

Pure Domain function:

```
derive(replicated facts for one medicine, cutoffDay, today) -> derived movements
```

Rules, in this order (each rule states its own day range):

1. **Intake consumption, days in `(cutoffDay, today]`**: for every
   intake with status `Taken`, one derived `Consumption` of its
   quantity at the intake's instant. Today is included because
   `RegisterIntake` books stock in real time today `[VERIFIED —
   RegisterIntake header comment]`. Intakes on days up to the cutoff
   that existed at the patch already have their `Legacy` movements and
   derive nothing.
1b. **Backdated intake on a frozen day**: an intake of any status
   created after the patch for a day `d <= cutoffDay` derives what
   `RegisterIntake` writes today: a `PositiveCorrection` reversing that
   day's `Legacy` automatic consumption (if the day has one and no
   other intake exists for it), then the `Taken` quantity if any.
   Frozen rows are never edited.
2. **Automatic consumption, days in `(cutoffDay, yesterday]`**: if no
   intake of any status exists for `d`, no count anchor has already
   derived `d`'s consumption (rule 3), the medicine is active on `d`
   (activity history, D15), and `d` is in the therapy window and not
   suspended, derived automatic consumption =
   `ConsumptionMaterializer` result for `d` (existing Domain code,
   unchanged), at local midday of `d` as today `[VERIFIED —
   ConsumptionCatchUp]`. An intake day never gets automatic
   consumption, so today's backdated-intake reversal movement is no
   longer needed.
3. **Stock-count anchors**: for each `StockCount`, the correction is the
   `ReconcileStock` formula applied to the ledger as of `countedAt`:
   expected = raw (unclamped, as today through `LedgerAlignment`)
   ledger total as of `countedAt` (every fact and derived row up to
   `countedAt`, which includes automatic consumption through the day
   before the count day) minus `takenToday`; correction =
   `counted - expected`. When `takenToday` equals the count day's whole
   scheduled quantity, the count day's automatic consumption is derived
   at the count (today's `MaterializesToday` branch) and rule 2 skips
   that day afterwards, as the catch-up skips a day that already has a
   `Consumption` today `[VERIFIED — ReconcileStock header comment]`;
   otherwise the count day stays to rule 2. Parity tests (§11) must reproduce the
   current `ReconcileStock` test cases exactly.

Derived movement ids are deterministic (a name-based GUID over
`medicineId`, rule, day or anchor id), so a re-derivation replaces rows
idempotently.

Automatic consumption for today is never derived, as today
`[VERIFIED — ConsumptionCatchUp]`, except through a count anchor
(rule 3). Derived rows depend on the local date; devices on different
dates (midnight, time zones) may differ until their dates agree. This
is why the convergence check (§12) hashes replicated state only.

`ConsumptionCatchUp` becomes a thin wrapper: "derive and replace
derived rows". It must run on **every** medicine, active or not: today
it only reads `ListActiveAsync` `[VERIFIED]`, and deriving only active
medicines would leave derived rows depending on when each device last
ran, which breaks R6. `MonitoringGate` still serializes it.

### 4.4 Stock epoch

`StockEpoch` becomes derived: order the medicine's epoch-advancing
facts (positive stock entries, and positive counts under today's
`ReconcileStock` rule) by HLC; the epoch of any instant is `1 +` the
number of advancing facts up to it. Two offline refills on two devices
then yield epochs 5 and 6 on every device.

Epoch 1 is opened by the medicine's `InitialLoad` movement (or, when
there is none, by the medicine id). Notification dedup
(`NotificationEvent`, device-local) keys on the
**id of the fact that opened the epoch** instead of the epoch number,
so a re-numbering after a merge does not re-trigger an already sent
warning. Additive column via idempotent patch (`CLAUDE.md` §7).

### 4.4b Inactive medicines

Today the catch-up skips inactive medicines, and after a reactivation
it resumes from the day after the last automatic consumption, so it
books every day of the inactive period at once `[INFERRED — from
ConsumptionCatchUp range computation and ListActiveAsync]`. A pure
derivation needs a stated rule. Proposal (D15): no automatic
consumption on days when the medicine was inactive, using the activity
history (§4.2). It differs from today's behavior only for medicines
reactivated after the cutoff.

### 4.5 Semantic conflicts

Merging can produce states that a single device would have rejected.
They are resolved deterministically and recorded in a local
`SyncConflicts` list, which the UI shows for review. Resolution never
depends on which device runs it.

| Conflict | Deterministic resolution | Shown to user |
|---|---|---|
| Same field of a medicine edited on two devices | LWW by HLC | Yes, with the losing value, one-tap restore |
| Two schedule rows with the same `EffectiveFrom` | Higher HLC wins | Yes |
| Overlapping suspensions | Kept as they are; the derivation treats a day as suspended if any suspension covers it (union) | Yes, as a hint |
| Two intakes for the same medicine, day and slot | Both kept (several intakes a day are legitimate today) | Yes when quantity sum exceeds the scheduled quantity for the day |
| Stock would go negative after merge | `MedicineStock.Current` already clamps to zero `[VERIFIED]`; no fact is dropped | Yes, existing warning |
| Two stock counts close in time | Both anchors apply in order; the later one decides the stock from its instant | No |
| Retraction of a fact that another device edited | Retraction wins | Yes |
| Slot set replaced on two devices | LWW on the set | Yes |

Rejected alternative: re-running use-case validation on merge. A
rejection on one device and acceptance on another would break R2.
Validation happens where the user acts; merge only applies.

### 4.6 What is not synchronized, and why

- **Profile registry, PIN, role**: installation concerns
  (`ANALYSIS.md` §5.2). A device joins a profile's sync group
  explicitly (§6).
- **SMTP settings and password**: the password is DPAPI-bound on
  desktop; spreading it to phones enlarges the attack surface. Email is
  sent by one designated device (§8.4).
- **Backup settings**: per installation.
- **Reference catalogue**: shipped with each app, not user data.

---

## 5. Remote storage layout and protocol

### 5.1 Layout

One folder per sync group (one group per profile), under an
app-specific root:

```
MedReminder-Sync/
  <groupId>/
    group.json                     cleartext: format, version, groupId, generation, KDF parameters
    key.<keyVersion>.wrap          group key wrapped with the passphrase-derived key
    genesis/<generation>.mrg       encrypted genesis snapshot
    checkpoints/<generation>/<deviceId>-<n>.mrc   encrypted compacted state + vector
    ops/<generation>/<deviceId>/<seq>.mrs         encrypted operation segments
    devices/<deviceId>.mrd         encrypted device record (name, platform, app version, applied vector, last seen)
```

`groupId`, `deviceId` are random GUIDs. No profile name, medicine name
or hostname appears in any path or cleartext file (R4).

### 5.2 Segments

- A segment holds the operations a device produced since its previous
  segment, in local order, with a header: `groupId`, `generation`,
  `deviceId`, `seq`, `opSchemaVersion`, `keyVersion`, and the device's
  **dependency vector**: its applied vector when the segment is sealed.
  That over-approximates the dependencies of every operation in the
  segment, which is safe.
- Encrypted with AES-256-GCM under the group key with a random 96-bit
  nonce per segment, far below the 2^32 messages per key that random
  nonces allow `[VERIFIED — NIST SP 800-38D, training knowledge]`. The
  header is bound as associated data, so a segment cannot be renamed,
  moved to another device folder or replayed under another `seq`
  without failing authentication. `IArchiveCipher.Encrypt` / `Decrypt`
  have no associated-data parameter today `[VERIFIED]`; the port gains
  overloads in Phase 3.
- Immutable: written once under a temporary name, then renamed or
  committed; readers ignore temporary names. A partially synced file
  fails authentication and is retried later.
- Writing: debounced after local writes (default 10 s), and on app
  pause / exit.

### 5.3 Apply loop

1. List `ops/<generation>/*/` and download segments with `seq` greater
   than the local applied vector for that device.
2. Verify and decrypt. On failure: skip for now, retry next run; after
   repeated failures surface a sync error.
3. **Causal buffer**: apply a segment only when the local applied
   vector covers its dependency vector and it is the next `seq` of its
   device. Otherwise hold it.
4. Apply operations through the merge rules (§4) in one transaction;
   record applied `(deviceId, seq)`; re-derive the ledger for touched
   medicines; re-plan notifications.
5. Publish local pending segments; update `devices/<deviceId>.mrd`.

Idempotency: every operation has a GUID; a local table records applied
operation ids, so re-downloading is harmless.

### 5.4 Group key and passphrase

- The group key is a random 256-bit key generated when sync is enabled.
- It is wrapped (AES-GCM) with a key derived from the **sync
  passphrase** via Argon2id, parameters recorded in `group.json`
  (reusing `Argon2Params.Default`: 3 iterations, 64 MiB, parallelism 1
  `[VERIFIED]`).
- Each device stores the unwrapped group key locally: DPAPI
  `CurrentUser` on desktop (`profiles\<id>\sync.protected`), platform
  secure storage on mobile (Android Keystore, iOS Keychain through MAUI
  `SecureStorage`).
- The sync passphrase may equal the cloud-backup passphrase; D10.

### 5.5 Genesis and bootstrap

- Enabling sync on the first device: write `group.json`, the wrapped
  key, and `genesis/<generation>.mrg`. The genesis uses the checkpoint
  format (§5.6) with an empty applied vector: all replicated facts
  (`Legacy` and later), the cutoff day, per-field versions initialized
  to the genesis HLC, no derived rows. `ExportPayload` is not enough:
  it has no field versions and no origin.
- Joining device: obtain the group key (§6), download the newest
  checkpoint (or the genesis), load it, then apply segments after the
  checkpoint's vector. A device that joins never uploads its previous
  local data for that profile: joining replaces the local profile
  database with the group state after explicit confirmation (the
  existing import confirmation pattern).

### 5.6 Compaction and retention

- A device writes a checkpoint when more than N operations (default
  5 000) have accumulated since the last checkpoint. A checkpoint holds
  the replicated state only: facts, frozen facts and cutoff, per-field
  HLC versions, tombstones and the applied vector. Without field
  versions and tombstones, LWW and retractions would break after a
  bootstrap. Derived rows, conflicts and device-local tables are not
  included.
- A segment can be deleted when a checkpoint covers it **and** every
  active device's published applied vector covers it.
- A device not seen for 90 days (configurable) is marked stale and no
  longer blocks deletion. When it returns, it rebuilds from the newest
  checkpoint and then publishes its own pending operations, which the
  others still accept (they are new `seq` values of that device).
- Only the writer of a file deletes it, except that segments of a
  **revoked** device are deleted by the device that revoked it.

### 5.7 Generations: import, restore, reset

Overwriting a profile (C.3 import, C.3+ restore, raw backup restore)
is incompatible with incremental sync. On a synced profile these
actions become a **reset**:

1. The device publishes its pending segments.
2. It creates generation `g + 1` with a new genesis from the restored
   state.
3. Other devices detect the new generation, publish any pending
   operations of generation `g` (for audit only, they are not applied
   to `g + 1`), show a notice with the count of discarded pending
   operations, and bootstrap from the new genesis.

The confirmation dialogs of import and restore state this consequence
before proceeding.

### 5.8 Transport port

`IArchiveStorage` covers whole-file upload, download, list and delete
`[VERIFIED]`. Sync needs, in addition, listing by prefix, create-only
writes (fail if the name exists) and a change cursor where the provider
offers one. New Application port `ISyncTransport`, implemented by:

| Transport | Desktop | Mobile | Phase |
|---|---|---|---|
| `LocalFolderSyncTransport` (a folder synced by a third-party client, or a NAS share) | Yes | No | 3 |
| `OneDriveSyncTransport` (Microsoft Graph, MSAL public client) | Yes | Yes | 4 |
| `GoogleDriveSyncTransport` (Drive REST API) | Yes | Yes | 4 |
| iCloud | Only through `LocalFolder` on Windows | Native container | Not planned, D12 |

Why desktop also uses the provider API rather than only the synced
folder: with Google Drive the narrow `drive.file` scope lets an app see
only files it created or the user opened with it, so files written by
the Google Drive desktop client might be invisible to the phone app
`[UNCERTAIN — spike S7]`. Using the same API on both sides avoids the
issue. For OneDrive the app-folder permission (`Files.ReadWrite.AppFolder`)
limits access to `/Apps/<app>` `[UNCERTAIN — spike S6 also checks that
the Windows OneDrive client syncs it]`.

`IArchiveStorage` gets matching provider implementations in the same
phase (the C.3++ Phase 2 deliverable), so cloud backups can use the
same account.

### 5.9 Future backend

A hosted relay (C.1) would be one more `ISyncTransport`. The operation
format, encryption and merge rules do not change. This is recorded so
that no design choice here depends on the file-based transport beyond
the port.

---

## 6. Device pairing and security

### 6.1 Pairing

Two ways to give a new device the group key:

1. **QR pairing (default)**: the desktop (or any paired device) shows a
   QR code containing `groupId`, provider, a transport hint and the
   group key, valid for 10 minutes and only while the dialog is open.
   The phone scans it with the camera. No passphrase typing. The QR code
   is a secret: the dialog says so, and its window is excluded from
   screen capture where the platform allows it `[UNCERTAIN — WinForms
   support, SetWindowDisplayAffinity]`.
2. **Passphrase**: the device signs in to the provider, finds the group,
   and unwraps the key with the sync passphrase. Needed when no paired
   device is at hand (lost PC).

### 6.2 Revocation and key rotation

Removing a device (lost phone): a paired device generates a new group
key (`keyVersion + 1`) and writes new segments with it. The flow
**requires a new sync passphrase** for the wrap: the lost device may
still hold a signed-in provider session and could read the new wrap
file with the old passphrase. The flow also tells the user to end the
lost device's sessions in the provider account (Microsoft or Google
account security page), which the app cannot do itself. Old segments
stay readable by the revoked device if it still has local copies; this
is inherent and stated in the UI. Remaining devices find segments with
an unknown `keyVersion` and ask for the new passphrase once, or pair
again by QR from the revoking device.

The revocation itself is a replicated `DeviceRevoked(deviceId,
lastAcceptedSeq)` operation, encrypted with the new key. Every device
then ignores segments of the revoked device after `lastAcceptedSeq`,
because the lost device can still write to its own folder while its
provider session lasts.

### 6.3 Threat model

| Threat | Mitigation |
|---|---|
| Cloud provider or stolen cloud account reads data | End-to-end encryption; only ciphertext and random ids in storage |
| Tampering or substitution of segments | AES-GCM with header as associated data |
| Deletion or rollback of remote files | A missing middle segment is a gap: readers stall on that peer and report it. Deleting the **latest** segments is not visible to readers, so each author keeps its operations locally until every active peer's published vector covers them and re-publishes any of its segments found missing |
| Stolen phone | Device lock; optional app lock; revocation (§6.2); data in app sandbox |
| Passphrase loss | QR pairing from a surviving device; otherwise the remote data is unreadable (inherent, stated at setup); a printable recovery sheet is offered |
| Health data leaking to OS backups | Android backup exclusion, iOS backup exclusion for the database directory (§9.2) |

### 6.4 Privacy and regulation

- No data reaches infrastructure controlled by the project; data lives
  on user devices and in the user's own cloud account `[INFERRED — the
  project is then not a processor under GDPR; not legal advice]`.
- Store disclosures (Google Play Data safety, Apple privacy labels):
  no data collected by the developer; data synced with the user's cloud
  provider at the user's request. To be reviewed against the forms.
- Non-medical positioning unchanged.

---

## 7. Architecture

### 7.1 Solution after Phases 1–3

| Project | Target | Content |
|---|---|---|
| `MedReminder.Domain` | `net10.0` | + `LedgerDeriver`, `StockCount`, HLC value type, merge rules (pure) |
| `MedReminder.Application` | `net10.0` | + operation model, `IOperationLog`, `ISyncTransport`, `SyncEngine`, `IArchiveReader`, moved `MedicineOverviewLoader`, `NotificationPlanner` |
| `MedReminder.Infrastructure.Portable` (new) | `net10.0` | EF Core model and repositories, `DatabaseInitializer`, sync tables, `ArchiveCipher`, `ArchiveReader`, segment codec, `LocalFolderSyncTransport`, provider transports, localization loader |
| `MedReminder.Infrastructure` | `net10.0-windows` | DPAPI stores, registry, balloon notifications, `AppDataPaths`, `ProfileRegistry`, Windows shells of import / export |
| `MedReminder.UI` | `net10.0-windows10.0.19041.0` | + sync settings, pairing, conflict review, device list; `SyncHostedService` |
| `MedReminder.Mobile` (new) | `net10.0-android`, then `net10.0-ios` | MAUI app, platform adapters |

Layering rules of `ANALYSIS.md` §2.1 apply unchanged. The portable
project must not reference Windows APIs or `%LOCALAPPDATA%`; paths
reach it through an `IAppDataLocation` port.

### 7.2 Operation capture

- Use cases emit semantic operations through `IOperationLog` in the
  same unit of work as their local write. The operation is the fact
  (for example `StockCountRecorded`), not the derived row.
- No EF Core change-tracker interception: it would capture derived
  rows and lose intent.
- Every write path outside use cases (P8, `MainForm`) is moved into a
  use case first. A test asserts that each repository write method is
  reached only from use cases or from the sync apply layer
  `[INFERRED — enforceable with a reflection or analyzer test]`.

### 7.3 New persistence objects

Idempotent boot patches in `DatabaseInitializer` (`CLAUDE.md` §7):

| Object | Purpose |
|---|---|
| `StockMovements.Origin` column (`Legacy`, `User`, `Derived`) | Separate facts from derived rows |
| `StockCounts` table | Count anchors |
| `SyncOperations` table | Local outbox and applied-operation ids |
| `SyncFieldVersions` table | HLC per `(entity, id, field)` for LWW |
| `SyncPeers` table | Applied vector per remote device |
| `SyncConflicts` table | Conflict review list |
| `SyncTombstones` table | Retractions |
| `NotificationEvents.EpochFactId` column | Dedup key stable across merges (§4.4) |

Export format: `ExportFormat.CurrentSchemaVersion` bump for `Origin`
and `StockCounts` (`ANALYSIS.md` §8.1 rule). The `.mrz` payload keeps
the full ledger, derived rows included and tagged by `Origin`, so the
exported stock stays readable; an importer of the new version drops
derived rows and re-derives. Archives of the current schema version
(no `Origin`) import as `Legacy` with the cutoff at the import day.
Sync metadata tables are not exported in `.mrz`.

Files: `profiles\<id>\sync.settings.json` (transport, group id, device
id, interval) and `profiles\<id>\sync.protected` (DPAPI group key and
OAuth token cache reference). Everything stays under
`%LOCALAPPDATA%\MedReminder\` (`CLAUDE.md` §5); the remote folder is
user-chosen, as the C.3+ cloud folder already is.

### 7.4 Sync runtime

- **Desktop**: `SyncHostedService` (UI project, like the other four
  services, `ANALYSIS.md` §6): run at start, then every 5 minutes
  (configurable), plus debounced after local writes.
- **Write gate**: today only the catch-up, the monitor, `RegisterIntake`
  and `ReconcileStock` run under `MonitoringGate` `[VERIFIED —
  ANALYSIS.md §4.4]`. With sync, **every** use case and the sync apply
  step must run under one per-process write gate, because an apply can
  change a medicine between a use case's read and its save. One process
  per database stays the rule (`CLAUDE.md` §7).
- **Stale forms**: an edit dialog opened before a sync must not write
  back fields the user did not touch. Use cases receive the user's
  changes as a diff against the values loaded when the dialog opened,
  and emit operations only for changed fields. Otherwise every save
  would stamp all fields with a new HLC and silently overwrite remote
  edits. After an apply, open views refresh.
- **Mobile**: sync on start, resume, after local writes, and in the
  background with Android WorkManager periodic work (minimum interval
  15 minutes `[VERIFIED — Android WorkManager documentation, training
  knowledge]`) and iOS background app refresh, whose timing the OS
  decides `[INFERRED]`.
- Status surface on every device: last successful sync, pending
  operations, peer devices and their last seen time, errors, clock
  warnings, conflicts to review.

### 7.5 Port mapping for mobile

| Port | Desktop | Mobile |
|---|---|---|
| Repositories, `IUnitOfWork`, `IArchiveCipher`, `IArchiveReader` | Portable | Portable |
| `ILocalizationService` | Portable loader + `%LOCALAPPDATA%` override | Portable loader, embedded dictionaries |
| `ICurrentProfile` | `CurrentProfile` | `MobileCurrentProfile` |
| Low-stock and dose notifications | Toast / balloon + polling services | `ILocalNotificationScheduler` + `NotificationPlanner` (§8) |
| `IEmailNotificationService` | MailKit | MailKit, only when the device is the designated mail device (§8.4) |
| Secret stores | DPAPI | `SecureStorage` |
| `ISyncTransport` | LocalFolder, OneDrive, Google Drive | OneDrive, Google Drive |
| Camera barcode capture | A2 webcam variant | Platform camera (§10) |
| `IAutoStartService`, `IApplicationRestarter`, `IBackupService` | Windows | Not applicable |

### 7.6 UI framework

.NET MAUI. MAUI 10 ships with .NET 10; a MAUI major version is
supported at least 6 months after its successor ships; MAUI 10 binds
Android 16 (API 36) and iOS 26 `[VERIFIED — Microsoft .NET MAUI support
policy, "What's new in .NET MAUI for .NET 10"]`. Effective iOS floor:
13.0 for AES-GCM (P12); proposed floors in D13. Avalonia only if
desktop Linux becomes a target (D2).

---

## 8. Notifications across devices

### 8.1 Planning model on mobile

Phones cannot rely on a polling loop. `NotificationPlanner`
(Application, pure, tested with a fake `TimeProvider`) computes future
notifications from the local state; the platform adapter replaces the
scheduled set. Re-planning runs after every local write, after every
sync that changed state, on start, resume, reboot and time-zone
change.

- **Low-stock**: one notification at `runOutDate - ThresholdDays` at a
  configurable local time (default 09:00), deduplicated by the
  epoch-opening fact id (§4.4).
- **Dose**: one per timed slot over a rolling window, skipping
  suspended days, days outside the therapy window, days with projected
  stock zero and **slots that already have an intake recorded on any
  device** (known after sync; an intake is matched to a slot by its
  `ScheduledAt`). Budget per platform (iOS pending limit
  `[UNCERTAIN — 64 per app, Apple documentation, training knowledge;
  spike S5]`); a final "open MedReminder to keep reminders active"
  notification closes the window.

### 8.2 Platform constraints

- Android 13+: runtime `POST_NOTIFICATIONS` permission.
- Android 14+: `SCHEDULE_EXACT_ALARM` denied by default for new
  installs targeting API 33+; `USE_EXACT_ALARM` reserved by Play policy
  to alarm-clock and calendar apps `[VERIFIED — Android 14 behavior
  changes]`. Plan: request `SCHEDULE_EXACT_ALARM` with an explanation;
  inexact fallback stated in the UI.
- Android alarms are cleared on reboot; re-plan on `BOOT_COMPLETED`
  `[INFERRED]`.
- iOS: explicit authorization.
- MAUI has no built-in local-notification API `[INFERRED]`; thin native
  adapters (`AlarmManager` + `NotificationCompat`, `UNUserNotificationCenter`)
  behind `ILocalNotificationScheduler` (spike S5).

### 8.3 Duplication policy

Per device and per kind, the user chooses whether that device notifies
(D4 proposes: dose reminders on the phone, low-stock on every device).
An intake recorded on one device suppresses the pending dose reminder
on the others after the next sync. Sync latency is minutes on
foreground devices and OS-dependent in background, so a duplicate
reminder is possible and accepted.

### 8.4 Email

Exactly one **designated mail device** per profile sends low-stock and
caregiver email (default: the desktop that enabled sync). The
designation is a replicated LWW field. Mobile uses MailKit (runs on
Android and iOS `[INFERRED — managed library]`) only if designated;
SMTP settings are then configured on that device (not synced, §4.6).
The desktop sends only if designated. If the designated device is off,
no email is sent; the device list shows its last-seen time, and any
device can take over the designation. Prescription requests (user
initiated) are sent from the device where the user acts, via `mailto:`
or SMTP if configured there.

---

## 9. Mobile app

### 9.1 Screens (end state)

1. Onboarding: disclaimer, create or join a profile (QR or provider
   sign-in + passphrase), notification permissions.
2. Medicine list: stock, days remaining, run-out date, warning badges,
   sync status.
3. Medicine detail and edit: all fields, schedule change, slots,
   suspensions, catalogue link.
4. Stock: new package, manual add, correction, guided count.
5. Intake: record, edit status, today's slots with quick actions.
6. Timeline (read-only, as desktop PR #75).
7. Prescription request draft (as desktop PR #74).
8. Conflicts to review.
9. Profiles on this device; devices of the sync group; revoke.
10. Settings: language, notifications per kind, designated mail device,
    app lock, sync interval and status, about, licenses.

Accessibility: system font scaling, screen-reader labels, no meaning by
color alone.

### 9.2 Local data protection

- Databases in the app sandbox (`FileSystem.AppDataDirectory`).
- Android: `android:allowBackup="false"` and data-extraction rules
  excluding the database directory `[INFERRED]`.
- iOS: file protection class and exclusion from iCloud backup
  `[UNCERTAIN — exact API, Phase 6]`.
- Logs in the sandbox, same content rules as desktop (`CLAUDE.md` §7).
  No analytics or crash-reporting SDK.

### 9.3 Localization

`assets/localization/strings.<lang>.json` for all five languages,
embedded in the app; new keys prefixed `Mobile.` and `Sync.`, added to
all five files in the same PR (`CLAUDE.md` §2, §6). Mobile and sync
sections added to the five user guides.

---

## 10. Feature parity (end state)

| Desktop feature | Mobile | Phase |
|---|---|---|
| Medicine list, forecast, warnings | Yes | 5 |
| Add / edit / deactivate medicine, slots, regimens (A1, stepped taper) | Yes | 5 |
| Schedule change, suspend, resume | Yes | 5 |
| Stock entries, corrections, guided count (4.1) | Yes | 5 |
| Intakes | Yes | 5 |
| Low-stock and dose notifications | Yes, local | 5 |
| Timeline view (PR #75) | Yes | 7 |
| Prescription request (PR #74) | Yes (`mailto:` / share) | 7 |
| Reference catalogue search | Yes, snapshot for the selected reference country (0.5–4.6 MB of embedded snapshot files per country directory `[VERIFIED — Assets/Catalogue]`) | 7 |
| Barcode scan (A2) | Yes, phone camera | 7 |
| Email notifications | Only on the designated mail device | 7 |
| Caregiver recipient (A3) | Via synced notification settings | 5 |
| Multiple profiles | Yes, one sync group each | 5 |
| Encrypted `.mrz` export / import | Export yes; import = group reset (§5.7) | 7 |
| Cloud backup snapshots (C.3+) | No: sync plus desktop backups cover it | — |
| Raw database backup, auto-start, tray, update check | Not applicable (OS / store handle them) | — |
| Donation links (A6) | Android yes; iOS `[UNCERTAIN — App Store rules on external payment links]`, D14 | 7 |
| Printing | Share as PDF `[UNCERTAIN — library choice]` | 7 |
| DataImporter | Maintainer tool, not shipped | — |

---

## 11. Tests

| Level | Coverage |
|---|---|
| Domain unit | `LedgerDeriver`: parity with today's `ConsumptionCatchUp` + `RegisterIntake` + `ReconcileStock` results on the existing test scenarios for days after cutoff; count anchors; epoch derivation; genesis cutoff leaves frozen days untouched. HLC ordering, drift. Merge rules per class |
| Application unit | Operation emission for every use case; `SyncEngine` apply loop with fake transport; causal buffer; generation reset; `NotificationPlanner` (DST, time zone, budget, suspended days, intake on another device) |
| **Convergence simulation** | Deterministic seeded harness: N simulated devices (2–5), random operations from the real use cases, random delivery order, duplication, delay, partition, device offline for weeks, compaction running concurrently. After full delivery, assert identical state hash on all devices and all invariants of `ANALYSIS.md` §4.4. Thousands of seeds in CI; any failing seed is kept as a regression case |
| Transport | Contract test suite shared by all `ISyncTransport` implementations (like `ArchiveStorageContractTests` `[VERIFIED — tests/MedReminder.Infrastructure.Tests/Backup]`): create-only, listing, partial files, missing files; provider transports against a recorded or sandbox account (manual job) |
| Crypto / format | Segment tamper, rename, replay under other `seq`, wrong key version, unknown `opSchemaVersion` (device stops, R7) |
| Regression | Existing Windows tests green after every phase; `.mrz` round-trip with the new schema version; old archives still import |
| Manual | Device checklist: pairing, offline edits on two devices, conflict review, revocation, reset via import, reboot, exact alarm denied, permission revoked, battery saver, language, font scaling |

A formal model check (for example TLA+) of the apply loop and
compaction is recommended but not required `[INFERRED — the
simulation harness covers the same properties empirically]`.

---

## 12. Risks

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| Divergence bug in merge or derivation | Medium | High | Pure Domain rules; convergence simulation in CI; state-hash exchange between devices (each device publishes a hash of its **replicated** state per applied vector, derived rows excluded; mismatch raises an error and offers re-bootstrap) |
| Ledger refactor changes existing numbers | Medium | High | Cutoff (§3.5); parity tests; refactor shipped (Phase 2) before sync |
| Provider API limits, scope policies, OAuth verification | Medium `[UNCERTAIN]` | Blocking per provider | Spikes S6, S7; OneDrive first; `LocalFolder` fallback on desktop |
| Background sync on mobile too infrequent | High | Stale data, duplicate reminders | Foreground sync on open; status always visible; accepted duplication (§8.3) |
| Exact alarms denied | High on Android 14+ | Late reminders | Explained permission request; inexact fallback |
| Passphrase loss | Medium | Remote data unreadable | QR pairing; recovery sheet; local databases on each device remain usable |
| Clock skew | Medium | Surprising LWW outcomes | HLC; clock warning; conflict review with restore |
| Remote storage growth | Low | Quota | Compaction (§5.6) |
| Scope creep toward C.1 | Medium | Schedule | §5.9 draws the line: no backend in B.1 |
| Effort underestimated | High `[INFERRED]` | Schedule | Phases each deliver usable value (desktop-to-desktop sync in Phase 3) |

---

## 13. Phases

Each phase lists entry criteria, actions, deliverables and exit
criteria. `dotnet build` and `dotnet test` must be green before every
commit (`CLAUDE.md` §6); every phase updates `CHANGE_LOG.md`,
`ANALYSIS.md` and the user guides where behavior is visible.

### Phase 0 — Decisions, spikes, accounts

**Entry**: product owner approves B.1 with mandatory sync, and its
place relative to A2 (`EVOLUTION.md` §2.0).

**Actions**

1. Decide D1–D5, D9–D13; D6, D8, D15 before Phase 2 (§16).
2. Spikes (throw-away branches; results appended to §18):
   - S1 AES-GCM on Android (`AesGcm.IsSupported`, decrypt a desktop
     archive).
   - S2 Argon2id cost on a low-end phone with `Argon2Params.Default`
     (target under 5 s, no out-of-memory).
   - S3 EF Core SQLite on Android and iOS with Release trimming / AOT.
   - S4 `StripReleaseDebugArtifacts` (`Directory.Build.props`) against a
     Release Android build. `CLAUDE.md` §7 forbids weakening it; any
     exclusion for the mobile project needs approval (D11).
   - S5 Local notifications: fire when killed, after reboot, exact
     alarm denied, iOS pending limit.
   - S6 OneDrive: Graph app folder with MSAL public client on Windows
     and Android; create-only semantics; whether the Windows OneDrive
     client syncs `/Apps/<app>`.
   - S7 Google Drive: `drive.file` visibility across the desktop and
     Android OAuth clients of one Cloud project; OAuth verification
     requirements for the chosen scope.
   - S8 Background sync: WorkManager and iOS background refresh
     frequency under battery saver.
3. App registrations (Entra, Google Cloud), store accounts, macOS build
   host if iOS is in scope.

**Exit**: S1, S3, S6 pass or have accepted mitigations; D1–D5 decided.

**Effort**: 8–12 days `[INFERRED]`.

### Phase 1 — Portability refactor (desktop only, no behavior change)

**Entry**: Phase 0 exit; no concurrent A2 work on `Infrastructure`.

**Actions**

1. Create `MedReminder.Infrastructure.Portable` (`net10.0`); move
   persistence, `ExportMapper`, `ExportJson`, `ArchiveCipher`, the
   localization loader.
2. `IAppDataLocation` port; `AppDataPaths` as Windows implementation.
3. Extract `IArchiveReader` / `ArchiveReader` and the replica builder
   from `ImportService`; keep `ImportService` as the Windows shell.
4. Move `MedicineOverviewLoader` to Application.
5. `MedReminder.Infrastructure.Portable.Tests` (`net10.0`, runs on
   Linux); desktop-produced fixture archive in `tests/fixtures/`.

**Exit**: Windows tests green; portable tests green on Linux; manual
smoke of export, import, cloud restore, main grid; no Windows reference
in the portable project.

**Effort**: 6–9 days `[INFERRED]`.

### Phase 2 — Ledger derivation and write-path audit (desktop only)

**Entry**: Phase 1 merged; D6, D8 and D15 decided.

**Actions**

1. Route every write through use cases (P8), starting with `MainForm`.
2. `StockCount` fact and table; `ReconcileStock` records a count;
   `StockMovements.Origin`.
3. `LedgerDeriver` (Domain); `ConsumptionCatchUp`, `RegisterIntake`,
   `ReconcileStock` produce facts and let the deriver produce derived
   rows.
4. Derived `StockEpoch`; `NotificationEvents.EpochFactId`.
5. Cutoff support (§3.5): the patch freezes existing rows and stores
   C, so numbers up to C never change.
6. Schema patches in `DatabaseInitializer`; `ExportFormat` schema
   version bump; `docs/EXPORT-FORMAT.md` update; import of older
   archives mapped to `Legacy` rows.

**Exit**: parity tests prove identical stock, forecast and
notifications for existing scenarios; manual check on a copy of a real
profile database (numbers before and after the patch identical);
retraction UI only if D8 = yes.

**Effort**: 12–18 days `[INFERRED]`.

### Phase 3 — Sync engine and desktop-to-desktop sync

**Entry**: Phase 2 merged and released at least once (field
validation of the derivation); D7, D10 decided.

**Actions**

1. HLC, operation model, `IOperationLog`, emission from every use case.
2. Merge rules, sync tables, conflict list, tombstones.
3. Segment codec, group key, wrap / unwrap, genesis, checkpoints,
   compaction, generations (§5).
4. `ISyncTransport` + `LocalFolderSyncTransport` + contract tests.
5. `SyncHostedService`; desktop UI: enable sync, join via passphrase,
   status, devices, conflict review; reset flow in import and restore
   dialogs.
6. Convergence simulation harness in CI.
7. New public document `docs/SYNC-FORMAT.md` (layout, segment format,
   operation catalogue, versioning rules), mirroring `EXPORT-FORMAT.md`.

**Deliverable**: two PCs of the same user in sync through a synced
folder. Useful on its own and the proving ground before mobile.

**Exit**: simulation green over the agreed seed count; manual two-PC
checklist; no plaintext in the remote folder (inspection test).

**Effort**: 30–45 days `[INFERRED]`.

### Phase 4 — Cloud provider transports (includes C.3++ Phase 2)

**Entry**: Phase 3 exit; S6 / S7 results; D3 decided.

**Actions**: `OneDriveSyncTransport` and `OneDriveArchiveStorage`
(MSAL, token cache under DPAPI on desktop); then Google Drive; provider
selection and sign-in in settings; QR pairing generator on desktop;
contract tests; `THIRD-PARTY-NOTICES.md` for MSAL and Google client
libraries.

**Exit**: desktop-to-desktop sync through each provider API; existing
`ArchiveStorageContractTests` green for the new archive backends.

**Effort**: 15–25 days `[INFERRED]`.

### Phase 5 — Android full client

**Entry**: Phase 4 exit for at least OneDrive; D1, D3, D4, D13
decided; Play Console account.

**Actions**: MAUI project; composition root; screens §9.1 items 1–5, 8–10;
QR pairing scanner; provider sign-in; `NotificationPlanner` + Android
adapter, boot receiver, WorkManager sync; secure storage; app lock;
backup exclusion; localization; CI Android job; signing outside the
repository; internal testing track; `docs/PACKAGING.md` mobile section;
user guides.

**Exit**: manual checklist on an Android 14+ device and an older
supported version; phone and desktop converge in the offline /
conflict scenarios of the checklist; no health data in logs.

**Effort**: 40–60 days `[INFERRED — strongly dependent on MAUI
experience]`.

### Phase 6 — iOS

**Entry**: Phase 5 exit; macOS host; Apple Developer Program; S1, S3,
S5 repeated on iOS.

**Actions**: `net10.0-ios` target; notification adapter;
`BGAppRefreshTask`; Keychain; file protection and backup exclusion;
macOS CI job; TestFlight; privacy labels; App Store submission.

**Exit**: checklist on a supported iPhone; review passed.

**Effort**: 15–25 days `[INFERRED]`.

### Phase 7 — Completion

**Entry**: Phase 5 (and 6 if in scope) released.

**Actions**: timeline and prescription request on mobile; catalogue
snapshot on mobile and barcode scan with the phone camera (A2 mobile
path); designated mail device and MailKit on mobile; `.mrz` export on
mobile; donation links per D14; PDF share; device-side state-hash
verification UI; documentation completion.

**Exit**: feature-parity table (§10) fully satisfied or each gap
accepted by the product owner.

**Effort**: 20–30 days `[INFERRED]`.

### 13.1 Summary

| Phase | Content | Depends on | Effort `[INFERRED]` |
|---|---|---|---|
| 0 | Decisions, spikes S1–S8, accounts | PO approval | 8–12 d |
| 1 | Portability refactor | 0 | 6–9 d |
| 2 | Ledger derivation, write-path audit | 1, D6, D8, D15 | 12–18 d |
| 3 | Sync engine, desktop-to-desktop | 2 released, D7, D10 | 30–45 d |
| 4 | OneDrive, Google Drive transports | 3, S6, S7 | 15–25 d |
| 5 | Android full client | 4, D1, D3, D4, D13 | 40–60 d |
| 6 | iOS | 5, macOS, Apple account | 15–25 d |
| 7 | Completion (parity) | 5 / 6 | 20–30 d |

Total: about 146–224 developer-days, roughly 7–11 developer-months.
`EVOLUTION.md` §7.4 estimated 2–4 months for a mobile MVP **without**
sync; the difference is the sync engine, the ledger refactor and the
provider transports.

---

## 14. Retro-compatibility

- Phase 2 changes storage (facts and derived rows) for every profile,
  synced or not. Numbers up to the cutoff never change. After the
  cutoff, two behaviors change on purpose: retroactive schedule changes
  are re-derived (D6), and two schedule rows with the same
  `EffectiveFrom` resolve to the later one (§17).
- `.mrz` archives of older schema versions import unchanged, mapped to
  `Legacy` rows.
- Older desktop versions cannot join a sync group (no sync code). A
  newer device that meets an operation schema it does not know stops
  applying for that group and asks for an update (R7).
- Single-instance mutex and single process per database are unchanged.

---

## 15. Non-goals — restated

Backend service; shared database file; sync of registry, PIN, role,
SMTP or backup settings; medical-device functions; desktop Linux;
iCloud transport; tablet-specific layouts; web client.

---

## 16. Decisions still to confirm

| # | Decision | Options | Proposal | Needed by |
|---|---|---|---|---|
| D1 | Platforms and order | Android then iOS; both; Android only | Android then iOS | Phase 0 |
| D2 | UI framework | MAUI; Avalonia | MAUI | Phase 0 |
| D3 | Providers and order | OneDrive, Google Drive, Dropbox | OneDrive, then Google Drive; Dropbox later | Phase 0 |
| D4 | Notification defaults per device | Proposal in §8.3 | Dose on phone, low-stock everywhere | Phase 5 |
| D5 | Relative order with A2 | A2 first; B.1 first | A2 phase 1 first (independent, smaller) | Phase 0 |
| D6 | Retroactive schedule changes after cutoff re-derive past days | Yes; no (freeze on first derivation) | Yes (convergent and more correct) | Phase 2 |
| D7 | Conflict review scope | Show all LWW losses; show only listed cases (§4.5) | §4.5 list | Phase 3 |
| D8 | Retraction (delete a mistaken fact) | Add now; later | Add in Phase 2 | Phase 2 |
| D9 | Portable project name, namespaces | `MedReminder.Infrastructure.Portable`, keep namespaces | As proposed | Phase 1 |
| D10 | Sync passphrase vs cloud-backup passphrase | Same; separate | Separate, with an option to reuse | Phase 3 |
| D11 | `StripReleaseDebugArtifacts` exclusion for mobile if S4 fails | Approve; reject | Decide on S4 evidence | Phase 5 |
| D12 | iCloud transport | Plan; exclude | Exclude | Phase 0 |
| D13 | Minimum OS versions | — | Android 8.0 (API 26), iOS 15 `[INFERRED — not measured]` | Phase 5 |
| D14 | Donation links on iOS | Include; exclude | Exclude unless verified compliant | Phase 7 |
| D15 | Automatic consumption for inactive periods | None on inactive days (activity history); today's catch-up on reactivation | None on inactive days | Phase 2 |

---

## 17. Corrections to other documents

To apply in the Phase 1 PR:

- `EVOLUTION.md` §2.0, §6, §7, §8: B.1 now includes synchronization,
  C.3++ Phase 2 and the functional goal of C.1 without a backend; C.1
  shrinks to "hosted relay transport", optional.
- `EVOLUTION.md` §7.2: MAUI has no built-in local-notification API.
- `EVOLUTION.md` §7.6: the localization loader is behind
  `ILocalizationService` already; its implementation is in the Windows
  Infrastructure project, not in `MedReminder.UI`.
- `ANALYSIS-C3PP-CLOUD-PROVIDERS.md` §6.1: `ExportService` and
  `ImportService` are not `net10.0`; they are Windows-only
  `[VERIFIED]`. §6.3 (file picker first on mobile) is superseded: sync
  needs provider APIs from the first mobile release.
- `ANALYSIS.md` §4.4: invariants change in Phase 2 (facts and derived
  rows, count anchors, derived epoch, write gate on every use case).
- Existing behavior found during this analysis:
  `ChangeMedicationSchedule` always inserts a new row, even when one
  with the same `EffectiveFrom` exists. `DailyConsumption` picks the
  latest row with a strict `>` on `EffectiveFrom`, and the repository
  orders by `EffectiveFrom` only, so among equal dates the **first
  enumerated** row wins and a second change for the same date is
  ignored `[VERIFIED — DailyConsumption.cs, MedicationScheduleHistoryRepository.cs]`
  (which row SQLite returns first without a secondary sort key is
  `[UNCERTAIN]`). Phase 2 fixes this independently of sync: the most
  recently recorded row wins.
- Existing behavior found during this analysis: the catch-up skips
  inactive medicines and, on reactivation, books the whole inactive
  period at once (§4.4b, D15).

---

## 18. Spike results

Empty until Phase 0 runs. One subsection per spike: date, device / OS,
result, decision.

---

## 19. Sources

- .NET MAUI support policy — dotnet.microsoft.com/platform/support/policy/maui
- Supported platforms for .NET MAUI apps (.NET 10) — learn.microsoft.com/dotnet/maui/supported-platforms
- What's new in .NET MAUI for .NET 10 — learn.microsoft.com/dotnet/maui/whats-new/dotnet-10
- `AesGcm`; "Support AES-GCM for iOS-like platforms", dotnet/runtime
  issue #91523 — learn.microsoft.com/dotnet/api/system.security.cryptography.aesgcm; github.com/dotnet/runtime/issues/91523
- Android 14: schedule exact alarms denied by default — developer.android.com/about/versions/14/changes/schedule-exact-alarms
- SQLite: "How To Corrupt An SQLite Database File" and FAQ on network
  and synced filesystems (sqlite.org, from training knowledge)
- Hybrid logical clocks: Kulkarni et al., "Logical Physical Clocks"
  (2014), from training knowledge
- Internal: `EVOLUTION.md` §2.0, §6–§9; `ANALYSIS.md` §2, §4.4, §6,
  §8, §10; `EXPORT-FORMAT.md`; `ANALYSIS-C3-EXPORT-IMPORT.md`;
  `ANALYSIS-C3PLUS-CLOUD-BACKUP.md`; `ANALYSIS-C3PP-CLOUD-PROVIDERS.md`;
  `notes/AZURE-ENTRA-PUBLIC-CLIENT-APPLICATION-GUIDE.md`.

---

## 20. Change log for this document

- 2026-09-26 — initial version: read-only replica first, operation
  journal as optional later phase.
- 2026-09-26 — revision 2: synchronization made mandatory. Replicated
  operation log over the user's cloud storage with end-to-end
  encryption and no backend; facts / derived split of the stock ledger
  with count anchors and genesis cutoff; HLC and per-class merge rules;
  per-device segments, causal delivery, compaction, generations;
  QR pairing and key rotation; provider transports (absorbs C.3++
  Phase 2); full-client feature parity; phases 0–7; decisions D1–D14.
- 2026-09-26 — review against the code: stock-count anchor carries
  `takenToday` and reuses the `ReconcileStock` formula; intake
  consumption derived through today; one cutoff set by the Phase 2
  patch; `StartDate` immutable and schedule summary from the latest
  recorded row; checkpoint content; dependency vector at seal time;
  associated data needs an `IArchiveCipher` extension; revocation
  requires a new passphrase; tail-truncation mitigation; write gate on
  every use case and field-diff operation emission; schedule tie
  behavior recorded in §17.
- 2026-09-26 — second review: deriver signature and per-rule day
  ranges; count-day consumption not derived twice; derivation runs on
  inactive medicines too; activity history and D15; genesis in
  checkpoint format; `.mrz` keeps derived rows tagged by origin;
  authenticated device revocation; QR secrecy; designated mail device
  failover; clock warning shown by the receiver; `Legacy` facts not
  retractable; phase dependencies (P3, Phase 4 entry on D3).
