using FluentAssertions;
using MedReminder.Application.DoseTimes;
using MedReminder.Application.Overview;
using MedReminder.Application.Tests.Support;
using MedReminder.Application.UseCases;
using MedReminder.Domain.Medicines;
using MedReminder.Domain.Notifications;
using Xunit;

namespace MedReminder.Application.Tests.Overview;

// The main list shows the stock after today's doses already due
// (docs/analysis/ANALYSIS-INTRADAY-CONSUMPTION.md §4, §7).
public class IntradayStockTests
{
    private static readonly DateOnly Start = new(2026, 9, 10);

    private static DateTimeOffset At(int day, int hour, int minute = 0) => new(2026, 9, day, hour, minute, 0, TimeSpan.Zero);

    private static DueToday Due(ApplicationTestScope scope)
        => new(scope.Intakes, scope.Counts, scope.Clock, scope.DoseTimePresets);

    private static async Task<MedicineListItem> RowAsync(ApplicationTestScope scope, Guid id)
    {
        var loader = new MedicineOverviewLoader(scope.Medicines, scope.Stock, scope.Schedules, scope.Suspensions,
            scope.Slots, scope.Clock, new JsonDictionaryLocalizationService("en"), dueToday: Due(scope));
        return (await loader.LoadAsync(default)).Single(i => i.Id == id);
    }

    private static async Task<Guid> SeedAsync(ApplicationTestScope scope, int perDay = 2,
        params AdministrationSlotInput[] slots)
    {
        var id = await scope.AddMedicine.ExecuteAsync(new AddMedicineCommand(
            "Enalapril", "compresse", 1m, perDay, Start, 7, NotificationChannels.Windows,
            InitialQuantity: 30m, AdministrationSlots: slots.Length == 0 ? null : slots), default);
        await scope.ConsumptionCatchUp.RunAsync(default);
        return id;
    }

    [Fact]
    public async Task The_stock_drops_when_each_dose_time_passes()
    {
        var scope = new ApplicationTestScope(At(13, 7));
        var id = await SeedAsync(scope, 2,
            new AdministrationSlotInput(1m, new TimeOnly(8, 0), null),
            new AdministrationSlotInput(1m, new TimeOnly(20, 0), null));
        // Sep 10..12 booked: 30 - 6.
        (await RowAsync(scope, id)).CurrentStock.Should().Be(24m);

        scope.Clock.SetUtcNow(At(13, 8));
        var row = await RowAsync(scope, id);
        row.CurrentStock.Should().Be(23m);
        row.LedgerStock.Should().Be(24m);
        row.DueTodaySoFar.Should().Be(1m);

        scope.Clock.SetUtcNow(At(13, 20));
        (await RowAsync(scope, id)).CurrentStock.Should().Be(22m);
    }

    [Fact]
    public async Task The_forecast_keeps_the_start_of_day_stock()
    {
        var scope = new ApplicationTestScope(At(13, 7));
        var id = await SeedAsync(scope);
        var before = await RowAsync(scope, id);

        scope.Clock.SetUtcNow(At(13, 21));
        var after = await RowAsync(scope, id);

        after.CurrentStock.Should().Be(before.CurrentStock - 2m);
        after.DaysRemaining.Should().Be(before.DaysRemaining);
        after.EstimatedRunOutDate.Should().Be(before.EstimatedRunOutDate);
    }

    [Fact]
    public async Task The_estimate_is_continuous_across_midnight()
    {
        var scope = new ApplicationTestScope(At(13, 23, 59));
        var id = await SeedAsync(scope, 3);
        var evening = (await RowAsync(scope, id)).CurrentStock;

        scope.Clock.SetUtcNow(At(14, 0, 0));
        await scope.ConsumptionCatchUp.RunAsync(default);
        var midnight = await RowAsync(scope, id);

        midnight.CurrentStock.Should().Be(evening);
        midnight.DueTodaySoFar.Should().Be(0m);
    }

    [Fact]
    public async Task A_recorded_intake_replaces_the_estimate_but_an_extra_one_does_not()
    {
        var scope = new ApplicationTestScope(At(13, 21));
        var scheduled = await SeedAsync(scope);
        var extra = await SeedAsync(scope);

        await scope.RegisterIntake.ExecuteAsync(
            new RegisterIntakeCommand(scheduled, new DateOnly(2026, 9, 13), IntakeStatus.Taken, 2m), default);
        await scope.RegisterIntake.ExecuteAsync(
            new RegisterIntakeCommand(extra, new DateOnly(2026, 9, 13), IntakeStatus.Taken, 1m, IsExtra: true), default);

        var s = await RowAsync(scope, scheduled);
        s.DueTodaySoFar.Should().Be(0m);
        s.CurrentStock.Should().Be(22m, "the intake booked today's two doses");

        var e = await RowAsync(scope, extra);
        e.DueTodaySoFar.Should().Be(2m);
        e.CurrentStock.Should().Be(21m, "the extra dose plus today's two scheduled doses");
    }

    [Fact]
    public async Task Preset_times_and_user_changes_place_untimed_slots()
    {
        var scope = new ApplicationTestScope(At(13, 9));
        var lunch = BuiltInDoseTimePresets.IdOf("BeforeLunch");
        var id = await SeedAsync(scope, 1, new AdministrationSlotInput(1m, null, "Prima di pranzo", PresetId: lunch));
        (await RowAsync(scope, id)).DueTodaySoFar.Should().Be(0m);

        var settings = DoseTimeSettings.BuiltIn;
        await new SaveDoseTimeSettings(scope.DoseTimePresets, scope.Uow).ExecuteAsync(settings with
        {
            Presets = [.. settings.Presets.Select(p => p.Id == lunch ? p with { Time = new TimeOnly(8, 30) } : p)],
        }, default);

        (await RowAsync(scope, id)).DueTodaySoFar.Should().Be(1m);
    }

    [Fact]
    public async Task An_empty_estimate_shows_the_empty_status()
    {
        var scope = new ApplicationTestScope(At(13, 7));
        var id = await scope.AddMedicine.ExecuteAsync(new AddMedicineCommand(
            "Enalapril", "compresse", 1m, 1, new DateOnly(2026, 9, 13), 7, NotificationChannels.Windows,
            InitialQuantity: 1m), default);

        (await RowAsync(scope, id)).Status.Should().NotBe(MedicineRowStatus.Empty);
        scope.Clock.SetUtcNow(At(13, 9));
        var row = await RowAsync(scope, id);
        row.CurrentStock.Should().Be(0m);
        row.Status.Should().Be(MedicineRowStatus.Empty);
    }

    [Fact]
    public async Task The_count_dialog_suggests_what_the_list_subtracts()
    {
        var scope = new ApplicationTestScope(At(13, 14));
        // No slots: two a day at 08:00 and 20:00.
        var id = await SeedAsync(scope);
        var reconcile = new ReconcileStock(scope.Medicines, scope.Schedules, scope.Suspensions, scope.Slots,
            scope.Counts, scope.Ledger, scope.Operations, scope.Uow, scope.Clock, Due(scope));

        var snapshot = await reconcile.LoadAsync(id, default);

        snapshot.DefaultTakenToday.Should().Be(1m);
        (await RowAsync(scope, id)).DueTodaySoFar.Should().Be(snapshot.DefaultTakenToday);
    }
}
