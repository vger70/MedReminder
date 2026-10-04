using MedReminder.Application.Abstractions;
using MedReminder.Domain.Medicines;

namespace MedReminder.Application.Tests.Support;

internal sealed class InMemoryDoseTimePresetRepository : IDoseTimePresetRepository
{
    public List<DoseTimePreset> Presets { get; } = new();
    public List<DoseTimeDefault> Defaults { get; } = new();

    public Task<IReadOnlyList<DoseTimePreset>> ListPresetsAsync(CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<DoseTimePreset>>([.. Presets.OrderBy(p => p.Order)]);

    public Task<IReadOnlyList<DoseTimeDefault>> ListDefaultsAsync(CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<DoseTimeDefault>>([.. Defaults]);

    public Task ReplaceAllAsync(
        IReadOnlyList<DoseTimePreset> presets, IReadOnlyList<DoseTimeDefault> defaults,
        CancellationToken cancellationToken)
    {
        Presets.Clear();
        Presets.AddRange(presets);
        Defaults.Clear();
        Defaults.AddRange(defaults);
        return Task.CompletedTask;
    }
}
