using FluentAssertions;
using MedReminder.Application.Prescriptions;
using MedReminder.Application.Tests.Support;
using MedReminder.Application.UseCases;
using MedReminder.Domain.Notifications;
using Xunit;

namespace MedReminder.Application.Tests.Sync;

// Prescriptions between two devices of a sync group: one register per
// prescription, last writer wins, deletion included.
public class PrescriptionSyncTests
{
    private static readonly DateOnly Today = new(2026, 10, 1);
    private readonly ApplicationTestScope _a = new(new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero));
    private readonly ApplicationTestScope _b = new(new DateTimeOffset(2026, 10, 1, 9, 0, 0, 500, TimeSpan.Zero));
    private readonly Guid _medicine;

    public PrescriptionSyncTests()
    {
        var group = _a.EnableSync(Guid.Parse("0a000000-0000-0000-0000-000000000000"));
        _b.SyncSettingsStore.Save(group with { DeviceId = Guid.Parse("0b000000-0000-0000-0000-000000000000") });
        _medicine = _a.AddMedicine.ExecuteAsync(new AddMedicineCommand(
            Name: "Enalapril", Unit: "tablets", DosePerAdministration: 1m, AdministrationsPerDay: 1,
            StartDate: Today, ThresholdDays: 7, NotificationChannels: NotificationChannels.Windows, EndDate: null,
            InitialQuantity: 30m), default).GetAwaiter().GetResult();
    }

    private async Task ExchangeAsync()
    {
        await _b.ApplyRemote.ExecuteAsync(
            (await _a.SyncOperations.ListAllAsync(default)).Where(o => o.DeviceId != Device(_b)).ToList(), default);
        await _a.ApplyRemote.ExecuteAsync(
            (await _b.SyncOperations.ListAllAsync(default)).Where(o => o.DeviceId != Device(_a)).ToList(), default);
    }

    private static Guid Device(ApplicationTestScope scope) => scope.SyncSettingsStore.Load()!.DeviceId;

    private SavePrescriptionCommand Command(Guid? id, int packages, DateOnly? collected = null)
        => new(id, _medicine, Today, Today, "NRE-1", packages, Today.AddDays(29), collected);

    [Fact]
    public async Task A_prescription_reaches_the_other_device()
    {
        var id = await _a.SavePrescription.ExecuteAsync(Command(null, 2), default);

        await ExchangeAsync();

        var copy = _b.Prescriptions.All.Should().ContainSingle().Subject;
        copy.Id.Should().Be(id);
        copy.Code.Should().Be("NRE-1");
        copy.Packages.Should().Be(2);
        copy.ValidUntil.Should().Be(Today.AddDays(29));
    }

    [Fact]
    public async Task Concurrent_changes_end_with_the_later_one_on_both_devices()
    {
        var id = await _a.SavePrescription.ExecuteAsync(Command(null, 2), default);
        await ExchangeAsync();

        _a.Clock.AdvanceBy(TimeSpan.FromMinutes(1));
        _b.Clock.AdvanceBy(TimeSpan.FromMinutes(2));
        await _a.SavePrescription.ExecuteAsync(Command(id, 3), default);
        await _b.SavePrescription.ExecuteAsync(Command(id, 4, collected: Today), default);
        await ExchangeAsync();

        foreach (var scope in new[] { _a, _b })
        {
            var p = scope.Prescriptions.All.Should().ContainSingle().Subject;
            p.Packages.Should().Be(4);
            p.CollectedOn.Should().Be(Today);
        }
    }

    [Fact]
    public async Task A_deletion_reaches_the_other_device()
    {
        var id = await _a.SavePrescription.ExecuteAsync(Command(null, 2), default);
        await ExchangeAsync();

        _a.Clock.AdvanceBy(TimeSpan.FromMinutes(1));
        await _a.DeletePrescription.ExecuteAsync(id, default);
        await ExchangeAsync();

        _a.Prescriptions.All.Should().BeEmpty();
        _b.Prescriptions.All.Should().BeEmpty();
    }

    [Fact]
    public async Task An_older_change_arriving_after_a_deletion_does_not_bring_it_back()
    {
        var id = await _a.SavePrescription.ExecuteAsync(Command(null, 2), default);
        await ExchangeAsync();

        // B changes first, A deletes later; B's change reaches A after.
        _b.Clock.AdvanceBy(TimeSpan.FromMinutes(1));
        await _b.SavePrescription.ExecuteAsync(Command(id, 5), default);
        _a.Clock.AdvanceBy(TimeSpan.FromMinutes(2));
        await _a.DeletePrescription.ExecuteAsync(id, default);
        await ExchangeAsync();

        _a.Prescriptions.All.Should().BeEmpty();
        _b.Prescriptions.All.Should().BeEmpty();
    }
}
