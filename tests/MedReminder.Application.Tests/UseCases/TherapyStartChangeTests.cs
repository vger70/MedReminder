using FluentAssertions;
using MedReminder.Application.Sync;
using MedReminder.Application.Tests.Support;
using MedReminder.Application.UseCases;
using MedReminder.Domain.Notifications;
using MedReminder.Domain.Sync;
using Xunit;

namespace MedReminder.Application.Tests.UseCases;

// The therapy start date changed from the edit form (UpdateMedicine,
// TherapyStartChange): the date is saved, the schedule and the slots
// follow it, and the change replicates. "Today" is 2026-09-13, so the
// ledger consumes through 2026-09-12.
public class TherapyStartChangeTests
{
    private static readonly DateOnly Start = new(2026, 9, 10);

    private readonly ApplicationTestScope _a = new(new DateTimeOffset(2026, 9, 13, 12, 0, 0, TimeSpan.Zero));
    private readonly ApplicationTestScope _b = new(new DateTimeOffset(2026, 9, 13, 12, 0, 0, 500, TimeSpan.Zero));

    private async Task<Guid> SeedAsync(IReadOnlyList<AdministrationSlotInput>? slots = null)
        => await _a.AddMedicine.ExecuteAsync(new AddMedicineCommand(
            "Enalapril", "compresse", 1m, 2, Start, 7, NotificationChannels.Windows,
            InitialQuantity: 30m, AdministrationSlots: slots), default);

    private static UpdateMedicineCommand Form(Guid id, DateOnly start, DateOnly? end = null,
        IReadOnlyList<AdministrationSlotInput>? slots = null)
        => new(id, "Enalapril", null, null, "compresse", 7, NotificationChannels.Windows, end, null, null, true,
            AdministrationSlots: slots ?? [], StartDate: start);

    private static async Task MoveStartAsync(ApplicationTestScope scope, Guid id, DateOnly from, DateOnly to,
        IReadOnlyList<AdministrationSlotInput>? slots = null)
        => await scope.UpdateMedicine.ExecuteAsync(
            Form(id, to, slots: slots) with { Baseline = Form(id, from, slots: slots) }, default);

    private static async Task<decimal> StockAsync(ApplicationTestScope scope, Guid id)
    {
        var medicine = await scope.Medicines.GetAsync(id, default);
        return (await scope.Ledger.SynchronizeAsync(medicine!, default)).Ledger.Stock;
    }

    [Fact]
    public async Task An_earlier_start_is_saved_and_consumes_the_days_gained()
    {
        var id = await SeedAsync();
        (await StockAsync(_a, id)).Should().Be(24m);

        await MoveStartAsync(_a, id, Start, new DateOnly(2026, 9, 5));

        (await _a.Medicines.GetAsync(id, default))!.StartDate.Should().Be(new DateOnly(2026, 9, 5));
        (await StockAsync(_a, id)).Should().Be(30m - 8 * 2m);
    }

    [Fact]
    public async Task An_earlier_start_keeps_the_slots_on_the_days_gained()
    {
        IReadOnlyList<AdministrationSlotInput> slots = [new AdministrationSlotInput(3m, new TimeOnly(8, 0), null)];
        var id = await SeedAsync(slots);

        await MoveStartAsync(_a, id, Start, new DateOnly(2026, 9, 5), slots);

        (await StockAsync(_a, id)).Should().Be(30m - 8 * 3m);
        (await _a.Slots.ListForMedicineAsync(id, default)).Select(s => s.Dose).Should().Equal(3m);
    }

    [Fact]
    public async Task A_later_start_drops_the_days_before_it()
    {
        var id = await SeedAsync();

        await MoveStartAsync(_a, id, Start, new DateOnly(2026, 9, 12));

        (await StockAsync(_a, id)).Should().Be(30m - 1 * 2m);
    }

    [Fact]
    public async Task A_start_after_the_end_date_is_refused()
    {
        var id = await SeedAsync();

        var act = () => _a.UpdateMedicine.ExecuteAsync(
            Form(id, new DateOnly(2026, 10, 1), end: new DateOnly(2026, 9, 30)), default);

        await act.Should().ThrowAsync<ArgumentException>();
        (await _a.Medicines.GetAsync(id, default))!.StartDate.Should().Be(Start);
    }

    [Fact]
    public async Task An_untouched_start_is_left_alone()
    {
        var id = await SeedAsync();
        var rows = (await _a.Schedules.ListForMedicineAsync(id, default)).Count;

        await _a.UpdateMedicine.ExecuteAsync(
            Form(id, Start) with { Notes = "x", Baseline = Form(id, Start) }, default);

        (await _a.Medicines.GetAsync(id, default))!.StartDate.Should().Be(Start);
        (await _a.Schedules.ListForMedicineAsync(id, default)).Should().HaveCount(rows);
    }

    [Fact]
    public async Task The_change_replicates_and_keeps_the_schedule_summary()
    {
        var group = _a.EnableSync(Guid.Parse("0a000000-0000-0000-0000-000000000000"));
        _b.SyncSettingsStore.Save(group with { DeviceId = Guid.Parse("0b000000-0000-0000-0000-000000000000") });
        var id = await SeedAsync();
        await _a.ChangeMedicationSchedule.ExecuteAsync(
            new ChangeMedicationScheduleCommand(id, 2m, 1, new DateOnly(2026, 9, 12)), default);
        await _b.ApplyRemote.ExecuteAsync(await _a.SyncOperations.ListAllAsync(default), default);

        await MoveStartAsync(_a, id, Start, new DateOnly(2026, 9, 5));
        var result = await _b.ApplyRemote.ExecuteAsync(await _a.SyncOperations.ListAllAsync(default), default);

        result.Blocked.Should().BeNull();
        var start = _a.SyncOperations.All.Single(o => o.Type == nameof(MedicineStartChanged));
        start.SchemaVersion.Should().Be(10);
        var copy = await _b.Medicines.GetAsync(id, default);
        copy!.StartDate.Should().Be(new DateOnly(2026, 9, 5));
        (copy.DosePerAdministration, copy.AdministrationsPerDay).Should().Be((2m, 1));
        (await StockAsync(_b, id)).Should().Be(await StockAsync(_a, id));
        // 2026-09-05..11 at 1 x 2, 2026-09-12 at 2 x 1.
        (await StockAsync(_a, id)).Should().Be(30m - 7 * 2m - 2m);
    }
}
