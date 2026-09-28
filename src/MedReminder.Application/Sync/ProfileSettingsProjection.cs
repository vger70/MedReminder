using MedReminder.Application.Abstractions;
using MedReminder.Domain.Sync;

namespace MedReminder.Application.Sync;

// The replicated profile settings (B.1, P8, docs/analysis/
// ANALYSIS-B1-MOBILE-SYNC.md §4.2) are last-writer-wins registers of the
// profile pseudo-entity (Guid.Empty) in SyncFieldVersions. The profile
// registry and notifications.settings.json hold the local copy: after an
// apply, and at the end of every sync run, the winner of each setting
// that has a version is written there when it differs. A setting with no
// version (a group created before this existed, never edited since) is
// left as it is on each device.
public static class ProfileSettingsProjection
{
    public static readonly Guid Entity = Guid.Empty;

    public static string Register(string setting) => "Profile." + setting;

    // Returns the number of settings written.
    public static async Task<int> ProjectAsync(SyncRegisters registers, IProfileSettingsStore store,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(registers);
        ArgumentNullException.ThrowIfNull(store);
        var current = store.Read();
        var changes = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var setting in ProfileSetting.All)
        {
            if (await registers.WinnerAsync(Entity, Register(setting), cancellationToken) is not { } winner) continue;
            // A profile always has a name.
            if (setting == ProfileSetting.DisplayName && string.IsNullOrWhiteSpace(winner.Value)) continue;
            if (!string.Equals(current.GetValueOrDefault(setting) ?? string.Empty, winner.Value ?? string.Empty,
                    StringComparison.Ordinal))
            {
                changes[setting] = winner.Value ?? string.Empty;
            }
        }
        if (changes.Count > 0) store.Write(changes);
        return changes.Count;
    }
}
