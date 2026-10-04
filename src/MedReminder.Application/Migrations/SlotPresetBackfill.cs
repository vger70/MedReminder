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

    private readonly IPendingDataMigrations _migrations;
    private readonly IMedicineRepository _medicines;
    private readonly IMedicationAdministrationSlotRepository _slots;
    private readonly IUnitOfWork _uow;
    private readonly BuiltInPresetLabels _labels;

    public SlotPresetBackfill(
        IPendingDataMigrations migrations,
        IMedicineRepository medicines,
        IMedicationAdministrationSlotRepository slots,
        IUnitOfWork uow,
        ILocalizationService localization)
    {
        _migrations = migrations;
        _medicines = medicines;
        _slots = slots;
        _uow = uow;
        _labels = new BuiltInPresetLabels(localization);
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
                    if (slot.PresetId is null && _labels.PresetFor(slot.TimingLabel) is { } preset)
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
