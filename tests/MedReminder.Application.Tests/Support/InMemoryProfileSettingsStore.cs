using MedReminder.Application.Abstractions;
using MedReminder.Domain.Sync;

namespace MedReminder.Application.Tests.Support;

// The profile registry and notifications.settings.json of a device.
internal sealed class InMemoryProfileSettingsStore : IProfileSettingsStore
{
    private readonly Dictionary<string, string?> _values =
        ProfileSetting.All.ToDictionary(s => s, _ => (string?)string.Empty, StringComparer.Ordinal);

    public InMemoryProfileSettingsStore(string displayName = "Mario") => _values[ProfileSetting.DisplayName] = displayName;

    public IReadOnlyDictionary<string, string?> Read() => new Dictionary<string, string?>(_values, StringComparer.Ordinal);

    public void Write(IReadOnlyDictionary<string, string?> changes)
    {
        foreach (var (key, value) in changes) _values[key] = value;
    }
}
