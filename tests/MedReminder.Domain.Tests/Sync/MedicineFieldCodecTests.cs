using FluentAssertions;
using MedReminder.Domain.Catalogue;
using MedReminder.Domain.Medicines;
using MedReminder.Domain.Notifications;
using MedReminder.Domain.Sync;
using Xunit;

namespace MedReminder.Domain.Tests.Sync;

public class MedicineFieldCodecTests
{
    private static Medicine Full() => new()
    {
        Name = "Enalapril",
        ActiveIngredient = "enalapril maleate",
        Package = "28 tablets",
        Unit = "tablet",
        ThresholdDays = 9,
        DoctorName = "Dr. Rossi",
        Notes = "with food",
        NotificationChannels = NotificationChannels.Windows | NotificationChannels.Email,
        RemindOnDose = true,
        NationalCode = "023921048",
        AtcCode = AtcCode.Parse("C09AA02"),
        LinkedReferenceMedicineId = Guid.Parse("11111111-2222-3333-4444-555555555555"),
        EndDate = new DateOnly(2026, 12, 31),
        StartDate = new DateOnly(2026, 1, 1),
    };

    [Fact]
    public void Field_list_is_the_replicated_set_of_the_analysis()
        => MedicineFieldCodec.FieldNames.Should().Equal(
            "Name", "ActiveIngredient", "Package", "Unit", "ThresholdDays", "DoctorName", "Notes",
            "NotificationChannels", "RemindOnDose", "NationalCode", "AtcCode",
            "LinkedReferenceMedicineId", "EndDate");

    [Fact]
    public void Text_form_is_culture_invariant()
    {
        var snapshot = MedicineFieldCodec.Snapshot(Full()).ToDictionary(v => v.Field, v => v.Value);

        snapshot["ThresholdDays"].Should().Be("9");
        snapshot["NotificationChannels"].Should().Be(((int)(NotificationChannels.Windows | NotificationChannels.Email)).ToString());
        snapshot["RemindOnDose"].Should().Be("true");
        snapshot["AtcCode"].Should().Be("C09AA02");
        snapshot["LinkedReferenceMedicineId"].Should().Be("11111111-2222-3333-4444-555555555555");
        snapshot["EndDate"].Should().Be("2026-12-31");
    }

    [Fact]
    public void Every_field_round_trips()
    {
        var source = Full();
        var target = new Medicine { Name = "x", Unit = "y" };

        foreach (var value in MedicineFieldCodec.Snapshot(source))
        {
            MedicineFieldCodec.Set(target, value.Field, value.Value);
        }

        MedicineFieldCodec.Snapshot(target).Should().Equal(MedicineFieldCodec.Snapshot(source));
    }

    [Fact]
    public void Nullable_fields_round_trip_null()
    {
        var target = Full();
        foreach (var field in new[] { "ActiveIngredient", "Package", "DoctorName", "Notes", "NationalCode",
                     "AtcCode", "LinkedReferenceMedicineId", "EndDate" })
        {
            MedicineFieldCodec.Set(target, field, null);
            MedicineFieldCodec.Get(target, field).Should().BeNull(field);
        }
    }

    [Fact]
    public void Diff_lists_only_changed_fields_in_order()
    {
        var medicine = Full();
        var before = MedicineFieldCodec.Snapshot(medicine);
        medicine.Notes = null;
        medicine.Name = "Enalapril 5";

        MedicineFieldCodec.Diff(before, medicine).Should().Equal(
            new MedicineFieldValue("Name", "Enalapril 5"),
            new MedicineFieldValue("Notes", null));
    }

    [Fact]
    public void Unknown_field_and_bad_values_throw()
    {
        var medicine = Full();

        FluentActions.Invoking(() => MedicineFieldCodec.Set(medicine, "IsActive", "true"))
            .Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => MedicineFieldCodec.Set(medicine, "Name", null))
            .Should().Throw<FormatException>();
        FluentActions.Invoking(() => MedicineFieldCodec.Set(medicine, "RemindOnDose", "True"))
            .Should().Throw<FormatException>();
    }
}
