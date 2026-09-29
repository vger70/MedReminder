using FluentAssertions;
using MedReminder.Application.Abstractions;
using MedReminder.Application.Monitoring;
using MedReminder.Application.Sync;
using MedReminder.Application.UseCases;
using MedReminder.Domain.Medicines;
using MedReminder.Domain.Notifications;
using MedReminder.Domain.Sync;
using MedReminder.Infrastructure.Persistence;
using MedReminder.Infrastructure.Tests.Sync;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MedReminder.Infrastructure.Tests.Persistence;

// DeleteMedicine on a real SQLite file with foreign keys enforced: every
// row referring to the medicine goes in one SaveChanges, in an order the
// Restrict constraints accept, and a second device applies the
// MedicineDeleted operation and skips later operations for the medicine.
public sealed class MedicineDeletionPersistenceTests : IDisposable
{
    private static readonly DateOnly Start = new(2026, 9, 1);
    private static readonly DateTimeOffset Now = new(2026, 9, 13, 9, 0, 0, TimeSpan.Zero);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "mr-del-" + Guid.NewGuid().ToString("N"));

    public MedicineDeletionPersistenceTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private static Task<Guid> AddAsync(SyncDevice device, string name, decimal initialQuantity = 0m)
        => device.RunAsync(sp => sp.GetRequiredService<AddMedicine>().ExecuteAsync(new AddMedicineCommand(
            name, "tablet", 1m, 2, Start, 7, NotificationChannels.Windows, InitialQuantity: initialQuantity,
            AdministrationSlots: [new AdministrationSlotInput(1m, new TimeOnly(8, 0), null),
                new AdministrationSlotInput(1m, new TimeOnly(20, 0), null)]), CancellationToken.None));

    private static Task<DeleteMedicineOutcome> DeleteAsync(SyncDevice device, Guid id)
        => device.RunAsync(sp => sp.GetRequiredService<DeleteMedicine>().ExecuteAsync(
            new DeleteMedicineCommand(id), CancellationToken.None));

    private static Task<T> QueryAsync<T>(SyncDevice device, Func<MedReminderDbContext, Task<T>> query)
        => device.RunAsync(sp => query(sp.GetRequiredService<MedReminderDbContext>()));

    [Fact]
    public async Task Every_row_of_the_medicine_is_removed_and_the_others_stay()
    {
        using var device = new SyncDevice("a", Path.Combine(_root, "a.db"), Now,
            new SyncSettings(Guid.NewGuid(), Guid.NewGuid(), 1));
        await device.InitializeAsync();
        var id = await AddAsync(device, "Mistake");
        var other = await AddAsync(device, "Kept", initialQuantity: 30m);
        await device.RunAsync(sp => sp.GetRequiredService<ChangeMedicationSchedule>().ExecuteAsync(
            new ChangeMedicationScheduleCommand(id, 2m, 1, new DateOnly(2026, 9, 10)), CancellationToken.None));
        await device.RunAsync(sp => sp.GetRequiredService<ConsumptionCatchUp>().RunAsync(CancellationToken.None));
        await device.RunAsync(sp => sp.GetRequiredService<DeactivateMedicine>().ExecuteAsync(
            new DeactivateMedicineCommand(id), CancellationToken.None));
        await device.RunAsync(async sp =>
        {
            var db = sp.GetRequiredService<MedReminderDbContext>();
            db.NotificationEvents.Add(new NotificationEvent
            {
                MedicineId = id, StockEpoch = 1, TriggeredAt = Now, Channel = NotificationChannels.Windows,
                DaysRemainingAtSend = 0, Success = true,
            });
            db.DoseReminderEvents.Add(new DoseReminderEvent
            {
                MedicineId = id, SlotKey = "08:00", LocalDate = new DateOnly(2026, 9, 13), FiredAt = Now,
                Channel = NotificationChannels.Windows,
            });
            await db.SaveChangesAsync();
        });
        (await QueryAsync(device, db => db.StockMovements.CountAsync(m => m.MedicineId == id))).Should().BeGreaterThan(0);

        (await DeleteAsync(device, id)).Should().Be(DeleteMedicineOutcome.Deleted);

        (await QueryAsync(device, db => db.Medicines.Select(m => m.Id).ToListAsync())).Should().Equal(other);
        (await QueryAsync(device, db => db.StockMovements.AnyAsync(m => m.MedicineId == id))).Should().BeFalse();
        (await QueryAsync(device, db => db.MedicationScheduleHistories.AnyAsync(m => m.MedicineId == id))).Should().BeFalse();
        (await QueryAsync(device, db => db.MedicationAdministrationSlotSets.AnyAsync(m => m.MedicineId == id))).Should().BeFalse();
        (await QueryAsync(device, db => db.MedicationAdministrationSlots.AnyAsync(m => m.MedicineId == id))).Should().BeFalse();
        (await QueryAsync(device, db => db.MedicineActivityChanges.AnyAsync(m => m.MedicineId == id))).Should().BeFalse();
        (await QueryAsync(device, db => db.NotificationEvents.AnyAsync(m => m.MedicineId == id))).Should().BeFalse();
        (await QueryAsync(device, db => db.DoseReminderEvents.AnyAsync(m => m.MedicineId == id))).Should().BeFalse();
        (await QueryAsync(device, db => db.SyncFieldVersions.AnyAsync(m => m.MedicineId == id))).Should().BeFalse();
        (await QueryAsync(device, db => db.SyncOperations.CountAsync(o => o.MedicineId == id && o.Type == "MedicineDeleted")))
            .Should().Be(1);
        (await QueryAsync(device, db => db.MedicationAdministrationSlots.AnyAsync(m => m.MedicineId == other))).Should().BeTrue();
    }

    [Fact]
    public async Task A_medicine_with_a_stock_entry_is_not_deleted()
    {
        using var device = new SyncDevice("a", Path.Combine(_root, "a.db"), Now, settings: null);
        await device.InitializeAsync();
        var id = await AddAsync(device, "Stocked", initialQuantity: 30m);

        (await DeleteAsync(device, id)).Should().Be(DeleteMedicineOutcome.HasRecordedFacts);

        (await QueryAsync(device, db => db.Medicines.AnyAsync(m => m.Id == id))).Should().BeTrue();
    }

    [Fact]
    public async Task The_other_device_applies_the_deletion_and_skips_later_operations()
    {
        var group = new SyncSettings(Guid.NewGuid(), Guid.NewGuid(), 1);
        using var a = new SyncDevice("a", Path.Combine(_root, "a.db"), Now, group);
        using var b = new SyncDevice("b", Path.Combine(_root, "b.db"), Now.AddSeconds(1), group with { DeviceId = Guid.NewGuid() });
        await a.InitializeAsync();
        await b.InitializeAsync();
        var id = await AddAsync(a, "Mistake");
        await PullAsync(a, b);

        await b.RunAsync(sp => sp.GetRequiredService<RegisterIntake>().ExecuteAsync(
            new RegisterIntakeCommand(id, new DateOnly(2026, 9, 12), IntakeStatus.Taken, 1m), CancellationToken.None));
        (await DeleteAsync(a, id)).Should().Be(DeleteMedicineOutcome.Deleted);

        var onA = await PullAsync(b, a);
        var onB = await PullAsync(a, b);

        onA.Applied.Should().Be(0);
        onB.TouchedMedicines.Should().NotContain(id);
        foreach (var device in new[] { a, b })
        {
            (await QueryAsync(device, db => db.Medicines.AnyAsync())).Should().BeFalse();
            (await QueryAsync(device, db => db.MedicationIntakes.AnyAsync())).Should().BeFalse();
            (await QueryAsync(device, db => db.StockMovements.AnyAsync())).Should().BeFalse();
        }
        (await PullAsync(b, a)).Applied.Should().Be(0);
    }

    private static async Task<ApplyRemoteResult> PullAsync(SyncDevice from, SyncDevice to)
    {
        var log = await from.RunAsync(sp => sp.GetRequiredService<ISyncOperationRepository>().ListAllAsync(CancellationToken.None));
        var result = await to.RunAsync(sp => sp.GetRequiredService<ApplyRemoteOperations>().ExecuteAsync(log, CancellationToken.None));
        result.Blocked.Should().BeNull();
        return result;
    }
}
