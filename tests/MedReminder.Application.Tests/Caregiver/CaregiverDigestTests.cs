using FluentAssertions;
using MedReminder.Application.Notifications;
using MedReminder.Application.Overview;
using MedReminder.Application.Tests.Support;
using MedReminder.Application.UseCases;
using MedReminder.Domain.Notifications;
using MedReminder.Domain.Sync;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MedReminder.Application.Tests.Caregiver;

// Caregiver options (docs/notes/EVOLUTION-PROPOSALS-2.md §3.8): the kinds
// of email copied, and the weekly stock summary.
public class CaregiverDigestTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 10, 1);

    private readonly ApplicationTestScope _scope = new(Now);
    private readonly InMemoryProfileSettingsStore _settings = new("Mario");

    private CaregiverDigest Digest(JsonDictionaryLocalizationService? loc = null)
    {
        loc ??= new JsonDictionaryLocalizationService("en");
        var overview = new MedicineOverviewLoader(_scope.Medicines, _scope.Stock, _scope.Schedules, _scope.Suspensions,
            _scope.Slots, _scope.Clock, loc);
        return new CaregiverDigest(_settings, overview, _scope.Email, _scope.Operations, loc,
            NullLogger<CaregiverDigest>.Instance);
    }

    private void Configure(string digest = CaregiverDigestFrequency.Weekly, string caregiver = "carer@example.org")
        => _settings.Write(new Dictionary<string, string?>
        {
            [ProfileSetting.CaregiverAddress] = caregiver,
            [ProfileSetting.CaregiverDigest] = digest,
        });

    private Task<Guid> AddMedicineAsync(string name, decimal stock)
        => _scope.AddMedicine.ExecuteAsync(new AddMedicineCommand(
            Name: name, Unit: "tablets", DosePerAdministration: 1m, AdministrationsPerDay: 1,
            StartDate: Today, ThresholdDays: 7, NotificationChannels: NotificationChannels.Windows, EndDate: null,
            InitialQuantity: stock), default);

    [Fact]
    public void Every_kind_is_copied_until_the_profile_chooses()
    {
        CaregiverEmails.Parse("").Should().BeEquivalentTo(CaregiverEmails.Choices);
        CaregiverEmails.Parse("None").Should().BeEmpty();
        CaregiverEmails.Parse("LowStock, Deadline,Future").Should().BeEquivalentTo([EmailKind.LowStock, EmailKind.Deadline]);
        CaregiverEmails.Copies("LowStock", EmailKind.DoseReminder).Should().BeFalse();
        CaregiverEmails.Copies("None", null).Should().BeTrue("an email with no kind is copied as before");

        CaregiverEmails.Format(CaregiverEmails.Choices).Should().BeEmpty();
        CaregiverEmails.Format([]).Should().Be("None");
        CaregiverEmails.Format([EmailKind.Shortage, EmailKind.LowStock]).Should().Be("LowStock,Shortage");
    }

    [Fact]
    public async Task The_summary_goes_to_the_caregiver_once_a_week_and_is_replicated()
    {
        _scope.EnableSync();
        Configure();
        await AddMedicineAsync("Enalapril", 30m);
        await AddMedicineAsync("Aspirin", 3m);
        var digest = Digest();

        (await digest.RunAsync(Today, sendsEmail: true, default)).Should().BeTrue();
        (await digest.RunAsync(Today.AddDays(6), sendsEmail: true, default)).Should().BeFalse();
        (await digest.RunAsync(Today.AddDays(7), sendsEmail: true, default)).Should().BeTrue();

        _scope.Email.Sent.Should().HaveCount(2).And.OnlyContain(m => m.Kind == EmailKind.Digest);
        var body = _scope.Email.Sent[0].Body;
        body.Should().Contain("Enalapril").And.Contain("Aspirin").And.Contain("Mario");
        body.IndexOf("Aspirin", StringComparison.Ordinal).Should().BeLessThan(body.IndexOf("Enalapril", StringComparison.Ordinal),
            "the medicine closest to running out comes first");
        _settings.Read()[ProfileSetting.CaregiverDigestSentOn].Should().Be("2026-10-08");
        _scope.SyncOperations.All.Where(o => o.Type == nameof(ProfileSettingChanged)).Should().HaveCount(2);
    }

    [Fact]
    public async Task No_summary_without_caregiver_choice_or_email_role()
    {
        Configure(digest: CaregiverDigestFrequency.Off);
        (await Digest().RunAsync(Today, sendsEmail: true, default)).Should().BeFalse();

        Configure(caregiver: "");
        (await Digest().RunAsync(Today, sendsEmail: true, default)).Should().BeFalse();

        Configure();
        (await Digest().RunAsync(Today, sendsEmail: false, default)).Should().BeFalse("only a device that sends email");
        _scope.Email.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task A_summary_another_device_sent_this_week_is_not_sent_again()
    {
        Configure();
        _settings.Write(new Dictionary<string, string?> { [ProfileSetting.CaregiverDigestSentOn] = "2026-09-28" });

        (await Digest().RunAsync(Today, sendsEmail: true, default)).Should().BeFalse();
        (await Digest().RunAsync(new DateOnly(2026, 10, 5), sendsEmail: true, default)).Should().BeTrue();
    }

    [Fact]
    public async Task A_failed_summary_is_attempted_again()
    {
        Configure();
        _scope.Email.ShouldFail = true;
        (await Digest().RunAsync(Today, sendsEmail: true, default)).Should().BeFalse();
        _settings.Read()[ProfileSetting.CaregiverDigestSentOn].Should().BeEmpty();

        _scope.Email.ShouldFail = false;
        (await Digest().RunAsync(Today, sendsEmail: true, default)).Should().BeTrue();
    }

    [Fact]
    public async Task Automated_emails_carry_their_kind()
    {
        await _scope.AddMedicine.ExecuteAsync(new AddMedicineCommand(
            Name: "Enalapril", Unit: "tablets", DosePerAdministration: 1m, AdministrationsPerDay: 1,
            StartDate: Today, ThresholdDays: 7, NotificationChannels: NotificationChannels.Email, EndDate: null,
            InitialQuantity: 3m), default);

        await _scope.Monitor.RunAsync(default);

        _scope.Email.Sent.Should().ContainSingle().Which.Kind.Should().Be(EmailKind.LowStock);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("it")]
    [InlineData("fr")]
    [InlineData("es")]
    [InlineData("de")]
    public async Task Every_language_has_the_summary_texts(string language)
    {
        var loc = new JsonDictionaryLocalizationService(language);
        Configure();
        await AddMedicineAsync("Enalapril", 30m);

        (await Digest(loc).RunAsync(Today, sendsEmail: true, default)).Should().BeTrue();
        CaregiverDigest.Build("Mario", Today, [], loc);
        foreach (var kind in CaregiverEmails.Choices) loc.Get("Ui.SettingsDialog.Notifications.Caregiver.Kind." + kind);

        loc.FallbackHits.Should().BeEmpty();
        _scope.Email.Sent.Single().Body.Should().Contain("Enalapril").And.Contain(Today.ToString("d", loc.CurrentCulture));
    }
}
