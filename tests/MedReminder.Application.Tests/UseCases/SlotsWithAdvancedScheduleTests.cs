using FluentAssertions;
using MedReminder.Application.Tests.Support;
using MedReminder.Application.UseCases;
using MedReminder.Domain.Medicines;
using MedReminder.Domain.Notifications;
using Xunit;

namespace MedReminder.Application.Tests.UseCases;

// A medicine with slots and an advanced schedule
// (docs/analysis/ANALYSIS-SLOTS-ADVANCED-SCHEDULES.md): both are kept,
// and the ledger consumes the schedule's quantity, not the slots' sum.
// "Today" is 2026-09-13, so the ledger consumes through 2026-09-12.
public class SlotsWithAdvancedScheduleTests
{
    private readonly ApplicationTestScope _scope = new();

    [Fact]
    public async Task A_cyclic_plan_with_slots_skips_its_pause_days()
    {
        var id = await _scope.AddMedicine.ExecuteAsync(new AddMedicineCommand(
            "Pill", "compresse", 1m, 1, new DateOnly(2026, 9, 1), 7, NotificationChannels.Windows,
            InitialQuantity: 30m,
            AdministrationSlots:
            [
                new AdministrationSlotInput(1m, new TimeOnly(8, 0), null),
                new AdministrationSlotInput(1m, new TimeOnly(20, 0), null),
            ],
            InitialSchedule: new CyclicSchedule(3, 2, 2m)), default);

        (await _scope.Slots.ListForMedicineAsync(id, default)).Should().HaveCount(2);
        var medicine = await _scope.Medicines.GetAsync(id, default);
        // On days 09-01..03, 06..08, 11..12: 8 days × 2.
        (await _scope.Ledger.SynchronizeAsync(medicine!, default)).Ledger.Stock.Should().Be(30m - 16m);
    }
}
