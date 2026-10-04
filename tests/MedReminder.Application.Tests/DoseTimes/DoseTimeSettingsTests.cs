using FluentAssertions;
using MedReminder.Application.DoseTimes;
using MedReminder.Application.Migrations;
using MedReminder.Application.Tests.Support;
using MedReminder.Application.UseCases;
using MedReminder.Domain.Medicines;
using MedReminder.Domain.Notifications;
using Xunit;

namespace MedReminder.Application.Tests.DoseTimes;

// Time-of-day presets (docs/analysis/ANALYSIS-INTRADAY-CONSUMPTION.md §6).
public class DoseTimeSettingsTests
{
    private static readonly Guid Morning = BuiltInDoseTimePresets.IdOf("Morning");

    [Fact]
    public void Without_stored_rows_the_built_ins_apply()
    {
        var settings = DoseTimeSettings.Merge([], []);

        settings.Presets.Select(p => p.BuiltInKey).Should().Equal(BuiltInDoseTimePresets.All.Select(d => d.Key));
        settings.Find(Morning)!.Time.Should().Be(new TimeOnly(8, 0));
        settings.Defaults[2].Should().Equal(new TimeOnly(8, 0), new TimeOnly(20, 0));
    }

    [Fact]
    public void Stored_rows_override_built_ins_and_add_user_presets()
    {
        var stored = new[]
        {
            new DoseTimePreset { Id = Morning, BuiltInKey = "Morning", Time = new TimeOnly(7, 15) },
            new DoseTimePreset { Id = BuiltInDoseTimePresets.IdOf("Night"), BuiltInKey = "Night", Time = new TimeOnly(23, 30), IsHidden = true },
            new DoseTimePreset { Label = "Dopo la palestra", Time = new TimeOnly(18, 0), Order = 0 },
        };
        var defaults = new[] { new DoseTimeDefault { AdministrationsPerDay = 2, Times = "07:00;19:00" } };

        var settings = DoseTimeSettings.Merge(stored, defaults);

        settings.Find(Morning)!.Time.Should().Be(new TimeOnly(7, 15));
        settings.Find(BuiltInDoseTimePresets.IdOf("Night"))!.IsHidden.Should().BeTrue();
        settings.Presets[^1].Label.Should().Be("Dopo la palestra");
        settings.Defaults[2].Should().Equal(new TimeOnly(7, 0), new TimeOnly(19, 0));
        settings.Defaults[1].Should().Equal(new TimeOnly(8, 0));
    }

    [Fact]
    public void By_time_of_day_places_user_presets_among_the_built_ins()
    {
        var stored = new[]
        {
            new DoseTimePreset { Label = "A pranzo", Time = new TimeOnly(13, 0), Order = 0 },
            new DoseTimePreset { Label = "Senza orario", Time = null, Order = 1 },
        };

        var names = DoseTimeSettings.ByTimeOfDay(DoseTimeSettings.Merge(stored, []).Presets)
            .Select(p => p.BuiltInKey ?? p.Label)
            .ToList();

        // Same time: built-in order first, then the user preset.
        names.IndexOf("A pranzo").Should().Be(names.IndexOf("BeforeLunch") + 1);
        names.IndexOf("MorningEmptyStomach").Should().BeLessThan(names.IndexOf("Morning"));
        names.Should().EndWith([BuiltInDoseTimePresets.AsNeededKey, "Senza orario"]);
    }

    [Fact]
    public async Task Save_stores_only_what_differs_from_the_built_ins()
    {
        var scope = new ApplicationTestScope();
        var settings = DoseTimeSettings.BuiltIn;
        var custom = new EffectiveDoseTimePreset(Guid.NewGuid(), null, " Dopo la palestra ", new TimeOnly(18, 0), false, 99, false);
        var changed = settings with
        {
            Presets =
            [
                .. settings.Presets.Select(p => p.Id == Morning ? p with { Time = new TimeOnly(7, 0) } : p),
                custom,
            ],
            Defaults = new Dictionary<int, IReadOnlyList<TimeOnly>>(settings.Defaults)
            {
                [1] = [new TimeOnly(9, 0)],
            },
        };

        await new SaveDoseTimeSettings(scope.DoseTimePresets, scope.Uow).ExecuteAsync(changed, default);

        scope.DoseTimePresets.Presets.Should().HaveCount(2);
        scope.DoseTimePresets.Presets.Single(p => p.BuiltInKey == "Morning").Time.Should().Be(new TimeOnly(7, 0));
        scope.DoseTimePresets.Presets.Single(p => p.BuiltInKey is null).Label.Should().Be("Dopo la palestra");
        scope.DoseTimePresets.Defaults.Should().ContainSingle(d => d.AdministrationsPerDay == 1 && d.Times == "09:00");

        var reloaded = await new DoseTimeSettingsQuery(scope.DoseTimePresets).LoadAsync(default);
        reloaded.Find(Morning)!.Time.Should().Be(new TimeOnly(7, 0));
        reloaded.Find(custom.Id)!.Label.Should().Be("Dopo la palestra");
        reloaded.Defaults[1].Should().Equal(new TimeOnly(9, 0));
    }

    [Fact]
    public async Task A_user_preset_needs_a_label()
    {
        var scope = new ApplicationTestScope();
        var settings = DoseTimeSettings.BuiltIn with
        {
            Presets = [.. DoseTimeSettings.BuiltIn.Presets, new EffectiveDoseTimePreset(Guid.NewGuid(), null, " ", null, false, 0, false)],
        };

        await FluentActions.Awaiting(() => new SaveDoseTimeSettings(scope.DoseTimePresets, scope.Uow)
                .ExecuteAsync(settings, default))
            .Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Slots_keep_the_preset_they_were_saved_with()
    {
        var scope = new ApplicationTestScope();
        var id = await scope.AddMedicine.ExecuteAsync(new AddMedicineCommand(
            "Enalapril", "compresse", 1m, 1, new DateOnly(2026, 9, 1), 7, NotificationChannels.Windows,
            AdministrationSlots: [new AdministrationSlotInput(1m, null, "Al mattino", PresetId: Morning)]), default);

        (await scope.Slots.ListForMedicineAsync(id, default)).Single().PresetId.Should().Be(Morning);
    }

    [Fact]
    public async Task Backfill_links_built_in_descriptions_in_any_language()
    {
        var scope = new ApplicationTestScope();
        var id = await scope.AddMedicine.ExecuteAsync(new AddMedicineCommand(
            "Enalapril", "compresse", 1m, 1, new DateOnly(2026, 9, 1), 7, NotificationChannels.Windows,
            AdministrationSlots:
            [
                new AdministrationSlotInput(1m, null, "al mattino"),
                new AdministrationSlotInput(1m, null, "Before lunch"),
                new AdministrationSlotInput(1m, null, "con il caffè"),
            ]), default);

        scope.PendingMigrations.Pending.Add(SlotPresetBackfill.MigrationName);
        (await scope.PresetBackfill.RunAsync(default)).Should().Be(2);
        (await scope.PresetBackfill.RunAsync(default)).Should().Be(0, "it runs once");

        var slots = await scope.Slots.ListForMedicineAsync(id, default);
        slots.Select(s => s.PresetId).Should().Equal(Morning, BuiltInDoseTimePresets.IdOf("BeforeLunch"), null);
    }

    [Fact]
    public async Task The_as_needed_backfill_keeps_the_preset_of_the_slots_it_copies()
    {
        var scope = new ApplicationTestScope();
        var asNeeded = BuiltInDoseTimePresets.IdOf(BuiltInDoseTimePresets.AsNeededKey);
        var id = await scope.AddMedicine.ExecuteAsync(new AddMedicineCommand(
            "Enalapril", "compresse", 1m, 1, new DateOnly(2026, 9, 1), 7, NotificationChannels.Windows,
            AdministrationSlots:
            [
                new AdministrationSlotInput(1m, null, "Al mattino", PresetId: Morning),
                new AdministrationSlotInput(1m, null, "Al bisogno", PresetId: asNeeded),
            ]), default);

        scope.PendingMigrations.Pending.Add(AsNeededSlotBackfill.MigrationName);
        await scope.AsNeededBackfill.RunAsync(default);

        var slots = await scope.Slots.ListForMedicineAsync(id, default);
        slots.Select(s => s.PresetId).Should().Equal(Morning, asNeeded);
        slots.Select(s => s.IsAsNeeded).Should().Equal(false, true);
    }

    // The backfill recognizes every built-in in every UI language, read
    // from the dictionaries whatever the current language.
    [Theory]
    [InlineData("en")]
    [InlineData("it")]
    [InlineData("fr")]
    [InlineData("es")]
    [InlineData("de")]
    public void Backfill_labels_match_the_dictionaries(string language)
    {
        var loc = new JsonDictionaryLocalizationService(language);
        var labels = new BuiltInPresetLabels(new JsonDictionaryLocalizationService("en"));

        foreach (var definition in BuiltInDoseTimePresets.All)
        {
            var label = loc.Get("Ui.AdministrationSlotDialog.Preset." + definition.Key);
            labels.PresetFor(label).Should().Be(definition.Id, $"{language}: {label}");
        }
    }
}
