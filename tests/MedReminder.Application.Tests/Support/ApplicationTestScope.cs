using MedReminder.Application.Monitoring;
using MedReminder.Application.UseCases;
using Microsoft.Extensions.Logging.Abstractions;

namespace MedReminder.Application.Tests.Support;

// Wiring uniforme delle dipendenze per i test: fornisce repository
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
    public InMemoryNotificationEventRepository Notifications { get; } = new();
    public InMemoryUnitOfWork Uow { get; } = new();
    public RecordingEmailNotificationService Email { get; } = new();
    public RecordingWindowsNotificationService Windows { get; } = new();

    public AddMedicine AddMedicine { get; }
    public UpdateMedicine UpdateMedicine { get; }
    public AddStock AddStock { get; }
    public AdjustStockDown AdjustStockDown { get; }
    public SuspendMedication SuspendMedication { get; }
    public ResumeMedication ResumeMedication { get; }
    public ChangeMedicationSchedule ChangeMedicationSchedule { get; }
    public RegisterIntake RegisterIntake { get; }
    public ConsumptionCatchUp ConsumptionCatchUp { get; }
    public MedicationMonitor Monitor { get; }

    public ApplicationTestScope(DateTimeOffset? now = null)
    {
        Clock = new FakeTimeProvider(now ?? new DateTimeOffset(2026, 9, 13, 12, 0, 0, TimeSpan.Zero));

        AddMedicine = new AddMedicine(Medicines, Schedules, Stock, Uow, Clock);
        UpdateMedicine = new UpdateMedicine(Medicines, Uow, Clock);
        AddStock = new AddStock(Medicines, Stock, Uow, Clock);
        AdjustStockDown = new AdjustStockDown(Medicines, Stock, Uow, Clock);
        SuspendMedication = new SuspendMedication(Medicines, Suspensions, Uow, Clock);
        ResumeMedication = new ResumeMedication(Medicines, Suspensions, Uow, Clock);
        ChangeMedicationSchedule = new ChangeMedicationSchedule(Medicines, Schedules, Uow, Clock);
        RegisterIntake = new RegisterIntake(Medicines, Intakes, Stock, Uow, Clock);

        ConsumptionCatchUp = new ConsumptionCatchUp(
            Medicines, Schedules, Suspensions, Stock, Intakes, Uow, Clock);

        Monitor = new MedicationMonitor(
            Medicines, Stock, Schedules, Suspensions, Notifications,
            Email, Windows, Uow, Clock,
            NullLogger<MedicationMonitor>.Instance);
    }
}
