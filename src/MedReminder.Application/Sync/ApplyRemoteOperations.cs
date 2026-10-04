using System.Globalization;
using MedReminder.Application.Abstractions;
using MedReminder.Application.Ledger;
using MedReminder.Domain.Calculations;
using MedReminder.Domain.Deadlines;
using MedReminder.Domain.Ledger;
using MedReminder.Domain.Medicines;
using MedReminder.Domain.Notifications;
using MedReminder.Domain.Prescriptions;
using MedReminder.Domain.Stock;
using MedReminder.Domain.Sync;

namespace MedReminder.Application.Sync;

public sealed record ApplyRemoteResult(
    int Applied,
    int Skipped,
    SyncOperation? Blocked,
    string? BlockReason,
    IReadOnlyCollection<Guid> TouchedMedicines);

// Applies operations produced by other devices (B.1 Phase 3b,
// docs/analysis/ANALYSIS-B1-MOBILE-SYNC.md §4.2, §4.5, §5.3 step 4):
// merge rules only, never the use-case validation, which already ran on
// the device where the user acted (§4.5, rejected alternative).
//
//   - Facts (stock entries, intakes, counts, suspensions, schedule rows,
//     slot sets, activity changes) are a union by id: a fact already
//     present, or retracted, is not written again.
//   - Registers (medicine fields, IsActive, a suspension's EndDate, the
//     schedule summary) are last writer wins by HLC (SyncRegisters); an
//     older write is kept as a version and may be listed as a conflict.
//   - A retraction wins over its fact whatever the order of arrival.
//   - A deleted medicine (MedicineDeleted) wins over every operation for
//     it: its rows are removed, and a later operation for it, from a
//     device that had not seen the deletion, is logged but not applied.
//     The tombstone is the MedicineDeleted entry of the operation log,
//     which is never pruned and travels in checkpoint images.
//
// Each operation commits on its own, together with its record in
// SyncOperations, so an interrupted batch resumes by re-delivery (the
// applied ids are skipped, §5.3). The repositories read committed rows,
// which is why an operation cannot wait for the end of the batch. Then
// the ledger of every touched medicine is derived again and saved; if
// that last step fails, the next catch-up derives it. The caller
// delivers operations in causal order: an operation that refers to a
// medicine this device does not have is an error (the causal buffer of
// §5.3 is Phase 3c). An operation of an unknown type or schema version
// stops the batch there (R7): what came before it is applied, the rest
// waits for an app update.
//
// Stock counts keep the outcome evaluated on the recording device until
// Phase 3b-2 re-evaluates them.
public sealed class ApplyRemoteOperations
{
    private readonly ISyncSettingsStore _settings;
    private readonly ISyncOperationRepository _operations;
    private readonly SyncRegisters _registers;
    private readonly IMedicineRepository _medicines;
    private readonly IMedicationScheduleHistoryRepository _schedules;
    private readonly IMedicationAdministrationSlotRepository _slots;
    private readonly IStockMovementRepository _stock;
    private readonly IMedicationIntakeRepository _intakes;
    private readonly IStockCountRepository _counts;
    private readonly IMedicationSuspensionRepository _suspensions;
    private readonly IMedicineActivityRepository _activity;
    private readonly IFactRetractionRepository _retractions;
    private readonly IMedicineDeletionRepository _deletion;
    private readonly LedgerSynchronizer _ledger;
    private readonly IUnitOfWork _uow;
    private readonly TimeProvider _clock;
    private readonly IProfileSettingsStore? _profileSettings;
    private readonly IPrescriptionRepository? _prescriptions;
    private readonly IDeadlineRepository? _deadlines;
    private readonly IStockPackageRepository? _packages;
    private readonly IPrescriptionDispensationRepository? _dispensations;
    private readonly ISentEmailNotificationRepository? _sentEmails;

    // Facts added or updated in this batch, by id: the context tracks
    // these instances, so a later update or removal in the batch must use
    // them rather than a fresh read.
    private readonly Dictionary<Guid, object> _trackedFacts = new();
    // Tombstones added in this batch, by fact id (tracked instances).
    private readonly Dictionary<Guid, FactRetraction> _tombstones = new();
    private readonly Dictionary<Guid, Medicine> _medicineCache = new();
    // Whether a medicine has a MedicineDeleted in the log, by id.
    private readonly Dictionary<Guid, bool> _deleted = new();

    public ApplyRemoteOperations(
        ISyncSettingsStore settings,
        ISyncOperationRepository operations,
        SyncRegisters registers,
        IMedicineRepository medicines,
        IMedicationScheduleHistoryRepository schedules,
        IMedicationAdministrationSlotRepository slots,
        IStockMovementRepository stock,
        IMedicationIntakeRepository intakes,
        IStockCountRepository counts,
        IMedicationSuspensionRepository suspensions,
        IMedicineActivityRepository activity,
        IFactRetractionRepository retractions,
        IMedicineDeletionRepository deletion,
        LedgerSynchronizer ledger,
        IUnitOfWork uow,
        TimeProvider clock,
        IProfileSettingsStore? profileSettings = null,
        ISentEmailNotificationRepository? sentEmails = null,
        IPrescriptionRepository? prescriptions = null,
        IDeadlineRepository? deadlines = null,
        IStockPackageRepository? packages = null,
        IPrescriptionDispensationRepository? dispensations = null)
    {
        _dispensations = dispensations;
        _prescriptions = prescriptions;
        _deadlines = deadlines;
        _packages = packages;
        _profileSettings = profileSettings;
        _sentEmails = sentEmails;
        _settings = settings;
        _operations = operations;
        _registers = registers;
        _medicines = medicines;
        _schedules = schedules;
        _slots = slots;
        _stock = stock;
        _intakes = intakes;
        _counts = counts;
        _suspensions = suspensions;
        _activity = activity;
        _retractions = retractions;
        _deletion = deletion;
        _ledger = ledger;
        _uow = uow;
        _clock = clock;
    }

    public Task<ApplyRemoteResult> ExecuteAsync(
        IReadOnlyList<SyncOperation> operations, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operations);
        return WriteGate.RunExclusiveAsync(ct => ExecuteCoreAsync(operations, ct), cancellationToken);
    }

    // Phase 4c (docs/analysis/ANALYSIS-B1-MOBILE-SYNC.md §6.2): after a key
    // rotation on another device, this device rebuilt from the genesis of
    // the new generation, which holds what the rotating device had
    // applied. This device's own operations of the previous generation
    // that are not in it (recorded offline, or published but not yet
    // applied there) are applied again under the new generation and stay
    // pending (SegmentSeq null), so the next run publishes them and
    // nothing recorded here is lost. Operations already in the new log
    // are skipped by id. An operation for a medicine the new generation
    // does not have (it came from a device the rotating device had not
    // heard from) cannot be applied and is dropped; the count is returned.
    public Task<(int Applied, int Dropped)> ApplyCarriedAsync(
        IReadOnlyList<SyncOperation> own, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(own);
        return WriteGate.RunExclusiveAsync(async ct =>
        {
            var settings = _settings.Load()
                ?? throw new InvalidOperationException("Sync is not enabled for this profile.");
            var known = (await _medicines.ListAllAsync(ct)).Select(m => m.Id).ToHashSet();
            var batch = new List<SyncOperation>();
            var dropped = 0;
            foreach (var operation in own.Where(o => o.DeviceId == settings.DeviceId).OrderBy(o => o.Timestamp))
            {
                if (operation.Type == nameof(MedicineCreated))
                {
                    known.Add(operation.MedicineId);
                }
                else if (operation.MedicineId != ProfileSettingsProjection.Entity
                         && !known.Contains(operation.MedicineId) && !await IsDeletedAsync(operation.MedicineId, ct))
                {
                    dropped++;
                    continue;
                }
                batch.Add(new SyncOperation
                {
                    Id = operation.Id,
                    HlcPhysicalMs = operation.HlcPhysicalMs,
                    HlcCounter = operation.HlcCounter,
                    DeviceId = operation.DeviceId,
                    Generation = settings.Generation,
                    Type = operation.Type,
                    SchemaVersion = operation.SchemaVersion,
                    MedicineId = operation.MedicineId,
                    EntityId = operation.EntityId,
                    Payload = operation.Payload,
                });
            }
            var result = await ExecuteCoreAsync(batch, ct, carried: true);
            return (result.Applied, dropped);
        }, cancellationToken);
    }

    private async Task<ApplyRemoteResult> ExecuteCoreAsync(
        IReadOnlyList<SyncOperation> operations, CancellationToken cancellationToken, bool carried = false)
    {
        var settings = _settings.Load()
            ?? throw new InvalidOperationException("Sync is not enabled for this profile.");

        var applied = 0;
        var skipped = 0;
        SyncOperation? blocked = null;
        string? blockReason = null;
        var seen = new HashSet<Guid>();
        var touched = new HashSet<Guid>();
        var profileTouched = false;

        foreach (var operation in operations)
        {
            if (operation.Generation != settings.Generation)
            {
                throw new InvalidOperationException(
                    $"Operation {operation.Id} belongs to generation {operation.Generation}, " +
                    $"this device is on generation {settings.Generation}.");
            }
            if ((operation.DeviceId == settings.DeviceId && !carried)
                || !seen.Add(operation.Id)
                || await _operations.ExistsAsync(operation.Id, cancellationToken))
            {
                skipped++;
                continue;
            }

            SyncOperationBody body;
            try
            {
                body = OperationCodec.Deserialize(operation.Type, operation.SchemaVersion, operation.Payload);
            }
            catch (NotSupportedException ex)
            {
                blocked = operation;
                blockReason = ex.Message;
                break;
            }

            var ofDeleted = await IsDeletedAsync(body.MedicineId, cancellationToken);
            if (!ofDeleted) await ApplyAsync(body, operation.Timestamp, cancellationToken);
            await _operations.AddAsync(new SyncOperation
            {
                Id = operation.Id,
                HlcPhysicalMs = operation.HlcPhysicalMs,
                HlcCounter = operation.HlcCounter,
                DeviceId = operation.DeviceId,
                Generation = operation.Generation,
                Type = operation.Type,
                SchemaVersion = operation.SchemaVersion,
                MedicineId = operation.MedicineId,
                EntityId = Operations.EntityOf(body),
                Payload = operation.Payload,
            }, cancellationToken);
            if (_medicineCache.TryGetValue(body.MedicineId, out var changed))
            {
                changed.UpdatedAt = _clock.GetUtcNow();
                await _medicines.UpdateAsync(changed, cancellationToken);
            }
            await _uow.SaveChangesAsync(cancellationToken);
            if (ofDeleted)
            {
                skipped++;
                continue;
            }
            if (body is ProfileSettingChanged) profileTouched = true;
            else if (body is HouseholdLinked)
            {
                // The log row is the state (HouseholdLinks).
            }
            else if (body is MedicineDeleted) touched.Remove(body.MedicineId);
            // A sent email, a prescription, a dispensation, a deadline or a
            // package changes no fact of the ledger.
            else if (body is not (EmailNotificationSent or PrescriptionChanged or DispensationChanged
                     or DeadlineChanged or PackageChanged))
                touched.Add(body.MedicineId);
            applied++;
        }

        foreach (var id in touched)
        {
            await _ledger.SynchronizeAsync(await GetMedicineAsync(id, cancellationToken), cancellationToken);
        }
        if (touched.Count > 0)
        {
            await _uow.SaveChangesAsync(cancellationToken);
        }
        if (profileTouched && _profileSettings is not null)
        {
            await ProfileSettingsProjection.ProjectAsync(_registers, _profileSettings, cancellationToken);
        }
        return new ApplyRemoteResult(applied, skipped, blocked, blockReason, touched);
    }

    private async Task ApplyAsync(SyncOperationBody body, HybridTimestamp timestamp, CancellationToken ct)
    {
        switch (body)
        {
            case MedicineCreated created:
                await CreateMedicineAsync(created, ct);
                break;
            case MedicineFieldChanged changed:
                await RecordRegistersAsync(body, timestamp, ct);
                var medicine = await GetMedicineAsync(changed.MedicineId, ct);
                var winner = await _registers.WinnerAsync(changed.MedicineId, changed.Field, ct);
                MedicineFieldCodec.Set(medicine, changed.Field, winner!.Value);
                return;
            case MedicineStartChanged start:
                await RecordRegistersAsync(body, timestamp, ct);
                (await GetMedicineAsync(start.MedicineId, ct)).StartDate = DateOnly.ParseExact(
                    (await _registers.WinnerAsync(start.MedicineId, SyncRegisters.StartDate, ct))!.Value!,
                    "yyyy-MM-dd", CultureInfo.InvariantCulture);
                return;
            case MedicineActivityChanged activity:
                if (await IsNewFactAsync(activity.MedicineId, activity.ChangeId, ct))
                {
                    await _activity.AddAsync(new MedicineActivityChange
                    {
                        Id = activity.ChangeId,
                        MedicineId = activity.MedicineId,
                        Day = activity.Day,
                        Active = activity.Active,
                        RecordedAt = activity.RecordedAt,
                    }, ct);
                }
                await RecordRegistersAsync(body, timestamp, ct);
                (await GetMedicineAsync(activity.MedicineId, ct)).IsActive =
                    (await _registers.WinnerAsync(activity.MedicineId, SyncRegisters.IsActive, ct))!.Value == "true";
                return;
            case ScheduleRowRecorded row:
                await ApplyScheduleRowAsync(row, timestamp, ct);
                return;
            case SlotSetRecorded set:
                await ApplySlotSetAsync(set, timestamp, ct);
                return;
            case StockEntryRecorded entry:
                await ApplyStockEntryAsync(entry, ct);
                return;
            case IntakeRecorded intake:
                await ApplyIntakeAsync(intake, timestamp, ct);
                return;
            case StockCountRecorded count:
                await ApplyCountAsync(count, ct);
                return;
            case SuspensionRecorded suspension:
                await ApplySuspensionAsync(suspension, timestamp, ct);
                return;
            case SuspensionEndChanged end:
                await ApplySuspensionEndAsync(end, timestamp, ct);
                return;
            case FactRetracted retraction:
                await ApplyRetractionAsync(retraction, timestamp, ct);
                return;
            case MedicineDeleted deleted:
                await ApplyDeletionAsync(deleted, ct);
                return;
            case ProfileSettingChanged:
                // P8: a register of the profile; the local copy is
                // written after the batch (ProfileSettingsProjection).
                await RecordRegistersAsync(body, timestamp, ct);
                return;
            case EmailNotificationSent email:
                await ApplyEmailSentAsync(email, ct);
                return;
            case HouseholdLinked:
                return;
            case PrescriptionChanged prescription:
                await ApplyPrescriptionAsync(prescription, timestamp, ct);
                return;
            case DeadlineChanged deadline:
                await ApplyDeadlineAsync(deadline, timestamp, ct);
                return;
            case PackageChanged package:
                await ApplyPackageAsync(package, timestamp, ct);
                return;
            case DispensationChanged dispensation:
                await ApplyDispensationAsync(dispensation, timestamp, ct);
                return;
            default:
                throw new NotSupportedException($"No apply rule for {body.GetType().Name}.");
        }

        await RecordRegistersAsync(body, timestamp, ct);
    }

    // Household step H1: an email another device sent for an epoch. The
    // medicine exists (causal order; a deleted medicine's operations are
    // skipped before this point).
    private async Task ApplyEmailSentAsync(EmailNotificationSent email, CancellationToken ct)
    {
        if (_sentEmails is null || await _sentEmails.ExistsAsync(email.NotificationId, ct)) return;
        if (await _medicines.GetAsync(email.MedicineId, ct) is null) return;
        await _sentEmails.AddAsync(new SentEmailNotification
        {
            Id = email.NotificationId,
            MedicineId = email.MedicineId,
            StockEpoch = email.StockEpoch,
            EpochFactId = email.EpochFactId,
            SentAt = email.SentAt,
            Stage = email.Stage,
        }, ct);
    }

    // A prescription is one register: the winning version holds its whole
    // state, and this device's row is made to match it (deleted when the
    // winner is a deletion).
    private async Task ApplyPrescriptionAsync(PrescriptionChanged change, HybridTimestamp timestamp, CancellationToken ct)
    {
        await GetMedicineAsync(change.MedicineId, ct);
        await RecordRegistersAsync(change, timestamp, ct);
        if (_prescriptions is null) return;
        var winner = await _registers.WinnerAsync(change.PrescriptionId, SyncRegisters.PrescriptionState, ct);
        var state = winner?.Value is { } value ? SyncRegisters.ParsePrescription(value) : change;
        var row = await _prescriptions.GetAsync(change.PrescriptionId, ct);
        if (state.Deleted)
        {
            if (row is not null) await _prescriptions.RemoveAsync(row, ct);
            return;
        }
        if (row is null)
        {
            row = new Prescription { Id = state.PrescriptionId, MedicineId = state.MedicineId, RecordedAt = state.RecordedAt };
            CopyState(state, row);
            await _prescriptions.AddAsync(row, ct);
        }
        else
        {
            CopyState(state, row);
            await _prescriptions.UpdateAsync(row, ct);
        }
    }

    private static void CopyState(PrescriptionChanged state, Prescription row)
    {
        row.RequestedOn = state.RequestedOn;
        row.IssuedOn = state.IssuedOn;
        row.Code = state.Code;
        row.Packages = state.Packages;
        row.ValidUntil = state.ValidUntil;
        row.CollectedOn = state.CollectedOn;
        row.Dispensations = state.Dispensations;
        row.UpdatedAt = state.RecordedAt;
    }

    // A dispensation is one register, like a package. Its row follows its
    // own register only, whatever happened to the prescription: a
    // dispensation recorded on one device while another deleted the
    // prescription stays on every device, unused, so all devices hold the
    // same rows (no foreign key to Prescriptions for that reason).
    private async Task ApplyDispensationAsync(DispensationChanged change, HybridTimestamp timestamp, CancellationToken ct)
    {
        await GetMedicineAsync(change.MedicineId, ct);
        await RecordRegistersAsync(change, timestamp, ct);
        if (_dispensations is null) return;
        var winner = await _registers.WinnerAsync(change.DispensationId, SyncRegisters.DispensationState, ct);
        var state = winner?.Value is { } value ? SyncRegisters.ParseDispensation(value) : change;
        var row = await _dispensations.GetAsync(change.DispensationId, ct);
        if (state.Deleted)
        {
            if (row is not null) await _dispensations.RemoveAsync(row, ct);
            return;
        }
        if (row is null)
        {
            row = new PrescriptionDispensation
            {
                Id = state.DispensationId,
                PrescriptionId = state.PrescriptionId,
                MedicineId = state.MedicineId,
                RecordedAt = state.RecordedAt,
            };
            CopyState(state, row);
            await _dispensations.AddAsync(row, ct);
        }
        else
        {
            CopyState(state, row);
            await _dispensations.UpdateAsync(row, ct);
        }
    }

    private static void CopyState(DispensationChanged state, PrescriptionDispensation row)
    {
        row.CollectedOn = state.CollectedOn;
        row.Packages = state.Packages;
        row.UpdatedAt = state.RecordedAt;
    }

    // A deadline is one register, like a prescription. A deadline of a
    // medicine needs the medicine (causal order); a deadline of the
    // profile (Guid.Empty) needs none.
    private async Task ApplyDeadlineAsync(DeadlineChanged change, HybridTimestamp timestamp, CancellationToken ct)
    {
        if (change.MedicineId != Guid.Empty) await GetMedicineAsync(change.MedicineId, ct);
        await RecordRegistersAsync(change, timestamp, ct);
        if (_deadlines is null) return;
        var winner = await _registers.WinnerAsync(change.DeadlineId, SyncRegisters.DeadlineState, ct);
        var state = winner?.Value is { } value ? SyncRegisters.ParseDeadline(value) : change;
        var row = await _deadlines.GetAsync(change.DeadlineId, ct);
        if (state.Deleted)
        {
            if (row is not null) await _deadlines.RemoveAsync(row, ct);
            return;
        }
        if (row is null)
        {
            row = new Deadline { Id = state.DeadlineId, RecordedAt = state.RecordedAt };
            CopyState(state, row);
            await _deadlines.AddAsync(row, ct);
        }
        else
        {
            CopyState(state, row);
            await _deadlines.UpdateAsync(row, ct);
        }
    }

    private static void CopyState(DeadlineChanged state, Deadline row)
    {
        row.MedicineId = state.MedicineId == Guid.Empty ? null : state.MedicineId;
        row.Kind = state.Kind;
        row.Label = state.Label;
        row.DueOn = state.DueOn;
        row.LeadDays = state.LeadDays;
        row.RepeatMonths = state.RepeatMonths;
        row.Channels = state.Channels;
        row.DoneOn = state.DoneOn;
        row.UpdatedAt = state.RecordedAt;
    }

    // A package is one register, like a prescription, with one exception:
    // a discard is final. It also wrote a stock correction, a separate
    // fact, so a concurrent edit that wins the register must not reopen
    // the package. The latest discard by HLC keeps its closure whatever
    // version wins; the result depends only on the set of versions, so
    // every device agrees. A deletion still removes the package.
    private async Task ApplyPackageAsync(PackageChanged change, HybridTimestamp timestamp, CancellationToken ct)
    {
        await GetMedicineAsync(change.MedicineId, ct);
        await RecordRegistersAsync(change, timestamp, ct);
        if (_packages is null) return;
        var versions = await _registers.ListAsync(change.PackageId, SyncRegisters.PackageState, ct);
        var winner = versions.MaxBy(v => v.Version);
        var state = winner?.Value is { } value ? SyncRegisters.ParsePackage(value) : change;
        var row = await _packages.GetAsync(change.PackageId, ct);
        if (state.Deleted)
        {
            if (row is not null) await _packages.RemoveAsync(row, ct);
            return;
        }
        if (state.Closure != PackageClosure.Discarded
            && versions
                .Where(v => v.Value is not null)
                .OrderByDescending(v => v.Version)
                .Select(v => SyncRegisters.ParsePackage(v.Value!))
                .FirstOrDefault(p => p.Closure == PackageClosure.Discarded) is { } discard)
        {
            state = state with { ClosedOn = discard.ClosedOn, Closure = PackageClosure.Discarded };
        }
        if (row is null)
        {
            row = new StockPackage { Id = state.PackageId, MedicineId = state.MedicineId, RecordedAt = state.RecordedAt };
            CopyState(state, row);
            await _packages.AddAsync(row, ct);
        }
        else
        {
            CopyState(state, row);
            await _packages.UpdateAsync(row, ct);
        }
    }

    private static void CopyState(PackageChanged state, StockPackage row)
    {
        row.MovementId = state.MovementId;
        row.Quantity = state.Quantity;
        row.ExpiresOn = state.ExpiresOn;
        row.UseWithinDays = state.UseWithinDays;
        row.OpenedOn = state.OpenedOn;
        row.Batch = state.Batch;
        row.ClosedOn = state.ClosedOn;
        row.Closure = state.Closure;
        row.UpdatedAt = state.RecordedAt;
    }

    private async Task CreateMedicineAsync(MedicineCreated created, CancellationToken ct)
    {
        if (await _medicines.GetAsync(created.MedicineId, ct) is not null) return;
        var medicine = new Medicine
        {
            Id = created.MedicineId,
            Name = string.Empty,
            Unit = string.Empty,
            StartDate = created.StartDate,
            IsActive = true,
            StockEpoch = 1,
            CreatedAt = created.CreatedAt,
            UpdatedAt = _clock.GetUtcNow(),
        };
        foreach (var field in created.Fields)
        {
            MedicineFieldCodec.Set(medicine, field.Field, field.Value);
        }
        await _medicines.AddAsync(medicine, ct);
        _medicineCache[medicine.Id] = medicine;
    }

    private async Task ApplyScheduleRowAsync(ScheduleRowRecorded row, HybridTimestamp timestamp, CancellationToken ct)
    {
        var medicine = await GetMedicineAsync(row.MedicineId, ct);
        if (await IsNewFactAsync(row.MedicineId, row.RowId, ct)
            && (await _schedules.ListForMedicineAsync(row.MedicineId, ct)).All(s => s.Id != row.RowId))
        {
            await _schedules.AddAsync(new MedicationScheduleHistory
            {
                Id = row.RowId,
                MedicineId = row.MedicineId,
                EffectiveFrom = row.EffectiveFrom,
                DosePerAdministration = row.DosePerAdministration,
                AdministrationsPerDay = row.AdministrationsPerDay,
                ScheduleKind = row.ScheduleKind,
                SchedulePayload = row.SchedulePayload,
                RecordedAt = row.RecordedAt,
            }, ct);
        }
        var wins = await _registers.RecordAsync(row.MedicineId, row, timestamp, ct);
        if (wins[SyncRegisters.LatestSchedule])
        {
            // §4.2: the current schedule summary follows the most recently
            // recorded row, whatever its EffectiveFrom.
            medicine.DosePerAdministration = row.DosePerAdministration;
            medicine.AdministrationsPerDay = row.AdministrationsPerDay;
        }
    }

    private async Task ApplySlotSetAsync(SlotSetRecorded set, HybridTimestamp timestamp, CancellationToken ct)
    {
        await GetMedicineAsync(set.MedicineId, ct);
        var known = await _slots.ListSetsForMedicineAsync(set.MedicineId, ct);
        if (await IsNewFactAsync(set.MedicineId, set.SetId, ct) && known.All(e => e.Set.Id != set.SetId))
        {
            var entity = new MedicationAdministrationSlotSet
            {
                Id = set.SetId,
                MedicineId = set.MedicineId,
                EffectiveFrom = set.EffectiveFrom,
                RecordedAt = set.RecordedAt,
            };
            await _slots.AddSetAsync(entity, [.. set.Slots.Select(s => new MedicationAdministrationSlot
            {
                Id = s.SlotId,
                MedicineId = set.MedicineId,
                SetId = set.SetId,
                Dose = s.Dose,
                Time = s.Time,
                TimingLabel = s.TimingLabel,
                Order = s.Order,
                IsAsNeeded = s.IsAsNeeded,
                PresetId = s.PresetId,
            })], ct);
        }
        await _registers.RecordAsync(set.MedicineId, set, timestamp, ct);
    }

    private async Task ApplyStockEntryAsync(StockEntryRecorded entry, CancellationToken ct)
    {
        var medicine = await GetMedicineAsync(entry.MedicineId, ct);
        if (!await IsNewFactAsync(entry.MedicineId, entry.MovementId, ct)) return;
        if ((await _stock.ListForMedicineAsync(entry.MedicineId, ct)).Any(m => m.Id == entry.MovementId)) return;
        var movement = new StockMovement
        {
            Id = entry.MovementId,
            MedicineId = entry.MedicineId,
            OccurredAt = entry.OccurredAt,
            Kind = entry.Kind,
            QuantityDelta = entry.QuantityDelta,
            // Local bookkeeping only: epochs are derived (§4.4).
            StockEpoch = medicine.StockEpoch,
            Origin = StockMovementOrigin.User,
            Notes = entry.Notes,
        };
        await _stock.AddAsync(movement, ct);
        _trackedFacts[movement.Id] = movement;
    }

    private async Task ApplyIntakeAsync(IntakeRecorded intake, HybridTimestamp timestamp, CancellationToken ct)
    {
        var medicine = await GetMedicineAsync(intake.MedicineId, ct);
        if (!await IsNewFactAsync(intake.MedicineId, intake.IntakeId, ct)) return;
        var existing = await _intakes.ListForMedicineAsync(intake.MedicineId, ct);
        if (existing.Any(i => i.Id == intake.IntakeId)) return;

        var added = new MedicationIntake
        {
            Id = intake.IntakeId,
            MedicineId = intake.MedicineId,
            Day = intake.Day,
            Status = intake.Status,
            Quantity = intake.Quantity,
            ScheduledAt = intake.ScheduledAt,
            ActualAt = intake.ActualAt,
            Notes = intake.Notes,
            RecordedAt = intake.RecordedAt,
            IsExtra = intake.IsExtra,
        };
        await _intakes.AddAsync(added, ct);
        _trackedFacts[added.Id] = added;

        // §4.5 hint: intakes of one day from different devices that
        // together exceed the day's scheduled quantity.
        // Extra intakes are on top of the plan by definition.
        var sameDay = existing
            .Where(i => i.Day == intake.Day && i.Status == IntakeStatus.Taken && !i.IsExtra)
            .ToList();
        if (intake.Status != IntakeStatus.Taken || intake.IsExtra || sameDay.Count == 0) return;
        var planned = await PlannedQuantityAsync(medicine, intake.Day, ct);
        var total = sameDay.Sum(i => i.Quantity) + intake.Quantity;
        if (total <= planned) return;
        var other = sameDay.MaxBy(i => i.RecordedAt)!;
        await _registers.AddHintAsync(new SyncConflict
        {
            Id = SyncRegisters.ConflictId(intake.MedicineId, SyncConflictKind.IntakesOverSchedule,
                intake.IntakeId, null, other.Id.ToString("N")),
            Kind = SyncConflictKind.IntakesOverSchedule,
            MedicineId = intake.MedicineId,
            SubjectId = intake.IntakeId,
            OtherId = other.Id,
            LosingDeviceId = timestamp.DeviceId,
            DetectedAt = _clock.GetUtcNow(),
        }, ct);
    }

    private async Task ApplyCountAsync(StockCountRecorded count, CancellationToken ct)
    {
        await GetMedicineAsync(count.MedicineId, ct);
        if (!await IsNewFactAsync(count.MedicineId, count.CountId, ct)) return;
        if ((await _counts.ListForMedicineAsync(count.MedicineId, ct)).Any(c => c.Id == count.CountId)) return;
        var stored = new StockCount
        {
            Id = count.CountId,
            MedicineId = count.MedicineId,
            CountDay = count.CountDay,
            CountedQuantity = count.CountedQuantity,
            TakenToday = count.TakenToday,
            ThresholdAtCount = count.ThresholdAtCount,
            RecordedAt = count.RecordedAt,
            Notes = count.Notes,
            LedgerAtStartOfDay = count.LedgerAtStartOfDay,
            CountDayScheduled = count.CountDayScheduled,
            Correction = count.Correction,
            MaterializesCountDay = count.MaterializesCountDay,
            AdvancesEpoch = count.AdvancesEpoch,
        };
        await _counts.AddAsync(stored, ct);
        _trackedFacts[stored.Id] = stored;
    }

    private async Task ApplySuspensionAsync(SuspensionRecorded suspension, HybridTimestamp timestamp, CancellationToken ct)
    {
        await GetMedicineAsync(suspension.MedicineId, ct);
        await RecordRegistersAsync(suspension, timestamp, ct);
        if (!await IsNewFactAsync(suspension.MedicineId, suspension.SuspensionId, ct)) return;
        var existing = await _suspensions.ListForMedicineAsync(suspension.MedicineId, ct);
        if (existing.Any(s => s.Id == suspension.SuspensionId)) return;

        var winner = await _registers.WinnerAsync(suspension.SuspensionId, SyncRegisters.EndDate, ct);
        var entity = new MedicationSuspension
        {
            Id = suspension.SuspensionId,
            MedicineId = suspension.MedicineId,
            StartDate = suspension.StartDate,
            EndDate = ParseDate(winner!.Value),
            Reason = suspension.Reason,
            RecordedAt = suspension.RecordedAt,
        };
        await _suspensions.AddAsync(entity, ct);
        _trackedFacts[entity.Id] = entity;

        // §4.5 hint: overlapping suspensions are kept as they are (the
        // derivation takes their union).
        foreach (var other in existing.Where(s => Overlap(s, entity)))
        {
            await _registers.AddHintAsync(new SyncConflict
            {
                Id = SyncRegisters.ConflictId(suspension.MedicineId, SyncConflictKind.OverlappingSuspensions,
                    entity.Id, null, other.Id.ToString("N")),
                Kind = SyncConflictKind.OverlappingSuspensions,
                MedicineId = suspension.MedicineId,
                SubjectId = entity.Id,
                OtherId = other.Id,
                LosingDeviceId = timestamp.DeviceId,
                DetectedAt = _clock.GetUtcNow(),
            }, ct);
        }
    }

    private async Task ApplySuspensionEndAsync(SuspensionEndChanged end, HybridTimestamp timestamp, CancellationToken ct)
    {
        await GetMedicineAsync(end.MedicineId, ct);
        await RecordRegistersAsync(end, timestamp, ct);
        var suspension = _trackedFacts.GetValueOrDefault(end.SuspensionId) as MedicationSuspension
            ?? (await _suspensions.ListForMedicineAsync(end.MedicineId, ct))
                .FirstOrDefault(s => s.Id == end.SuspensionId);
        // Retracted or not arrived yet: the version is kept for later.
        if (suspension is null) return;
        var winner = await _registers.WinnerAsync(end.SuspensionId, SyncRegisters.EndDate, ct);
        suspension.EndDate = ParseDate(winner!.Value);
        await _suspensions.UpdateAsync(suspension, ct);
        _trackedFacts[suspension.Id] = suspension;
    }

    private async Task ApplyRetractionAsync(FactRetracted retraction, HybridTimestamp timestamp, CancellationToken ct)
    {
        await GetMedicineAsync(retraction.MedicineId, ct);
        var existing = _tombstones.GetValueOrDefault(retraction.FactId)
            ?? (await _retractions.ListForMedicineAsync(retraction.MedicineId, ct))
                .FirstOrDefault(r => r.FactId == retraction.FactId);
        if (existing is not null)
        {
            // Retracted on two devices: one tombstone per fact, the
            // earliest (recording instant, then id) on every device.
            if ((existing.RecordedAt, existing.Id).CompareTo((retraction.RecordedAt, retraction.RetractionId)) <= 0) return;
            await _retractions.RemoveAsync(existing, ct);
            await AddTombstoneAsync(retraction, ct);
            return;
        }
        await AddTombstoneAsync(retraction, ct);

        switch (retraction.Kind)
        {
            case FactKind.StockEntry:
                var entry = _trackedFacts.GetValueOrDefault(retraction.FactId) as StockMovement
                    ?? (await _stock.ListForMedicineAsync(retraction.MedicineId, ct))
                        .FirstOrDefault(m => m.Id == retraction.FactId && m.Origin == StockMovementOrigin.User);
                if (entry is not null) await _stock.RemoveRangeAsync([entry], ct);
                break;
            case FactKind.Intake:
                var intake = _trackedFacts.GetValueOrDefault(retraction.FactId) as MedicationIntake
                    ?? (await _intakes.ListForMedicineAsync(retraction.MedicineId, ct))
                        .FirstOrDefault(i => i.Id == retraction.FactId);
                if (intake is not null) await _intakes.RemoveAsync(intake, ct);
                break;
            case FactKind.StockCount:
                var count = _trackedFacts.GetValueOrDefault(retraction.FactId) as StockCount
                    ?? (await _counts.ListForMedicineAsync(retraction.MedicineId, ct))
                        .FirstOrDefault(c => c.Id == retraction.FactId);
                if (count is not null) await _counts.RemoveAsync(count, ct);
                break;
            case FactKind.Suspension:
                var suspension = _trackedFacts.GetValueOrDefault(retraction.FactId) as MedicationSuspension
                    ?? (await _suspensions.ListForMedicineAsync(retraction.MedicineId, ct))
                        .FirstOrDefault(s => s.Id == retraction.FactId);
                if (suspension is not null) await _suspensions.RemoveAsync(suspension, ct);

                // §4.5: the retraction wins; an end date set on another
                // device after the suspension was recorded is listed. The
                // oldest version is the one SuspensionRecorded wrote.
                var versions = (await _registers.ListAsync(retraction.FactId, SyncRegisters.EndDate, ct))
                    .OrderBy(v => v.Version).Skip(1)
                    .Where(v => v.DeviceId != timestamp.DeviceId);
                foreach (var edit in versions)
                {
                    await AddRetractedEditHintAsync(retraction, edit, ct);
                }
                break;
        }
        _trackedFacts.Remove(retraction.FactId);
    }

    private async Task ApplyDeletionAsync(MedicineDeleted deleted, CancellationToken ct)
    {
        await _deletion.RemoveAsync(deleted.MedicineId, ct);
        _medicineCache.Remove(deleted.MedicineId);
        _deleted[deleted.MedicineId] = true;
    }

    private async Task<bool> IsDeletedAsync(Guid medicineId, CancellationToken ct)
    {
        if (_deleted.TryGetValue(medicineId, out var deleted)) return deleted;
        deleted = (await _operations.ListForMedicineAsync(medicineId, ct))
            .Any(o => o.Type == nameof(MedicineDeleted));
        _deleted[medicineId] = deleted;
        return deleted;
    }

    private async Task AddTombstoneAsync(FactRetracted retraction, CancellationToken ct)
    {
        var tombstone = new FactRetraction
        {
            Id = retraction.RetractionId,
            MedicineId = retraction.MedicineId,
            FactId = retraction.FactId,
            Kind = retraction.Kind,
            RecordedAt = retraction.RecordedAt,
        };
        await _retractions.AddAsync(tombstone, ct);
        _tombstones[retraction.FactId] = tombstone;
    }

    private Task AddRetractedEditHintAsync(FactRetracted retraction, SyncFieldVersion edit, CancellationToken ct)
        => _registers.AddHintAsync(new SyncConflict
        {
            Id = SyncRegisters.ConflictId(retraction.MedicineId, SyncConflictKind.RetractedFactEdited,
                retraction.FactId, edit.Register, edit.Version.ToString()),
            Kind = SyncConflictKind.RetractedFactEdited,
            MedicineId = retraction.MedicineId,
            SubjectId = retraction.FactId,
            Register = edit.Register,
            LosingValue = edit.Value,
            LosingDeviceId = edit.DeviceId,
            OtherId = retraction.RetractionId,
            DetectedAt = _clock.GetUtcNow(),
        }, ct);

    private async Task RecordRegistersAsync(SyncOperationBody body, HybridTimestamp timestamp, CancellationToken ct)
        => await _registers.RecordAsync(body.MedicineId, body, timestamp, ct);

    // False for a retracted fact: the tombstone wins whatever the order
    // of arrival (§4.2).
    private async Task<bool> IsNewFactAsync(Guid medicineId, Guid factId, CancellationToken ct)
        => !_tombstones.ContainsKey(factId)
            && (await _retractions.ListForMedicineAsync(medicineId, ct)).All(r => r.FactId != factId);

    private async Task<Medicine> GetMedicineAsync(Guid medicineId, CancellationToken ct)
    {
        if (_medicineCache.TryGetValue(medicineId, out var cached)) return cached;
        var medicine = await _medicines.GetAsync(medicineId, ct)
            ?? throw new InvalidOperationException(
                $"Medicine {medicineId} is unknown: operations must be delivered in causal order.");
        _medicineCache[medicineId] = medicine;
        return medicine;
    }

    private async Task<decimal> PlannedQuantityAsync(Medicine medicine, DateOnly day, CancellationToken ct)
        => ConsumptionMaterializer.Plan(
                medicine, day, day,
                await _schedules.ListForMedicineAsync(medicine.Id, ct),
                await _suspensions.ListForMedicineAsync(medicine.Id, ct),
                await _slots.ListForMedicineAsync(medicine.Id, ct))
            .Sum(p => p.Quantity);

    private static bool Overlap(MedicationSuspension a, MedicationSuspension b)
        => a.StartDate <= (b.EndDate ?? DateOnly.MaxValue) && b.StartDate <= (a.EndDate ?? DateOnly.MaxValue);

    private static DateOnly? ParseDate(string? value)
        => value is null ? null : DateOnly.ParseExact(value, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
}
