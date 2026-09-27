using MedReminder.Application.Ledger;
using MedReminder.Application.Monitoring;
using MedReminder.Application.UseCases;
using MedReminder.Domain.Medicines;
using MedReminder.Domain.Stock;
using Microsoft.Extensions.Logging.Abstractions;

namespace MedReminder.Application.Tests.Support;

// Uniform wiring of the dependencies for the tests: provides
// in-memory, orologio manuale e servizi di notifica registranti, oltre
// alle istanze pre-costruite dei principali use case e del monitor.
internal sealed class ApplicationTestScope
{
    public FakeTimeProvider Clock { get; }
    public InMemoryMedicineRepository Medicines { get; } = new();
    public InMemoryStockMovementRepository Stock { get; } = new();
    public InMemoryMedicationScheduleHistoryRepository Schedules { get; } = new();
    public InMemoryMedicationSuspensionRepository Suspensions { get; } = new();
    public InMemoryMedicationIntakeRepository Intakes { get; } = new();
    public InMemoryMedicationAdministrationSlotRepository Slots { get; } = new();
    public InMemoryNotificationEventRepository Notifications { get; } = new();
    public InMemoryDoseReminderEventRepository DoseEvents { get; } = new();
    public InMemoryMedicineActivityRepository Activity { get; } = new();
    public InMemoryStockCountRepository Counts { get; } = new();
    public InMemoryLedgerCutoffRepository Cutoff { get; } = new();
    public InMemoryFactRetractionRepository Retractions { get; } = new();
    public InMemoryUnitOfWork Uow { get; } = new();
    public RecordingEmailNotificationService Email { get; } = new();
    public RecordingWindowsNotificationService Windows { get; } = new();

    public AddMedicine AddMedicine { get; }
    public UpdateMedicine UpdateMedicine { get; }
    public DeactivateMedicine DeactivateMedicine { get; }
    public AddStock AddStock { get; }
    public AdjustStockDown AdjustStockDown { get; }
    public SuspendMedication SuspendMedication { get; }
    public ResumeMedication ResumeMedication { get; }
    public ChangeMedicationSchedule ChangeMedicationSchedule { get; }
    public RegisterIntake RegisterIntake { get; }
    public ReconcileStock ReconcileStock { get; }
    public LedgerSynchronizer Ledger { get; }
    public FactHistoryQuery FactHistory { get; }
    public RetractFact RetractFact { get; }
    public ConsumptionCatchUp ConsumptionCatchUp { get; }
    public MedicationMonitor Monitor { get; }

    public ApplicationTestScope(DateTimeOffset? now = null)
    {
        Clock = new FakeTimeProvider(now ?? new DateTimeOffset(2026, 9, 13, 12, 0, 0, TimeSpan.Zero));

        Ledger = new LedgerSynchronizer(
            new LedgerFactsLoader(Stock, Intakes, Schedules, Suspensions, Slots, Activity, Counts, Cutoff),
            Stock, Medicines, Notifications, Clock);
        FactHistory = new FactHistoryQuery(Medicines, Stock, Intakes, Counts, Suspensions, Cutoff, Clock);
        RetractFact = new RetractFact(
            FactHistory, Medicines, Stock, Intakes, Counts, Suspensions, Retractions, Ledger, Uow, Clock);

        AddMedicine = new AddMedicine(Medicines, Schedules, Slots, Stock, Uow, Clock);
        UpdateMedicine = new UpdateMedicine(Medicines, Slots, Activity, Uow, Clock);
        DeactivateMedicine = new DeactivateMedicine(Medicines, Activity, Uow, Clock);
        AddStock = new AddStock(Medicines, Stock, Uow, Clock);
        AdjustStockDown = new AdjustStockDown(Medicines, Stock, Uow, Clock);
        SuspendMedication = new SuspendMedication(Medicines, Suspensions, Uow, Clock);
        ResumeMedication = new ResumeMedication(Medicines, Suspensions, Uow, Clock);
        ChangeMedicationSchedule = new ChangeMedicationSchedule(Medicines, Schedules, Uow, Clock);
        RegisterIntake = new RegisterIntake(Medicines, Intakes, Ledger, Uow, Clock);

        ConsumptionCatchUp = new ConsumptionCatchUp(Medicines, Ledger, Uow);
        ReconcileStock = new ReconcileStock(
            Medicines, Schedules, Suspensions, Slots, Counts, Ledger, Uow, Clock);

        Monitor = new MedicationMonitor(
            Medicines, Stock, Schedules, Suspensions, Slots, Notifications,
            Email, Windows, Uow, Clock,
            NullLogger<MedicationMonitor>.Instance);
    }

    // Mirrors LedgerFreeze (Infrastructure.Portable): what the B.1 boot
    // patch or an import does to an existing database. The clock moves
    // one second first, so nothing is recorded at the freeze instant.
    public void FreezeLedger()
    {
        Clock.AdvanceBy(TimeSpan.FromSeconds(1));
        var now = Clock.GetUtcNow();
        var cutoffDay = DateOnly.FromDateTime(now.UtcDateTime).AddDays(-1);

        var rows = Stock.All.ToList();
        Stock.RemoveRangeAsync(rows, default).GetAwaiter().GetResult();
        Stock.AddRangeAsync(rows.Select(m => new StockMovement
        {
            Id = m.Id,
            MedicineId = m.MedicineId,
            OccurredAt = m.OccurredAt,
            Kind = m.Kind,
            QuantityDelta = m.QuantityDelta,
            StockEpoch = m.StockEpoch,
            Notes = m.Notes,
            Origin = StockMovementOrigin.Legacy,
        }), default).GetAwaiter().GetResult();

        foreach (var medicine in Medicines.ListAllAsync(default).GetAwaiter().GetResult())
        {
            medicine.LedgerBaselineEpoch = medicine.StockEpoch;
            if (!medicine.IsActive)
            {
                Activity.AddAsync(new MedicineActivityChange
                {
                    Id = medicine.Id,
                    MedicineId = medicine.Id,
                    Day = cutoffDay.AddDays(1),
                    Active = false,
                    RecordedAt = now,
                }, default).GetAwaiter().GetResult();
            }
        }

        Cutoff.Cutoff = new LedgerCutoff { CutoffDay = cutoffDay, FrozenAt = now };
    }

    // Builds a DoseReminderService (A5) wired to the same in-memory
    // dependencies. graceWindow is optional so tests can exercise the
    // "missed dose" drop path with a short window.
    public DoseReminderService BuildDoseReminder(TimeSpan? graceWindow = null)
        => new(
            Medicines, Stock, Schedules, Suspensions, Slots, DoseEvents,
            Email, Windows, Uow, Clock,
            NullLogger<DoseReminderService>.Instance,
            localization: null,
            graceWindow: graceWindow);
}
