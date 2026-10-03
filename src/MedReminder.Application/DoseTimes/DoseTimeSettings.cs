using MedReminder.Application.Abstractions;
using MedReminder.Domain.Medicines;

namespace MedReminder.Application.DoseTimes;

// One preset as the user sees it: a built-in with its stored overrides,
// or a preset the user added. Label is null for a built-in (the UI
// localizes BuiltInKey).
public sealed record EffectiveDoseTimePreset(
    Guid Id,
    string? BuiltInKey,
    string? Label,
    TimeOnly? Time,
    bool IsAsNeeded,
    int Order,
    bool IsHidden)
{
    public bool IsBuiltIn => BuiltInKey is not null;
}

// The time-of-day settings of the profile: presets in display order and
// the default times of medicines without slots
// (docs/analysis/ANALYSIS-INTRADAY-CONSUMPTION.md §4.2, §6).
public sealed record DoseTimeSettings(
    IReadOnlyList<EffectiveDoseTimePreset> Presets,
    IReadOnlyDictionary<int, IReadOnlyList<TimeOnly>> Defaults)
{
    public static DoseTimeSettings BuiltIn { get; } = Merge([], []);

    public EffectiveDoseTimePreset? Find(Guid id) => Presets.FirstOrDefault(p => p.Id == id);

    // Built-ins first in their code order, then the presets the user
    // added in the order they were added.
    public static DoseTimeSettings Merge(
        IReadOnlyList<DoseTimePreset> stored, IReadOnlyList<DoseTimeDefault> storedDefaults)
    {
        var byId = stored.ToDictionary(p => p.Id);
        var presets = new List<EffectiveDoseTimePreset>();
        for (var i = 0; i < BuiltInDoseTimePresets.All.Count; i++)
        {
            var d = BuiltInDoseTimePresets.All[i];
            presets.Add(byId.TryGetValue(d.Id, out var row)
                ? new EffectiveDoseTimePreset(d.Id, d.Key, null, row.Time, d.IsAsNeeded, i, row.IsHidden)
                : new EffectiveDoseTimePreset(d.Id, d.Key, null, d.Time, d.IsAsNeeded, i, false));
        }
        var builtInCount = BuiltInDoseTimePresets.All.Count;
        presets.AddRange(stored
            .Where(p => p.BuiltInKey is null && !string.IsNullOrWhiteSpace(p.Label))
            .Select(p => new EffectiveDoseTimePreset(
                p.Id, null, p.Label, p.Time, p.IsAsNeeded, builtInCount + p.Order, p.IsHidden)));

        var defaults = new Dictionary<int, IReadOnlyList<TimeOnly>>(DoseTimeDefault.BuiltIn);
        foreach (var row in storedDefaults)
        {
            if (row.AdministrationsPerDay is < 1 or > DoseTimeDefault.MaxAdministrations) continue;
            if (DoseTimeDefault.Parse(row.Times, row.AdministrationsPerDay) is { } times)
                defaults[row.AdministrationsPerDay] = times;
        }

        return new DoseTimeSettings([.. presets.OrderBy(p => p.Order)], defaults);
    }
}

public sealed class DoseTimeSettingsQuery
{
    private readonly IDoseTimePresetRepository _presets;

    public DoseTimeSettingsQuery(IDoseTimePresetRepository presets)
    {
        _presets = presets;
    }

    public async Task<DoseTimeSettings> LoadAsync(CancellationToken cancellationToken)
        => DoseTimeSettings.Merge(
            await _presets.ListPresetsAsync(cancellationToken),
            await _presets.ListDefaultsAsync(cancellationToken));
}

// Saves the whole settings page. Built-ins equal to their defaults are
// not stored, so a later change of a default in code reaches them.
public sealed class SaveDoseTimeSettings
{
    private readonly IDoseTimePresetRepository _presets;
    private readonly IUnitOfWork _uow;

    public SaveDoseTimeSettings(IDoseTimePresetRepository presets, IUnitOfWork uow)
    {
        _presets = presets;
        _uow = uow;
    }

    public async Task ExecuteAsync(DoseTimeSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var rows = new List<DoseTimePreset>();
        var customOrder = 0;
        foreach (var p in settings.Presets)
        {
            if (p.IsBuiltIn)
            {
                var definition = BuiltInDoseTimePresets.Find(p.Id)
                    ?? throw new ArgumentException($"Unknown built-in preset {p.Id}.", nameof(settings));
                if (p.Time == definition.Time && !p.IsHidden) continue;
                rows.Add(new DoseTimePreset
                {
                    Id = p.Id, BuiltInKey = definition.Key, Time = p.Time,
                    IsAsNeeded = definition.IsAsNeeded, IsHidden = p.IsHidden,
                });
            }
            else
            {
                if (string.IsNullOrWhiteSpace(p.Label))
                    throw new ArgumentException("A preset needs a label.", nameof(settings));
                rows.Add(new DoseTimePreset
                {
                    Id = p.Id, Label = p.Label.Trim(), Time = p.Time,
                    IsAsNeeded = p.IsAsNeeded, Order = customOrder++, IsHidden = p.IsHidden,
                });
            }
        }

        var defaults = settings.Defaults
            .Where(d => d.Key is >= 1 and <= DoseTimeDefault.MaxAdministrations)
            .Where(d => !DoseTimeDefault.BuiltIn[d.Key].SequenceEqual(d.Value.Order()))
            .Select(d => new DoseTimeDefault { AdministrationsPerDay = d.Key, Times = DoseTimeDefault.Format(d.Value) })
            .ToList();

        await _presets.ReplaceAllAsync(rows, defaults, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);
    }
}
