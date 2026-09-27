using MedReminder.Application.Abstractions;

namespace MedReminder.Application.Tests.Support;

internal sealed class InMemorySyncSettingsStore : ISyncSettingsStore
{
    public SyncSettings? Settings { get; set; }

    public SyncSettings? Load() => Settings;

    public void Save(SyncSettings? settings) => Settings = settings;
}
