using FluentAssertions;
using MedReminder.Application.Notifications;
using MedReminder.Application.Tests.Support;
using MedReminder.Application.UseCases;
using MedReminder.Domain.Medicines;
using MedReminder.Domain.Notifications;
using MedReminder.Domain.Stock;
using Xunit;

namespace MedReminder.Application.Tests.Notifications;

// Backlog B0-02: the plan computed once must match what the desktop's
// polling shows over the same days. The harness plans at the start, then
// runs the desktop passes every 15 minutes (ConsumptionCatchUp,
// MedicationMonitor, DoseReminderService) and compares the device
// notifications they show with the plan: same kind, medicine and local
// day; the same low-stock stage; a dose reminder at its planned instant
// (the first pass after it). A daily notice is compared by day only: the
// desktop shows it at the first pass of the day, the plan at
// DailyNoticeTime.
public sealed class NotificationPlannerParityTests
{
    private static readonly TimeSpan Tick = TimeSpan.FromMinutes(15);
    private static readonly DateOnly Start = new(2026, 9, 13);
    private static readonly DateTimeOffset StartInstant = new(2026, 9, 13, 7, 0, 0, TimeSpan.Zero);

    private sealed record Shown(PlannedNotificationKind Kind, Guid MedicineId, DateOnly Day, int Stage,
        TimeOnly? SlotTime, DateTimeOffset At);

    [Fact]
    public async Task Low_stock_stages_and_dose_reminders_until_the_stock_runs_out()
    {
        // 20 units, 2 a day: stage 1 when 7 days remain, stage 2 at 3,
        // no dose reminder from the day the stock reaches zero.
        var scope = new ApplicationTestScope(StartInstant);
        await AddAsync(scope, quantity: 20m, slots: [new(1m, new TimeOnly(8, 0), null), new(1m, new TimeOnly(20, 0), null)]);

        var (planned, shown) = await RunAsync(scope, days: 12);

        AssertParity(planned, shown);
        planned.Where(p => p.Kind == PlannedNotificationKind.LowStock).Select(p => p.Stage).Should().Equal(1, 2);
        planned.Should().Contain(p => p.Kind == PlannedNotificationKind.DoseReminder);
    }

    [Fact]
    public async Task A_suspension_and_a_schedule_change_move_the_low_stock_days()
    {
        var scope = new ApplicationTestScope(StartInstant);
        var id = await AddAsync(scope, quantity: 30m, dose: 1m, perDay: 2, remindOnDose: false);
        await scope.SuspendMedication.ExecuteAsync(new SuspendMedicationCommand(id, Start.AddDays(2)), default);
        await scope.ResumeMedication.ExecuteAsync(new ResumeMedicationCommand(id, Start.AddDays(4)), default);
        await scope.ChangeMedicationSchedule.ExecuteAsync(
            new ChangeMedicationScheduleCommand(id, 1m, 3, Start.AddDays(6)), default);

        var (planned, shown) = await RunAsync(scope, days: 14);

        AssertParity(planned, shown);
        planned.Should().Contain(p => p.Kind == PlannedNotificationKind.LowStock);
    }

    [Fact]
    public async Task No_low_stock_notice_when_the_therapy_ends_before_the_run_out()
    {
        var scope = new ApplicationTestScope(StartInstant);
        await AddAsync(scope, quantity: 10m, dose: 1m, perDay: 1, remindOnDose: false, endDate: Start.AddDays(5));

        var (planned, shown) = await RunAsync(scope, days: 8);

        AssertParity(planned, shown);
        planned.Should().NotContain(p => p.Kind == PlannedNotificationKind.LowStock);
    }

    [Fact]
    public async Task Package_expiry_soon_then_expired_including_an_inactive_medicine()
    {
        var scope = new ApplicationTestScope(StartInstant);
        var active = await AddAsync(scope, quantity: 0m, dose: 1m, perDay: 1, remindOnDose: false, threshold: 0);
        await scope.AddStock.ExecuteAsync(new AddStockCommand(active, 60m, StockMovementKind.NewPackage,
            Packages: new NewPackagesInput(1, Start.AddDays(40), null, false, null)), default);
        // Inactive: it consumes nothing, so the second package, which 3 a
        // day would use up by day 10, still expires in the cabinet.
        var inactive = await AddAsync(scope, quantity: 0m, dose: 3m, perDay: 1, remindOnDose: false, threshold: 0);
        await scope.AddStock.ExecuteAsync(new AddStockCommand(inactive, 15m, StockMovementKind.NewPackage,
            Packages: new NewPackagesInput(1, Start.AddDays(3), null, false, null)), default);
        await scope.AddStock.ExecuteAsync(new AddStockCommand(inactive, 15m, StockMovementKind.NewPackage,
            Packages: new NewPackagesInput(1, Start.AddDays(10), null, false, null)), default);
        await scope.DeactivateMedicine.ExecuteAsync(new DeactivateMedicineCommand(inactive), default);

        var (planned, shown) = await RunAsync(scope, days: 14);

        AssertParity(planned, shown);
        planned.Where(p => p.Kind == PlannedNotificationKind.PackageExpiry && p.MedicineId == inactive)
            .Select(p => p.Stage)
            .Should().Equal(PackageExpiryNoticeEvent.SoonStage, PackageExpiryNoticeEvent.ExpiredStage,
                PackageExpiryNoticeEvent.ExpiredStage);
        planned.Should().Contain(p => p.Kind == PlannedNotificationKind.PackageExpiry && p.MedicineId == active);
        // The packages each notice names are the ones the desktop records
        // that day (the clock of this test is UTC).
        foreach (var notice in planned.Where(p => p.Kind == PlannedNotificationKind.PackageExpiry))
        {
            scope.PackageNoticeEvents.All
                .Where(e => e.MedicineId == notice.MedicineId && e.Stage == notice.Stage
                    && e.Channel == NotificationChannels.Windows
                    && DateOnly.FromDateTime(e.FiredAt.UtcDateTime) == notice.Day)
                .Select(e => e.PackageId)
                .Should().BeEquivalentTo(notice.PackageIds!);
        }
    }

    [Fact]
    public async Task An_as_needed_slot_and_a_medicine_without_device_channel_get_nothing()
    {
        var scope = new ApplicationTestScope(StartInstant);
        await AddAsync(scope, quantity: 30m,
            slots: [new(1m, new TimeOnly(9, 0), null), new(1m, new TimeOnly(15, 0), "PRN", IsAsNeeded: true)]);
        await AddAsync(scope, quantity: 4m, channels: NotificationChannels.Email,
            slots: [new(1m, new TimeOnly(9, 0), null)]);

        var (planned, shown) = await RunAsync(scope, days: 3);

        AssertParity(planned, shown);
        planned.Should().NotContain(p => p.SlotTime == new TimeOnly(15, 0));
    }

    [Fact]
    public async Task A_slot_reminded_and_a_day_booked_before_the_plan()
    {
        // At 08:10 the 08:00 reminder was already shown, and an intake
        // booked today: the plan neither repeats it nor subtracts today's
        // consumption twice.
        var scope = new ApplicationTestScope(new DateTimeOffset(2026, 9, 13, 8, 10, 0, TimeSpan.Zero));
        var id = await AddAsync(scope, quantity: 16m, threshold: 5,
            slots: [new(1m, new TimeOnly(8, 0), null), new(1m, new TimeOnly(8, 20), null), new(1m, new TimeOnly(20, 0), null)]);
        await scope.BuildDoseReminder().RunAsync(default);
        await scope.RegisterIntake.ExecuteAsync(new RegisterIntakeCommand(id, Start, IntakeStatus.Taken, 1m), default);
        scope.Windows.Targets.Clear();

        var (planned, shown) = await RunAsync(scope, days: 6);

        AssertParity(planned, shown);
        planned.Should().NotContain(p => p.Day == Start && p.SlotTime == new TimeOnly(8, 0));
        planned.Should().Contain(p => p.Day == Start && p.SlotTime == new TimeOnly(8, 20));
    }

    [Fact]
    public async Task Dose_reminders_across_the_end_of_daylight_saving_time()
    {
        // Central European time ends DST on 25 October 2026 at 03:00.
        var zone = CentralEurope();
        var scope = new ApplicationTestScope(new DateTimeOffset(2026, 10, 23, 22, 0, 0, TimeSpan.Zero), zone);
        await AddAsync(scope, quantity: 40m, start: new DateOnly(2026, 10, 23),
            slots: [new(1m, new TimeOnly(2, 30), null), new(1m, new TimeOnly(9, 0), null)]);

        var (planned, shown) = await RunAsync(scope, days: 4);

        AssertParity(planned, shown);
        var autumn = planned.Single(p => p.Day == new DateOnly(2026, 10, 25) && p.SlotTime == new TimeOnly(9, 0));
        autumn.FireAt.Offset.Should().Be(TimeSpan.FromHours(1));
    }

    [Fact]
    public async Task Dose_reminders_across_the_start_of_daylight_saving_time()
    {
        // Central European time starts DST on 28 March 2027 at 02:00:
        // 02:30 does not exist that day.
        var zone = CentralEurope();
        var scope = new ApplicationTestScope(new DateTimeOffset(2027, 3, 26, 22, 0, 0, TimeSpan.Zero), zone);
        await AddAsync(scope, quantity: 40m, start: new DateOnly(2027, 3, 26),
            slots: [new(1m, new TimeOnly(2, 30), null), new(1m, new TimeOnly(9, 0), null)]);

        var (planned, shown) = await RunAsync(scope, days: 4);

        AssertParity(planned, shown);
        planned.Single(p => p.Day == new DateOnly(2027, 3, 28) && p.SlotTime == new TimeOnly(9, 0))
            .FireAt.Offset.Should().Be(TimeSpan.FromHours(2));
    }

    // ---- harness ----

    private static async Task<(IReadOnlyList<PlannedNotification> Planned, IReadOnlyList<Shown> Shown)> RunAsync(
        ApplicationTestScope scope, int days)
    {
        await scope.ConsumptionCatchUp.RunAsync(default);
        var zone = scope.Clock.LocalTimeZone;
        var startLocal = TimeZoneInfo.ConvertTime(scope.Clock.GetUtcNow(), zone);
        var firstDay = DateOnly.FromDateTime(startLocal.DateTime);
        // Plan and passes cover the same span: from now to the end of the
        // last day.
        var lastLocal = firstDay.AddDays(days).ToDateTime(TimeOnly.MinValue);
        var end = new DateTimeOffset(lastLocal, zone.GetUtcOffset(lastLocal));
        var options = new NotificationPlanOptions
        {
            DailyHorizonDays = days,
            DoseWindow = end - startLocal - TimeSpan.FromTicks(1),
        };
        var planned = await scope.BuildPlanLoader().PlanAsync(options, default);
        var dose = scope.BuildDoseReminder();
        var shown = new List<Shown>();
        var seenEvents = scope.Notifications.All.Count;
        while (scope.Clock.GetUtcNow() < end)
        {
            await scope.ConsumptionCatchUp.RunAsync(default);
            var before = scope.Windows.Targets.Count;
            await scope.Monitor.RunAsync(default);
            await dose.RunAsync(default);

            var at = TimeZoneInfo.ConvertTime(scope.Clock.GetUtcNow(), zone);
            var day = DateOnly.FromDateTime(at.DateTime);
            var events = scope.Notifications.All.Skip(seenEvents).ToList();
            seenEvents += events.Count;
            foreach (var target in scope.Windows.Targets.Skip(before))
            {
                var kind = target.Kind switch
                {
                    NotificationKind.DoseReminder => PlannedNotificationKind.DoseReminder,
                    NotificationKind.LowStock => PlannedNotificationKind.LowStock,
                    NotificationKind.PackageExpiry => PlannedNotificationKind.PackageExpiry,
                    _ => throw new InvalidOperationException($"Unexpected notification {target.Kind}."),
                };
                var stage = kind == PlannedNotificationKind.LowStock
                    ? events.Single(e => e.MedicineId == target.MedicineId).Stage
                    : 0;
                shown.Add(new Shown(kind, target.MedicineId, day, stage, target.SlotTime, at));
            }
            scope.Clock.AdvanceBy(Tick);
        }
        return (planned, shown);
    }

    private static void AssertParity(IReadOnlyList<PlannedNotification> planned, IReadOnlyList<Shown> shown)
    {
        static string Describe(PlannedNotificationKind kind, Guid medicine, DateOnly day, int stage, TimeOnly? slot)
            => $"{kind} {medicine:N} {day:yyyy-MM-dd} stage {stage} slot {slot}";

        planned.Select(p => Describe(p.Kind, p.MedicineId, p.Day,
                p.Kind == PlannedNotificationKind.LowStock ? p.Stage : 0, p.SlotTime))
            .Should().BeEquivalentTo(shown.Select(s => Describe(s.Kind, s.MedicineId, s.Day, s.Stage, s.SlotTime)));

        foreach (var dose in planned.Where(p => p.Kind == PlannedNotificationKind.DoseReminder))
        {
            var match = shown.Single(s => s.Kind == PlannedNotificationKind.DoseReminder
                && s.MedicineId == dose.MedicineId && s.Day == dose.Day && s.SlotTime == dose.SlotTime);
            match.At.Should().BeOnOrAfter(dose.FireAt).And.BeBefore(dose.FireAt + Tick);
        }
    }

    private static Task<Guid> AddAsync(
        ApplicationTestScope scope,
        decimal quantity,
        decimal dose = 1m,
        int perDay = 2,
        int threshold = 7,
        bool remindOnDose = true,
        NotificationChannels channels = NotificationChannels.Windows,
        DateOnly? start = null,
        DateOnly? endDate = null,
        IReadOnlyList<AdministrationSlotInput>? slots = null)
        => scope.AddMedicine.ExecuteAsync(new AddMedicineCommand(
            Name: "Enalapril",
            Unit: "tablets",
            DosePerAdministration: dose,
            AdministrationsPerDay: perDay,
            StartDate: start ?? Start,
            ThresholdDays: threshold,
            NotificationChannels: channels,
            EndDate: endDate,
            InitialQuantity: quantity,
            AdministrationSlots: slots,
            RemindOnDose: remindOnDose && slots is not null), default);

    // UTC+1, DST from the last Sunday of March at 02:00 to the last
    // Sunday of October at 03:00: built here so the tests do not depend
    // on the machine's time zone database.
    private static TimeZoneInfo CentralEurope()
    {
        var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
            DateTime.MinValue.Date, DateTime.MaxValue.Date, TimeSpan.FromHours(1),
            TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 2, 0, 0), 3, 5, DayOfWeek.Sunday),
            TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 3, 0, 0), 10, 5, DayOfWeek.Sunday));
        return TimeZoneInfo.CreateCustomTimeZone("Test/CentralEurope", TimeSpan.FromHours(1), "Test CET", "Test CET",
            "Test CEST", [rule]);
    }
}
