using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using FluentAssertions;
using MedReminder.Application.Abstractions;
using MedReminder.Infrastructure.Cloud.GoogleDrive;
using MedReminder.Infrastructure.Cloud.OneDrive;
using Xunit;

namespace MedReminder.Infrastructure.Tests.Cloud;

// The Google OAuth protocol for installed apps (B.1 Phase 4b, spike S7
// C0, C1): PKCE, code exchange, refresh, account identity.
public sealed class GoogleOAuthClientTests
{
    private sealed class Endpoint(Func<HttpRequestMessage, string, HttpResponseMessage> answer) : HttpMessageHandler
    {
        public List<(string Url, string Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(ct);
            Requests.Add((request.RequestUri!.ToString(), body));
            return answer(request, body);
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, JsonNode body)
        => new(status) { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };

    [Fact]
    public void The_authorization_url_asks_for_offline_access_with_PKCE_and_both_scopes()
    {
        var pkce = GoogleOAuthClient.CreatePkce();
        var url = new GoogleOAuthClient(new HttpClient(), "client.apps.googleusercontent.com", "secret")
            .AuthorizationUrl("http://127.0.0.1:5555", pkce, "user@example.com");

        url.Should().StartWith("https://accounts.google.com/o/oauth2/v2/auth?")
            .And.Contain("code_challenge_method=S256").And.Contain($"code_challenge={pkce.Challenge}")
            .And.Contain("access_type=offline").And.Contain($"state={pkce.State}")
            .And.Contain(Uri.EscapeDataString("https://www.googleapis.com/auth/drive.appdata"))
            .And.Contain(Uri.EscapeDataString("http://127.0.0.1:5555"))
            .And.NotContain("secret");
        var expected = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(pkce.Verifier)))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        pkce.Challenge.Should().Be(expected);
    }

    [Fact]
    public async Task The_code_exchange_sends_the_verifier_and_returns_both_tokens()
    {
        var endpoint = new Endpoint((_, _) => Json(HttpStatusCode.OK, new JsonObject
        {
            ["access_token"] = "at", ["refresh_token"] = "rt", ["expires_in"] = 3599,
        }));
        var pkce = GoogleOAuthClient.CreatePkce();
        var tokens = await new GoogleOAuthClient(new HttpClient(endpoint), "id", "secret")
            .ExchangeCodeAsync("the-code", "http://127.0.0.1:1", pkce, default);

        tokens.Should().Be(new GoogleTokens("at", "rt", TimeSpan.FromSeconds(3599)));
        endpoint.Requests.Single().Body.Should().Contain("grant_type=authorization_code")
            .And.Contain($"code_verifier={pkce.Verifier}").And.Contain("code=the-code");
    }

    [Fact]
    public async Task A_refused_refresh_token_means_a_new_sign_in()
    {
        var endpoint = new Endpoint((_, _) => Json(HttpStatusCode.BadRequest, new JsonObject { ["error"] = "invalid_grant" }));

        await FluentActions.Awaiting(() => new GoogleOAuthClient(new HttpClient(endpoint), "id", "secret").RefreshAsync("rt", default))
            .Should().ThrowAsync<CloudSignInRequiredException>()
            .Where(e => e.Provider == CloudProvider.GoogleDrive);
    }

    [Fact]
    public async Task Other_token_errors_are_storage_errors_without_secrets()
    {
        var endpoint = new Endpoint((_, _) => Json(HttpStatusCode.InternalServerError, new JsonObject { ["error"] = "backend" }));

        var failure = await FluentActions.Awaiting(() => new GoogleOAuthClient(new HttpClient(endpoint), "id", "s3cr3t").RefreshAsync("rt", default))
            .Should().ThrowAsync<CloudStorageException>();
        failure.Which.Message.Should().NotContain("s3cr3t").And.NotContain("rt");
    }

    [Fact]
    public async Task The_account_is_identified_by_its_Drive_permission_id()
    {
        var endpoint = new Endpoint((request, _) =>
        {
            request.Headers.Authorization!.Parameter.Should().Be("at");
            return Json(HttpStatusCode.OK, new JsonObject
            {
                ["user"] = new JsonObject { ["permissionId"] = "12345", ["emailAddress"] = "user@example.com" },
            });
        });

        var (id, name) = await new GoogleOAuthClient(new HttpClient(endpoint), "id", "secret").GetUserAsync("at", default);

        (id, name).Should().Be(("12345", "user@example.com"));
        endpoint.Requests.Single().Url.Should().Contain("/drive/v3/about");
    }
}
