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

For Android, revised by `ANALYSIS-B1-ANDROID-PLAN.md` (approved
2026-10-08), which takes precedence:
the app also works on its own, without a PC or an account.

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
| P5 | Persistence usable outside Windows | **Met by Phase 1** | EF Core model, repositories, `DatabaseInitializer` moved to `MedReminder.Infrastructure.Portable` (`net10.0`); their tests run on Linux |
| P6 | Archive read path usable outside Windows | **Met by Phase 1** | `ArchiveReader` (`IArchiveReader`) and `ProfileDatabaseBuilder` in the portable project; `ImportService` is the Windows shell (file swap, DPAPI rewrap, settings files). `ExportService` stays Windows-only (mobile export is Phase 7) |
| P7 | View models outside WinForms | **Met by Phase 1** | `MedicineOverviewLoader` and `MedicineListItem` moved to `MedReminder.Application/Overview` |
| P8 | Every data write goes through an Application use case | **Met by Phase 2a for the profile database** | The only direct UI write (`MainForm` deactivate: `medicine.IsActive = false` + `SaveChangesAsync`) moved to the `DeactivateMedicine` use case; `UiWritePathGuardTests` fails on any repository write, `SaveChangesAsync` or `DbContext` use under `src/MedReminder.UI` `[VERIFIED]`. Open: two replicated values (§4.2) are still written by the UI outside the database: per-profile notification settings (`SettingsDialog` writes `notifications.settings.json`) and the profile display name (`ProfilesManagerForm` calls `IProfileRegistry.Rename`). They needed a use case before operation capture reached them; the product owner deferred them out of Phase 2a (2026-09-27). **Closed on 2026-09-28**: `UpdateNotificationSettings` and `RenameProfile` record `ProfileSettingChanged`; `UiWritePathGuardTests` forbids the direct writes in the UI |
| P9 | Stock ledger is a deterministic function of user facts | **Met by Phase 2c-2** (after the cutoff; counts carry their stored outcome until Phase 3) | `LedgerSynchronizer` derives consumption, reversals, count corrections and `StockEpoch` from the facts; the stored ledger equals a fresh derivation after every action of the random scenarios (`LedgerParityTests`) `[VERIFIED]` |
| P10 | Stable GUID identity on every replicated entity | **Met** | All entities use `Guid Id` generated at creation `[VERIFIED — Domain entities]` |
| P11 | Medicines are never hard-deleted | **Met, with one exception (2026-09-28)** | Deactivation via `IsActive` `[VERIFIED]`; slots are no longer deleted: since Phase 2b `UpdateMedicine` appends a slot set `[VERIFIED]`. A medicine without recorded facts can be deleted: `MedicineDeleted` operation, §4.2 |
| P12 | AES-GCM on mobile | iOS 13+ on .NET 9+ **met**; Android **met** (S1, §18.1) | dotnet/runtime #91523 `[VERIFIED]`; spike S1 on Android 16 |
| P13 | EF Core SQLite on Android / iOS AOT | Android **met** with the Release defaults, not with full trimming (S3, §18.3); iOS `[UNCERTAIN]` | Spike S3 |
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
countDay, countedQuantity, takenToday, thresholdAtCount)`, recorded
with its HLC: an **anchor**. `takenToday` is the user-entered quantity
already taken on the count day, an input of today's `ReconcileStock`
command `[VERIFIED — ReconcileStockCommand.TakenToday]`. The derived
correction is computed with the **existing** `ReconcileStock` formula
(moved to Domain unchanged) on the facts recorded before the count
(§4.3 rule 3), so a count taken on the phone is correct against
whatever the desktop recorded before it, and facts recorded after the
count apply on top of it.

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
- Implemented by Phase 2b: single-row table `LedgerCutoff`
  (`CutoffDay`, `FrozenAt`). A database created after the patch has no
  row; the deriver then uses the day before each medicine's
  `StartDate`. `FrozenAt` is stored because rows recorded on day C+1
  before the patch are `Legacy` too: rule 1 (§4.3) must not derive
  intake consumption for intakes recorded before `FrozenAt`, or C+1
  would count them twice `[INFERRED — from §4.3 rule 1 and the patch
  semantics]`.
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
| Administration slots of a medicine | Dated set history | Today slots have no date and apply to every day not yet booked `[VERIFIED — DailyConsumption comment "current retroactive"]`; a pure derivation would rewrite every past day after a slot change. Each slot-set change is recorded with `EffectiveFrom` = the day it is made (the set given at creation: from the beginning). On day d, the most recently recorded set among those in force wins, so a change made today also replaces a set whose start is still in the future `[VERIFIED — S9, §18]`. Stored since Phase 2b as `MedicationAdministrationSlotSets`; a set may be empty (slots cleared, back to dose × frequency), which is why the set is a row of its own and not columns on the slot rows |
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

Every last-writer-wins register keeps its versions, not only the
winner: count anchors are evaluated "as of" their HLC (§4.3), and the
conflict review restores a losing value. History can be pruned only
behind the anchor horizon (§5.6).

Deletion: medicines with history are deactivated, never deleted (P11).
The only deletions are slot-set replacement (covered by the set
register), the user deleting a mistaken fact (stock entry, count,
intake, suspension), and the deletion of a medicine without recorded
facts (below). That becomes a **retraction** operation: a tombstone keyed
by the fact `Id`, which wins over the fact whatever the order of
arrival. Today the UI does not delete these facts; retraction is an
addition (D8). `Legacy` facts cannot be retracted: a mistake before the
cutoff is fixed with a correction, as today.

Implemented by Phase 2d, with one more restriction (product owner,
2026-09-27): a fact can be retracted only if no stock count was
recorded after it. A count's outcome already includes the facts
recorded before it: retracting one of them afterwards would leave the
stock off by its quantity (count 40 after a mistaken refill of 28:
correction -28; retracting the refill would show 12). The count
itself, when it is the latest, can be retracted. Locally the fact row
is removed and a tombstone is kept in `FactRetractions`, keyed by fact
id for Phase 3.

Retraction and a concurrent count (product owner, 2026-09-27): a
retracted fact never happened, on every device. It is left out of
every count snapshot too, so a count recorded on another device that
had included it is re-evaluated without it and the stock equals what
was counted. Implemented by Phase 3b-2: a retracted fact's row is
removed, so no snapshot contains it. When the same fact is retracted
on two devices, the earliest tombstone (recording instant, then id) is
kept on every device.

Deletion of a medicine (product owner, 2026-09-28): a medicine entered
by mistake can be deleted while no fact was recorded for it on the
deleting device (no `User` or `Legacy` stock entry, intake, count or
suspension). The deletion is a `MedicineDeleted` operation (operation
schema version 2) and wins over every operation for the medicine,
whatever the order of arrival: the receiving device removes the
medicine and every row that refers to it, facts recorded there
concurrently included, and logs without applying any later operation
for it. The tombstone is the `MedicineDeleted` row of the operation
log, which is never pruned and travels in checkpoint images, so a
device that joins from an image skips the late operations too. Images
moved to schema version 2, since an older app cannot skip them.

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
   Frozen rows are never edited. Implemented (Phase 2c-1) for any day
   carrying `Legacy` consumption, not only days up to the cutoff: the
   patch can run in the middle of day C+1 after a count materialized
   it.
2. **Automatic consumption, days in `(cutoffDay, yesterday]`**: if no
   intake of any status exists for `d`, no count anchor has already
   derived `d`'s consumption (rule 3), the medicine is active on `d`
   (activity history, D15), and `d` is in the therapy window and not
   suspended, derived automatic consumption =
   `ConsumptionMaterializer` result for `d` (existing Domain code,
   unchanged), at local midday of `d` as today `[VERIFIED —
   ConsumptionCatchUp]`. A day that already carries `Legacy`
   consumption is skipped too (Phase 2c-1, same reason as rule 1b). An intake day never gets automatic
   consumption, so today's backdated-intake reversal movement is no
   longer needed.
3. **Stock-count anchors**: for each `StockCount`, in HLC order, the
   correction is the `ReconcileStock` formula evaluated on a
   **snapshot**: the facts **recorded** before the count (HLC order),
   derived as they would have been at that moment, plus the earlier
   anchors. Recording order, not `OccurredAt`: an intake's movement is
   dated at midday of its day, not at the moment it was entered, and
   today's `ReconcileStock` only sees what was entered before it
   `[VERIFIED — S9, §18]`. expected = raw (unclamped, as today through
   `LedgerAlignment`) total of that snapshot, which includes automatic
   consumption through the day before the count day, minus
   `takenToday`; correction = `counted - expected`. When `takenToday`
   equals the count day's whole scheduled quantity, the count day's
   consumption is booked with the quantity **fixed in the snapshot**
   (today's `MaterializesToday` branch) and rule 2 skips that day; a
   later intake for that day still removes it, as `RegisterIntake`
   reverses it today. The epoch rule uses the threshold captured in the
   fact (`thresholdAtCount`). Snapshot evaluation needs the value of
   every register "as of" an HLC, so registers keep their version
   history (§4.2). Until Phase 3 there is no version history: the
   outcome (start-of-day ledger, count-day quantity, correction,
   materialization, epoch advance) is evaluated when the count is
   recorded and stored with the fact. On one device this is exact;
   Phase 3 re-evaluates it when facts from other devices arrive
   (product owner, 2026-09-27). Implemented by Phase 3b-2
   (`CountReevaluation`, `LedgerDeriver.ReevaluateCount`): with sync
   enabled, every count recorded after sync was enabled is evaluated
   again, in HLC order, on the facts whose recording operation is older
   than its own (`SyncOperations.EntityId`), with the therapy end date
   and the suspensions' end dates as they were at its HLC (register
   versions; `SyncGenesis` records the values from before sync at the
   genesis timestamp) and with the earlier counts re-evaluated first.
   Counts from before sync keep their stored outcome. The stored input
   `takenToday` is capped by what the snapshot leaves due on the count
   day, since a merged intake can already have booked it. On one device
   the re-evaluated outcome equals the stored one (parity test).

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
ran, which breaks R6. The write gate (`WriteGate`, `MonitoringGate`
before Phase 3a) still serializes it.

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
warning. Implemented by Phase 2d (`Medicine.StockEpochFactId`,
`NotificationEvents.EpochFactId`), because a retraction already
renumbers epochs on one device; the baseline epoch at the freeze has a
name-based id. Additive column via idempotent patch (`CLAUDE.md` §7).

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

Which writes are conflicts (product owner, 2026-09-27): only concurrent
ones. The three register operations that can conflict
(`MedicineFieldChanged`, `ScheduleRowRecorded`, `SlotSetRecorded`)
carry the version of the register their device held when it wrote
(`BaseVersion`). A device writes only over what it has seen, and the
HLC makes everything it has seen older than its write, so of two
versions the later one did not see the earlier one exactly when its
base is older (`RegisterMerge`, Domain). The listed register conflicts
are the versions concurrent with the winner: a pure function of the
versions, identical on every device that holds them. A later write by a
device that had seen both clears them. The hints (overlapping
suspensions, intakes over the schedule, retraction of an edited
suspension) are raised when an incoming operation meets local state,
so they are local notices and may differ between devices. Implemented
by Phase 3b-1.

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
- **Anchor horizon**: count anchors older than the stale-device window
  (90 days) are frozen with their computed correction; register
  history older than the horizon can then be pruned from checkpoints.
  Without a horizon, state size equals the full operation log (S9
  finding) `[INFERRED — not exercised by S9]`.
- A bootstrap picks the newest checkpoint that covers every folder's
  first remaining segment, not simply the newest one: a checkpoint
  written by a device that was still catching up may not cover
  segments already deleted `[VERIFIED — S9]`.
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
| `OneDriveSyncTransport` (Microsoft Graph, MSAL public client) | Yes | Yes | 4a |
| `GoogleDriveSyncTransport` (Drive REST API) | Yes | Yes | 4b |
| iCloud | Only through `LocalFolder` on Windows | Native container | Excluded, D12 |

Why desktop also uses the provider API rather than only the synced
folder: with Google Drive the narrow `drive.file` scope lets an app see
only files it created or the user opened with it, so files written by
the Google Drive desktop client would be invisible to the phone app.
Using the same API on both sides avoids the issue; spike S7 confirmed
that files created by one OAuth client of the project are visible to
another (§18.7). For OneDrive the app-folder permission (`Files.ReadWrite.AppFolder`)
limits access to `/Apps/<app>`; spike S6 confirmed the isolation and
found that the Windows OneDrive client does not download that folder
(§18.6), so the desktop uses the API for OneDrive as well.

Layout inside a provider's app folder (Phase 4a): sync files under
`sync/` (the root of `SYNC-FORMAT.md` §2), backups under `backups/`.
Google Drive (Phase 4b): sync files flat in the hidden app data folder,
named by their relative path and tagged with a public property; backups
in a visible `MedReminder/backups` folder of My Drive (product owner,
2026-09-28).

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

   As implemented (Phase 4c): the code holds `groupId`, the showing
   device's id, the provider and a 32-byte random secret, **not the
   group key**. The key lies in `pairing/<deviceId>.mrp`, encrypted with
   that secret, with the expiry inside the ciphertext; the file is
   deleted when the dialog closes. "Only while the dialog is open" is
   thus enforced, not advisory: a photo of the code opens nothing once
   the offer ended, unless the storage file was also copied during the
   offer. The same code is shown as text, so a PC can join or take a new
   key with it (Join with a pairing code). The window used
   `SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)`; a manual test on
   2026-09-30 showed the window invisible in a remote-control session
   (RustDesk), where the application looked frozen behind it. Since then
   the window is always visible and the code stays hidden until the user
   chooses "Show the code" (product owner, 2026-09-30): it is kept out of
   screenshots and shared screens taken before, and the exclusion was no
   barrier against a program running as the user, which can read the
   protected keys directly.
   Format: `docs/SYNC-FORMAT.md` §4.4.
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

**As implemented (Phase 4c), a rotation starts a new generation**
(`RotateSyncKey`): `key.<v+1>.wrap` with the new passphrase, then a
genesis of the rotating device's database sealed with key `v+1`. A
generation has exactly one key version, the one its genesis is sealed
with. Differences from the design above, and why:

- No `DeviceRevoked` operation and no key chain. Within one generation,
  a revoked device holding the old key could forge old-key segments in
  any device's folder, and a rule accepting old-key segments from
  lagging honest devices cannot tell them apart. With one key per
  generation the new key is the capability: files the removed device
  can write are sealed with an older key and are ignored.
- Remaining devices stop **before publishing** when a newer generation
  is sealed with a newer key (`SyncRunResult.NewKeyRequired`), so none
  of their later operations is sealed with a key the removed device
  holds. After the new passphrase or a pairing code
  (`JoinSyncGroup.RekeyAsync`) they rebuild from the new genesis and
  **carry over** their own operations of the old generation that the
  genesis lacks (`ApplyRemoteOperations.ApplyCarriedAsync`: same ids and
  timestamps, pending in the new generation). Unlike a reset (§5.7),
  nothing an honest device recorded is lost, except operations for a
  medicine the new generation does not have (it came from a device the
  rotating device had not heard from); these are counted and reported.
  Operations of the removed device not yet applied by the rotating
  device are lost, by intent.
- A newer genesis sealed with an older key version is ignored; device
  records and checkpoints sealed with another key version are skipped.
- Residual risk while the removed device keeps a provider session: it
  cannot read anything new, but it can write files that disturb sync
  (a forged wrap and genesis with a key of its own make the others ask
  for a passphrase that does not open it). Ending its sessions, as the
  UI asks, closes this.

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
  Phase 2a implements the UI half as a source scan
  (`UiWritePathGuardTests`): the UI project targets `net10.0-windows`
  and cannot be loaded on Linux, and the check adds no NuGet
  dependency. The write-method names come by reflection from the
  repository ports. Writes inside `MedReminder.Application` that are
  not use cases (`MedicationMonitor`, `ConsumptionCatchUp`,
  `DoseReminderService`) are not covered; they produce derived rows
  and notification events, which Phase 2c revisits.
- Implemented by Phase 3a. `IOperationLog.AppendAsync` adds the
  operations to `SyncOperations` in the use case's unit of work, each
  with the next HLC (§4.1), and records nothing while sync is disabled
  for the profile (product owner, 2026-09-27: the genesis carries what
  was written before). The catalogue, schema version 1
  (`Domain/Sync/SyncOperationBodies.cs`, JSON form in `OperationCodec`):
  `MedicineCreated`, `MedicineFieldChanged` (one per replicated field,
  text form in `MedicineFieldCodec`), `MedicineActivityChanged`,
  `ScheduleRowRecorded`, `SlotSetRecorded`, `StockEntryRecorded`,
  `IntakeRecorded`, `StockCountRecorded` (inputs and outcome),
  `SuspensionRecorded`, `SuspensionEndChanged`, `FactRetracted`. Each
  carries the ids of the rows written, so a replay is idempotent.
  `OperationEmissionGuardTests` requires every Application service that
  saves a unit of work to take `IOperationLog`, except the writers of
  derived and device-local rows (`ConsumptionCatchUp`,
  `MedicationMonitor`, `DoseReminderService`). The notification
  recipients and the profile display name (P8) are covered since
  2026-09-28 by `ProfileSettingChanged` (operation schema 3), recorded
  by `UpdateNotificationSettings` and `RenameProfile`.

### 7.3 New persistence objects

Idempotent boot patches in `DatabaseInitializer` (`CLAUDE.md` §7):

| Object | Purpose |
|---|---|
| `StockMovements.Origin` column (`Legacy`, `User`, `Derived`) | Separate facts from derived rows. Phase 2b |
| `StockCounts` table | Count anchors. Table in Phase 2b; written from Phase 2c |
| `MedicationAdministrationSlotSets` table (`EffectiveFrom`, `RecordedAt`, later the recording HLC) and `MedicationAdministrationSlots.SetId` | Dated slot sets (§4.2), including empty sets. Phase 2b |
| `LedgerCutoff` table (`CutoffDay`, `FrozenAt`) | Cutoff C (§3.5). Phase 2b |
| `SyncOperations` table | Local outbox and applied-operation ids. Phase 3a: outbox (HLC, generation, type, schema version, JSON payload, `SegmentSeq` null while pending); Phase 3b-1: also the operations applied from other devices |
| `SyncFieldVersions` table | HLC per `(entity, id, field)` for LWW. Phase 3b-1: every version of every register, with its base version |
| `SyncPeers` table | Applied vector per remote device. Phase 3c: also this device's published segment and checkpoint counters |
| `SyncConflicts` table | Conflict review list. Phase 3b-1 |
| `SyncTombstones` table | Retractions. Not created: `FactRetractions` (Phase 2d) already is the tombstone table, keyed by fact id |
| `NotificationEvents.EpochFactId` column | Dedup key stable across merges (§4.4) |

Export format: `ExportFormat.CurrentSchemaVersion` bump for `Origin`
and `StockCounts` (`ANALYSIS.md` §8.1 rule). The `.mrz` payload keeps
the full ledger, derived rows included and tagged by `Origin`, so the
exported stock stays readable; an importer of the new version drops
derived rows and re-derives. Archives of the current schema version
(no `Origin`) import as `Legacy` with the cutoff at the day before the
import, like the boot patch. Phase 2b implemented schema version 2
(`docs/EXPORT-FORMAT.md` §5.1); dropping derived rows on import waits
for the deriver (Phase 2c).
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
  per database stays the rule (`CLAUDE.md` §7). Implemented by Phase 3a
  (`WriteGate`, the former `MonitoringGate`). Consequence
  `[INFERRED]`: a user action now waits while a monitor pass sends a
  low-stock email, since the monitor holds the gate during the send.
- **Stale forms**: an edit dialog opened before a sync must not write
  back fields the user did not touch. Use cases receive the user's
  changes as a diff against the values loaded when the dialog opened,
  and emit operations only for changed fields. Otherwise every save
  would stamp all fields with a new HLC and silently overwrite remote
  edits. After an apply, open views refresh. Implemented by Phase 3a
  for the medicine edit dialog, the only form that saves a whole record:
  `UpdateMedicineCommand.Baseline` carries the values the dialog loaded,
  and only the fields that differ from it are written (slots and
  catalogue link included). Behavior change on one device: saving the
  dialog without touching the slots no longer records a new slot set.
- **Mobile**: sync on start, resume, after local writes, and in the
  background with Android WorkManager periodic work (minimum interval
  15 minutes `[VERIFIED — Android WorkManager documentation, training
  knowledge]`) and iOS background app refresh, whose timing the OS
  decides `[INFERRED]`. Measured on one Android 16 phone, the 15-minute
  work ran every 1 to 4 hours (S8, §18.8): background sync is best
  effort, and the sync status shows the time of the last sync.
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
| `IEmailNotificationService` | MailKit | MailKit, only when the device is the household master (§8.4) |
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
  `[VERIFIED — S5, §18.5]`.
- iOS: explicit authorization.
- MAUI has no built-in local-notification API `[INFERRED]`; thin native
  adapters (`AlarmManager` + `NotificationCompat`, `UNUserNotificationCenter`)
  behind `ILocalNotificationScheduler`. On Android the exact kinds
  (`setExactAndAllowWhileIdle`, `setAlarmClock`) fire within seconds;
  the inexact fallback fires up to 25 minutes late (S5, §18.5).

### 8.3 Duplication policy

By default, dose reminders are delivered by the phone on which they
are scheduled; low-stock alerts are delivered on every paired device.
The user can override notification delivery per device and kind.
Medicine details on the lock screen are hidden by default and may be
shown using a per-device control. Android's own visibility settings may
also affect displayed details. These D4 defaults were selected on
2026-10-08. An intake recorded on one device suppresses the pending dose
reminder on the others after the next sync. Sync latency is minutes on
foreground devices and OS-dependent in background, so a duplicate
reminder is possible and accepted.

### 8.4 Email

Superseded on 2026-10-08. The "designated mail device" first designed
here (one per profile, a replicated LWW field) was never implemented
and is replaced by the **household master**
(`ANALYSIS-HOUSEHOLD-MASTER-DEVICE.md` R5, C4, C5, §7.6), as backlog
item B5-03 of `ANALYSIS-B1-ANDROID-IMPLEMENTATION-BACKLOG.md` specifies:

- Only the master sends email (low-stock, caregiver copies, weekly
  digest), for every profile of the installation, and only when SMTP
  is configured on it. A non-master device never sends email through
  SMTP.
- The device list shows which device is master and its last-seen time.
  If the master is off, no email is sent; the role moves only through
  the household handover or takeover, never silently.
- A phone may be master, with the timing limits of §8.2 and the
  household recommendation of a PC as master (C8); when a PC joins a
  phone-first installation, the phone proposes to hand the master role
  to the PC.
- Mobile uses MailKit on the master (Android from M5 of
  `ANALYSIS-B1-ANDROID-PLAN.md`).
- Prescription requests (user initiated) are sent from the device where
  the user acts, via `mailto:` on any device or SMTP on the master.

---

## 9. Mobile app

### 9.1 Screens (end state)

For Android, superseded by `ANALYSIS-B1-ANDROID-PLAN.md` (standalone
app, feature inventory of v2.16.0, milestones M0–M5), approved
2026-10-08, and by `ANALYSIS-B1-UI-REQUIREMENTS.md` for the screens.

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
10. Settings: language, notifications per kind, email (master only),
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

For Android, superseded by `ANALYSIS-B1-ANDROID-PLAN.md` §3 (approved
2026-10-08).

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
| Email notifications | Only on the household master (§8.4) | 7 |
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
| Divergence bug in merge or derivation | Medium | High | Pure Domain rules; convergence simulation in CI; state-hash exchange between devices (each device publishes, per applied vector, a hash of its replicated state **and** of the resolved current values, derived rows excluded; mismatch raises an error and offers re-bootstrap). A hash of the version sets alone does not detect a wrong winner selection `[VERIFIED — S9 negative control]` |
| Ledger refactor changes existing numbers | Medium | High | Cutoff (§3.5); parity tests; refactor shipped (Phase 2) before sync |
| Provider API limits, scope policies, OAuth verification | Medium `[UNCERTAIN]` | Blocking per provider | Spikes S6, S7; Google Drive first for Android, then OneDrive; `LocalFolder` fallback on desktop |
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

1. Decide the remaining open items needed before Phase 1 (D9) and before
   Phase 5 (D11, decided 2026-10-08). D4 was decided 2026-10-08; validate the provisional
   Android API 26 floor in the M1 technical spike (D13). D1–D3, D5,
   D6, D8, D10, D15 were decided on 2026-09-26.
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
   - **S9 Convergence prototype (first spike, gates Phase 1)**: pure
     `net10.0` prototype of `LedgerDeriver` (§4.3, §4.4, §4.4b), the
     merge rules (§4.2, §4.5) and the convergence simulation harness
     (§11). Acceptance: (a) the deriver reproduces the expected results
     of the existing `ConsumptionCatchUp`, `RegisterIntake` and
     `ReconcileStock` tests for days after the cutoff; (b) the
     simulation converges (identical replicated-state hash, `ANALYSIS.md`
     §4.4 invariants hold) over at least 10 000 random seeds with
     2–5 devices, duplication, reordering and partitions. A failure is
     fixed in this document first, then in the prototype. The
     prototype is throw-away; Phases 2 and 3 re-implement it in
     production code with its tests as the starting point.
     Reason: this is the part verified only on paper, and two reviews
     of this document already found errors in it.
3. App registrations (Entra, Google Cloud), store accounts, macOS build
   host if iOS is in scope.

**Exit**: S9 passes; S1, S3 pass or have accepted mitigations; S6
passes for Windows; D9 decided. Met on 2026-10-05 for Android: S1 and
S3 pass with the Release defaults (§18.1, §18.3). The Android halves of S6 and S7 need
the Android OAuth clients (P14) and run with Phase 5 (§18).

**Effort**: 8–12 days, plus 8–12 days for S9 `[INFERRED]`.

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

**Split into pull requests** (product owner, 2026-09-27):

| PR | Scope | State |
|---|---|---|
| 2a | Action 1 (P8) | Merged (#80) |
| 2b | Schema and export for actions 2, 5, 6: `Origin`, `StockCounts` table, dated slot sets, `Legacy` freeze and cutoff, export schema version 2. No behavior change: `ReconcileStock` does not record counts yet | Merged (#81) |
| 2c-1 | Action 3, Domain part: `LedgerDeriver` and `EvaluateCount` in `MedReminder.Domain/Ledger`, S9 parity harness ported to the use cases. Not called by the application: no behavior change | Merged (#82) |
| 2c-2 | Actions 2 (count recording), 3 (use cases write facts, derived rows replaced), 4 without `EpochFactId`; schema for activity history, recording instants, epoch baseline; re-freeze at boot and on every import | Merged (#83) |
| 2d | Fact retraction (D8) with a history window, and `EpochFactId` | #84 |

Phase 2c-2 decisions (product owner, 2026-09-27): `EpochFactId` moves
to 2d, because without retraction or merge an epoch is never
renumbered on one device and dedup by epoch number stays exact; derived
rows are re-derived by the catch-up tick for every medicine and at once
by `RegisterIntake` and `ReconcileStock`, as today; every `.mrz` import
applies the same freeze as the boot patch (`LedgerFreeze`) instead of a
new export schema version, so an imported profile keeps its numbers and
its facts before the import are frozen.

2c was split into 2c-1 and 2c-2 by the product owner (2026-09-27).

Constraint for 2c-2, decided (product owner, 2026-09-27): rows written
between the 2b patch and 2c-2 carry `Derived` count corrections without
a `StockCount` fact, and intakes have no recording instant. The 2c-2
patch therefore re-freezes: every existing movement and intake becomes
`Legacy`, C moves to the day before the 2c-2 patch, and each medicine's
current `StockEpoch` is stored as its epoch baseline. No past number
changes.

Phase 2c-1 result: parity holds on 10 000 random 25-day scenarios (one
third patched on day 12, at a random point of the day) plus scripted
cases; the four differences of §18.9 are reproduced with the same
figures.

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

**Split into pull requests** (product owner, 2026-09-27):

| PR | Scope | State |
|---|---|---|
| 3a | Action 1: HLC, operation catalogue, `IOperationLog` and the `SyncOperations` outbox, emission from every use case; one write gate for every use case; stale-form diff (§7.4). Operations recorded only when sync is enabled, so no behavior change until 3d | Merged (#85) |
| 3b-1 | Action 2: apply of remote operations, register versions and LWW, tombstones, conflict list (§4.5), convergence harness on real databases; counts keep their stored outcome | Merged (#86) |
| 3b-2 | Re-evaluation of count outcomes on the snapshot by HLC (facts and register values "as of"), retracted facts left out of every snapshot (§4.2); genesis register versions; harness extended to concurrent counts | Merged (#87) |
| 3c | Actions 3, 4, 7 in one PR (product owner, 2026-09-27): associated data on the cipher, key wrap, envelope, segments with dependency vectors, causal buffer, `SyncPeers`, device records, genesis and checkpoint images, join, compaction, generations, `ISyncTransport` with `LocalFolderSyncTransport` and contract tests, `SYNC-FORMAT.md`. Key rotation and revocation (§6.2) move to Phase 4 with QR pairing | Merged (#88) |
| 3d | Actions 5, 6: `SyncHostedService`, Tools → Sync… (status, devices, conflict review with restore and dismiss; enable, join, rebuild, disable for the administrator), reset flow in import and restore, manual two-PC checklist (`docs/SYNC-TWO-PC-CHECKLIST.md`). No CI workflow (product owner): the convergence harness runs with the test suites | #89 |
| 3c | Actions 3, 4, 7: segment codec, group key, genesis, checkpoints, compaction, generations, `ISyncTransport` with `LocalFolderSyncTransport` and contract tests, `SYNC-FORMAT.md` | — |
| 3d | Actions 5, 6: `SyncHostedService`, desktop UI, reset flow in import and restore, convergence simulation in CI, two-PC checklist | — |

### Phase 4 — Cloud provider transports (includes C.3++ Phase 2)

**Entry**: Phase 3 exit; S6 / S7 results; D3 decided.

Split on 2026-09-27 (product owner): **4a** OneDrive sync transport,
OneDrive backup storage and sign-in (after S6); **4b** Google Drive
(after S7); **4c** QR pairing, device revocation and key rotation
(§6.1, §6.2, moved out of Phase 3c).

| PR | Scope | State |
|---|---|---|
| 4a | OneDrive sync transport, backup storage, sign-in | Merged (#91) |
| 4b | Google Drive | Merged (#95) |
| 4c | Pairing codes and QR generator on desktop (QRCoder), key rotation as a new generation, device removal, rekey with carry-over, Tools → Sync… actions, `SYNC-FORMAT.md` §4.4 and §7 | #99 |

**Actions**: `OneDriveSyncTransport` and `OneDriveArchiveStorage`
(MSAL, token cache under DPAPI on desktop); then Google Drive; provider
selection and sign-in in settings; QR pairing generator on desktop;
contract tests; `THIRD-PARTY-NOTICES.md` for MSAL and Google client
libraries.

**Exit**: desktop-to-desktop sync through each provider API; existing
`ArchiveStorageContractTests` green for the new archive backends.

**Effort**: 15–25 days `[INFERRED]`.

### Phase 5 — Android full client

For Android, Phases 5 and 7 are replaced by milestones M0–M5 of
`ANALYSIS-B1-ANDROID-PLAN.md` §5 (approved 2026-10-08): the app works without
a PC (product owner, 2026-10-06) and covers every current desktop feature
that applies to a phone.

For Android, the entry, actions, exit and effort below are historical;
the plan's milestones apply.

**Entry**: Phase 4 exit with Google Drive available; D1, D3, D4, D13
decided; Play Console account. Android provider priority is Google
Drive, then OneDrive.

**Actions**: MAUI project; composition root; screens §9.1 items 1–5, 8–10;
QR pairing scanner; provider sign-in, after the Android halves of S6
and S7; `NotificationPlanner` + Android
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
path); MailKit on a mobile master (§8.4); `.mrz` export on
mobile; donation links per D14; PDF share; device-side state-hash
verification UI; documentation completion.

**Exit**: feature-parity table (§10) fully satisfied or each gap
accepted by the product owner.

**Effort**: 20–30 days `[INFERRED]`.

### 13.1 Summary

| Phase | Content | Depends on | Effort `[INFERRED]` |
|---|---|---|---|
| 0 | Decisions, spikes S1–S9, accounts | PO approval | 16–24 d |
| 1 | Portability refactor | 0 | 6–9 d |
| 2 | Ledger derivation, write-path audit | 1, D6, D8, D15 | 12–18 d |
| 3 | Sync engine, desktop-to-desktop | 2 released, D7, D10 | 30–45 d |
| 4 | OneDrive, Google Drive transports | 3, S6, S7 | 15–25 d |
| 5 | Android full client | 4, D1, D3, D4, D13 | 40–60 d |
| 6 | iOS | 5, macOS, Apple account | 15–25 d |
| 7 | Completion (parity) | 5 / 6 | 20–30 d |

Total: about 154–236 developer-days, roughly 7–11 developer-months.
`EVOLUTION.md` §7.4 estimated 2–4 months for a mobile MVP **without**
sync; the difference is the sync engine, the ledger refactor and the
provider transports.

---

## 14. Retro-compatibility

- Phase 2 changes storage (facts and derived rows) for every profile,
  synced or not. Numbers up to the cutoff never change. After the
  cutoff, three behaviors change on purpose: retroactive schedule
  changes are re-derived (D6), the inactive days of a reactivated
  medicine are not booked (D15), and two schedule rows with the same
  `EffectiveFrom` resolve to the later one (§17).
- Since Phase 2c-2 the cutoff is the day before the 2c-2 boot patch (or
  before an import), not the 2b one: the re-freeze moves it forward.
  Schedule rows recorded before the patch have no recording instant;
  two of them with the same `EffectiveFrom` resolve in the order the
  database returns them, which SQLite does not guarantee
  `[UNCERTAIN — rare: the UI does not prevent same-date rows]`.
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

Decided on 2026-09-26: D1, D2, D3, D5, D6, D8, D9, D10, D15; on
2026-09-27: D7. On 2026-10-08, D4 was decided and Android API 26 was
selected provisionally in D13, pending M1 technical validation; D11
was decided and D14 settled by DA9 of `ANALYSIS-B1-ANDROID-PLAN.md`.
D12 was decided on 2026-10-08 (no iCloud transport). No B.1 decision
is open. The iOS 15 D13 proposal remains inferred and unmeasured.

| # | Decision | Options | Proposal | Needed by |
|---|---|---|---|---|
| D1 | Platforms and order | Android then iOS; both; Android only | **Decided 2026-09-26**: Android, then iOS | Phase 0 |
| D2 | UI framework | MAUI; Avalonia | **Decided 2026-09-26**: MAUI | Phase 0 |
| D3 | Providers and order | OneDrive, Google Drive, Dropbox | Decided 2026-09-26: OneDrive, then Google Drive; **updated 2026-10-08**: Google Drive first for Android, then OneDrive; Dropbox later | Phase 0 |
| D4 | Notification defaults per device | Dose on phone; low-stock everywhere; lock-screen details hidden by default | **Decided 2026-10-08**: per-device and per-kind overrides; lock-screen detail can be enabled per device | Phase 5 |
| D5 | Relative order with A2 | A2 first; B.1 first | **Decided 2026-09-26**: A2 phase 1 has shipped (PR #72); A2 phase 2 (webcam) is independent and may run after B.1 or in parallel | Phase 0 |
| D6 | Retroactive changes after cutoff (schedule rows, suspensions, therapy end date) re-derive past days; frozen days never change | Yes; no (freeze on first derivation) | **Decided 2026-09-26**: yes | Phase 2 |
| D7 | Conflict review scope | Show all LWW losses; show only listed cases (§4.5) | **Decided 2026-09-27**: §4.5 list | Phase 3 |
| D8 | Retraction (delete a mistaken fact) | Add now; later | **Decided 2026-09-26**: add in Phase 2 | Phase 2 |
| D9 | Portable project name, namespaces | `MedReminder.Infrastructure.Portable`, keep namespaces | **Decided 2026-09-26**: as proposed | Phase 1 |
| D10 | Sync passphrase vs cloud-backup passphrase | Same; separate | **Decided 2026-09-26**: separate | Phase 3 |
| D11 | `StripReleaseDebugArtifacts` exclusion for mobile if S4 fails | Approve; reject | **Decided 2026-10-08**: reject. S4 passed (§18.4), no exclusion; the private app repository adopts an equivalent target so that no `*.pdb` / `*.xml` ships in the app's Release output (this repository's target covers only projects under its own tree, such as the shared libraries built from the submodule `[INFERRED — MSBuild imports Directory.Build.props from the project's directory upwards]`) | M1 |
| D12 | iCloud transport | Plan; exclude | **Decided 2026-10-08**: exclude. A native iCloud container serves Apple devices only and cannot reach Android or Windows through the provider-API transports `[INFERRED]`; a Windows PC with iCloud for Windows can still use its folder through `LocalFolder` | Phase 0 |
| D13 | Minimum OS versions | — | **Android decided 2026-10-08**: 8.0 (API 26), provisional pending M1 MAUI/alarm/Play validation; iOS 15 remains `[INFERRED — not measured]` | M1 validation (Android); Phase 7 (iOS) |
| D14 | Donation links on iOS | Include; exclude | **Settled 2026-10-07** by DA9 of `ANALYSIS-B1-ANDROID-PLAN.md`: no donation links on Android or iOS | Phase 7 |
| D15 | Automatic consumption for inactive periods | None on inactive days (activity history); today's catch-up on reactivation | **Decided 2026-09-26**: no automatic consumption on inactive days | Phase 2 |

---

## 17. Corrections to other documents

The corrections to `EVOLUTION.md` and `ANALYSIS-C3PP-CLOUD-PROVIDERS.md`
were applied in the Phase 1 PR. The `ANALYSIS.md` §4.4 change and the
two behavior findings belong to Phase 2.

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

One subsection per spike: date, environment, result, decision. S1–S5
and S8 done on Android; S6 and S7 done for Windows (their Android
halves run with Phase 5).

### 18.0 Android spike tool and device (S1–S4)

Tool in `spikes/Android/` on branch `claude/nifty-galileo-vfepsw`
(draft PR #106, not merged): a MAUI Android app that runs S1–S3 against
the production `MedReminder.Domain`, `MedReminder.Application` and
`MedReminder.Infrastructure.Portable` through
`AddMedReminderPortableInfrastructure`, and `run-s4.ps1` for S4. The
reports are in that folder's `results/`.

**Environment**: three phones, arm64:

| Phone | Android | RAM | Builds run |
|---|---|---|---|
| motorola edge 50 neo | 16 (API 36) | 7.4 GB | Debug, Release default, Release full trimming |
| Samsung Galaxy A52 5G (SM-A526B) | 14 (API 34) | 5.4 GB | Release default |
| Samsung Galaxy A32 4G (SM-A325F, MediaTek Helio G80), the low-end phone | 13 (API 33) | 3.6 GB | Release default |

.NET SDK 10.0.401 on Windows, runtime .NET 10.0.12, workloads
`android` 36.1.69 and `maui-android` 10.0.110; minimum API 26 (D13
proposal), target API 36. Three builds of the same code:

| Build | Settings |
|---|---|
| Debug | Interpreter (`RunAOTCompilation=false`, `IsDynamicCodeCompiled=false`) |
| Release, default | `PublishTrimmed=true`, `TrimMode=partial`, `RunAOTCompilation=true`, profiled AOT, `JsonSerializerIsReflectionEnabledByDefault=true` |
| Release, full trimming | As the default with `TrimMode=full`, which sets `JsonSerializerIsReflectionEnabledByDefault=false` |

The desktop archive for S1b was exported by MedReminder 2.16.0 (payload
schema 2, 18 medicines, 195 stock movements); the reports hold counts
only.

### 18.1 S1 — AES-GCM on Android (2026-10-05)

**Results**

| Check | Debug | Release, default | Release, full trimming |
|---|---|---|---|
| `AesGcm.IsSupported` | Pass | Pass | Pass |
| AES-256-GCM known answer (pyca/cryptography reference), in-box and through `IArchiveCipher` | Pass | Pass | Pass |
| Tampered tag rejected (`AuthenticationTagMismatchException`) | Pass | Pass | Pass |
| Argon2id known answer (argon2-cffi reference) through `IArchiveCipher` | Pass | Pass | Pass |
| S1b: desktop archive, manifest and payload through `IArchiveReader` | Pass (7.5 s) | Pass (1.2 s) | **Fail**: `JsonSerializerIsReflectionDisabled` |

The Release default build also passes every S1 check on the A52 (S1b
1.4 s; a wrong passphrase gives "The passphrase does not match this
file") and on the A32 (S1b 2.5 s).

**Decision**: P12 met on Android. The phone computes the same bytes as
the desktop and reads a desktop archive with the Release defaults. The
full-trimming failure is the JSON setting, not the cipher (§18.3).

### 18.2 S2 — Argon2id cost (2026-10-05)

**Results**: `Argon2Params.Default` (t=3, m=64 MiB, p=1) through
`IArchiveCipher`, three runs, key equal to the reference, no
out-of-memory.

| Phone, build | Per derivation | Peak working set |
|---|---|---|
| motorola edge 50 neo, Debug (interpreter) | 7.05–7.09 s | 367 MiB |
| motorola edge 50 neo, Release default | 0.88–0.90 s | 334 MiB |
| motorola edge 50 neo, Release full trimming | 0.89–0.91 s | 322 MiB |
| Galaxy A52 5G, Release default | 1.07–1.08 s | 254 MiB |
| Galaxy A32 4G (low-end, 3.6 GB), Release default | 1.90–2.04 s | 308 MiB |

**Decision**: the 5 s target holds in Release on every phone, the
low-end A32 included, with `Argon2Params.Default` unchanged. The Debug
figure is the interpreter and is not used.

### 18.3 S3 — EF Core SQLite with trimming / AOT (2026-10-05)

**Results**

| Check | Debug | Release, default | Release, full trimming |
|---|---|---|---|
| `DatabaseInitializer` on a new database (32 tables) | Pass | Pass | **Fail** |
| Write through `IMedicineRepository` and `IUnitOfWork` | Pass | Pass | **Fail** |
| Read through the repositories (decimal, `DateOnly`) | Pass | Pass | **Fail** |
| `MedicineOverviewLoader.LoadAsync` | Pass | Pass | **Fail** |
| `DatabaseInitializer` on the existing database (boot patches, WAL) | Pass | Pass | **Fail** |
| Reflection-based `System.Text.Json` (`ArchiveReader`, sync codec) | Pass | Pass | **Fail** |

Every full-trimming EF Core failure is the same
`MissingMethodException`: the constructor of
`EntryCurrentValueComparer<Guid>`, created by reflection, is trimmed.
The JSON failure is `JsonSerializerIsReflectionDisabled`. The
full-trimming publish reports 54 IL2026 and 8 IL2104 warnings (EF Core,
EF Core Relational, EF Core Sqlite, SQLitePCLRaw, the entity
configurations, reflection JSON); the default publish reports none.

The Release default build passes every S3 check on the A52 and the A32
too (`DatabaseInitializer` on a new database 2.0 s and 3.7 s).

**Decision**: P13 met on Android with the .NET for Android Release
defaults. The Phase 5 client keeps `TrimMode=partial`. Full trimming
would need source-generated JSON contexts in the portable code and EF
Core trimming support; it is not planned. iOS is not covered by this
spike (Phase 6).

### 18.4 S4 — `StripReleaseDebugArtifacts` on an Android Release build (2026-10-05)

**Results**: `dotnet publish -c Release -f net10.0-android` with
`Directory.Build.props` unchanged, default and full trimming: exit 0;
the target deletes the spike's `.pdb` and `.xml` from the output folder
and leaves no `*.pdb` / `*.xml` in the output or publish folders; the
signed APK (39.7 MiB default, 34.6 MiB full trimming) installs with adb
and starts; the default APK passes S1 and S3.

**Decision**: no exclusion of the mobile project from
`StripReleaseDebugArtifacts` is needed; D11 can be closed as "reject".

### 18.5 S5 — Local notifications on Android (2026-10-05/06)

**Environment**: motorola edge 50 neo, Android 16, the Release default
APK of §18.0 extended with S5 (draft PR #106). A *battery* schedules,
per time offset, one alarm of each kind: Exact
(`setExactAndAllowWhileIdle`), AlarmClock (`setAlarmClock`), Inexact
(`setAndAllowWhileIdle`) and Window (`setWindow`, 10 minutes). The
receiver logs the real firing time and posts a notification; a
`BOOT_COMPLETED` receiver re-plans the alarms still due, as the Phase 5
planner would. `POST_NOTIFICATIONS` granted. `SCHEDULE_EXACT_ALARM` was
**not granted after install** on Android 16; it was granted in the
settings for every run except the two with exact alarms denied.

**Results** (delay after the planned time; short battery: +2 to +20
minutes; long battery: +30 minutes to +8 hours)

| Scenario | Exact, AlarmClock | Inexact, Window | Notes |
|---|---|---|---|
| Screen off | 0–2 s, 8/8 | 17–239 s | |
| Swiped from recents | 1 s, 8/8 | 17–240 s | The process stayed cached: no alarm had to start it |
| Reboot | 2–4 s, 8/8 | 4–221 s | The boot receiver re-planned 16/16 without the app being opened; the first alarms started the process |
| Reboot, exact alarms denied | Refused | 11–777 s | 8/8 re-planned at boot |
| Exact alarms denied | Refused (`SecurityException`); `canScheduleExactAlarms` false | Inexact 92–442 s, Window 442–1522 s | |
| Force-stopped | 0/8 | 0/8 | No alarm fired. Opening the app again delivered `BOOT_COMPLETED` without a reboot, so the boot receiver ran; every alarm was already past due |
| Overnight, long battery (22:30–06:00) | 1 s, 10/10 | 171–243 s | Doze not observed at firing time (`isDeviceIdleMode` false) `[UNCERTAIN — whether the phone entered Doze]` |
| Foreground | 0 s, 8/8 | 90–347 s | |

**Decision**:

- Dose reminders use the exact kinds; they fire within 4 s in every
  scenario except force stop. §8.2 holds: request
  `SCHEDULE_EXACT_ALARM` with an explanation, since it is not granted
  after install; without it the inexact fallback is up to 25 minutes
  late, which the UI states.
- Re-plan on `BOOT_COMPLETED` works without the app being opened.
- Force stop cancels the alarms until the user opens the app again.
  On Android 15+ the app then receives `BOOT_COMPLETED` when it leaves
  the stopped state `[VERIFIED — S5 on Android 16; the Android 15
  change is reported by M. Murphy, "Random Musings on the Android 15
  Developer Preview 2"]`, so the same receiver re-plans; the planner
  also re-plans on start (§8.1). Reminders due while the app was
  stopped are lost; whether the planner shows them as missed on the
  next start is a Phase 5 design point.
- iOS (pending limit, authorization) is not covered; it belongs to
  Phase 6.

### 18.6 S6 — OneDrive app folder (2026-09-27)

**Environment**: Windows 11 (NT 10.0.26200), .NET 10.0.12, MSAL 4.90.1,
personal Microsoft account, app registration with personal accounts,
`http://localhost` redirect, public client, delegated
`Files.ReadWrite.AppFolder`. Tool in `spikes/S6-OneDrive/` on branch
`claude/b1-spike-s6-onedrive` (draft PR #90, not merged); two rounds,
reports in that folder's `results/`.

**Results**

| Check | Result |
|---|---|
| Sign-in in the system browser; silent refresh from a DPAPI-protected cache after a restart | Pass |
| App folder `special/approot`: `/drive/root:/Apps/<registration name>` (`MedReminder26`) | Pass |
| Scope isolation: the drive root lists 0 items, `special/documents` is 404 | Pass |
| `PUT …/content` with `conflictBehavior=fail`: 201, then 409 on the same name; missing parents created | Pass |
| `conflictBehavior=replace`: 201 then 200; read back equal; missing file 404; delete 204 then 404 | Pass |
| Upload session (6 MiB, ~3 s); create-only session refused at `createUploadSession` (409) | Pass |
| **An upload session's item is listed after its first chunk, size 0, content empty** | Fail |
| `deferCommit`: the item is still listed before the commit | Fail |
| Temporary name then `PATCH` rename; rename onto an existing name is 409 | Pass |
| `delta` on the app folder: works, reports new files with parent ids (no paths) | Pass |
| Latency: ~800 ms per small create, ~600 ms per listing call; no throttling over 20 creates | Measured |
| **The Windows OneDrive client does not download `/Apps/<name>`** (no local `Apps` folder after 15 min) | Fail |

**Decisions**

- Files above the simple-upload limit are uploaded under a
  `.`-prefixed temporary name and renamed with `conflictBehavior=fail`;
  readers already ignore such names (`SYNC-FORMAT.md` §1). No format
  change.
- The OneDrive transport and backup storage use Graph on every device;
  the folder transport of Phase 3 stays independent. §5.8's argument
  for the API on the desktop now holds for OneDrive too.
- Listing uses the `delta` feed kept in an index, with a full
  enumeration when the cursor expires.
- The app folder name is the registration's display name; the product
  owner keeps `MedReminder26`.

### 18.7 S7 — Google Drive (2026-09-28)

**Environment**: Windows 11 (NT 10.0.26200), .NET 10.0.12, one Cloud
project with the Drive API, consent screen External in Testing, scopes
`drive.file` and `drive.appdata`, two OAuth clients of type Desktop (A
and B) standing in for the desktop and the Android client. Hand-written
OAuth (system browser, loopback on `127.0.0.1`, PKCE), Drive REST v3.
Tool in `spikes/S7-GoogleDrive/` on branch
`claude/b1-spike-s7-google-drive` (draft PR #92, not merged); two
rounds, reports in that folder's `results/`.

**Results**

| Check | Result |
|---|---|
| Sign-in (loopback + PKCE) for A and B; refresh from a cached refresh token after a restart | Pass |
| `refresh_token_expires_in` = 604 799 s (7 days) with the consent screen in Testing | Measured |
| **Client B finds, lists, reads and writes into the files client A created (`drive.file`)** | Pass |
| **Client B sees client A's `appDataFolder` file** | Pass |
| A query on a public `properties` tag finds the file from either client (~0.4 s); `appProperties` also visible to B | Pass |
| Same name twice in one folder: both creates succeed | Fail (duplicates allowed) |
| Create with a pre-generated id, twice: 200 then 409 | Pass |
| Resumable upload (6 MiB, ~2 s); a partial upload is not listed | Pass |
| Replace content; delete 204 then 404 | Pass |
| `changes` feed reports a new file after 4–7 s, also to the other client | Pass (with lag) |
| Latency: ~1.2 s per small create, ~0.3 s per listing query, ~1 s per folder created; no throttling over 20 creates | Measured |

**Decisions**

- Sync in the hidden app data folder, backups in a visible folder
  (product owner). Both scopes are requested.
- No folders for sync: files flat, named by path, one query by property
  lists them.
- Create-only is emulated: check, create with a pre-generated id, check
  again; among duplicates the oldest (then smallest id) wins on every
  device.
- Own writes are remembered until the listing shows them (lag).
- Before release the Cloud project must be published: in Testing the
  refresh token lasts 7 days.

### 18.8 S8 — Background sync cadence on Android (2026-10-06)

**Environment**: motorola edge 50 neo, Android 16, the Release default
APK of §18.0 extended with S8 (draft PR #106). A unique periodic
WorkManager job (`Xamarin.AndroidX.Work.Runtime` 2.10.3), period 15
minutes, constraint "network connected"; the worker records its run
only, with no network call. The app was not exempt from battery
optimization. Recorded 08:31–20:57 on a day of normal use, on battery,
with battery saver on for part of it.

**Results**

| Measure | Value |
|---|---|
| Runs | 6 in 12.4 hours, the first at enqueue time |
| Gaps, all | 5: min 61 min, median 144 min, max 241 min |
| Gaps with battery saver off | 4: 61–241 min |
| Gap with battery saver on | 1: 179 min |
| Standby bucket at the runs | active ×5, working set ×1 |
| Runs that started the process | 0 (the process was cached each time) |

**Decision**: the requested 15 minutes is a floor, not a cadence: the
work ran every 1 to 4 hours, with battery saver on or off, and below
the 1-hour target the spike README had set. Background sync on Android
is best effort. Consequences for Phase 5:

- Sync on start, resume and after local writes (§7.4) carries the
  freshness the user sees; the periodic work only catches up.
- Dose reminders do not depend on sync: they are planned locally with
  exact alarms (§18.5).
- An intake recorded on another device can reach the phone hours
  later, so a duplicate dose reminder is likelier than §8.3 assumed;
  §8.3 already accepts it.
- Not tried: an exemption from battery optimization (Play policy
  restricts the request) or a push channel, which needs a backend
  (§5.9, C.1). One phone and one day; figures on other phones may
  differ `[UNCERTAIN]`.

### 18.9 S9 — Convergence prototype (2026-09-26)

**Environment**: Linux container, .NET SDK 10.0.112, code in
`prototypes/B1.SyncPrototype*` on branch
`claude/b1-s9-convergence-prototype` (PR #77; merged by mistake and
reverted in PR #78, so the code is not on `main`). The
prototype references `MedReminder.Domain` and reuses
`ConsumptionMaterializer`, `DailyConsumption`, `SuspensionState` and
`RunOutForecast` unchanged. The oracle is the real Application use
cases over the in-memory repositories of
`MedReminder.Application.Tests`.

**Result: passed.**

| Criterion | Run | Outcome |
|---|---|---|
| (a) Parity with today's behavior | 10 000 random 25-day scenarios (1–4 actions per day: add medicine, stock entries, corrections, intakes of every status including backdated, counts with every `takenToday` branch, schedule changes, suspend / resume, threshold, end date, slot changes, deactivation); one third apply the Phase 2 cutoff patch mid-way; plus 3 scripted cases | Stock and `StockEpoch` identical to the use cases after every action |
| (b) Convergence | 10 000 random histories: 2–5 devices, 30 days, 2.6 M operations, clock skew up to 2 h, devices offline 1–20 days, random order and duplicate delivery, checkpoints and compaction, late joiners, retractions, concurrent edits of the same fields and schedule keys | Every device equal to the HLC-ordered fold of all operations; identical derived ledgers; no operation lost; `ANALYSIS.md` §4.4 invariants hold. Exercised: 12 423 bootstraps, 340 157 compacted segments, 325 237 duplicates ignored, 2 908 same-key schedule conflicts |
| Negative controls | Broken winner selection; skipped bootstrap on compacted segments | Both detected (not committed) |

Parity excludes, by design, the changes whose behavior D6 and D15
change. `DocumentedDifferenceTests` pins them (50 units initial stock,
1 unit/day unless stated):

| Case | Today | Derived |
|---|---|---|
| Schedule changed on day 10 to 2/day from day 5 (D6) | 40 | 35 |
| Deactivated day 1, reactivated day 8 (D15) | 42 | 49 |
| Two changes for the same `EffectiveFrom` (1→2, then →3) | 45 (first row wins) | 43 (latest wins) |
| End date on day 2 cleared on day 7, cutoff on day 5 (D6) | 43 (frozen days re-opened) | 46 (frozen days unchanged) |

**Findings that changed this document**:

1. Count anchors are evaluated on facts **recorded** before the count
   (HLC), not dated before it; the count-day quantity is fixed in the
   snapshot; the threshold travels in the fact (§4.3 rule 3).
2. Slots have no date today; they need a dated history, with the
   creation set in force from the beginning (§4.2). Without it the
   derivation diverges from today's behavior and rewrites the past.
3. Registers keep their version history; state size then equals the
   operation log unless an anchor horizon allows pruning (§4.2, §5.6).
4. D6 covers every dated change (schedule rows, suspensions, therapy
   end date), and today even re-opens days before the cutoff; the
   derivation keeps frozen days frozen.
5. The convergence check must hash the resolved values, not only the
   version sets (§12).
6. Bootstrap must choose a checkpoint that covers the compacted
   segments (§5.6).
7. Parity with today holds only after a catch-up tick: `MedicationMonitor`
   runs it every 30 minutes, so the difference is transient except for
   the D6 / D15 cases.

**Not covered**: time zones and DST (UTC only), encryption, EF Core
persistence, generation reset, notification planning, field-diff
emission from real dialogs, the anchor horizon, performance on a phone.
Per-movement `StockEpoch` values (diagnostic only) were not compared;
the medicine's epoch was.

**Decision**: the model holds. Phase 1 may start once D9 is decided;
Phase 2 implements the derivation from the prototype and its tests.

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
  patch; `StartDate` immutable (editable since 2026-10, operation
  `MedicineStartChanged`) and schedule summary from the latest
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
- 2026-09-26 — product owner decided D1, D2, D3, D5, D6, D8, D10, D15
  as proposed. Added spike S9 (convergence prototype) as the first
  Phase 0 step, gating Phase 1.
- 2026-09-26 — S9 passed (§18.9). Updated from its findings: §4.3
  rule 3 (snapshot by recording order, fixed count-day quantity,
  threshold in the fact), §4.2 (dated slot history, register history),
  §5.6 (anchor horizon, checkpoint selection), §12 (resolved-value
  hash), D6 scope.
- 2026-09-26 — Phase 1 implemented: D9 decided; P5–P7 met; §17
  corrections applied to `EVOLUTION.md` and `ANALYSIS-C3PP`. Deviation
  from the Phase 1 actions: the reader tests build their archives with
  a portable test writer that follows `EXPORT-FORMAT.md`, not a
  desktop-produced fixture file; the Windows round-trip tests
  (`ExportImportRoundTripTests`) keep covering the real `ExportService`.
- 2026-09-27 — Phase 2a implemented: P8 met. The audit found one
  direct write outside `MedReminder.Application` on profile data
  (`MainForm` deactivate), now the `DeactivateMedicine` use case; a
  source-scan test guards the UI (§7.2). Out of scope and unchanged:
  the whole-file replacement paths (`ImportService`, `BackupService`,
  `CloudRestoreService`), `ProfileDatabaseBuilder` (builds a fresh
  database for import), `DatabaseInitializer` (schema) and the
  reference-catalogue import (`CsvReferenceCatalogueImporter`, not
  profile data). Still open: the replicated values written outside the
  database (notification settings file, profile display name), deferred
  by the product owner, see P8.
  Phase 2 is split into 2a (write paths), 2b (schema patches), 2c
  (`LedgerDeriver`) and 2d (fact retraction, D8).
- 2026-09-27 — Phase 2b implemented: `StockMovements.Origin`,
  `LedgerCutoff` (with `FrozenAt`), `StockCounts` (not written yet),
  export schema version 2. Deviation from §7.3, decided by the product
  owner: slot history is a `MedicationAdministrationSlotSets` table,
  because columns on the slot rows cannot record an empty set. §3.5
  records the day-after-cutoff constraint for rule 1; §13 records the
  Phase 2 split and the cutoff constraint for 2c.
- 2026-09-27 — Phase 2d implemented: retraction of stock entries,
  intakes, counts and suspensions (tombstones, history window),
  restricted to facts recorded after the latest count (§4.2); epoch fact
  id for the low-stock dedup (§4.4). Phase 2 complete.
- 2026-09-27 — Phase 2c-2 implemented: P9 met. Facts recorded by the
  use cases (count with outcome, activity, recording instants, epoch
  baseline); `LedgerSynchronizer` replaces derived rows; re-freeze at
  boot and on import; §17 applied to `DailyConsumption` too.
  `EpochFactId` moved to 2d (§13).
- 2026-09-27 — Phase 2c-1 implemented: `LedgerDeriver` in the Domain,
  parity on 10 000 scenarios including mid-day patches. Decisions of
  the product owner: 2c split into 2c-1 / 2c-2; count outcome stored
  with the fact until Phase 3 (§4.3 rule 3); 2c-2 re-freezes (§13).
  Rules 1b and 2 extended to days carrying `Legacy` consumption after
  the cutoff (§4.3).
- 2026-09-27 — Phase 3d implemented. Decisions of the product owner:
  sync under Tools → Sync…, configuration for the administrator only;
  joining replaces the profile's data with a safety copy; conflict review
  with restore (medicine fields) and dismiss; no CI workflow for pull
  requests (the repository has none; the convergence harness runs with
  the test suites). Implementation: a pending reset is marked in
  `sync.settings.json` before an import or a restore swaps the database,
  and the engine refuses to run until the new generation is started; the
  sync service is suspended before those operations; the receiver warns
  about a clock more than 24 hours ahead (§4.1).
- 2026-09-27 — Phase 3c implemented in one PR (product owner); key
  rotation and revocation moved to Phase 4 (§6.2). Decisions taken in
  the implementation: genesis and checkpoints are SQLite images of the
  profile database without non-replicated data (exact replicated
  state, register versions and fact clocks included; older images are
  upgraded by the boot patches; `SYNC-FORMAT.md` §5.3); the current
  generation and key version are the highest genesis and wrap files, so
  `group.json` is written once (R5); checkpoint headers carry their
  vector in cleartext (ids and counters) so a device can choose one
  without decrypting it; a device writes its record as soon as it takes
  part (creation, join, new generation), otherwise the others would
  delete segments it still needs (found by the folder harness); only a
  device's newest checkpoint is kept. Not yet: the anchor horizon of
  §5.6 (images carry the full operation log) and the re-publication of
  own segments found missing (§6.3).
- 2026-09-27 — Phase 3b-2 implemented: count re-evaluation by HLC with
  register values "as of" (§4.3 rule 3), `SyncGenesis` register
  versions (§5.5), `SyncOperations.EntityId`. Known limits: the history
  window still shows the stored correction of a count; the local
  retraction rule of Phase 2d (no retraction before a later count) is
  kept, though with sync enabled the re-evaluation would make it
  unnecessary; a fact recorded on a device whose clock is behind the
  profile's `FrozenAt` would read as `Legacy` on every device
  (deterministic, not observed).
- 2026-09-27 — Phase 3b split into 3b-1 and 3b-2 (§13). Decisions of
  the product owner: a retracted fact is left out of every count
  snapshot (§4.2); only concurrent writes are conflicts, detected with
  a base version carried by the operation (§4.5). Phase 3b-1
  implemented: `ApplyRemoteOperations`, `SyncRegisters`,
  `RegisterMerge`, `SyncFieldVersions`, `SyncConflicts`. Deviations
  from §7.3: `SyncPeers` moves to 3c, `FactRetractions` serves as the
  tombstone table. The fact loader breaks recording-instant ties by id,
  so devices derive the same ledger whatever order rows are read in.
- 2026-09-27 — Phase 2 released in v2.7.0. D7 decided (§4.5 list).
  Phase 3 split into 3a–3d (§13). Phase 3a implemented: HLC, operation
  catalogue and outbox, emission from every use case, recorded only
  while sync is enabled (§7.2); `WriteGate` on every use case and
  stale-form diff for the medicine edit dialog (§7.4).
- 2026-09-27 — S6 done for Windows (§18.6). Phase 4 split into 4a
  (OneDrive), 4b (Google Drive), 4c (QR pairing, revocation, key
  rotation). Phase 4a implemented: `OneDriveClient` (Graph REST, no
  SDK), `OneDriveSyncTransport` (sync files under `sync/` in the app
  folder; `delta` index plus an overlay of the device's own writes),
  `OneDriveArchiveStorage` (backups under `backups/`),
  `MsalCloudAccountService` (one DPAPI-protected token cache for all
  profiles, `onedrive.protected`), `SyncTarget` and
  `ISyncTransportFactory`. A session that needs a new sign-in reaches
  the archive caller as `CloudSignInRequiredException`, not as a
  missing folder. Deviation from the Phase 4
  actions: no Graph SDK; the QR pairing generator moves to 4c; MSAL is
  not added to `THIRD-PARTY-NOTICES.md`, which covers data sources
  only (NuGet package licences are not listed there, MailKit
  included).
- 2026-09-28 — S7 done for Windows (§18.7). Phase 4b implemented:
  `GoogleDriveClient` (Drive REST v3, no Google library),
  `GoogleDriveSyncTransport` (flat files in the app data folder tagged
  by a property, create-only emulated with a pre-generated id and an
  oldest-wins rule), `GoogleDriveArchiveStorage` (visible
  `MedReminder/backups` folder found by property), `GoogleOAuthClient`
  and `GoogleCloudAccountService` (loopback + PKCE, refresh tokens in
  `googledrive.protected`), `CloudAccountService` routing by provider.
  The Desktop client's id and secret are not in the repository: they
  come from configuration or are stamped at build time
  (`docs/PACKAGING.md`). Known limit, found by the listing-lag harness
  and shared with OneDrive (delta lag): when a device joins while the
  listing lags and another device compacts in those seconds, the new
  device finds a gap, reports `RebuildRequired`, and "Rebuild from the
  group" repairs it. Closing it needs the join to wait until its own
  device record is listed.
- 2026-09-28 — Phase 4c implemented (#99): pairing codes naming an
  ephemeral encrypted pairing file instead of carrying the group key
  (§6.1), QR rendering with QRCoder; key rotation as a new generation
  sealed with the new key, without a `DeviceRevoked` operation (§6.2,
  reasons there); remaining devices stop publishing until they take the
  new key, then rebuild and carry their own operations over; newer
  generations sealed with an older key ignored.
- 2026-09-28 — Decision of the product owner, replacing the Phase 3d
  one: every profile manages the sync of its own data (enable, join,
  join with a pairing code, pair a device, rebuild, new key, disable).
  Key rotation and device removal stay with the administrator. Reason:
  the dialog acts on the current profile only, so a profile with the
  user role could never be synced by anyone.
- 2026-09-28 — Decision of the product owner: key rotation and device
  removal are also open to every profile. A sync group belongs to one
  profile, so they reach only that profile's devices and data. No sync
  action depends on the profile role any more.
- 2026-09-28 — §13 Phase 0 exit aligned with §18: S6 is required for
  Windows only; the Android halves of S6 and S7 run with Phase 5,
  before provider sign-in on Android.
- 2026-09-28 — P8 residue closed. The profile's display name and the
  notification recipients (`ToAddress`, `CaregiverAddress`,
  `DoctorAddress`) are `ProfileSettingChanged` operations (schema 3),
  last writer wins per setting without a conflict entry, stored as
  registers of the profile pseudo-entity (empty id) in
  `SyncFieldVersions`. The profile registry and
  `notifications.settings.json` are a local copy: the apply step and
  every sync run write the winners into it (`ProfileSettingsProjection`
  via `IProfileSettingsStore`). The genesis records the writing
  device's values when the profile has no version yet, so a joining
  device takes the group's name and recipients. A synced profile other
  than the current one cannot be renamed from Manage profiles: its
  database is not open, so the rename could not be recorded.
- 2026-09-28 — Known limit of Phase 4b closed. A provider transport
  lists its own writes from memory, so "the join waits until its own
  record is listed" has to ask the provider:
  `IProviderListing.IsListedByProviderAsync`, implemented by the
  OneDrive and Google Drive transports. The join writes its record as
  soon as it picks an image, waits until the provider lists the record
  (`SyncEngineOptions.JoinListingTimeout`, 60 s), then checks with the
  listing it just fetched that the image still covers every device's
  first remaining segment, and picks again if not (up to
  `JoinAttempts`, 3). The database is built once, from the last image
  picked. A join by passphrase that opens several groups of the
  storage now asks which one (`JoinSyncGroup.FindGroupsAsync`) instead
  of joining the first.
- 2026-09-29 — Low-stock email deduplicated across devices (household
  step H1). A successful email is recorded as a `SentEmailNotification`
  and replicated as `EmailNotificationSent` (operation schema 4); the
  table travels in images (image schema 3). A device about to notify an
  epoch another device already emailed for leaves the email channel out
  and still shows its toast. §4.2 "`NotificationEvent` device-local" is
  unchanged: toasts stay per device. Two devices that notify before
  either has synced still both send; the master device of the
  household feature (step H4) takes the place of the designated mail
  device (§8.4) and removes that case.
- 2026-10-05 — Android spikes S1–S4 run with the tool of draft PR #106
  on a motorola edge 50 neo (Android 16), a Galaxy A52 5G (Android 14)
  and a low-end Galaxy A32 4G (Android 13, 3.6 GB) (§18.0–§18.4). S1,
  S3 and S4 pass with the .NET for Android Release defaults; S2 takes
  0.9–2.0 s in Release, under the 5 s target on the low-end phone; full
  trimming breaks reflection-based JSON and EF Core. S5 run on the
  motorola (§18.5): exact alarms fire within 4 s in every scenario
  except force stop, also after a reboot through the boot re-plan;
  `SCHEDULE_EXACT_ALARM` is not granted after install on Android 16;
  the inexact fallback is up to 25 minutes late. §8.2 updated. S8 run
  on the motorola (§18.8): the 15-minute WorkManager job ran every 1 to
  4 hours; background sync is best effort. §7.4 updated. P12 and P13 met
  for Android; Phase 0 exit met for Android; D11 recommendation:
  reject, no exclusion needed.
- 2026-10-06 — Android plan revised in `ANALYSIS-B1-ANDROID-PLAN.md`
  after two product-owner requests: a standalone Android app, and every
  current desktop feature that applies to a phone. §9.1, §10 and §13
  Phases 5 and 7 point to it for Android.
- 2026-10-08 — Consistency review with the Android plan revision 20:
  §8.4 superseded by the household master (backlog B5-03); §7.5, §9.1
  and §10 email rows aligned; "once approved" pointers replaced, since
  the Android plan was approved on 2026-10-08.
