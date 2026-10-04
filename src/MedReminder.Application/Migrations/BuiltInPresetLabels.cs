using MedReminder.Application.Abstractions;
using MedReminder.Domain.Medicines;

namespace MedReminder.Application.Migrations;

// Recognizes the text of a built-in time-of-day preset in any UI
// language. Before presets, the slot dialog stored the localized text
// of the preset the user picked, so the one-time backfills read every
// language's "Ui.AdministrationSlotDialog.Preset.<Key>" from the
// dictionaries (CLAUDE.md §2: translated text lives only there).
public sealed class BuiltInPresetLabels
{
    // Built on first use: the backfills need it only while pending.
    private readonly Lazy<Dictionary<string, Guid>> _presetByLabel;

    public BuiltInPresetLabels(ILocalizationService localization)
    {
        ArgumentNullException.ThrowIfNull(localization);
        _presetByLabel = new Lazy<Dictionary<string, Guid>>(() => Load(localization));
    }

    private static Dictionary<string, Guid> Load(ILocalizationService localization)
    {
        var map = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        foreach (var definition in BuiltInDoseTimePresets.All)
        {
            foreach (var language in SupportedLanguages.All)
            {
                var label = localization.GetIn(language.Code, "Ui.AdministrationSlotDialog.Preset." + definition.Key).Trim();
                if (label.Length > 0) map.TryAdd(label, definition.Id);
            }
        }
        return map;
    }

    // Built-in preset whose text, in any language, is `label`.
    public Guid? PresetFor(string? label)
        => !string.IsNullOrWhiteSpace(label) && _presetByLabel.Value.TryGetValue(label.Trim(), out var id) ? id : null;

    public bool IsAsNeeded(string? label)
        => PresetFor(label) == BuiltInDoseTimePresets.IdOf(BuiltInDoseTimePresets.AsNeededKey);
}
