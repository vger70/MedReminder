using System.Runtime.Versioning;
using System.Security.Cryptography;
using MedReminder.Application.Abstractions;
using MedReminder.Infrastructure.Cloud.OneDrive;
using MedReminder.Infrastructure.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Client;

namespace MedReminder.Infrastructure.Cloud;

// OneDrive accounts on the desktop (B.1 Phase 4a, docs/analysis/
// ANALYSIS-B1-MOBILE-SYNC.md §5.8, spike S6 in §18.6): MSAL public
// client, sign-in in the system browser with the http://localhost
// redirect, one token cache for every profile in
// %LOCALAPPDATA%\MedReminder\onedrive.protected (DPAPI CurrentUser).
// Profiles are told apart by account id, so two people on one Windows
// account can use two Microsoft accounts.
//
// The app registration's client id is public (a public client has no
// secret). OneDrive:ClientId and OneDrive:Authority in configuration
// override the defaults. Nothing about tokens or the account name is
// logged.
[SupportedOSPlatform("windows")]
internal sealed class MsalCloudAccountService : ICloudAccountService, IOneDriveAccessTokens
{
    public const string CacheFileName = "onedrive.protected";

    // MedReminder registration in Microsoft Entra (personal accounts,
    // Files.ReadWrite.AppFolder; the app folder is /Apps/MedReminder26).
    private const string DefaultClientId = "b6c24691-31a1-4ba1-ac80-478e312635bf";
    private const string DefaultAuthority = "consumers";
    private static readonly string[] Scopes = ["Files.ReadWrite.AppFolder"];

    private readonly string _clientId;
    private readonly string _authority;
    private readonly string _cachePath;
    private readonly ILogger<MsalCloudAccountService> _log;
    private readonly Lazy<IPublicClientApplication> _app;
    private readonly object _cacheLock = new();

    public MsalCloudAccountService(IConfiguration configuration, ILogger<MsalCloudAccountService> log)
        : this(configuration["OneDrive:ClientId"] ?? DefaultClientId,
            configuration["OneDrive:Authority"] ?? DefaultAuthority,
            Path.Combine(AppDataPaths.GetAppDataDirectory(), CacheFileName),
            log)
    {
    }

    internal MsalCloudAccountService(string clientId, string authority, string cachePath,
        ILogger<MsalCloudAccountService> log)
    {
        _clientId = clientId;
        _authority = authority;
        _cachePath = cachePath;
        _log = log;
        _app = new Lazy<IPublicClientApplication>(Build);
    }

    public bool IsAvailable(CloudProvider provider)
        => provider == CloudProvider.OneDrive && Guid.TryParse(_clientId, out _);

    public async Task<CloudAccount> SignInAsync(CloudProvider provider, string? accountId, CancellationToken cancellationToken)
    {
        EnsureOneDrive(provider);
        var builder = _app.Value.AcquireTokenInteractive(Scopes)
            .WithUseEmbeddedWebView(false)
            .WithPrompt(Prompt.SelectAccount);
        if (accountId is not null && await _app.Value.GetAccountAsync(accountId) is { } known)
        {
            builder = builder.WithLoginHint(known.Username);
        }
        var result = await builder.ExecuteAsync(cancellationToken);
        _log.LogInformation("OneDrive sign-in completed.");
        return new CloudAccount(CloudProvider.OneDrive, result.Account.HomeAccountId.Identifier, result.Account.Username);
    }

    public async Task<CloudAccount?> FindAsync(CloudProvider provider, string accountId, CancellationToken cancellationToken)
    {
        EnsureOneDrive(provider);
        var account = await _app.Value.GetAccountAsync(accountId);
        return account is null ? null : new CloudAccount(CloudProvider.OneDrive, accountId, account.Username);
    }

    public async Task<string> GetAccessTokenAsync(string accountId, bool forceRefresh, CancellationToken cancellationToken)
    {
        var account = await _app.Value.GetAccountAsync(accountId)
            ?? throw new CloudSignInRequiredException(CloudProvider.OneDrive);
        try
        {
            var result = await _app.Value.AcquireTokenSilent(Scopes, account)
                .WithForceRefresh(forceRefresh)
                .ExecuteAsync(cancellationToken);
            return result.AccessToken;
        }
        catch (MsalUiRequiredException ex)
        {
            _log.LogWarning("OneDrive session needs a new sign-in ({Code}).", ex.ErrorCode);
            throw new CloudSignInRequiredException(CloudProvider.OneDrive, ex);
        }
    }

    private static void EnsureOneDrive(CloudProvider provider)
    {
        if (provider != CloudProvider.OneDrive) throw new NotSupportedException($"{provider} is not supported.");
    }

    private IPublicClientApplication Build()
    {
        if (!Guid.TryParse(_clientId, out _)) throw new InvalidOperationException("No OneDrive app registration is configured.");
        var app = PublicClientApplicationBuilder.Create(_clientId)
            .WithAuthority($"https://login.microsoftonline.com/{_authority}")
            .WithRedirectUri("http://localhost")
            .Build();

        app.UserTokenCache.SetBeforeAccess(args =>
        {
            lock (_cacheLock)
            {
                if (!File.Exists(_cachePath)) return;
                try
                {
                    args.TokenCache.DeserializeMsalV3(ProtectedData.Unprotect(
                        File.ReadAllBytes(_cachePath), optionalEntropy: null, DataProtectionScope.CurrentUser));
                }
                catch (CryptographicException)
                {
                    // Copied from another Windows account or machine: start
                    // empty; the user signs in again.
                    _log.LogWarning("OneDrive token cache could not be read; a new sign-in is needed.");
                }
            }
        });
        app.UserTokenCache.SetAfterAccess(args =>
        {
            if (!args.HasStateChanged) return;
            lock (_cacheLock)
            {
                var data = ProtectedData.Protect(args.TokenCache.SerializeMsalV3(), optionalEntropy: null,
                    DataProtectionScope.CurrentUser);
                var temp = _cachePath + ".tmp";
                File.WriteAllBytes(temp, data);
                File.Move(temp, _cachePath, overwrite: true);
            }
        });
        return app;
    }
}
