using MedReminder.Domain.Medicines;

namespace MedReminder.Application.Abstractions;

// Stored time-of-day presets and per-day default times of the profile
// (device-local, docs/analysis/ANALYSIS-INTRADAY-CONSUMPTION.md §6).
public interface IDoseTimePresetRepository
{
    Task<IReadOnlyList<DoseTimePreset>> ListPresetsAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<DoseTimeDefault>> ListDefaultsAsync(CancellationToken cancellationToken);

    // Replaces every stored row. Does not call SaveChangesAsync.
    Task ReplaceAllAsync(
        IReadOnlyList<DoseTimePreset> presets,
        IReadOnlyList<DoseTimeDefault> defaults,
        CancellationToken cancellationToken);
}
