using FluentAssertions;
using MedReminder.Application.Abstractions;
using MedReminder.Application.Household;
using MedReminder.Application.Tests.Support;
using MedReminder.Application.UseCases;
using MedReminder.Domain.Household;
using Xunit;

namespace MedReminder.Application.Tests.Household;

// Household step H2b: installation settings written through use cases
// and recorded in the household.
public class InstallationSettingsTests
{
    private readonly InMemoryProfileRegistry _registry = new();
    private readonly InMemoryHouseholdStore _store = new();
    private readonly InMemoryInstallationSettingsStore _settings = new();
    private readonly InMemorySmtpCredentialStore _credentials = new();
    private readonly PrefixCredentialProtector _protector = new();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 9, 29, 10, 0, 0, TimeSpan.Zero));

    private static readonly ICurrentProfile Admin = new FixedCurrentProfile("admin", ProfileRole.Admin);
    private static readonly ICurrentProfile User = new FixedCurrentProfile("user", ProfileRole.User);

    private static readonly SmtpTransport Transport =
        new("smtp.example.org", 587, true, "anna", "anna@example.org", "MedReminder", 30);

    public InstallationSettingsTests()
    {
        _registry.Add("admin", "Anna", ProfileRole.Admin);
    }

    private HouseholdLog Log => new(_store, _clock);

    private Task<IReadOnlyDictionary<string, string?>> HeldAsync() => Log.SettingsAsync(CancellationToken.None);

    private ReconcileHousehold Reconcile() => new(_registry, Log, _settings, _credentials, _protector);

    private UpdateSmtpSettings Smtp(ICurrentProfile current) => new(_settings, _credentials, _protector, current, Log);

    [Fact]
    public async Task Saving_smtp_records_each_changed_field_once()
    {
        await Smtp(Admin).ExecuteAsync(Transport, null, false, CancellationToken.None);
        var first = _store.Operations.Count;
        await Smtp(Admin).ExecuteAsync(Transport, null, false, CancellationToken.None);
        await Smtp(Admin).ExecuteAsync(Transport with { Port = 465 }, null, false, CancellationToken.None);

        _settings.Smtp.Port.Should().Be(465);
        first.Should().Be(7, "the seven transport fields, no password");
        _store.Operations.Should().HaveCount(8);
        (await HeldAsync())[HouseholdSetting.SmtpPort].Should().Be("465");
    }

    [Fact]
    public async Task The_password_is_recorded_protected_and_a_clear_records_no_value()
    {
        await Smtp(Admin).ExecuteAsync(Transport, "secret", false, CancellationToken.None);

        _credentials.Password.Should().Be("secret");
        (await HeldAsync())[HouseholdSetting.SmtpPassword].Should().Be("protected:secret");
        _store.Operations.Should().NotContain(o => o.Payload.Contains("\"secret\"", StringComparison.Ordinal));

        await Smtp(Admin).ExecuteAsync(Transport, "ignored", true, CancellationToken.None);

        _credentials.Password.Should().BeNull();
        (await HeldAsync())[HouseholdSetting.SmtpPassword].Should().BeNull();
    }

    [Fact]
    public async Task A_user_cannot_change_smtp()
    {
        var act = () => Smtp(User).ExecuteAsync(Transport, "secret", false, CancellationToken.None);

        await act.Should().ThrowAsync<ProfileAdministrationException>();
        _settings.Smtp.Host.Should().BeEmpty();
        _credentials.Password.Should().BeNull();
        _store.Operations.Should().BeEmpty();
    }

    [Fact]
    public async Task Backup_records_the_cloud_policy_but_not_the_folders()
    {
        var backup = new BackupSettings { Enabled = true, Directory = @"D:\Backups", CloudFolderEnabled = true, CloudFolderRetention = 10 };
        await new UpdateBackupSettings(_settings, Admin, Log).ExecuteAsync(backup, CancellationToken.None);
        var recorded = _store.Operations.Count;

        await new UpdateBackupSettings(_settings, Admin, Log).ExecuteAsync(
            new BackupSettings { Enabled = false, Directory = @"E:\Other", CloudFolderEnabled = true, CloudFolderRetention = 10 },
            CancellationToken.None);

        _settings.Backup.Directory.Should().Be(@"E:\Other");
        _store.Operations.Should().HaveCount(recorded, "the local backup and the folders belong to the device");
        (await HeldAsync()).Should().Contain(HouseholdSetting.CloudBackupEnabled, "true")
            .And.Contain(HouseholdSetting.CloudBackupRetention, "10");
    }

    [Fact]
    public async Task A_user_changes_the_language_but_not_the_reference_country()
    {
        await Reconcile().ExecuteAsync(CancellationToken.None);
        var general = new UpdateGeneralSettings(_settings, User, Log);

        await general.ExecuteAsync(new UserSettings { Language = "it", ReferenceCountry = "IT" }, CancellationToken.None);
        var country = () => general.ExecuteAsync(new UserSettings { Language = "it", ReferenceCountry = "FR" },
            CancellationToken.None);

        _settings.User.Language.Should().Be("it");
        await country.Should().ThrowAsync<ProfileAdministrationException>();
        _settings.User.ReferenceCountry.Should().Be("IT");
        (await HeldAsync())[HouseholdSetting.ReferenceCountry].Should().Be("IT");
    }

    [Fact]
    public async Task An_admin_changes_the_reference_country()
    {
        await new UpdateGeneralSettings(_settings, Admin, Log)
            .ExecuteAsync(new UserSettings { ReferenceCountry = "FR" }, CancellationToken.None);

        (await HeldAsync())[HouseholdSetting.ReferenceCountry].Should().Be("FR");
    }

    [Fact]
    public async Task Reconcile_records_the_settings_and_the_password_once()
    {
        _settings.Smtp = Transport;
        _credentials.Password = "secret";

        var first = await Reconcile().ExecuteAsync(CancellationToken.None);
        var second = await Reconcile().ExecuteAsync(CancellationToken.None);

        first.Should().Be(1 + 7 + 4 + 1 + 1, "profile, SMTP, cloud backup, reference country, password");
        second.Should().Be(0);
        (await HeldAsync())[HouseholdSetting.SmtpPassword].Should().Be("protected:secret");
    }

    [Fact]
    public async Task Reconcile_records_a_password_cleared_or_changed_outside_the_app()
    {
        _credentials.Password = "secret";
        await Reconcile().ExecuteAsync(CancellationToken.None);

        _credentials.Password = "other";
        (await Reconcile().ExecuteAsync(CancellationToken.None)).Should().Be(1);
        _credentials.Password = null;
        (await Reconcile().ExecuteAsync(CancellationToken.None)).Should().Be(1);

        (await HeldAsync())[HouseholdSetting.SmtpPassword].Should().BeNull();
    }

    [Fact]
    public async Task Without_a_password_reconcile_records_none()
    {
        await Reconcile().ExecuteAsync(CancellationToken.None);

        (await HeldAsync()).Should().NotContainKey(HouseholdSetting.SmtpPassword);
    }

    [Fact]
    public async Task A_recorded_password_this_device_cannot_read_is_recorded_again()
    {
        _credentials.Password = "secret";
        await Log.AppendAsync([new HouseholdSettingChanged(HouseholdSetting.SmtpPassword, "dpapi-of-another-user")],
            CancellationToken.None);

        (await Reconcile().ExecuteAsync(CancellationToken.None)).Should().BeGreaterThan(0);

        (await HeldAsync())[HouseholdSetting.SmtpPassword].Should().Be("protected:secret");
    }
}
