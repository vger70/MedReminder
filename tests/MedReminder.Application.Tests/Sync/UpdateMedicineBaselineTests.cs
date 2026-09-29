using FluentAssertions;
using MedReminder.Application.Tests.Support;
using MedReminder.Application.UseCases;
using MedReminder.Domain.Catalogue;
using MedReminder.Domain.Notifications;
using Xunit;

namespace MedReminder.Application.Tests.Sync;

// B.1 Phase 3a, stale forms (docs/analysis/ANALYSIS-B1-MOBILE-SYNC.md
// §7.4): with a baseline, UpdateMedicine writes only what the user
// changed in the form.
public class UpdateMedicineBaselineTests
{
    private readonly ApplicationTestScope _scope = new();

    private static readonly IReadOnlyList<AdministrationSlotInput> Slots =
        [new AdministrationSlotInput(1m, new TimeOnly(8, 0), null)];

    private async Task<Guid> SeedAsync()
        => await _scope.AddMedicine.ExecuteAsync(
            new AddMedicineCommand(
                "Enalapril", "compresse", 1m, 1, new DateOnly(2026, 9, 1), 7, NotificationChannels.Windows,
                Notes: "with food", DoctorName: "Rossi", InitialQuantity: 30m, AdministrationSlots: Slots),
            CancellationToken.None);

    private static UpdateMedicineCommand Form(Guid id, string? notes = "with food", string? doctor = "Rossi",
        IReadOnlyList<AdministrationSlotInput>? slots = null, bool isActive = true)
        => new(id, "Enalapril", null, null, "compresse", 7, NotificationChannels.Windows, null, doctor, notes,
            isActive, AdministrationSlots: slots ?? Slots, Catalogue: new CatalogueLink(null, null, null));

    [Fact]
    public async Task Fields_the_user_did_not_touch_keep_a_value_changed_after_the_form_opened()
    {
        var id = await SeedAsync();
        var baseline = Form(id);
        // Another writer (a sync apply, from Phase 3b) changes two fields
        // while the form is open.
        var stored = await _scope.Medicines.GetAsync(id, default);
        stored!.DoctorName = "Bianchi";
        stored.NationalCode = "023921048";
        stored.AtcCode = AtcCode.Parse("C09AA02");

        await _scope.UpdateMedicine.ExecuteAsync(Form(id, notes: "after meals") with { Baseline = baseline }, default);

        var after = await _scope.Medicines.GetAsync(id, default);
        after!.Notes.Should().Be("after meals");
        after.DoctorName.Should().Be("Bianchi");
        after.NationalCode.Should().Be("023921048");
        after.AtcCode.Should().Be(AtcCode.Parse("C09AA02"));
    }

    [Fact]
    public async Task Without_a_baseline_every_field_is_written()
    {
        var id = await SeedAsync();
        var stored = await _scope.Medicines.GetAsync(id, default);
        stored!.DoctorName = "Bianchi";

        await _scope.UpdateMedicine.ExecuteAsync(Form(id), default);

        (await _scope.Medicines.GetAsync(id, default))!.DoctorName.Should().Be("Rossi");
    }

    [Fact]
    public async Task Unchanged_slots_record_no_new_set_with_a_baseline()
    {
        var id = await SeedAsync();
        var before = (await _scope.Slots.ListForMedicineAsync(id, default)).Select(s => s.Id).ToList();

        await _scope.UpdateMedicine.ExecuteAsync(Form(id, notes: "x") with { Baseline = Form(id) }, default);

        (await _scope.Slots.ListForMedicineAsync(id, default)).Select(s => s.Id).Should().Equal(before);
    }

    [Fact]
    public async Task Changed_slots_record_a_new_set_with_a_baseline()
    {
        var id = await SeedAsync();
        IReadOnlyList<AdministrationSlotInput> twice =
            [new AdministrationSlotInput(1m, new TimeOnly(8, 0), null), new AdministrationSlotInput(1m, new TimeOnly(20, 0), null)];
        // A set recorded at the same instant as the seed one would tie.
        _scope.Clock.AdvanceBy(TimeSpan.FromMinutes(1));

        await _scope.UpdateMedicine.ExecuteAsync(Form(id, slots: twice) with { Baseline = Form(id) }, default);

        (await _scope.Slots.ListForMedicineAsync(id, default)).Should().HaveCount(2);
    }

    [Fact]
    public async Task Activity_follows_the_baseline_rule()
    {
        var id = await SeedAsync();
        var baseline = Form(id);
        await _scope.DeactivateMedicine.ExecuteAsync(new DeactivateMedicineCommand(id), default);

        // The form still shows "active" and the user edits only the notes.
        await _scope.UpdateMedicine.ExecuteAsync(Form(id, notes: "x") with { Baseline = baseline }, default);

        (await _scope.Medicines.GetAsync(id, default))!.IsActive.Should().BeFalse();
    }
}
