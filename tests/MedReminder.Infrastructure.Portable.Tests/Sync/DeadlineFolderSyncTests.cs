using FluentAssertions;
using MedReminder.Application.Abstractions;
using MedReminder.Application.Deadlines;
using MedReminder.Application.Sync;
using MedReminder.Application.UseCases;
using MedReminder.Domain.Deadlines;
using MedReminder.Domain.Notifications;
using MedReminder.Domain.Sync;
using MedReminder.Infrastructure.Persistence;
using MedReminder.Infrastructure.Sync;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MedReminder.Infrastructure.Tests.Sync;

// Deadlines between two devices on real SQLite over a sync folder
// (docs/notes/EVOLUTION-PROPOSALS-2.md §3.6): a joining device takes them
// from the image, changes and deletions travel as DeadlineChanged, and
// the reminders a device showed stay on that device.
public sealed class DeadlineFolderSyncTests : IDisposable
{
    private const string Passphrase = "sync passphrase for tests";
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 10, 1);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "mr-dl-" + Guid.NewGuid().ToString("N"));
    private readonly List<SyncDevice> _devices = new();
    private Guid _medicine;

    public DeadlineFolderSyncTests() => Directory.CreateDirectory(_root);

    private string Folder => Path.Combine(_root, "remote");

    public void Dispose()
    {
        foreach (var d in _devices) d.Dispose();
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public async Task Deadlines_travel_with_the_image_and_the_operations()
    {
        var a = Track(new SyncDevice("A", Path.Combine(_root, "A.db"), Now, settings: null));
        await a.InitializeAsync();
        _medicine = await a.RunAsync(sp => sp.GetRequiredService<AddMedicine>().ExecuteAsync(new AddMedicineCommand(
            "Enalapril", "tablet", 1m, 2, new DateOnly(2026, 9, 1), 7, NotificationChannels.Windows,
            InitialQuantity: 60m), CancellationToken.None));
        var plan = await SaveAsync(a, null, _medicine, leadDays: 30);
        await a.RunAsync(async sp =>
        {
            await sp.GetRequiredService<IDeadlineReminderEventRepository>().AddAsync(new DeadlineReminderEvent
            {
                DeadlineId = plan, MedicineId = _medicine, DueOn = Today.AddDays(60), FiredAt = Now,
            }, CancellationToken.None);
            await sp.GetRequiredService<MedReminderDbContext>().SaveChangesAsync();
        });
        await a.RunAsync(sp => sp.GetRequiredService<CreateSyncGroup>().ExecuteAsync(
            new LocalFolderSyncTransport(Folder), Passphrase.ToCharArray(), SyncTarget.ForFolder(Folder),
            CancellationToken.None, SyncFileFormatTests.FastKdf));

        var b = await JoinAsync(a, "B");
        await b.SyncAsync();
        (await ListAsync(b)).Should().ContainSingle(d => d.Id == plan && d.LeadDays == 30 && d.MedicineId == _medicine);
        (await RemindersAsync(b)).Should().Be(0, "reminders are not replicated");

        // A changes the plan and records a deadline of the profile; B deletes the plan later.
        await SaveAsync(a, plan, _medicine, leadDays: 20);
        var renewal = await SaveAsync(a, null, null, leadDays: 10);
        await a.SyncAsync();
        await b.SyncAsync();
        (await ListAsync(b)).Should().HaveCount(2)
            .And.Contain(d => d.Id == plan && d.LeadDays == 20)
            .And.Contain(d => d.Id == renewal && d.MedicineId == null);

        b.Clock.Advance(TimeSpan.FromMinutes(5));
        await b.RunAsync(sp => sp.GetRequiredService<DeleteDeadline>().ExecuteAsync(plan, CancellationToken.None));
        await b.SyncAsync();
        await a.SyncAsync();

        (await ListAsync(a)).Select(d => d.Id).Should().Equal(renewal);
        (await ListAsync(b)).Select(d => d.Id).Should().Equal(renewal);
        (await SyncStateDescriber.DescribeAsync(a)).Should().Be(await SyncStateDescriber.DescribeAsync(b));
    }

    private static Task<Guid> SaveAsync(SyncDevice device, Guid? id, Guid? medicine, int leadDays)
        => device.RunAsync(sp => sp.GetRequiredService<SaveDeadline>().ExecuteAsync(new SaveDeadlineCommand(
            id, medicine, medicine is null ? DeadlineKind.ExemptionRenewal : DeadlineKind.TherapeuticPlan, null,
            Today.AddDays(60), leadDays, 12, NotificationChannels.Both, null), CancellationToken.None));

    private static Task<List<Deadline>> ListAsync(SyncDevice device)
        => device.RunAsync(sp => sp.GetRequiredService<MedReminderDbContext>().Deadlines.AsNoTracking().ToListAsync());

    private static Task<int> RemindersAsync(SyncDevice device)
        => device.RunAsync(sp => sp.GetRequiredService<MedReminderDbContext>().DeadlineReminderEvents.CountAsync());

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
