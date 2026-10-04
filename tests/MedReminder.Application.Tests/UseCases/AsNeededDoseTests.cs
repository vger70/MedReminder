using FluentAssertions;
using MedReminder.Application.Migrations;
using MedReminder.Application.Tests.Support;
using MedReminder.Application.UseCases;
using MedReminder.Domain.Medicines;
using MedReminder.Domain.Notifications;
using MedReminder.Domain.Stock;
using MedReminder.Domain.Sync;
using Xunit;

namespace MedReminder.Application.Tests.UseCases;

// As-needed doses (docs/analysis/ANALYSIS-INTRADAY-CONSUMPTION.md §5):
// extra intakes, the switch to PRN, and the one-time backfill.
public class AsNeededDoseTests
{
    private static readonly DateOnly Start = new(2026, 9, 1);
    // The scope's clock: 2026-09-13 12:00 UTC.
    private static readonly DateOnly Today = new(2026, 9, 13);

    private static Task<Guid> SeedAsync(ApplicationTestScope scope, params AdministrationSlotInput[] slots)
        => scope.AddMedicine.ExecuteAsync(new AddMedicineCommand(
            "Enalapril", "compresse", 1m, 1, Start, 7, NotificationChannels.Windows,
            InitialQuantity: 30m,
            AdministrationSlots: slots.Length == 0 ? null : slots), CancellationToken.None);

    private static async Task<decimal> StockAsync(ApplicationTestScope scope, Guid id)
        => (await scope.Stock.ListForMedicineAsync(id, CancellationToken.None)).Sum(m => m.QuantityDelta);

    [Fact]
    public async Task An_extra_intake_keeps_the_days_scheduled_consumption()
    {
        var scope = new ApplicationTestScope();
        var id = await SeedAsync(scope,
            new AdministrationSlotInput(1m, new TimeOnly(8, 0), null),
            new AdministrationSlotInput(1m, null, "Al bisogno", IsAsNeeded: true));
        await scope.ConsumptionCatchUp.RunAsync(CancellationToken.None);
        var before = await StockAsync(scope, id);

        await scope.RegisterIntake.ExecuteAsync(
            new RegisterIntakeCommand(id, Today, IntakeStatus.Taken, 1m, IsExtra: true), CancellationToken.None);
        (await StockAsync(scope, id)).Should().Be(before - 1m);

        // Next day: today is booked with its scheduled dose as well.
        scope.Clock.SetUtcNow(new DateTimeOffset(2026, 9, 14, 12, 0, 0, TimeSpan.Zero));
        await scope.ConsumptionCatchUp.RunAsync(CancellationToken.None);
        (await StockAsync(scope, id)).Should().Be(before - 2m);
    }

    [Fact]
    public async Task A_scheduled_intake_still_replaces_the_days_consumption()
    {
        var scope = new ApplicationTestScope();
        var id = await SeedAsync(scope, new AdministrationSlotInput(1m, new TimeOnly(8, 0), null));
        await scope.ConsumptionCatchUp.RunAsync(CancellationToken.None);
        var before = await StockAsync(scope, id);

        await scope.RegisterIntake.ExecuteAsync(
            new RegisterIntakeCommand(id, Today, IntakeStatus.Taken, 1m), CancellationToken.None);
        scope.Clock.SetUtcNow(new DateTimeOffset(2026, 9, 14, 12, 0, 0, TimeSpan.Zero));
        await scope.ConsumptionCatchUp.RunAsync(CancellationToken.None);

        (await StockAsync(scope, id)).Should().Be(before - 1m);
    }

    [Fact]
    public async Task Only_a_taken_intake_can_be_extra()
    {
        var scope = new ApplicationTestScope();
        var id = await SeedAsync(scope);

        await FluentActions.Awaiting(() => scope.RegisterIntake.ExecuteAsync(
                new RegisterIntakeCommand(id, Today, IntakeStatus.Skipped, 1m, IsExtra: true), CancellationToken.None))
            .Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Medicine_with_only_as_needed_slots_is_never_consumed_automatically()
    {
        var scope = new ApplicationTestScope();
        var id = await SeedAsync(scope, new AdministrationSlotInput(1m, null, "Al bisogno", IsAsNeeded: true));

        await scope.ConsumptionCatchUp.RunAsync(CancellationToken.None);

        (await StockAsync(scope, id)).Should().Be(30m);
    }

    // Under PRN the slots only place a quantity PRN does not have
    // (DailyConsumption): the schedule change leaves them as they are.
    [Fact]
    public async Task Switching_to_prn_keeps_the_slots_and_stops_consumption_from_its_date()
    {
        var scope = new ApplicationTestScope();
        var id = await SeedAsync(scope,
            new AdministrationSlotInput(1m, new TimeOnly(8, 0), null),
            new AdministrationSlotInput(1m, null, "Al bisogno", IsAsNeeded: true));
        var sets = (await scope.Slots.ListSetsForMedicineAsync(id, CancellationToken.None)).Count;

        await scope.ChangeMedicationSchedule.ExecuteAsync(
            new ChangeMedicationScheduleCommand(id, 1m, 1, new DateOnly(2026, 9, 10), new PrnSchedule()),
            CancellationToken.None);

        (await scope.Slots.ListSetsForMedicineAsync(id, CancellationToken.None)).Should().HaveCount(sets);
        (await scope.Slots.ListForMedicineAsync(id, CancellationToken.None)).Should().HaveCount(2);
        await scope.ConsumptionCatchUp.RunAsync(CancellationToken.None);
        // Sep 1..9 consumed (9 days), nothing from Sep 10.
        (await StockAsync(scope, id)).Should().Be(21m);
    }

    [Fact]
    public async Task Switching_to_prn_after_saving_as_needed_slots_keeps_them()
    {
        // The edit dialog saves the slots first, then the schedule.
        var scope = new ApplicationTestScope();
        var id = await SeedAsync(scope, new AdministrationSlotInput(1m, new TimeOnly(8, 0), null));
        scope.Clock.SetUtcNow(new DateTimeOffset(2026, 9, 13, 12, 5, 0, TimeSpan.Zero));
        await scope.UpdateMedicine.ExecuteAsync(new UpdateMedicineCommand(
            id, "Enalapril", null, null, "compresse", 7, NotificationChannels.Windows, null, null, null, true,
            AdministrationSlots: [new AdministrationSlotInput(1m, null, "Al bisogno", IsAsNeeded: true)]),
            CancellationToken.None);

        await scope.ChangeMedicationSchedule.ExecuteAsync(
            new ChangeMedicationScheduleCommand(id, 1m, 1, Today, new PrnSchedule()), CancellationToken.None);

        (await scope.Slots.ListForMedicineAsync(id, CancellationToken.None)).Should().ContainSingle()
            .Which.IsAsNeeded.Should().BeTrue();
    }

    [Fact]
    public async Task A_prn_change_dated_later_keeps_consuming_until_then()
    {
        var scope = new ApplicationTestScope();
        var id = await SeedAsync(scope, new AdministrationSlotInput(1m, new TimeOnly(8, 0), null));

        await scope.ChangeMedicationSchedule.ExecuteAsync(
            new ChangeMedicationScheduleCommand(id, 1m, 1, new DateOnly(2026, 9, 20), new PrnSchedule()),
            CancellationToken.None);

        (await scope.Slots.ListForMedicineAsync(id, CancellationToken.None)).Should().ContainSingle();
        scope.Clock.SetUtcNow(new DateTimeOffset(2026, 9, 23, 12, 0, 0, TimeSpan.Zero));
        await scope.ConsumptionCatchUp.RunAsync(CancellationToken.None);
        // Sep 1..19 consumed (19 days), nothing from Sep 20.
        (await StockAsync(scope, id)).Should().Be(11m);
    }

    [Fact]
    public async Task Backfill_flags_as_needed_slots_from_today_and_keeps_past_days()
    {
        var scope = new ApplicationTestScope();
        var id = await SeedAsync(scope,
            new AdministrationSlotInput(1m, new TimeOnly(8, 0), null),
            new AdministrationSlotInput(1m, null, " al BISOGNO "));
        await scope.ConsumptionCatchUp.RunAsync(CancellationToken.None);
        var pastStock = await StockAsync(scope, id);
        pastStock.Should().Be(30m - 2m * 12, "both slots were consumed every day before the flag");

        scope.PendingMigrations.Pending.Add(AsNeededSlotBackfill.MigrationName);
        await scope.ConsumptionCatchUp.RunAsync(CancellationToken.None);

        var current = await scope.Slots.ListForMedicineAsync(id, CancellationToken.None);
        current.Select(s => s.IsAsNeeded).Should().Equal(false, true);
        var sets = await scope.Slots.ListSetsForMedicineAsync(id, CancellationToken.None);
        sets.Should().HaveCount(2);
        sets[^1].Set.EffectiveFrom.Should().Be(Today);
        (await StockAsync(scope, id)).Should().Be(pastStock, "past days are not rewritten");
        scope.PendingMigrations.Pending.Should().BeEmpty();

        scope.Clock.SetUtcNow(new DateTimeOffset(2026, 9, 14, 12, 0, 0, TimeSpan.Zero));
        await scope.ConsumptionCatchUp.RunAsync(CancellationToken.None);
        (await StockAsync(scope, id)).Should().Be(pastStock - 1m, "from today only the scheduled slot is consumed");
    }

    [Fact]
    public async Task Backfill_is_idempotent()
    {
        var scope = new ApplicationTestScope();
        var id = await SeedAsync(scope, new AdministrationSlotInput(1m, null, "Bei Bedarf"));

        scope.PendingMigrations.Pending.Add(AsNeededSlotBackfill.MigrationName);
        (await scope.AsNeededBackfill.RunAsync(CancellationToken.None)).Should().Be(1);
        var setId = (await scope.Slots.ListSetsForMedicineAsync(id, CancellationToken.None))[^1].Set.Id;

        scope.PendingMigrations.Pending.Add(AsNeededSlotBackfill.MigrationName);
        (await scope.AsNeededBackfill.RunAsync(CancellationToken.None)).Should().Be(0);

        var sets = await scope.Slots.ListSetsForMedicineAsync(id, CancellationToken.None);
        sets.Should().HaveCount(2);
        sets[^1].Set.Id.Should().Be(setId);
    }

    [Fact]
    public async Task Backfill_does_nothing_unless_pending()
    {
        var scope = new ApplicationTestScope();
        var id = await SeedAsync(scope, new AdministrationSlotInput(1m, null, "Al bisogno"));

        (await scope.AsNeededBackfill.RunAsync(CancellationToken.None)).Should().Be(0);

        (await scope.Slots.ListSetsForMedicineAsync(id, CancellationToken.None)).Should().HaveCount(1);
    }

    [Fact]
    public async Task Backfill_leaves_the_slots_of_a_prn_medicine()
    {
        var scope = new ApplicationTestScope();
        var id = await SeedAsync(scope,
            new AdministrationSlotInput(1m, new TimeOnly(8, 0), null),
            new AdministrationSlotInput(1m, null, "Dopo cena", IsAsNeeded: true));
        await scope.Schedules.AddAsync(new MedicationScheduleHistory
        {
            MedicineId = id, EffectiveFrom = new DateOnly(2026, 9, 5), DosePerAdministration = 1m,
            AdministrationsPerDay = 1, ScheduleKind = ScheduleKind.Prn,
        }, CancellationToken.None);

        scope.PendingMigrations.Pending.Add(AsNeededSlotBackfill.MigrationName);
        (await scope.AsNeededBackfill.RunAsync(CancellationToken.None)).Should().Be(0);

        (await scope.Slots.ListSetsForMedicineAsync(id, CancellationToken.None)).Should().ContainSingle();
    }

    [Fact]
    public async Task Backfill_leaves_other_medicines_alone()
    {
        var scope = new ApplicationTestScope();
        var id = await SeedAsync(scope, new AdministrationSlotInput(1m, new TimeOnly(8, 0), "Al mattino"));

        scope.PendingMigrations.Pending.Add(AsNeededSlotBackfill.MigrationName);
        (await scope.AsNeededBackfill.RunAsync(CancellationToken.None)).Should().Be(0);

        (await scope.Slots.ListSetsForMedicineAsync(id, CancellationToken.None)).Should().HaveCount(1);
        scope.PendingMigrations.Pending.Should().BeEmpty();
    }

    [Fact]
    public async Task Backfill_emits_a_version_9_slot_set_when_sync_is_enabled()
    {
        var scope = new ApplicationTestScope();
        var id = await SeedAsync(scope, new AdministrationSlotInput(1m, null, "Au besoin"));
        scope.EnableSync();
        var before = scope.SyncOperations.All.Count;

        scope.PendingMigrations.Pending.Add(AsNeededSlotBackfill.MigrationName);
        await scope.AsNeededBackfill.RunAsync(CancellationToken.None);

        var op = scope.SyncOperations.All.Skip(before).Should().ContainSingle().Subject;
        op.Type.Should().Be(nameof(SlotSetRecorded));
        op.SchemaVersion.Should().Be(9);
        op.MedicineId.Should().Be(id);
    }

    // The backfill recognizes the "As needed" preset in every UI
    // language, read from the dictionaries.
    [Theory]
    [InlineData("en")]
    [InlineData("it")]
    [InlineData("fr")]
    [InlineData("es")]
    [InlineData("de")]
    public void Backfill_labels_match_the_dictionaries(string language)
    {
        var label = new JsonDictionaryLocalizationService(language).Get("Ui.AdministrationSlotDialog.Preset.AsNeeded");

        var labels = new BuiltInPresetLabels(new JsonDictionaryLocalizationService("en"));

        labels.IsAsNeeded(label).Should().BeTrue();
        labels.IsAsNeeded(" " + label.ToUpperInvariant() + " ").Should().BeTrue();
        labels.IsAsNeeded("con il caffè").Should().BeFalse();
    }
}
