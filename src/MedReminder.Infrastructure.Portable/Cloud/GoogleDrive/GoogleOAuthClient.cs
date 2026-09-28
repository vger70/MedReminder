using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using MedReminder.Application.Abstractions;
using MedReminder.Infrastructure.Cloud.OneDrive;

namespace MedReminder.Infrastructure.Cloud.GoogleDrive;

// The OAuth 2.0 protocol for installed apps against Google (B.1 Phase 4b,
// spike S7 C0, C1): authorization URL with PKCE (S256) and a loopback
// redirect, code exchange, refresh, and the account identity from the
// Drive about resource (no openid scope). The browser and the loopback
// listener belong to the host. A Desktop client's secret is sent as Google
// requires; Google does not treat it as confidential for installed apps.
//
// Refresh tokens last 7 days while the Cloud project's consent screen is
// in Testing (S7 C0): the project must be published for real use.
public sealed class GoogleOAuthClient
{
    public const string Scopes = "https://www.googleapis.com/auth/drive.file https://www.googleapis.com/auth/drive.appdata";

    private const string AuthorizeEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
    private const string TokenEndpoint = "https://oauth2.googleapis.com/token";
    private const string AboutEndpoint = "https://www.googleapis.com/drive/v3/about?fields=user(permissionId,emailAddress)";

    private readonly HttpClient _http;
    private readonly string _clientId;
    private readonly string _clientSecret;

    public GoogleOAuthClient(HttpClient http, string clientId, string clientSecret)
    {
        _http = http;
        _clientId = clientId;
        _clientSecret = clientSecret;
    }

    public static PkcePair CreatePkce()
    {
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        return new PkcePair(verifier, Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))),
            Base64Url(RandomNumberGenerator.GetBytes(16)));
    }

    public string AuthorizationUrl(string redirectUri, PkcePair pkce, string? loginHint)
        => AuthorizeEndpoint
           + $"?client_id={Uri.EscapeDataString(_clientId)}&redirect_uri={Uri.EscapeDataString(redirectUri)}"
           + $"&response_type=code&scope={Uri.EscapeDataString(Scopes)}"
           + $"&code_challenge={pkce.Challenge}&code_challenge_method=S256&state={pkce.State}"
           + "&access_type=offline&prompt=consent"
           + (loginHint is null ? string.Empty : $"&login_hint={Uri.EscapeDataString(loginHint)}");

    public Task<GoogleTokens> ExchangeCodeAsync(string code, string redirectUri, PkcePair pkce, CancellationToken ct)
        => PostAsync(new Dictionary<string, string>
        {
            ["code"] = code,
            ["client_id"] = _clientId,
            ["client_secret"] = _clientSecret,
            ["redirect_uri"] = redirectUri,
            ["grant_type"] = "authorization_code",
            ["code_verifier"] = pkce.Verifier,
        }, ct);

    // Throws CloudSignInRequiredException when Google refuses the refresh
    // token (invalid_grant: expired, revoked, password change).
    public Task<GoogleTokens> RefreshAsync(string refreshToken, CancellationToken ct)
        => PostAsync(new Dictionary<string, string>
        {
            ["refresh_token"] = refreshToken,
            ["client_id"] = _clientId,
            ["client_secret"] = _clientSecret,
            ["grant_type"] = "refresh_token",
        }, ct);

    // permissionId is stable per Google account; the e-mail is for display.
    public async Task<(string Id, string UserName)> GetUserAsync(string accessToken, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, AboutEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) throw new CloudStorageException(response.StatusCode, null, "Google Drive");
        var user = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct))?["user"];
        return (user?["permissionId"]?.GetValue<string>() ?? throw new CloudStorageException(response.StatusCode, "noUser", "Google Drive"),
            user["emailAddress"]?.GetValue<string>() ?? string.Empty);
    }

    private async Task<GoogleTokens> PostAsync(Dictionary<string, string> form, CancellationToken ct)
    {
        using var response = await _http.PostAsync(TokenEndpoint, new FormUrlEncodedContent(form), ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        string? error = null;
        try
        {
            error = JsonNode.Parse(text)?["error"]?.ToString();
        }
        catch
        {
            // Not JSON.
        }
        if (!response.IsSuccessStatusCode)
        {
            if (error is "invalid_grant" || response.StatusCode == HttpStatusCode.Unauthorized)
            {
                throw new CloudSignInRequiredException(CloudProvider.GoogleDrive);
            }
            throw new CloudStorageException(response.StatusCode, error, "Google sign-in");
        }
        var json = JsonNode.Parse(text)!;
        return new GoogleTokens(
            json["access_token"]!.GetValue<string>(),
            json["refresh_token"]?.GetValue<string>(),
            TimeSpan.FromSeconds(json["expires_in"]?.GetValue<int>() ?? 3600));
    }

    private static string Base64Url(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

public sealed record PkcePair(string Verifier, string Challenge, string State);

// RefreshToken is null on a refresh: Google keeps the one it issued.
public sealed record GoogleTokens(string AccessToken, string? RefreshToken, TimeSpan ExpiresIn);
