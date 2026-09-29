namespace MedReminder.Infrastructure.Cloud.GoogleDrive;

// Access tokens for Google Drive (drive.file, drive.appdata), provided by
// the host: the loopback OAuth flow with a DPAPI-protected refresh token
// on the desktop (GoogleCloudAccountService). Throws
// CloudSignInRequiredException when the account must sign in again.
public interface IGoogleDriveAccessTokens
{
    Task<string> GetAccessTokenAsync(string accountId, bool forceRefresh, CancellationToken cancellationToken);
}
