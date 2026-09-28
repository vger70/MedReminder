using FluentAssertions;
using MedReminder.Application.Abstractions;
using MedReminder.Infrastructure.Cloud.GoogleDrive;
using MedReminder.Infrastructure.Cloud.OneDrive;
using MedReminder.Infrastructure.Sync;
using Xunit;

namespace MedReminder.Infrastructure.Tests.Sync;

public sealed class SyncTransportFactoryTests
{
    private sealed class Tokens : IOneDriveAccessTokens, IGoogleDriveAccessTokens
    {
        public Task<string> GetAccessTokenAsync(string accountId, bool forceRefresh, CancellationToken cancellationToken)
            => Task.FromResult("token");
    }

    [Fact]
    public void A_folder_target_gets_a_folder_transport()
        => new SyncTransportFactory().Create(SyncTarget.ForFolder(Path.GetTempPath()))
            .Should().BeOfType<LocalFolderSyncTransport>();

    [Fact]
    public void One_OneDrive_transport_is_kept_per_account()
    {
        var factory = new SyncTransportFactory(new OneDriveClientFactory(new Tokens()));
        var a1 = factory.Create(SyncTarget.ForCloud(CloudProvider.OneDrive, "a"));

        a1.Should().BeOfType<OneDriveSyncTransport>();
        factory.Create(SyncTarget.ForCloud(CloudProvider.OneDrive, "a")).Should().BeSameAs(a1);
        factory.Create(SyncTarget.ForCloud(CloudProvider.OneDrive, "b")).Should().NotBeSameAs(a1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OneDrive_needs_a_token_source_from_the_host(bool withClientFactory)
        => FluentActions.Invoking(() => new SyncTransportFactory(withClientFactory ? new OneDriveClientFactory() : null)
                .Create(SyncTarget.ForCloud(CloudProvider.OneDrive, "a")))
            .Should().Throw<NotSupportedException>();

    [Fact]
    public void One_OneDrive_client_is_kept_per_account_for_sync_and_backup_alike()
    {
        var clients = new OneDriveClientFactory(new Tokens());

        clients.Get("a").Should().BeSameAs(clients.Get("a"));
        clients.Get("b").Should().NotBeSameAs(clients.Get("a"));
    }

    [Fact]
    public void One_Google_Drive_transport_is_kept_per_account()
    {
        var factory = new SyncTransportFactory(googleDrive: new GoogleDriveClientFactory(new Tokens()));
        var a1 = factory.Create(SyncTarget.ForCloud(CloudProvider.GoogleDrive, "a"));

        a1.Should().BeOfType<GoogleDriveSyncTransport>();
        factory.Create(SyncTarget.ForCloud(CloudProvider.GoogleDrive, "a")).Should().BeSameAs(a1);
        factory.Create(SyncTarget.ForCloud(CloudProvider.GoogleDrive, "b")).Should().NotBeSameAs(a1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Google_Drive_needs_a_token_source_from_the_host(bool withClientFactory)
        => FluentActions.Invoking(() => new SyncTransportFactory(googleDrive: withClientFactory ? new GoogleDriveClientFactory() : null)
                .Create(SyncTarget.ForCloud(CloudProvider.GoogleDrive, "a")))
            .Should().Throw<NotSupportedException>();
}
