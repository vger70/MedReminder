using System.Runtime.Versioning;
using FluentAssertions;
using MedReminder.Application.Abstractions;
using MedReminder.Infrastructure.Cloud;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MedReminder.Infrastructure.Tests.Cloud;

// Google Drive accounts on the desktop (B.1 Phase 4b): without a client
// id and secret the provider is not offered; an account that never
// signed in, or whose cache cannot be read, has no session.
[SupportedOSPlatform("windows")]
public sealed class GoogleCloudAccountServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mr-gaccount-" + Guid.NewGuid().ToString("N"));

    public GoogleCloudAccountServiceTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private GoogleCloudAccountService Service(string? id = "client.apps.googleusercontent.com", string? secret = "secret")
        => new(id, secret, Path.Combine(_dir, GoogleCloudAccountService.CacheFileName), new HttpClient(),
            NullLogger<GoogleCloudAccountService>.Instance);

    [Theory]
    [InlineData(null, "secret")]
    [InlineData("client", null)]
    [InlineData("", "")]
    public void Without_a_client_id_and_secret_Google_Drive_is_not_offered(string? id, string? secret)
        => Service(id, secret).IsAvailable(CloudProvider.GoogleDrive).Should().BeFalse();

    [Fact]
    public void With_both_it_is_offered_for_Google_Drive_only()
    {
        Service().IsAvailable(CloudProvider.GoogleDrive).Should().BeTrue();
        Service().IsAvailable(CloudProvider.OneDrive).Should().BeFalse();
    }

    [Fact]
    public async Task An_account_that_never_signed_in_has_no_session()
    {
        (await Service().FindAsync(CloudProvider.GoogleDrive, "123", default)).Should().BeNull();
        (await Service().HasSessionAsync(CloudProvider.GoogleDrive, "123", default)).Should().BeFalse();
        await FluentActions.Awaiting(() => Service().GetAccessTokenAsync("123", false, default))
            .Should().ThrowAsync<CloudSignInRequiredException>();
    }

    [Fact]
    public async Task An_unreadable_cache_reads_as_signed_out()
    {
        await File.WriteAllBytesAsync(Path.Combine(_dir, GoogleCloudAccountService.CacheFileName), [1, 2, 3]);

        (await Service().HasSessionAsync(CloudProvider.GoogleDrive, "123", default)).Should().BeFalse();
    }
}
