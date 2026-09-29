using MedReminder.Application.Abstractions;
using MedReminder.Application.Sync;
using MedReminder.Infrastructure.Persistence;
using MedReminder.Infrastructure.Persistence.Repositories;

namespace MedReminder.Infrastructure.Tests.Support;

// Operation log over a real context. Sync is disabled unless settings are
// given, as on every profile before Phase 3d.
internal static class TestOperationLog
{
    public static OperationLog For(MedReminderDbContext ctx, TimeProvider? clock = null, SyncSettings? settings = null)
        => new(new FixedSyncSettingsStore(settings), new SyncOperationRepository(ctx),
            new SyncRegisters(new SyncFieldVersionRepository(ctx), new SyncConflictRepository(ctx), clock ?? TimeProvider.System),
            clock ?? TimeProvider.System);

    private sealed class FixedSyncSettingsStore(SyncSettings? settings) : ISyncSettingsStore
    {
        private SyncSettings? _settings = settings;

        public SyncSettings? Load() => _settings;

        public void Save(SyncSettings? value) => _settings = value;
    }
}
