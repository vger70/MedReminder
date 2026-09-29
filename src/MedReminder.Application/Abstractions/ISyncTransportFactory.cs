namespace MedReminder.Application.Abstractions;

// Where a sync group lives (B.1 Phase 4a): a folder (Phase 3c), or a
// provider account whose app folder is reached through the provider API.
public sealed record SyncTarget(string? Folder, CloudProvider? Provider = null, string? AccountId = null)
{
    public static SyncTarget ForFolder(string folder) => new(folder);

    public static SyncTarget ForCloud(CloudProvider provider, string accountId) => new(null, provider, accountId);

    public static SyncTarget Of(SyncSettings settings) => new(settings.Folder, settings.Provider, settings.AccountId);
}

public interface ISyncTransportFactory
{
    ISyncTransport Create(SyncTarget target);
}
