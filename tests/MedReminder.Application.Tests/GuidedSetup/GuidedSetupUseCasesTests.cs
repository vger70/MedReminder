using FluentAssertions;
using MedReminder.Application.GuidedSetup;
using MedReminder.Application.Notifications;
using MedReminder.Application.Tests.Support;
using MedReminder.Application.UseCases;
using MedReminder.Domain.Notifications;
using MedReminder.Domain.Sync;
using Xunit;

namespace MedReminder.Application.Tests.GuidedSetup;

public class GuidedSetupUseCasesTests
{
    private static Task<Guid> AddAsync(ApplicationTestScope scope, string name, int threshold = 7,
        NotificationChannels channels = NotificationChannels.Windows)
        => scope.AddMedicine.ExecuteAsync(
            new AddMedicineCommand(name, "compresse", 1m, 1, new DateOnly(2026, 9, 1), threshold, channels,
                DoctorName: "Dr. Rossi", Notes: "  note  ", InitialQuantity: 30m),
            CancellationToken.None);

    private static ApplyGuidedSetupWarning Warning(ApplicationTestScope scope)
        => new(scope.Medicines, scope.UpdateMedicine);

    private static GuidedSetupEmail Email(ApplicationTestScope scope)
        => new(scope.ProfileSettings,
            new UpdateNotificationSettings(scope.ProfileSettings, scope.Registers, scope.Operations, scope.Uow));

    [Fact]
    public async Task Warning_is_applied_to_the_medicines_added_in_the_setup_only()
    {
        var scope = new ApplicationTestScope();
        var existing = await AddAsync(scope, "Enalapril");
        var added = await AddAsync(scope, "Ramipril");

        var applied = await Warning(scope).ExecuteAsync(
            [added], new NewMedicineDefaults(14, NotificationChannels.Both), CancellationToken.None);

        applied.Should().Be(1);
        var changed = await scope.Medicines.GetAsync(added, CancellationToken.None);
        changed!.ThresholdDays.Should().Be(14);
        changed.NotificationChannels.Should().Be(NotificationChannels.Both);
        var untouched = await scope.Medicines.GetAsync(existing, CancellationToken.None);
        untouched!.ThresholdDays.Should().Be(7);
        untouched.NotificationChannels.Should().Be(NotificationChannels.Windows);
    }

    [Fact]
    public async Task Warning_leaves_the_other_fields_of_the_medicine_as_stored()
    {
        var scope = new ApplicationTestScope();
        var id = await AddAsync(scope, "Ramipril");
        var medicine = await scope.Medicines.GetAsync(id, CancellationToken.None);
        // A value UpdateMedicine would trim if it were written.
        medicine!.Notes = "  note  ";

        await Warning(scope).ExecuteAsync([id], new NewMedicineDefaults(10, NotificationChannels.Email),
            CancellationToken.None);

        var after = await scope.Medicines.GetAsync(id, CancellationToken.None);
        after!.Name.Should().Be("Ramipril");
        after.Notes.Should().Be("  note  ");
        after.DoctorName.Should().Be("Dr. Rossi");
        after.IsActive.Should().BeTrue();
        after.ThresholdDays.Should().Be(10);
        after.NotificationChannels.Should().Be(NotificationChannels.Email);
    }

    [Fact]
    public async Task Warning_skips_a_medicine_deleted_meanwhile()
    {
        var scope = new ApplicationTestScope();
        var id = await AddAsync(scope, "Ramipril");

        var applied = await Warning(scope).ExecuteAsync([Guid.NewGuid(), id, id],
            new NewMedicineDefaults(10, NotificationChannels.Windows), CancellationToken.None);

        applied.Should().Be(1);
    }

    [Fact]
    public async Task Warning_refuses_no_channel()
    {
        var scope = new ApplicationTestScope();

        var act = () => Warning(scope).ExecuteAsync([], new NewMedicineDefaults(7, NotificationChannels.None),
            CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Saving_the_email_step_keeps_the_doctor_address()
    {
        var scope = new ApplicationTestScope();
        scope.ProfileSettings.Write(new Dictionary<string, string?>
        {
            [ProfileSetting.DoctorAddress] = "doctor@example.org",
            [ProfileSetting.PackageExpiryLeadDays] = "45",
        });

        await Email(scope).SaveAsync(new GuidedSetupEmailSettings(
                new GuidedSetupAddresses("me@example.org", "helper@example.org"),
                new HashSet<EmailKind> { EmailKind.LowStock },
                WeeklyDigest: true),
            CancellationToken.None);

        var stored = scope.ProfileSettings.Read();
        stored[ProfileSetting.DoctorAddress].Should().Be("doctor@example.org");
        stored[ProfileSetting.PackageExpiryLeadDays].Should().Be("45");
        stored[ProfileSetting.ToAddress].Should().Be("me@example.org");
        stored[ProfileSetting.CaregiverAddress].Should().Be("helper@example.org");
        stored[ProfileSetting.CaregiverEmails].Should().Be("LowStock");
        stored[ProfileSetting.CaregiverDigest].Should().Be(CaregiverDigestFrequency.Weekly);
    }

    [Fact]
    public void Reading_the_email_step_returns_the_stored_values()
    {
        var scope = new ApplicationTestScope();
        scope.ProfileSettings.Write(new Dictionary<string, string?>
        {
            [ProfileSetting.ToAddress] = " patient@example.org ",
            [ProfileSetting.CaregiverAddress] = "carer@example.org",
            [ProfileSetting.CaregiverEmails] = "",
            [ProfileSetting.CaregiverDigest] = "Off",
        });

        var read = Email(scope).Read();

        read.Addresses.Should().Be(new GuidedSetupAddresses("patient@example.org", "carer@example.org"));
        read.CaregiverEmails.Should().BeEquivalentTo(CaregiverEmails.Choices);
        read.WeeklyDigest.Should().BeFalse();
        read.HasRecipient.Should().BeTrue();
    }

    [Fact]
    public void No_address_means_no_recipient()
        => new GuidedSetupEmailSettings(new GuidedSetupAddresses("", ""), new HashSet<EmailKind>(), false)
            .HasRecipient.Should().BeFalse();
}
