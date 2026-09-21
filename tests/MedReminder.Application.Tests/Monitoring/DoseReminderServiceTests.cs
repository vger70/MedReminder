using FluentAssertions;
using MedReminder.Application.Tests.Support;
using MedReminder.Application.UseCases;
using MedReminder.Domain.Notifications;
using Xunit;

namespace MedReminder.Application.Tests.Monitoring;

// A5 dose-time reminder. The fake clock's LocalTimeZone is UTC, so the
// local wall-clock anchoring in DoseReminderService collapses to UTC —
// enough to exercise the fire / dedup / grace-window / gate logic.
public class DoseReminderServiceTests
{
    private static readonly DateOnly Today = new(2026, 9, 13);
    private static readonly TimeOnly SlotTime = new(9, 0);

    // Clock positioned 5 minutes after the 09:00 slot, inside the
    // default 30-minute grace window.
    private static DateTimeOffset JustAfterSlot =>
        new(2026, 9, 13, 9, 5, 0, TimeSpan.Zero);

    private static async Task<Guid> SeedAsync(
        ApplicationTestScope scope,
        bool remindOnDose = true,
        decimal initialQuantity = 30m,
        NotificationChannels channels = NotificationChannels.Windows,
        TimeOnly? slotTime = null)
    {
        var cmd = new AddMedicineCommand(
            Name: "Enalapril",
            Unit: "compresse",
            DosePerAdministration: 1m,
            AdministrationsPerDay: 1,
            StartDate: Today,
            ThresholdDays: 7,
            NotificationChannels: channels,
            InitialQuantity: initialQuantity,
            AdministrationSlots: new[]
            {
                new AdministrationSlotInput(Dose: 1m, Time: slotTime ?? SlotTime, TimingLabel: null),
            },
            RemindOnDose: remindOnDose);
        return await scope.AddMedicine.ExecuteAsync(cmd, CancellationToken.None);
    }

    [Fact]
    public async Task Fires_once_at_slot_time()
    {
        var scope = new ApplicationTestScope(JustAfterSlot);
        var id = await SeedAsync(scope);

        var result = await scope.BuildDoseReminder().RunAsync(CancellationToken.None);

        result.FiredCount.Should().Be(1);
        scope.Windows.Sent.Should().ContainSingle();
        scope.Email.Sent.Should().BeEmpty();

        var events = scope.DoseEvents.All;
        events.Should().ContainSingle();
        events[0].MedicineId.Should().Be(id);
        events[0].LocalDate.Should().Be(Today);
        events[0].Channel.Should().Be(NotificationChannels.Windows);
    }

    [Fact]
    public async Task Does_not_fire_before_slot_time()
    {
        var scope = new ApplicationTestScope(new DateTimeOffset(2026, 9, 13, 8, 0, 0, TimeSpan.Zero));
        _ = await SeedAsync(scope);

        var result = await scope.BuildDoseReminder().RunAsync(CancellationToken.None);

        result.FiredCount.Should().Be(0);
        scope.Windows.Sent.Should().BeEmpty();
        scope.DoseEvents.All.Should().BeEmpty();
    }

    [Fact]
    public async Task Second_tick_same_day_does_not_duplicate()
    {
        var scope = new ApplicationTestScope(JustAfterSlot);
        _ = await SeedAsync(scope);
        var service = scope.BuildDoseReminder();

        await service.RunAsync(CancellationToken.None);
        var second = await service.RunAsync(CancellationToken.None);

        second.FiredCount.Should().Be(0);
        scope.Windows.Sent.Should().ContainSingle();
        scope.DoseEvents.All.Should().ContainSingle();
    }

    [Fact]
    public async Task Dedup_survives_service_restart()
    {
        var scope = new ApplicationTestScope(JustAfterSlot);
        _ = await SeedAsync(scope);

        // First service instance fires.
        await scope.BuildDoseReminder().RunAsync(CancellationToken.None);
        // A fresh instance (simulated restart) shares the persisted
        // dedup table and must not re-fire the same slot.
        var second = await scope.BuildDoseReminder().RunAsync(CancellationToken.None);

        second.FiredCount.Should().Be(0);
        scope.Windows.Sent.Should().ContainSingle();
        scope.DoseEvents.All.Should().ContainSingle();
    }

    [Fact]
    public async Task Missed_dose_beyond_grace_window_is_dropped()
    {
        // Slot at 09:00, clock at 10:00 — one hour late, past the
        // 30-minute grace window.
        var scope = new ApplicationTestScope(new DateTimeOffset(2026, 9, 13, 10, 0, 0, TimeSpan.Zero));
        _ = await SeedAsync(scope);

        var result = await scope.BuildDoseReminder(TimeSpan.FromMinutes(30))
            .RunAsync(CancellationToken.None);

        result.FiredCount.Should().Be(0);
        scope.Windows.Sent.Should().BeEmpty();
        // No dedup row: a missed slot must not be recorded (ANALYSIS-A5 §4.3).
        scope.DoseEvents.All.Should().BeEmpty();
    }

    [Fact]
    public async Task Zero_stock_is_hard_off()
    {
        var scope = new ApplicationTestScope(JustAfterSlot);
        _ = await SeedAsync(scope, initialQuantity: 0m);

        var result = await scope.BuildDoseReminder().RunAsync(CancellationToken.None);

        result.FiredCount.Should().Be(0);
        scope.Windows.Sent.Should().BeEmpty();
        scope.DoseEvents.All.Should().BeEmpty();
    }

    [Fact]
    public async Task Suspended_medicine_does_not_fire()
    {
        var scope = new ApplicationTestScope(JustAfterSlot);
        var id = await SeedAsync(scope);
        await scope.SuspendMedication.ExecuteAsync(
            new SuspendMedicationCommand(id, Today), CancellationToken.None);

        var result = await scope.BuildDoseReminder().RunAsync(CancellationToken.None);

        result.FiredCount.Should().Be(0);
        scope.Windows.Sent.Should().BeEmpty();
        scope.DoseEvents.All.Should().BeEmpty();
    }

    [Fact]
    public async Task Medicine_without_opt_in_is_ignored()
    {
        var scope = new ApplicationTestScope(JustAfterSlot);
        _ = await SeedAsync(scope, remindOnDose: false);

        var result = await scope.BuildDoseReminder().RunAsync(CancellationToken.None);

        result.FiredCount.Should().Be(0);
        scope.Windows.Sent.Should().BeEmpty();
        scope.DoseEvents.All.Should().BeEmpty();
    }

    [Fact]
    public async Task Email_channel_dispatches_email_only()
    {
        var scope = new ApplicationTestScope(JustAfterSlot);
        _ = await SeedAsync(scope, channels: NotificationChannels.Email);

        var result = await scope.BuildDoseReminder().RunAsync(CancellationToken.None);

        result.FiredCount.Should().Be(1);
        scope.Email.Sent.Should().ContainSingle();
        scope.Windows.Sent.Should().BeEmpty();
        scope.DoseEvents.All.Single().Channel.Should().Be(NotificationChannels.Email);
    }

    [Fact]
    public async Task Both_channels_dispatch_both()
    {
        var scope = new ApplicationTestScope(JustAfterSlot);
        _ = await SeedAsync(
            scope, channels: NotificationChannels.Windows | NotificationChannels.Email);

        var result = await scope.BuildDoseReminder().RunAsync(CancellationToken.None);

        result.FiredCount.Should().Be(1);
        scope.Windows.Sent.Should().ContainSingle();
        scope.Email.Sent.Should().ContainSingle();
        scope.DoseEvents.All.Single().Channel
            .Should().Be(NotificationChannels.Windows | NotificationChannels.Email);
    }

    [Fact]
    public async Task No_dedup_row_when_dispatch_fails_so_next_tick_retries()
    {
        var scope = new ApplicationTestScope(JustAfterSlot);
        _ = await SeedAsync(scope);
        scope.Windows.ShouldFail = true;
        var service = scope.BuildDoseReminder();

        var first = await service.RunAsync(CancellationToken.None);

        first.FiredCount.Should().Be(0);
        scope.DoseEvents.All.Should().BeEmpty();

        // Recover: the next tick within the grace window retries and fires.
        scope.Windows.ShouldFail = false;
        var second = await service.RunAsync(CancellationToken.None);

        second.FiredCount.Should().Be(1);
        scope.Windows.Sent.Should().ContainSingle();
        scope.DoseEvents.All.Should().ContainSingle();
    }
}
