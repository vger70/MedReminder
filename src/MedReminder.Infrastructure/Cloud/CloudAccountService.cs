using System.Runtime.Versioning;
using MedReminder.Application.Abstractions;

namespace MedReminder.Infrastructure.Cloud;

// ICloudAccountService for the desktop (B.1 Phase 4b): routes each call to
// the provider's own service, OneDrive (MSAL) or Google Drive.
[SupportedOSPlatform("windows")]
internal sealed class CloudAccountService : ICloudAccountService
{
    private readonly MsalCloudAccountService _oneDrive;
    private readonly GoogleCloudAccountService _googleDrive;

    public CloudAccountService(MsalCloudAccountService oneDrive, GoogleCloudAccountService googleDrive)
    {
        _oneDrive = oneDrive;
        _googleDrive = googleDrive;
    }

    public bool IsAvailable(CloudProvider provider) => For(provider).IsAvailable(provider);

    public Task<CloudAccount> SignInAsync(CloudProvider provider, string? accountId, CancellationToken cancellationToken)
        => For(provider).SignInAsync(provider, accountId, cancellationToken);

    public Task<CloudAccount?> FindAsync(CloudProvider provider, string accountId, CancellationToken cancellationToken)
        => For(provider).FindAsync(provider, accountId, cancellationToken);

    public Task<bool> HasSessionAsync(CloudProvider provider, string accountId, CancellationToken cancellationToken)
        => For(provider).HasSessionAsync(provider, accountId, cancellationToken);

    private ICloudAccountService For(CloudProvider provider) => provider switch
    {
        CloudProvider.OneDrive => _oneDrive,
        CloudProvider.GoogleDrive => _googleDrive,
        _ => throw new NotSupportedException($"{provider} is not supported."),
    };
}
