using FluentAssertions;
using MedReminder.Application.Abstractions;
using MedReminder.Application.Ledger;
using MedReminder.Application.Monitoring;
using MedReminder.Application.Tests.Support;
using MedReminder.Application.UseCases;
using MedReminder.Domain.Ledger;
using MedReminder.Domain.Medicines;
using MedReminder.Domain.Notifications;
using MedReminder.Domain.Stock;
using Xunit;

namespace MedReminder.Application.Tests.Architecture;

// B.1 Phase 3a (docs/analysis/ANALYSIS-B1-MOBILE-SYNC.md §7.4): every
// use case waits while another writer holds WriteGate. The holder here is
// a catch-up blocked inside the gate.
public class WriteGateTests
{
    public static TheoryData<string> UseCases() => new(Actions.Keys);

    private static readonly Dictionary<string, Func<ApplicationTestScope, Guid, Task>> Actions = new()
    {
        ["AddMedicine"] = (s, _) => s.AddMedicine.ExecuteAsync(new AddMedicineCommand(
            "Other", "compresse", 1m, 1, new DateOnly(2026, 9, 1), 7, NotificationChannels.Windows), default),
        ["UpdateMedicine"] = (s, id) => s.UpdateMedicine.ExecuteAsync(new UpdateMedicineCommand(
            id, "Enalapril", null, null, "compresse", 7, NotificationChannels.Windows, null, null, "x", true), default),
        ["DeactivateMedicine"] = (s, id) => s.DeactivateMedicine.ExecuteAsync(new DeactivateMedicineCommand(id), default),
        ["AddStock"] = (s, id) => s.AddStock.ExecuteAsync(new AddStockCommand(id, 28m, StockMovementKind.NewPackage), default),
        ["AdjustStockDown"] = (s, id) => s.AdjustStockDown.ExecuteAsync(new AdjustStockDownCommand(id, 1m), default),
        ["ChangeMedicationSchedule"] = (s, id) => s.ChangeMedicationSchedule.ExecuteAsync(
            new ChangeMedicationScheduleCommand(id, 2m, 1, new DateOnly(2026, 9, 20)), default),
        ["SuspendMedication"] = (s, id) => s.SuspendMedication.ExecuteAsync(
            new SuspendMedicationCommand(id, new DateOnly(2026, 9, 20)), default),
        ["ResumeMedication"] = async (s, id) =>
        {
            s.Suspensions.AddAsync(new MedicationSuspension { MedicineId = id, StartDate = new DateOnly(2026, 9, 5) }, default)
                .GetAwaiter().GetResult();
            await s.ResumeMedication.ExecuteAsync(new ResumeMedicationCommand(id, new DateOnly(2026, 9, 10)), default);
        },
        ["RegisterIntake"] = (s, id) => s.RegisterIntake.ExecuteAsync(
            new RegisterIntakeCommand(id, new DateOnly(2026, 9, 12), IntakeStatus.Taken, 1m), default),
        ["ReconcileStock"] = (s, id) => s.ReconcileStock.ExecuteAsync(new ReconcileStockCommand(id, 20m, 0m), default),
        ["ApplyRemoteOperations"] = (s, _) =>
        {
            s.EnableSync();
            return s.ApplyRemote.ExecuteAsync([], default);
        },
        ["RetractFact"] = (s, id) => s.RetractFact.ExecuteAsync(new RetractFactCommand(
            id, FactKind.StockEntry, s.Stock.All.Single(m => m.Kind == StockMovementKind.InitialLoad).Id), default),
    };

    [Theory]
    [MemberData(nameof(UseCases))]
    public async Task Use_case_waits_for_the_gate(string name)
    {
        var scope = new ApplicationTestScope();
        var id = await scope.AddMedicine.ExecuteAsync(new AddMedicineCommand(
            "Enalapril", "compresse", 1m, 1, new DateOnly(2026, 9, 1), 7, NotificationChannels.Windows,
            InitialQuantity: 30m), default);

        var blocking = new BlockingMedicineRepository(scope.Medicines);
        var holder = new ConsumptionCatchUp(blocking, scope.Ledger, scope.Uow).RunAsync(default);
        await blocking.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Task action;
        Task early;
        try
        {
            action = Actions[name](scope, id);
            early = await Task.WhenAny(action, Task.Delay(200));
        }
        finally
        {
            // Always release: the gate is process-wide, and a holder left
            // blocked would hang every later test.
            blocking.Release.TrySetResult();
            await holder;
        }

        early.Should().NotBeSameAs(action, $"{name} must not run while the gate is held");
        await action.WaitAsync(TimeSpan.FromSeconds(10));
    }

    private sealed class BlockingMedicineRepository(IMedicineRepository inner) : IMedicineRepository
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<IReadOnlyList<Medicine>> ListAllAsync(CancellationToken cancellationToken)
        {
            Entered.TrySetResult();
            await Release.Task;
            return await inner.ListAllAsync(cancellationToken);
        }

        public Task<Medicine?> GetAsync(Guid id, CancellationToken ct) => inner.GetAsync(id, ct);
        public Task<IReadOnlyList<Medicine>> ListActiveAsync(CancellationToken ct) => inner.ListActiveAsync(ct);
        public Task<IReadOnlyList<Medicine>> ListActiveWithDoseReminderAsync(CancellationToken ct)
            => inner.ListActiveWithDoseReminderAsync(ct);
        public Task AddAsync(Medicine medicine, CancellationToken ct) => inner.AddAsync(medicine, ct);
        public Task UpdateAsync(Medicine medicine, CancellationToken ct) => inner.UpdateAsync(medicine, ct);
    }
}
