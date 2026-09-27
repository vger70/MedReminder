using FluentAssertions;
using MedReminder.Application.Tests.Support;
using MedReminder.Application.UseCases;
using MedReminder.Domain.Notifications;
using Xunit;

namespace MedReminder.Application.Tests.UseCases;

// B.1 Phase 2b: slot changes append a dated slot set instead of
// replacing the rows (docs/analysis/ANALYSIS-B1-MOBILE-SYNC.md §4.2).
// The current slots, which every calculation reads, stay those of the
// latest set, so behavior is unchanged.
public class AdministrationSlotSetTests
{
    // Clock of ApplicationTestScope: 2026-09-13 12:00 UTC, local = UTC.
    private static readonly DateOnly Today = new(2026, 9, 13);
    private static readonly DateOnly StartDate = new(2026, 9, 20);

    private readonly ApplicationTestScope _scope = new();

    private Task<Guid> AddAsync(IReadOnlyList<AdministrationSlotInput>? slots)
        => _scope.AddMedicine.ExecuteAsync(
            new AddMedicineCommand(
                "Enalapril", "compresse", 1m, 2,
                StartDate, 7,
                NotificationChannels.Windows,
                InitialQuantity: 30m,
                AdministrationSlots: slots),
            CancellationToken.None);

    private Task UpdateAsync(Guid id, IReadOnlyList<AdministrationSlotInput>? slots)
        => _scope.UpdateMedicine.ExecuteAsync(
            new UpdateMedicineCommand(
                id, "Enalapril", null, null, "compresse", 7,
                NotificationChannels.Windows, null, null, null, IsActive: true,
                AdministrationSlots: slots),
            CancellationToken.None);

    private static AdministrationSlotInput Slot(decimal dose, int hour)
        => new(dose, new TimeOnly(hour, 0), null);

    [Fact]
    public async Task AddMedicine_records_the_first_set_from_the_start_date()
    {
        var id = await AddAsync([Slot(1m, 8), Slot(0.5m, 20)]);

        var set = _scope.Slots.Sets.Should().ContainSingle().Subject;
        set.MedicineId.Should().Be(id);
        set.EffectiveFrom.Should().Be(StartDate);
        set.RecordedAt.Should().Be(_scope.Clock.GetUtcNow());

        var current = await _scope.Slots.ListForMedicineAsync(id, CancellationToken.None);
        current.Select(s => s.Dose).Should().Equal(1m, 0.5m);
        current.Should().OnlyContain(s => s.SetId == set.Id);
    }

    [Fact]
    public async Task AddMedicine_without_slots_records_no_set()
    {
        await AddAsync(null);
        await AddAsync([]);

        _scope.Slots.Sets.Should().BeEmpty();
    }

    [Fact]
    public async Task UpdateMedicine_appends_a_set_effective_today_and_keeps_history()
    {
        var id = await AddAsync([Slot(1m, 8)]);
        _scope.Clock.AdvanceBy(TimeSpan.FromMinutes(5));

        await UpdateAsync(id, [Slot(2m, 9), Slot(1m, 21)]);

        _scope.Slots.Sets.Should().HaveCount(2);
        var latest = _scope.Slots.Sets[1];
        latest.EffectiveFrom.Should().Be(Today);
        latest.RecordedAt.Should().Be(_scope.Clock.GetUtcNow());
        _scope.Slots.All.Should().HaveCount(3, "the first set's row is kept as history");

        var current = await _scope.Slots.ListForMedicineAsync(id, CancellationToken.None);
        current.Select(s => s.Dose).Should().Equal(2m, 1m);
        current.Select(s => s.Order).Should().Equal(0, 1);
    }

    [Fact]
    public async Task Clearing_the_slots_records_an_empty_set()
    {
        var id = await AddAsync([Slot(1m, 8)]);
        _scope.Clock.AdvanceBy(TimeSpan.FromMinutes(5));

        await UpdateAsync(id, []);

        _scope.Slots.Sets.Should().HaveCount(2);
        (await _scope.Slots.ListForMedicineAsync(id, CancellationToken.None)).Should().BeEmpty();
    }

    [Fact]
    public async Task Null_slots_leave_the_history_alone()
    {
        var id = await AddAsync([Slot(1m, 8)]);

        await UpdateAsync(id, null);

        _scope.Slots.Sets.Should().ContainSingle();
    }

    [Fact]
    public async Task Unchanged_slots_still_get_new_ids()
    {
        // The slot id is the dose-reminder dedup key; the former
        // delete + insert gave every save new ids.
        var id = await AddAsync([Slot(1m, 8)]);
        var before = (await _scope.Slots.ListForMedicineAsync(id, CancellationToken.None)).Single().Id;
        _scope.Clock.AdvanceBy(TimeSpan.FromMinutes(5));

        await UpdateAsync(id, [Slot(1m, 8)]);

        var after = (await _scope.Slots.ListForMedicineAsync(id, CancellationToken.None)).Single().Id;
        after.Should().NotBe(before);
    }
}
