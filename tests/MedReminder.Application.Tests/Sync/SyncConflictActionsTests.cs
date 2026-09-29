using FluentAssertions;
using MedReminder.Application.Sync;
using MedReminder.Application.Tests.Support;
using MedReminder.Application.UseCases;
using MedReminder.Domain.Notifications;
using MedReminder.Domain.Sync;
using Xunit;

namespace MedReminder.Application.Tests.Sync;

// B.1 Phase 3d: conflict review actions (restore, dismiss) and the
// activity signal the desktop sync service listens to.
public class SyncConflictActionsTests
{
    private readonly ApplicationTestScope _a = new(new DateTimeOffset(2026, 9, 13, 12, 0, 0, TimeSpan.Zero));
    private readonly ApplicationTestScope _b = new(new DateTimeOffset(2026, 9, 13, 12, 0, 0, 500, TimeSpan.Zero));

    private async Task<Guid> ConflictOnNotesAsync()
    {
        var group = _a.EnableSync(Guid.Parse("0a000000-0000-0000-0000-000000000000"));
        _b.SyncSettingsStore.Save(group with { DeviceId = Guid.Parse("0b000000-0000-0000-0000-000000000000") });
        var id = await _a.AddMedicine.ExecuteAsync(new AddMedicineCommand(
            "Enalapril", "compresse", 1m, 1, new DateOnly(2026, 9, 1), 7, NotificationChannels.Windows), default);
        await Pull(_a, _b);
        await _a.UpdateMedicine.ExecuteAsync(Edit(id, "from A"), default);
        await _b.UpdateMedicine.ExecuteAsync(Edit(id, "from B"), default);
        await Pull(_a, _b);
        await Pull(_b, _a);
        return id;
    }

    private static UpdateMedicineCommand Edit(Guid id, string notes) => new(
        id, "Enalapril", null, null, "compresse", 7, NotificationChannels.Windows, null, null, notes, true);

    private static async Task Pull(ApplicationTestScope from, ApplicationTestScope to)
        => await to.ApplyRemote.ExecuteAsync(await from.SyncOperations.ListAllAsync(default), default);

    private static RestoreSyncConflict Restore(ApplicationTestScope s)
        => new(s.SyncConflicts, s.Medicines, s.Operations, s.Uow, s.Clock);

    [Fact]
    public async Task The_query_names_the_medicine_and_only_fields_can_be_restored()
    {
        await ConflictOnNotesAsync();

        var items = await new SyncConflictsQuery(_a.SyncConflicts, _a.Medicines).LoadAsync(default);

        items.Should().ContainSingle();
        items[0].MedicineName.Should().Be("Enalapril");
        items[0].CanRestore.Should().BeTrue();
    }

    [Fact]
    public async Task Restoring_the_lost_value_wins_everywhere_and_clears_the_conflict()
    {
        var id = await ConflictOnNotesAsync();
        var conflict = _a.SyncConflicts.All.Single();
        conflict.LosingValue.Should().Be("from A");

        _a.Clock.AdvanceBy(TimeSpan.FromMinutes(1));
        await Restore(_a).ExecuteAsync(conflict.Id, default);
        await Pull(_a, _b);

        (await _a.Medicines.GetAsync(id, default))!.Notes.Should().Be("from A");
        (await _b.Medicines.GetAsync(id, default))!.Notes.Should().Be("from A");
        _a.SyncConflicts.All.Should().BeEmpty();
        _b.SyncConflicts.All.Should().BeEmpty();
    }

    [Fact]
    public async Task A_hint_cannot_be_restored_and_dismiss_removes_it_locally()
    {
        await ConflictOnNotesAsync();
        var hint = new SyncConflict
        {
            Id = Guid.NewGuid(), Kind = SyncConflictKind.OverlappingSuspensions, MedicineId = Guid.NewGuid(),
            SubjectId = Guid.NewGuid(), DetectedAt = DateTimeOffset.UnixEpoch,
        };
        await _a.SyncConflicts.AddAsync(hint, default);

        await FluentActions.Awaiting(() => Restore(_a).ExecuteAsync(hint.Id, default))
            .Should().ThrowAsync<InvalidOperationException>();
        await new DismissSyncConflict(_a.SyncConflicts, _a.Uow).ExecuteAsync(hint.Id, default);

        _a.SyncConflicts.All.Should().NotContain(c => c.Id == hint.Id);
        _b.SyncConflicts.All.Should().ContainSingle();
    }

    [Fact]
    public async Task Local_operations_raise_the_activity_signal_only_while_sync_is_on()
    {
        var activity = new SyncActivity();
        var raised = 0;
        activity.LocalOperationsRecorded += (_, _) => raised++;
        var log = new OperationLog(_a.SyncSettingsStore, _a.SyncOperations, _a.Registers, _a.Clock, activity);
        var body = new MedicineFieldChanged(Guid.NewGuid(), "Notes", "x");

        await log.AppendAsync([body], default);
        raised.Should().Be(0);

        _a.EnableSync();
        await log.AppendAsync([body], default);
        raised.Should().Be(1);
    }
}
