using FluentAssertions;
using MedReminder.Application.Notifications;
using MedReminder.Application.Overview;
using MedReminder.Application.Packages;
using MedReminder.Application.Tests.Support;
using MedReminder.Application.UseCases;
using MedReminder.Domain.Notifications;
using MedReminder.Domain.Stock;
using MedReminder.Domain.Sync;
using Xunit;

namespace MedReminder.Application.Tests.Packages;

// Package expiry notices (docs/analysis/ANALYSIS-PACKAGE-EXPIRY.md §4):
// once per stage, expired covering soon, used-up packages left out, the
// medicine's channels, one email per pass, retry on failure, lead days
// from the profile settings.
public class PackageExpiryNoticeTests
{
    private static readonly DateOnly Today = new(2026, 9, 13);
    private readonly ApplicationTestScope _scope = new();

    private Task<Guid> AddMedicineAsync(string name = "Timolol",
        NotificationChannels channels = NotificationChannels.Windows)
        => _scope.AddMedicine.ExecuteAsync(new AddMedicineCommand(
            Name: name, Unit: "ml", DosePerAdministration: 0.1m, AdministrationsPerDay: 2,
            StartDate: Today, ThresholdDays: 7, NotificationChannels: channels, EndDate: null,
            InitialQuantity: 30m), default);

    private Task<Guid> PackageAsync(Guid medicine, DateOnly expiresOn, decimal quantity = 5m)
        => _scope.SaveStockPackage.ExecuteAsync(
            new SaveStockPackageCommand(null, medicine, quantity, expiresOn, null, null, null), default);

    private Task<int> RunAsync(bool sendsEmail = true)
        => _scope.PackageExpiryNotices.RunAsync(
            DateOnly.FromDateTime(_scope.Clock.GetUtcNow().UtcDateTime), sendsEmail, default);

    [Fact]
    public async Task A_package_expiring_soon_is_notified_once()
    {
        var medicine = await AddMedicineAsync();
        await PackageAsync(medicine, new DateOnly(2026, 9, 30));

        (await RunAsync()).Should().Be(1);
        (await RunAsync()).Should().Be(0);

        _scope.Windows.Sent.Should().ContainSingle().Which.Title.Should().Contain("expires soon");
        _scope.Windows.Targets.Should().ContainSingle().Which
            .Should().Be(NotificationTarget.PackageExpiry(medicine));
        _scope.PackageNoticeEvents.All.Should().ContainSingle().Which.Stage
            .Should().Be(PackageExpiryNoticeEvent.SoonStage);
    }

    [Fact]
    public async Task A_package_first_seen_expired_gets_the_expired_notice_only()
    {
        var medicine = await AddMedicineAsync();
        await PackageAsync(medicine, new DateOnly(2026, 9, 12));

        await RunAsync();

        _scope.Windows.Sent.Should().ContainSingle().Which.Title.Should().Contain("has expired");
        _scope.PackageNoticeEvents.All.Should().ContainSingle().Which.Stage
            .Should().Be(PackageExpiryNoticeEvent.ExpiredStage);
    }

    [Fact]
    public async Task The_expired_notice_follows_the_expiring_soon_one()
    {
        var medicine = await AddMedicineAsync();
        await PackageAsync(medicine, new DateOnly(2026, 9, 20));

        await RunAsync();
        _scope.Clock.AdvanceBy(TimeSpan.FromDays(8));
        await RunAsync();
        await RunAsync();

        _scope.Windows.Sent.Select(s => s.Title).Should().HaveCount(2)
            .And.Satisfy(t => t.Contains("expires soon"), t => t.Contains("has expired"));
    }

    [Fact]
    public async Task A_used_up_package_is_not_notified()
    {
        // 30 in stock: the later package holds all of it.
        var medicine = await AddMedicineAsync();
        await PackageAsync(medicine, new DateOnly(2026, 8, 31), 10m);
        await PackageAsync(medicine, new DateOnly(2027, 12, 31), 30m);

        (await RunAsync()).Should().Be(0);
        _scope.Windows.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task A_medicine_without_channels_is_left_out_and_an_inactive_one_is_notified()
    {
        var silent = await AddMedicineAsync("Silent", NotificationChannels.None);
        var inactive = await AddMedicineAsync("Inactive");
        await PackageAsync(silent, new DateOnly(2026, 9, 1));
        await PackageAsync(inactive, new DateOnly(2026, 9, 1));
        (await _scope.Medicines.GetAsync(inactive, default))!.IsActive = false;

        await RunAsync();

        _scope.Windows.Targets.Should().ContainSingle().Which.MedicineId.Should().Be(inactive);
    }

    [Fact]
    public async Task One_email_per_pass_lists_every_medicine_and_only_where_this_device_sends_email()
    {
        var first = await AddMedicineAsync("First", NotificationChannels.Email);
        var second = await AddMedicineAsync("Second", NotificationChannels.Both);
        var third = await AddMedicineAsync("Third", NotificationChannels.Email);
        await PackageAsync(first, new DateOnly(2026, 9, 1));
        await PackageAsync(second, new DateOnly(2026, 9, 25));

        (await RunAsync(sendsEmail: false)).Should().Be(1, "only the toast of Second can be shown");
        _scope.Email.Sent.Should().BeEmpty();

        await PackageAsync(third, new DateOnly(2026, 9, 2));
        await RunAsync(sendsEmail: true);

        var email = _scope.Email.Sent.Should().ContainSingle().Subject;
        email.Kind.Should().Be(EmailKind.PackageExpiry);
        email.Body.Should().Contain("First").And.Contain("Third");
        email.Body.Should().NotContain("Second", "Second was notified by its toast already");
    }

    [Fact]
    public async Task A_failed_notice_is_tried_again()
    {
        var medicine = await AddMedicineAsync();
        await PackageAsync(medicine, new DateOnly(2026, 9, 1));
        _scope.Windows.ShouldFail = true;

        (await RunAsync()).Should().Be(0);
        _scope.PackageNoticeEvents.All.Should().BeEmpty();

        _scope.Windows.ShouldFail = false;
        (await RunAsync()).Should().Be(1);
    }

    [Theory]
    [InlineData("10", 0)]
    [InlineData("20", 1)]
    [InlineData("0", 0)]
    [InlineData("", 1)]
    public async Task The_printed_lead_comes_from_the_profile_settings(string leadDays, int expected)
    {
        // Expires in 17 days.
        var medicine = await AddMedicineAsync();
        await PackageAsync(medicine, new DateOnly(2026, 9, 30));
        _scope.ProfileSettings.Write(new Dictionary<string, string?> { [ProfileSetting.PackageExpiryLeadDays] = leadDays });

        (await RunAsync()).Should().Be(expected);
    }

    [Fact]
    public async Task The_monitor_runs_the_notices()
    {
        var medicine = await AddMedicineAsync();
        await PackageAsync(medicine, new DateOnly(2026, 9, 1));

        await _scope.Monitor.RunAsync(default);

        _scope.Windows.Targets.Should().Contain(NotificationTarget.PackageExpiry(medicine));
    }

    [Theory]
    [InlineData("", "", PackageLeadDays.DefaultPrinted, PackageLeadDays.DefaultInUse)]
    [InlineData("45", "7", 45, 7)]
    [InlineData("999", "-3", PackageLeadDays.MaxPrinted, 0)]
    [InlineData("soon", "x", PackageLeadDays.DefaultPrinted, PackageLeadDays.DefaultInUse)]
    public void Lead_days_are_read_from_the_settings(string printed, string inUse, int expectedPrinted, int expectedInUse)
        => PackageSettings.LeadDays(printed, inUse).Should().Be(new PackageLeadDays(expectedPrinted, expectedInUse));

    [Fact]
    public async Task Saving_the_settings_records_the_lead_days()
    {
        _scope.EnableSync();
        var update = new UpdateNotificationSettings(_scope.ProfileSettings, _scope.Registers, _scope.Operations, _scope.Uow);

        await update.ExecuteAsync("", "", "", default, packageExpiryLeadDays: "45", packageInUseLeadDays: "7");

        var settings = _scope.ProfileSettings.Read();
        settings[ProfileSetting.PackageExpiryLeadDays].Should().Be("45");
        settings[ProfileSetting.PackageInUseLeadDays].Should().Be("7");
        (await _scope.SyncOperations.ListAllAsync(default))
            .Count(o => o.Type == "ProfileSettingChanged" && o.Payload.Contains("PackageExpiryLeadDays"))
            .Should().Be(1);
    }

    [Fact]
    public void The_caregiver_digest_tells_an_expired_package()
    {
        var loc = new JsonDictionaryLocalizationService("en");
        var item = new MedicineListItem
        {
            Name = "Timolol", Unit = "ml", CurrentStock = 4m, IsActive = true, StatusDisplay = "OK",
            NextExpiry = new DateOnly(2026, 9, 1), NextExpiryStatus = PackageExpiryStatus.Expired,
            ExpiryDisplay = "9/1/2026 (expired)",
        };

        var message = CaregiverDigest.Build("Mario", Today, [item], loc);

        message.Body.Should().Contain(loc.Get("Notifications.Digest.Expiry", "9/1/2026 (expired)"));
    }
}
