using FluentAssertions;
using MedReminder.Application.Tests.Support;
using MedReminder.Application.UseCases;
using MedReminder.Domain.Medicines;
using MedReminder.Domain.Notifications;
using MedReminder.Domain.Stock;
using Xunit;

namespace MedReminder.Application.Tests.UseCases;

public class AddMedicineTests
{
    private static AddMedicineCommand ValidCommand(decimal initialQuantity = 30m) =>
        new(
            Name: "Enalapril",
            Unit: "compresse",
            DosePerAdministration: 1m,
            AdministrationsPerDay: 2,
            StartDate: new DateOnly(2026, 9, 1),
            ThresholdDays: 7,
            NotificationChannels: NotificationChannels.Windows,
            InitialQuantity: initialQuantity);

    [Fact]
    public async Task Creates_medicine_schedule_and_initial_load()
    {
        var scope = new ApplicationTestScope();

        var id = await scope.AddMedicine.ExecuteAsync(ValidCommand(30m), CancellationToken.None);

        var medicine = await scope.Medicines.GetAsync(id, CancellationToken.None);
        medicine.Should().NotBeNull();
        medicine!.Name.Should().Be("Enalapril");
        medicine.StockEpoch.Should().Be(1);

        var schedule = await scope.Schedules.ListForMedicineAsync(id, CancellationToken.None);
        schedule.Should().ContainSingle().Which.DosePerAdministration.Should().Be(1m);

        var movements = await scope.Stock.ListForMedicineAsync(id, CancellationToken.None);
        movements.Should().ContainSingle();
        movements[0].Kind.Should().Be(StockMovementKind.InitialLoad);
        movements[0].QuantityDelta.Should().Be(30m);
        movements[0].StockEpoch.Should().Be(1);
    }

    [Fact]
    public async Task Initial_quantity_zero_produces_no_stock_movement()
    {
        var scope = new ApplicationTestScope();

        var id = await scope.AddMedicine.ExecuteAsync(ValidCommand(0m), CancellationToken.None);

        var movements = await scope.Stock.ListForMedicineAsync(id, CancellationToken.None);
        movements.Should().BeEmpty();
    }

    [Fact]
    public async Task Rejects_empty_name()
    {
        var scope = new ApplicationTestScope();
        var cmd = ValidCommand() with { Name = "  " };

        await FluentActions
            .Awaiting(() => scope.AddMedicine.ExecuteAsync(cmd, CancellationToken.None))
            .Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Rejects_non_positive_dose()
    {
        var scope = new ApplicationTestScope();
        var cmd = ValidCommand() with { DosePerAdministration = 0m };

        await FluentActions
            .Awaiting(() => scope.AddMedicine.ExecuteAsync(cmd, CancellationToken.None))
            .Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Rejects_end_date_before_start_date()
    {
        var scope = new ApplicationTestScope();
        var cmd = ValidCommand() with
        {
            StartDate = new DateOnly(2026, 9, 10),
            EndDate = new DateOnly(2026, 9, 1),
        };

        await FluentActions
            .Awaiting(() => scope.AddMedicine.ExecuteAsync(cmd, CancellationToken.None))
            .Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Rejects_negative_initial_quantity()
    {
        var scope = new ApplicationTestScope();
        var cmd = ValidCommand(-1m);

        await FluentActions
            .Awaiting(() => scope.AddMedicine.ExecuteAsync(cmd, CancellationToken.None))
            .Should().ThrowAsync<ArgumentException>();
    }

    // ---------- A1 InitialSchedule ---------------------------------

    [Fact]
    public async Task Persists_schedule_kind_and_payload_when_initial_schedule_is_supplied()
    {
        var scope = new ApplicationTestScope();
        var cyclic = new CyclicSchedule(onDays: 21, offDays: 7, quantityPerOnDay: 1m);
        var cmd = ValidCommand() with
        {
            DosePerAdministration = 0.75m,   // display value, not projection input
            AdministrationsPerDay = 1,
            InitialSchedule = cyclic,
        };

        var id = await scope.AddMedicine.ExecuteAsync(cmd, CancellationToken.None);

        var schedule = await scope.Schedules.ListForMedicineAsync(id, CancellationToken.None);
        var entry = schedule.Should().ContainSingle().Subject;
        entry.ScheduleKind.Should().Be(ScheduleKind.Cyclic);
        entry.SchedulePayload.Should().NotBeNullOrWhiteSpace();

        // Round-trip via the codec must yield the same value object.
        var round = ScheduleCodec.Deserialize(
            entry.ScheduleKind,
            entry.SchedulePayload,
            entry.DosePerAdministration,
            entry.AdministrationsPerDay);
        round.Should().Be(cyclic);
    }

    [Fact]
    public async Task Prn_schedule_accepts_zero_display_dose()
    {
        var scope = new ApplicationTestScope();
        var cmd = ValidCommand() with
        {
            DosePerAdministration = 0m,   // PRN display convention
            AdministrationsPerDay = 1,
            InitialSchedule = new PrnSchedule(),
        };

        var id = await scope.AddMedicine.ExecuteAsync(cmd, CancellationToken.None);

        var schedule = await scope.Schedules.ListForMedicineAsync(id, CancellationToken.None);
        schedule.Should().ContainSingle()
            .Which.ScheduleKind.Should().Be(ScheduleKind.Prn);
    }

    [Fact]
    public async Task Legacy_command_still_persists_fixed_daily_with_null_payload()
    {
        var scope = new ApplicationTestScope();
        var id = await scope.AddMedicine.ExecuteAsync(ValidCommand(), CancellationToken.None);

        var entry = (await scope.Schedules.ListForMedicineAsync(id, CancellationToken.None))
            .Should().ContainSingle().Subject;
        entry.ScheduleKind.Should().Be(ScheduleKind.FixedDaily);
        entry.SchedulePayload.Should().BeNull();
    }
}
