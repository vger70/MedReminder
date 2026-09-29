using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Web;
using MedReminder.Application.Abstractions;
using MedReminder.Infrastructure.Cloud.GoogleDrive;
using MedReminder.Infrastructure.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace MedReminder.Infrastructure.Cloud;

// Google Drive accounts on the desktop (B.1 Phase 4b, docs/analysis/
// ANALYSIS-B1-MOBILE-SYNC.md §5.8, spike S7 in §18.7): sign-in in the
// system browser with a loopback redirect on 127.0.0.1 and PKCE
// (GoogleOAuthClient), refresh tokens for every profile in
// %LOCALAPPDATA%\MedReminder\googledrive.protected (DPAPI CurrentUser),
// access tokens in memory. Accounts are told apart by the Drive
// permission id, so two profiles can use two Google accounts.
//
// The Desktop OAuth client's id and secret come from configuration
// (GoogleDrive:ClientId, GoogleDrive:ClientSecret) or from the build
// (MEDREMINDER_GOOGLE_CLIENT_ID / _SECRET, see docs/PACKAGING.md); they
// are not in the repository. Without them Google Drive is not offered.
// Nothing about tokens, the secret or the account name is logged.
[SupportedOSPlatform("windows")]
internal sealed class GoogleCloudAccountService : ICloudAccountService, IGoogleDriveAccessTokens
{
    public const string CacheFileName = "googledrive.protected";

    private static readonly TimeSpan SignInTimeout = TimeSpan.FromMinutes(5);

    private readonly GoogleOAuthClient? _oauth;
    private readonly string _cachePath;
    private readonly ILogger<GoogleCloudAccountService> _log;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, (string Token, DateTimeOffset Expires)> _accessTokens = new(StringComparer.Ordinal);

    public GoogleCloudAccountService(IConfiguration configuration, ILogger<GoogleCloudAccountService> log)
        : this(Setting(configuration, "GoogleDrive:ClientId", "GoogleDriveClientId"),
            Setting(configuration, "GoogleDrive:ClientSecret", "GoogleDriveClientSecret"),
            Path.Combine(AppDataPaths.GetAppDataDirectory(), CacheFileName),
            CloudHttp.Shared.Client,
            log)
    {
    }

    internal GoogleCloudAccountService(string? clientId, string? clientSecret, string cachePath, HttpClient http,
        ILogger<GoogleCloudAccountService> log)
    {
        _oauth = string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret)
            ? null
            : new GoogleOAuthClient(http, clientId, clientSecret);
        _cachePath = cachePath;
        _log = log;
    }

    public bool IsAvailable(CloudProvider provider) => provider == CloudProvider.GoogleDrive && _oauth is not null;

    public async Task<CloudAccount> SignInAsync(CloudProvider provider, string? accountId, CancellationToken cancellationToken)
    {
        var oauth = Require(provider);
        var hint = accountId is not null && Load().TryGetValue(accountId, out var known) ? known.UserName : null;

        var pkce = GoogleOAuthClient.CreatePkce();
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var redirect = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}";
            Process.Start(new ProcessStartInfo(oauth.AuthorizationUrl(redirect, pkce, hint)) { UseShellExecute = true });

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(SignInTimeout);
            var code = await ReceiveCodeAsync(listener, pkce.State, timeout.Token);

            var tokens = await oauth.ExchangeCodeAsync(code, redirect, pkce, cancellationToken);
            if (tokens.RefreshToken is null) throw new InvalidOperationException("Google did not return a refresh token.");
            var (id, userName) = await oauth.GetUserAsync(tokens.AccessToken, cancellationToken);

            await _gate.WaitAsync(cancellationToken);
            try
            {
                var accounts = Load();
                accounts[id] = new StoredAccount(tokens.RefreshToken, userName);
                Save(accounts);
                _accessTokens[id] = (tokens.AccessToken, DateTimeOffset.UtcNow + tokens.ExpiresIn - TimeSpan.FromMinutes(1));
            }
            finally
            {
                _gate.Release();
            }
            _log.LogInformation("Google Drive sign-in completed.");
            return new CloudAccount(CloudProvider.GoogleDrive, id, userName);
        }
        finally
        {
            listener.Stop();
        }
    }

    public Task<CloudAccount?> FindAsync(CloudProvider provider, string accountId, CancellationToken cancellationToken)
    {
        Require(provider);
        return Task.FromResult(Load().TryGetValue(accountId, out var stored)
            ? new CloudAccount(CloudProvider.GoogleDrive, accountId, stored.UserName)
            : null);
    }

    public async Task<bool> HasSessionAsync(CloudProvider provider, string accountId, CancellationToken cancellationToken)
    {
        Require(provider);
        try
        {
            await GetAccessTokenAsync(accountId, forceRefresh: false, cancellationToken);
            return true;
        }
        catch (CloudSignInRequiredException)
        {
            return false;
        }
    }

    public async Task<string> GetAccessTokenAsync(string accountId, bool forceRefresh, CancellationToken cancellationToken)
    {
        var oauth = _oauth ?? throw new NotSupportedException("Google Drive is not available in this build.");
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!forceRefresh && _accessTokens.TryGetValue(accountId, out var cached) && cached.Expires > DateTimeOffset.UtcNow)
            {
                return cached.Token;
            }
            if (!Load().TryGetValue(accountId, out var stored)) throw new CloudSignInRequiredException(CloudProvider.GoogleDrive);
            try
            {
                var tokens = await oauth.RefreshAsync(stored.RefreshToken, cancellationToken);
                _accessTokens[accountId] = (tokens.AccessToken, DateTimeOffset.UtcNow + tokens.ExpiresIn - TimeSpan.FromMinutes(1));
                return tokens.AccessToken;
            }
            catch (CloudSignInRequiredException)
            {
                _log.LogWarning("Google Drive session needs a new sign-in.");
                _accessTokens.Remove(accountId);
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private GoogleOAuthClient Require(CloudProvider provider)
    {
        if (provider != CloudProvider.GoogleDrive) throw new NotSupportedException($"{provider} is not supported.");
        return _oauth ?? throw new NotSupportedException("Google Drive is not available in this build.");
    }

    // One request on the loopback port: GET /?code=…&state=… from the
    // browser. A TcpListener needs no URL reservation, unlike HttpListener.
    private static async Task<string> ReceiveCodeAsync(TcpListener listener, string state, CancellationToken ct)
    {
        while (true)
        {
            using var client = await listener.AcceptTcpClientAsync(ct);
            await using var stream = client.GetStream();
            var buffer = new byte[8192];
            var read = await stream.ReadAsync(buffer, ct);
            var requestLine = Encoding.ASCII.GetString(buffer, 0, read).Split("\r\n")[0].Split(' ');
            if (requestLine.Length < 2) continue;
            var query = HttpUtility.ParseQueryString(new Uri("http://127.0.0.1" + requestLine[1]).Query);
            var code = query["code"];
            var ok = code is not null && query["state"] == state;
            // Plain page, in English like the OS browser chrome around it.
            var body = ok
                ? "MedReminder: sign-in complete. You can close this tab."
                : "MedReminder: sign-in was not completed.";
            var response = "HTTP/1.1 200 OK\r\nContent-Type: text/plain; charset=utf-8\r\n"
                + $"Content-Length: {Encoding.UTF8.GetByteCount(body)}\r\nConnection: close\r\n\r\n{body}";
            await stream.WriteAsync(Encoding.UTF8.GetBytes(response), ct);
            if (ok) return code!;
            if (query["error"] is { } error)
            {
                throw new InvalidOperationException($"Google sign-in was not completed ({error}).");
            }
            // A stray request (favicon): keep waiting.
        }
    }

    private sealed record StoredAccount(string RefreshToken, string UserName);

    private Dictionary<string, StoredAccount> Load()
    {
        if (!File.Exists(_cachePath)) return new Dictionary<string, StoredAccount>(StringComparer.Ordinal);
        try
        {
            var json = ProtectedData.Unprotect(File.ReadAllBytes(_cachePath), optionalEntropy: null, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<Dictionary<string, StoredAccount>>(json)
                   ?? new Dictionary<string, StoredAccount>(StringComparer.Ordinal);
        }
        catch (Exception ex)
        {
            // Copied from another Windows account, or corrupt: signed out.
            _log.LogWarning("Google Drive token cache could not be read ({Error}); a new sign-in is needed.", ex.GetType().Name);
            return new Dictionary<string, StoredAccount>(StringComparer.Ordinal);
        }
    }

    private void Save(Dictionary<string, StoredAccount> accounts)
    {
        var data = ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(accounts), optionalEntropy: null,
            DataProtectionScope.CurrentUser);
        var temp = _cachePath + ".tmp";
        File.WriteAllBytes(temp, data);
        File.Move(temp, _cachePath, overwrite: true);
    }

    // Configuration first, then the value stamped into the assembly at
    // build time (AssemblyMetadata, from MEDREMINDER_GOOGLE_CLIENT_*).
    private static string? Setting(IConfiguration configuration, string key, string metadata)
        => configuration[key] is { Length: > 0 } configured
            ? configured
            : typeof(GoogleCloudAccountService).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(a => a.Key == metadata)?.Value;
}
