using MedReminder.Application.Abstractions;
using MedReminder.Domain.Medicines;
using Microsoft.EntityFrameworkCore;

namespace MedReminder.Infrastructure.Persistence.Repositories;

internal sealed class DoseTimePresetRepository : IDoseTimePresetRepository
{
    private readonly MedReminderDbContext _db;

    public DoseTimePresetRepository(MedReminderDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<DoseTimePreset>> ListPresetsAsync(CancellationToken cancellationToken)
        => await _db.DoseTimePresets.AsNoTracking().OrderBy(p => p.Order).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<DoseTimeDefault>> ListDefaultsAsync(CancellationToken cancellationToken)
        => await _db.DoseTimeDefaults.AsNoTracking().ToListAsync(cancellationToken);

    public async Task ReplaceAllAsync(
        IReadOnlyList<DoseTimePreset> presets,
        IReadOnlyList<DoseTimeDefault> defaults,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(presets);
        ArgumentNullException.ThrowIfNull(defaults);
        _db.DoseTimePresets.RemoveRange(await _db.DoseTimePresets.ToListAsync(cancellationToken));
        _db.DoseTimeDefaults.RemoveRange(await _db.DoseTimeDefaults.ToListAsync(cancellationToken));
        await _db.DoseTimePresets.AddRangeAsync(presets, cancellationToken);
        await _db.DoseTimeDefaults.AddRangeAsync(defaults, cancellationToken);
    }
}
