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

    [Fact]
    public async Task Switching_to_prn_clears_the_slots_from_the_same_day()
    {
        var scope = new ApplicationTestScope();
        var id = await SeedAsync(scope, new AdministrationSlotInput(1m, new TimeOnly(8, 0), null));
        var from = new DateOnly(2026, 9, 10);

        await scope.ChangeMedicationSchedule.ExecuteAsync(
            new ChangeMedicationScheduleCommand(id, 1m, 1, from, new PrnSchedule()), CancellationToken.None);

        (await scope.Slots.ListForMedicineAsync(id, CancellationToken.None)).Should().BeEmpty();
        var sets = await scope.Slots.ListSetsForMedicineAsync(id, CancellationToken.None);
        sets[^1].Set.EffectiveFrom.Should().Be(from);

        await scope.ConsumptionCatchUp.RunAsync(CancellationToken.None);
        // Sep 1..9 consumed (9 days), nothing from Sep 10.
        (await StockAsync(scope, id)).Should().Be(21m);
    }

    [Fact]
    public async Task Switching_to_prn_without_slots_records_no_slot_set()
    {
        var scope = new ApplicationTestScope();
        var id = await SeedAsync(scope);

        await scope.ChangeMedicationSchedule.ExecuteAsync(
            new ChangeMedicationScheduleCommand(id, 1m, 1, Today, new PrnSchedule()), CancellationToken.None);

        (await scope.Slots.ListSetsForMedicineAsync(id, CancellationToken.None)).Should().BeEmpty();
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
    public async Task Backfill_clears_the_slots_of_a_prn_medicine()
    {
        var scope = new ApplicationTestScope();
        var id = await SeedAsync(scope, new AdministrationSlotInput(1m, new TimeOnly(8, 0), null));
        // A PRN row recorded directly, as the change-schedule dialog did
        // before it cleared the slots.
        await scope.Schedules.AddAsync(new MedicationScheduleHistory
        {
            MedicineId = id, EffectiveFrom = new DateOnly(2026, 9, 5), DosePerAdministration = 1m,
            AdministrationsPerDay = 1, ScheduleKind = ScheduleKind.Prn,
        }, CancellationToken.None);

        scope.PendingMigrations.Pending.Add(AsNeededSlotBackfill.MigrationName);
        (await scope.AsNeededBackfill.RunAsync(CancellationToken.None)).Should().Be(1);

        (await scope.Slots.ListForMedicineAsync(id, CancellationToken.None)).Should().BeEmpty();
        (await scope.Slots.ListSetsForMedicineAsync(id, CancellationToken.None))[^1].Set.EffectiveFrom
            .Should().Be(Today);
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
    // language: its list must match the shipped dictionaries.
    [Theory]
    [InlineData("en")]
    [InlineData("it")]
    [InlineData("fr")]
    [InlineData("es")]
    [InlineData("de")]
    public void Backfill_labels_match_the_dictionaries(string language)
    {
        var label = new JsonDictionaryLocalizationService(language).Get("Ui.AdministrationSlotDialog.Preset.AsNeeded");

        AsNeededSlotBackfill.AsNeededPresetLabels.Should().Contain(label);
        AsNeededSlotBackfill.IsAsNeededLabel(label.ToUpperInvariant()).Should().BeTrue();
    }
}
