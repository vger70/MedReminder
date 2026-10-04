using FluentAssertions;
using MedReminder.Application.Abstractions;
using MedReminder.Application.Prescriptions;
using MedReminder.Application.Sync;
using MedReminder.Application.UseCases;
using MedReminder.Domain.Notifications;
using MedReminder.Domain.Prescriptions;
using MedReminder.Domain.Sync;
using MedReminder.Infrastructure.Persistence;
using MedReminder.Infrastructure.Sync;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MedReminder.Infrastructure.Tests.Sync;

// Prescriptions between two devices on real SQLite over a sync folder
// (docs/notes/EVOLUTION-PROPOSALS-2.md §3.2): a joining device takes them
// from the image, changes and deletions travel as PrescriptionChanged,
// and the reminders a device showed stay on that device.
public sealed class PrescriptionFolderSyncTests : IDisposable
{
    private const string Passphrase = "sync passphrase for tests";
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 10, 1);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "mr-rx-" + Guid.NewGuid().ToString("N"));
    private readonly List<SyncDevice> _devices = new();
    private Guid _medicine;

    public PrescriptionFolderSyncTests() => Directory.CreateDirectory(_root);

    private string Folder => Path.Combine(_root, "remote");

    public void Dispose()
    {
        foreach (var d in _devices) d.Dispose();
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public async Task Prescriptions_travel_with_the_image_and_the_operations()
    {
        var a = Track(new SyncDevice("A", Path.Combine(_root, "A.db"), Now, settings: null));
        await a.InitializeAsync();
        _medicine = await a.RunAsync(sp => sp.GetRequiredService<AddMedicine>().ExecuteAsync(new AddMedicineCommand(
            "Enalapril", "tablet", 1m, 2, new DateOnly(2026, 9, 1), 7, NotificationChannels.Windows,
            InitialQuantity: 60m), CancellationToken.None));
        var first = await SaveAsync(a, null, packages: 2);
        await a.RunAsync(async sp =>
        {
            await sp.GetRequiredService<IPrescriptionReminderEventRepository>().AddAsync(new PrescriptionReminderEvent
            {
                PrescriptionId = first, MedicineId = _medicine, ValidUntil = Today.AddDays(29), FiredAt = Now,
            }, CancellationToken.None);
            await sp.GetRequiredService<MedReminderDbContext>().SaveChangesAsync();
        });
        await a.RunAsync(sp => sp.GetRequiredService<CreateSyncGroup>().ExecuteAsync(
            new LocalFolderSyncTransport(Folder), Passphrase.ToCharArray(), SyncTarget.ForFolder(Folder),
            CancellationToken.None, SyncFileFormatTests.FastKdf));

        var b = await JoinAsync(a, "B");
        await b.SyncAsync();
        (await ListAsync(b)).Should().ContainSingle(p => p.Id == first && p.Packages == 2);
        (await RemindersAsync(b)).Should().Be(0, "reminders are not replicated");

        // A changes the first one and records a second; B deletes the first later.
        await SaveAsync(a, first, packages: 3);
        var second = await SaveAsync(a, null, packages: 1);
        await a.SyncAsync();
        await b.SyncAsync();
        (await ListAsync(b)).Should().HaveCount(2).And.Contain(p => p.Id == first && p.Packages == 3);

        b.Clock.Advance(TimeSpan.FromMinutes(5));
        await b.RunAsync(sp => sp.GetRequiredService<DeletePrescription>().ExecuteAsync(first, CancellationToken.None));
        await b.SyncAsync();
        await a.SyncAsync();

        (await ListAsync(a)).Select(p => p.Id).Should().Equal(second);
        (await ListAsync(b)).Select(p => p.Id).Should().Equal(second);
        (await SyncStateDescriber.DescribeAsync(a)).Should().Be(await SyncStateDescriber.DescribeAsync(b));
    }

    // Repeatable prescription: two devices record a dispensation before
    // syncing and both survive; a dispensation removed on one device is
    // removed on the other; the repeatable prescription travels with its
    // number of dispensations.
    [Fact]
    public async Task Dispensations_recorded_on_two_devices_both_survive()
    {
        var a = Track(new SyncDevice("A", Path.Combine(_root, "A.db"), Now, settings: null));
        await a.InitializeAsync();
        _medicine = await a.RunAsync(sp => sp.GetRequiredService<AddMedicine>().ExecuteAsync(new AddMedicineCommand(
            "Enalapril", "tablet", 1m, 2, new DateOnly(2026, 9, 1), 7, NotificationChannels.Windows,
            InitialQuantity: 60m), CancellationToken.None));
        await a.RunAsync(sp => sp.GetRequiredService<CreateSyncGroup>().ExecuteAsync(
            new LocalFolderSyncTransport(Folder), Passphrase.ToCharArray(), SyncTarget.ForFolder(Folder),
            CancellationToken.None, SyncFileFormatTests.FastKdf));
        var b = await JoinAsync(a, "B");

        var id = await a.RunAsync(sp => sp.GetRequiredService<SavePrescription>().ExecuteAsync(new SavePrescriptionCommand(
            null, _medicine, null, Today, "NRE-R", 1, PrescriptionRules.DefaultRepeatableValidUntil(Today), null,
            Dispensations: 12), CancellationToken.None));
        await a.SyncAsync();
        await b.SyncAsync();
        (await ListAsync(b)).Should().ContainSingle(p => p.Id == id && p.Dispensations == 12);

        await a.RunAsync(sp => sp.GetRequiredService<RecordDispensation>()
            .ExecuteAsync(id, Today, 1, CancellationToken.None));
        b.Clock.Advance(TimeSpan.FromMinutes(1));
        await b.RunAsync(sp => sp.GetRequiredService<RecordDispensation>()
            .ExecuteAsync(id, Today.AddDays(1), 1, CancellationToken.None));
        await a.SyncAsync();
        await b.SyncAsync();
        await a.SyncAsync();

        (await DispensationsAsync(a)).Should().HaveCount(2);
        (await DispensationsAsync(b)).Should().HaveCount(2);
        (await SyncStateDescriber.DescribeAsync(a)).Should().Be(await SyncStateDescriber.DescribeAsync(b));

        // B removes A's dispensation through the editor.
        var keep = (await DispensationsAsync(b)).Single(d => d.CollectedOn == Today.AddDays(1));
        var drop = (await DispensationsAsync(b)).Single(d => d.Id != keep.Id);
        b.Clock.Advance(TimeSpan.FromMinutes(1));
        await b.RunAsync(sp => sp.GetRequiredService<SavePrescription>().ExecuteAsync(new SavePrescriptionCommand(
            id, _medicine, null, Today, "NRE-R", 1, PrescriptionRules.DefaultRepeatableValidUntil(Today), null,
            Dispensations: 12, RemovedDispensations: [drop.Id]),
            CancellationToken.None));
        await b.SyncAsync();
        await a.SyncAsync();

        (await DispensationsAsync(a)).Select(d => d.Id).Should().Equal(keep.Id);
        (await SyncStateDescriber.DescribeAsync(a)).Should().Be(await SyncStateDescriber.DescribeAsync(b));
    }

    private static Task<List<PrescriptionDispensation>> DispensationsAsync(SyncDevice device)
        => device.RunAsync(sp => sp.GetRequiredService<MedReminderDbContext>().PrescriptionDispensations
            .AsNoTracking().ToListAsync());

    private Task<Guid> SaveAsync(SyncDevice device, Guid? id, int packages)
        => device.RunAsync(sp => sp.GetRequiredService<SavePrescription>().ExecuteAsync(new SavePrescriptionCommand(
            id, _medicine, Today, Today, "NRE-1", packages, Today.AddDays(29), null), CancellationToken.None));

    private static Task<List<Prescription>> ListAsync(SyncDevice device)
        => device.RunAsync(sp => sp.GetRequiredService<MedReminderDbContext>().Prescriptions.AsNoTracking().ToListAsync());

    private static Task<int> RemindersAsync(SyncDevice device)
        => device.RunAsync(sp => sp.GetRequiredService<MedReminderDbContext>().PrescriptionReminderEvents.CountAsync());

    private async Task<SyncDevice> JoinAsync(SyncDevice via, string name)
    {
        var path = Path.Combine(_root, $"{name}.db");
        var joined = await via.RunAsync(sp => sp.GetRequiredService<JoinSyncGroup>().ExecuteAsync(
            new LocalFolderSyncTransport(Folder), via.Settings.Load()!.GroupId, Passphrase.ToCharArray(), path,
            SyncTarget.ForFolder(Folder), CancellationToken.None));
        var device = Track(new SyncDevice(name, path, via.Clock.GetUtcNow(), joined.Settings, joined.Key));
        await device.InitializeAsync();
        return device;
    }

    private SyncDevice Track(SyncDevice device)
    {
        _devices.Add(device);
        return device;
    }
}
