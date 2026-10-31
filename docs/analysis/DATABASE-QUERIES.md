# Database queries: inventory and measurements

Issue #328, PR #329. State of the repository reads of
`MedReminder.Infrastructure.Portable` after the PR: what each read
loads, which index it uses, who calls it, and the measured cost on
long-lived profiles.

## 1. Method

- **Benchmark harness**: `tests/MedReminder.Infrastructure.Portable.Tests/Benchmarks/`,
  opt-in (skipped unless `MEDREMINDER_BENCHMARKS=1`). It seeds profiles of
  5 / 15 / 30 medicines over 1 / 2 / 5 years behind the portable host
  the mobile app composes, times every repository read and hot loader
  in a fresh scope (median, minimum, maximum of 7 runs), and records
  the SQLite query plan (`EXPLAIN QUERY PLAN`) of the commands each one
  ran.

  ```bash
  MEDREMINDER_BENCHMARKS=1 MEDREMINDER_BENCHMARK_OUT=bench.md \
    dotnet test tests/MedReminder.Infrastructure.Portable.Tests -c Release \
    --filter "Category=Benchmark"
  ```

  Per medicine and day the seed writes two intakes (each with its sync
  operation) and one derived consumption movement; every fifteen days a
  new package; every month a count and a prescription with its
  reminder; every year a schedule row; a few suspensions and
  retractions. The 30 x 5 profile holds 109,620 intakes, 58,500
  movements and 109,620 operations (111 MB).
- **Schema comparison**: `ModelIndexPatchTests` rebuilds the first
  schema (commit `5a9b1a9`: six tables, their original columns and
  indexes), boots it twice through `DatabaseInitializer`, and compares
  tables, columns and indexes with a new database.
- **Machine for the numbers below**: Apple Silicon, 12 cores, macOS,
  .NET 10, file database. A phone is slower; the ratios are what
  carries over.

## 2. Indexes on upgraded databases

| Finding | Action |
|---|---|
| The six first tables (`Medicines`, `StockMovements`, `MedicationScheduleHistories`, `MedicationSuspensions`, `MedicationIntakes`, `NotificationEvents`) had every index of the issue's list from the first schema on. No upgraded database lacks them. | None needed; the generic patch below now guarantees it. |
| `IX_MedicationIntakes_MedicineId_Day` was the only model index missing on upgraded databases. | Fixed in #326. |
| Every table added later has its indexes in the patch that creates it, unique ones included. | None. |
| The dedup patch named its index `IX_DoseReminderEvents_Dedup`; EnsureCreated names it `IX_DoseReminderEvents_MedicineId_SlotKey_LocalDate`. Every database created after A5 got a second identical unique index on its second boot. | The patch creates the EF name, then drops the old one. |
| `IX_MedicationIntakes_MedicineId` is a prefix of `(MedicineId, Day)`; SQLite already chose the composite index for MedicineId-only reads. | Dropped from the model and by a boot patch. |
| `IX_StockMovements_MedicineId` and `IX_MedicationAdministrationSlots_MedicineId` are prefixes of `(MedicineId, Kind, OccurredAt)` and `(MedicineId, Order)`. Unlike the intake one, SQLite used the stock index for the full-history read. | Dropped as well. Measured at 30 x 5 (median of 11, two runs each): 1,000 movements written 15.5 -> 13.9 ms, database 111.5 -> 108.7 MB, full movement history 53.7 -> 57.2 ms, `LedgerFactsLoader` 206 -> 209 ms. A trade, not a clear gain; the commit can be reverted alone. |

`ModelIndexes` (last boot patch) reads the relational model and creates
any missing index under its EF name; an equivalent index (same columns
and uniqueness) counts as present. A missing unique index is created
only when no two rows share its key; otherwise it is skipped with a
warning and tried on the next boot, and no row is changed. Boot cost:
0.6-1.5 ms.

## 3. Repository reads

Index: the plan SQLite chose on the 30 x 5 profile. All reads are
`AsNoTracking` unless marked tracked.

| Repository | Read | Filter / order | Index |
|---|---|---|---|
| Medicine | `GetAsync` (tracked) | Id | PK |
| | `ListActiveAsync`, `ListActiveWithDoseReminderAsync`, `ListAllAsync` | IsActive / RemindOnDose, by Name | scan of `IX_Medicines_Name` (one row per medicine) |
| MedicationIntake | `ListForMedicineAsync` | MedicineId, by Day | `(MedicineId, Day)` |
| | `ListForMedicineBetweenAsync`, `ListForDayAsync`, `ListManualIntakeDaysAsync` | MedicineId, Day range | `(MedicineId, Day)` range |
| | `GetAsync` (new) | Id, MedicineId | PK |
| | `ListMedicinesWithDayIntakeAsync` | Day, not extra | full walk of `(MedicineId, Day)`, 2.8 ms |
| StockMovement | `ListForMedicineAsync` | MedicineId, by OccurredAt | `(MedicineId, Kind, OccurredAt)` prefix + sort |
| | `GetCurrentStockAsync` (new) | MedicineId, delta column only | `(MedicineId, Kind, OccurredAt)` prefix |
| | `GetLastNewPackageQuantityAsync` (new) | MedicineId, Kind = NewPackage, newest first | `(MedicineId, Kind, OccurredAt)` |
| | `GetLastConsumptionDayAsync` | MedicineId, Kind, newest | covering `(MedicineId, Kind, OccurredAt)` |
| | `GetAsync` (new) | Id, MedicineId | PK |
| StockCount | `ListForMedicineAsync` | MedicineId, by RecordedAt | `(MedicineId)` + sort |
| | `GetAsync` (new) | Id, MedicineId | PK |
| | `ListMedicinesWithMaterializedCountAsync` | CountDay | scan of `(MedicineId)` (one row a month per medicine) |
| MedicationSuspension | `ListForMedicineAsync`, `GetOpenSuspensionAsync` (tracked) | MedicineId | `(MedicineId, EndDate)` |
| | `GetAsync` (new) | Id, MedicineId | PK |
| MedicationScheduleHistory | `ListForMedicineAsync` | MedicineId, by EffectiveFrom, RecordedAt | `(MedicineId, EffectiveFrom)` |
| MedicationAdministrationSlot | `ListForMedicineAsync`, `ListSetsForMedicineAsync` | latest set of the medicine | `(MedicineId, RecordedAt)`, `(SetId)` |
| MedicineActivity | `ListForMedicineAsync` | MedicineId | `(MedicineId, RecordedAt)` |
| FactRetraction | `ListForMedicineAsync` | MedicineId | `(MedicineId)` |
| | `GetForFactAsync` (new) | FactId | unique `(FactId)` |
| NotificationEvent | `GetLatestForMedicineAsync` | MedicineId, newest | `(MedicineId, TriggeredAt)` |
| | `AssignEpochFactIdsAsync` (tracked) | MedicineId, EpochFactId null | `(MedicineId)` |
| SentEmailNotification | `GetLatestForMedicineAsync` | MedicineId, newest | `(MedicineId, SentAt)` |
| DoseReminderEvent | `ExistsAsync` | key | unique dedup index |
| | `ListFiredAsync` (new) | MedicineId, LocalDate, SlotKey in list | same index |
| | `PruneOlderThanAsync` | LocalDate | scan (cold, once a day) |
| Prescription | `ListForMedicineAsync`, `ListAllAsync`, `GetAsync` | MedicineId / all / Id | `(MedicineId)` / scan / PK |
| PrescriptionDispensation | `CountByPrescriptionAsync` and lists | MedicineId / PrescriptionId | `(MedicineId)`, `(PrescriptionId)` |
| PrescriptionReminderEvent | `ExistsAsync` | key | unique `(PrescriptionId, ValidUntil)` |
| | `ListRemindedAsync` (new) | PrescriptionId in list | same index |
| StockPackage | `ListForMedicineAsync`, `GetAsync` | MedicineId / Id | `(MedicineId)` / PK |
| PackageExpiryNoticeEvent | `ExistsAsync` | key | unique 4-column index |
| | `ListShownAsync` (new) | PackageId in list, channel | same index |
| Deadline, DeadlineReminderEvent, ShortageNoticeEvent | `ListAllAsync`, `ExistsAsync` | all / key | scan (few rows) / unique index |
| DeadlineReminderEvent | `ListRemindedAsync` (new) | DeadlineId in list | unique `(DeadlineId, DueOn)` |
| SyncOperation | `ExistsAsync` | Id | PK |
| | `ListForMedicineAsync` | MedicineId, payload included | `(MedicineId)` |
| | `ExistsForMedicineAsync`, `ListStampsForMedicineAsync` (new) | MedicineId (+ Type), no payload | `(MedicineId)` |
| | `ListPendingAsync`, `ListByDeviceAndGenerationAsync`, `ListOfTypeAsync`, `ListAllAsync`, `CountAsync` | device / type / all | scans; publish, rekey, household (cold) |
| SyncFieldVersion, SyncConflict | `ListForEntityAsync`, `ListForSubjectAsync` | EntityId / SubjectId | `(EntityId)`, `(SubjectId)` |
| SyncPeer, LedgerCutoff, DoseTimePreset, PendingDataMigrations | all / single row | - | few rows |

## 4. Call sites that read a full history

Hot: startup, timers, Today/agenda/intake log, sync apply. Warm:
opening a screen or a user action. Cold: export, migration, backfill,
deletion.

| Call site | Class | Before | Now |
|---|---|---|---|
| `ApplyRemoteOperations` fact checks (intake, stock entry, count, suspension end, retraction, `IsNewFactAsync`) | hot | whole list, then `Any`/`FirstOrDefault` on Id | by-id reads |
| `ApplyRemoteOperations.ApplyIntakeAsync` over-schedule hint | hot | whole intake list filtered by day | `ListForDayAsync` |
| `ApplyRemoteOperations.IsDeletedAsync` | hot (once per medicine) | every operation with payload | `ExistsForMedicineAsync` |
| `CountReevaluation.ApplyAsync` (every ledger load with sync on) | hot | every operation with payload | `ListStampsForMedicineAsync` |
| `MedicationMonitor`, `DoseReminderService`, `NotificationPlanLoader`, `MedicineOverviewLoader`, `CoveragePlanQuery`, `TherapyTimelineQuery` | hot | every movement, summed | `GetCurrentStockAsync` |
| `CalendarExportQuery`, `PackageListQuery`, `ExpiringPackagesQuery`, `EquivalentsQuery`, `AdjustStockDown` | warm | every movement, summed | `GetCurrentStockAsync` |
| `RestockByScanQuery`, `CoveragePlanQuery` | warm | every movement, last new package | `GetLastNewPackageQuantityAsync` |
| `NotificationPlanLoader` prescription reminders and package notices | hot | one `ExistsAsync` per prescription, two per package | `ListRemindedAsync`, `ListShownAsync` |
| `NotificationPlanLoader` dose reminders and deadlines | hot | one `ExistsAsync` per slot, per due repetition step and per open deadline | `ListFiredAsync`, `ListRemindedAsync` |
| `PackageExpiryNotices.PendingAsync` | hot | one `ExistsAsync` per due package | `ListShownAsync` |
| `RetractFact` (intake, count, suspension) | warm | whole list, `.Single` | by-id reads |
| `LedgerFactsLoader`, `LedgerSynchronizer`, `FactHistoryQuery` | warm (one medicine per write) | full history | unchanged: the derivation replays and rewrites every fact. 7 ms per medicine at 5 years. |
| `DeleteMedicine.HasRecordedFactsAsync` | cold | four full lists, `Count`/`Any` | unchanged |
| `SyncGenesis`, slot backfills | cold | full lists, once | unchanged |

Per-medicine loops (N+1) left as they are: each iteration now reads a
few short lists and one stock balance. Measured below as part of the
loaders.

## 5. Numbers

Median of 7 runs, ms. "Before" is `main` at `cb40b61` (the baseline
commit of the harness), "after" is the end of PR #329.

| Read / loader | 15 med x 2 y before | after | 30 med x 5 y before | after |
|---|---:|---:|---:|---:|
| Sync apply, 50 incoming intakes looked up | 90.7 | 1.4 | 230.9 | 1.4 |
| Operations read for `MedicineDeleted`, every medicine | 25.3 | 2.3 | 134.6 | 10.0 |
| Operations read for the count re-evaluation, every medicine | 25.3 | 12.0 | 134.6 | 62.0 |
| `NotificationPlanLoader.LoadAsync` | 45.0 | 10.1 | 210.6 | 44.8 |
| `MedicationMonitor.RunAsync` | 25.7 | 9.9 | 120.3 | 47.8 |
| `CoveragePlanQuery.LoadAsync` | 11.8 | 4.6 | 56.8 | 21.9 |
| `TherapyTimelineQuery.LoadAsync` | 11.8 | 4.2 | 56.6 | 21.5 |
| `MedicineOverviewLoader.LoadAsync` | - | 6.6 | - | 30.3 |
| 1,000 intakes written | 31.4 | 26.2 | 47.5 | 38.4 |
| `LedgerFactsLoader`, every medicine | 41.5 | 41.8 | 205.7 | 202.7 |
| `DatabaseInitializer.InitializeAsync` (boot) | 0.3 | 0.7 | 0.3 | 0.8 |

`MedicineOverviewLoader` was added to the harness with the stock
commit; its stock read went from 10.5 to 2.8 ms (15 x 2) and from 54.1
to 17.4 ms (30 x 5) for every medicine. `DoseReminderService` reads
nothing on the seeded profiles (no medicine has a dose reminder); it
shares the monitor's reads.

## 6. Follow-ups

- Mobile: after the `core-v*` tag, advance the submodule and switch the
  mobile call sites that read whole histories to the new reads:
  `DayPlanLoader` (stock balance through `GetCurrentStockAsync`) and
  `DoseActions` undo (the intake through `GetAsync`).
- `ListMedicinesWithDayIntakeAsync` walks the whole `(MedicineId, Day)`
  index once per plan (2.8 ms at 30 x 5). A `(Day)` index would make it
  a range read at the cost of one more index on the largest table; not
  worth it at these numbers.
