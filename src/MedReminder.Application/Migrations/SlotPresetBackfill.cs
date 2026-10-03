using MedReminder.Application.Abstractions;
using MedReminder.Domain.Medicines;

namespace MedReminder.Application.Migrations;

// One-time link of existing slots to the built-in time-of-day presets
// (docs/analysis/ANALYSIS-INTRADAY-CONSUMPTION.md §6): before presets,
// the slot dialog stored the localized text of the preset the user
// picked. A slot without PresetId whose description is a built-in preset
// text in any UI language gets that preset. Display only, so the rows
// are updated in place. Typed descriptions stay without preset.
//
// Runs once, while the migration is pending; idempotent. The caller
// holds WriteGate.
public sealed class SlotPresetBackfill
{
    public const string MigrationName = "SlotPresets";

    // Text of "Ui.AdministrationSlotDialog.Preset.<Key>" in en, it, fr,
    // es, de (pinned against the dictionaries by a test).
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> LabelsByKey =
        new Dictionary<string, IReadOnlyList<string>>
        {
            ["Morning"] = ["Morning", "Al mattino", "Le matin", "Por la mañana", "Morgens"],
            ["MorningEmptyStomach"] =
                ["Morning on empty stomach", "Al mattino a stomaco vuoto", "Le matin à jeun", "Por la mañana en ayunas", "Morgens nüchtern"],
            ["BeforeBreakfast"] =
                ["Before breakfast", "Prima di colazione", "Avant le petit-déjeuner", "Antes del desayuno", "Vor dem Frühstück"],
            ["AfterBreakfast"] =
                ["After breakfast", "Dopo colazione", "Après le petit-déjeuner", "Después del desayuno", "Nach dem Frühstück"],
            ["MidMorning"] = ["Mid-morning", "A metà mattina", "En milieu de matinée", "A media mañana", "Am Vormittag"],
            ["BeforeLunch"] = ["Before lunch", "Prima di pranzo", "Avant le déjeuner", "Antes de comer", "Vor dem Mittagessen"],
            ["AfterLunch"] = ["After lunch", "Dopo pranzo", "Après le déjeuner", "Después de comer", "Nach dem Mittagessen"],
            ["Afternoon"] = ["In the afternoon", "Nel pomeriggio", "L'après-midi", "Por la tarde", "Am Nachmittag"],
            ["BeforeDinner"] = ["Before dinner", "Prima di cena", "Avant le dîner", "Antes de cenar", "Vor dem Abendessen"],
            ["AfterDinner"] = ["After dinner", "Dopo cena", "Après le dîner", "Después de cenar", "Nach dem Abendessen"],
            ["BeforeSleep"] = ["Before sleep", "Prima di dormire", "Avant de dormir", "Antes de dormir", "Vor dem Schlafen"],
            ["Night"] = ["During the night", "Durante la notte", "Pendant la nuit", "Durante la noche", "In der Nacht"],
            [BuiltInDoseTimePresets.AsNeededKey] = AsNeededSlotBackfill.AsNeededPresetLabels,
        };

    private readonly IPendingDataMigrations _migrations;
    private readonly IMedicineRepository _medicines;
    private readonly IMedicationAdministrationSlotRepository _slots;
    private readonly IUnitOfWork _uow;

    public SlotPresetBackfill(
        IPendingDataMigrations migrations,
        IMedicineRepository medicines,
        IMedicationAdministrationSlotRepository slots,
        IUnitOfWork uow)
    {
        _migrations = migrations;
        _medicines = medicines;
        _slots = slots;
        _uow = uow;
    }

    // Built-in preset whose text, in any language, is `label`.
    public static Guid? BuiltInPresetFor(string? label)
    {
        if (string.IsNullOrWhiteSpace(label)) return null;
        var trimmed = label.Trim();
        foreach (var (key, labels) in LabelsByKey)
        {
            if (labels.Any(l => string.Equals(l, trimmed, StringComparison.OrdinalIgnoreCase)))
                return BuiltInDoseTimePresets.IdOf(key);
        }
        return null;
    }

    // Returns the number of slots linked.
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        if (!await _migrations.IsPendingAsync(MigrationName, cancellationToken)) return 0;

        var links = new Dictionary<Guid, Guid>();
        foreach (var medicine in await _medicines.ListAllAsync(cancellationToken))
        {
            foreach (var entry in await _slots.ListSetsForMedicineAsync(medicine.Id, cancellationToken))
            {
                foreach (var slot in entry.Slots)
                {
                    if (slot.PresetId is null && BuiltInPresetFor(slot.TimingLabel) is { } preset)
                        links[slot.Id] = preset;
                }
            }
        }

        if (links.Count > 0)
        {
            await _slots.SetPresetIdsAsync(links, cancellationToken);
            await _uow.SaveChangesAsync(cancellationToken);
        }
        await _migrations.CompleteAsync(MigrationName, cancellationToken);
        return links.Count;
    }
}
