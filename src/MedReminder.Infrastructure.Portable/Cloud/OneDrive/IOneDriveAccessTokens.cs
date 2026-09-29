namespace MedReminder.Infrastructure.Cloud.OneDrive;

// Access tokens for the OneDrive app folder (Files.ReadWrite.AppFolder),
// provided by the host: MSAL with a DPAPI-protected cache on the desktop
// (MsalCloudAccountService). Throws CloudSignInRequiredException when the
// account must sign in again.
public interface IOneDriveAccessTokens
{
    Task<string> GetAccessTokenAsync(string accountId, bool forceRefresh, CancellationToken cancellationToken);
}
