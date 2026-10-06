using MedReminder.Application.Abstractions;
using MedReminder.Application.Notifications;
using MedReminder.Application.UseCases;
using MedReminder.Domain.Notifications;
using MedReminder.Domain.Sync;

namespace MedReminder.Application.GuidedSetup;

// Warning step of the guided setup: the chosen lead time and channels
// on the medicines added during the setup, through UpdateMedicine. The
// command carries the stored values as its baseline, so only the two
// fields are written (and replicated); a medicine deleted meanwhile is
// skipped. Returns how many medicines were found.
public sealed class ApplyGuidedSetupWarning
{
    private readonly IMedicineRepository _medicines;
    private readonly UpdateMedicine _update;

    public ApplyGuidedSetupWarning(IMedicineRepository medicines, UpdateMedicine update)
    {
        _medicines = medicines;
        _update = update;
    }

    public async Task<int> ExecuteAsync(IReadOnlyCollection<Guid> medicineIds, NewMedicineDefaults warning,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(medicineIds);
        ArgumentNullException.ThrowIfNull(warning);
        if (NewMedicineDefaults.TryCreate(warning.ThresholdDays, warning.Channels) is null)
            throw new ArgumentException("The lead time or the channels are out of range.", nameof(warning));

        var applied = 0;
        foreach (var id in medicineIds.Distinct())
        {
            var medicine = await _medicines.GetAsync(id, cancellationToken);
            if (medicine is null) continue;
            var stored = new UpdateMedicineCommand(
                MedicineId: medicine.Id,
                Name: medicine.Name,
                ActiveIngredient: medicine.ActiveIngredient,
                Package: medicine.Package,
                Unit: medicine.Unit,
                ThresholdDays: medicine.ThresholdDays,
                NotificationChannels: medicine.NotificationChannels,
                EndDate: medicine.EndDate,
                DoctorName: medicine.DoctorName,
                Notes: medicine.Notes,
                IsActive: medicine.IsActive,
                RemindOnDose: medicine.RemindOnDose);
            await _update.ExecuteAsync(
                stored with
                {
                    ThresholdDays = warning.ThresholdDays,
                    NotificationChannels = warning.Channels,
                    Baseline = stored,
                },
                cancellationToken);
            applied++;
        }
        return applied;
    }
}

// What the email step shows and saves: the two addresses, the kinds of
// email copied to the caregiver and the weekly summary.
public sealed record GuidedSetupEmailSettings(
    GuidedSetupAddresses Addresses,
    IReadOnlySet<EmailKind> CaregiverEmails,
    bool WeeklyDigest)
{
    // The warnings go to ToAddress; the caregiver only gets copies.
    public bool HasRecipient => Addresses.ToAddress.Length > 0;
}

// Email step of the guided setup: reads the profile's recipients and
// saves them through UpdateNotificationSettings. The doctor address and
// the package lead days are not on the step: the stored doctor address
// is passed back unchanged (the method takes it and would otherwise
// clear it on every device of the profile), the lead days as null.
public sealed class GuidedSetupEmail
{
    private readonly IProfileSettingsStore _store;
    private readonly UpdateNotificationSettings _update;

    public GuidedSetupEmail(IProfileSettingsStore store, UpdateNotificationSettings update)
    {
        _store = store;
        _update = update;
    }

    public GuidedSetupEmailSettings Read()
    {
        var current = _store.Read();
        return new GuidedSetupEmailSettings(
            new GuidedSetupAddresses(
                Value(current, ProfileSetting.ToAddress),
                Value(current, ProfileSetting.CaregiverAddress)),
            CaregiverEmails.Parse(current.GetValueOrDefault(ProfileSetting.CaregiverEmails)),
            CaregiverDigestFrequency.IsWeekly(current.GetValueOrDefault(ProfileSetting.CaregiverDigest)));
    }

    public Task SaveAsync(GuidedSetupEmailSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var doctorAddress = Value(_store.Read(), ProfileSetting.DoctorAddress);
        return _update.ExecuteAsync(
            settings.Addresses.ToAddress,
            settings.Addresses.CaregiverAddress,
            doctorAddress,
            cancellationToken,
            caregiverEmails: Notifications.CaregiverEmails.Format(settings.CaregiverEmails),
            caregiverDigest: settings.WeeklyDigest ? CaregiverDigestFrequency.Weekly : CaregiverDigestFrequency.Off);
    }

    private static string Value(IReadOnlyDictionary<string, string?> settings, string name)
        => settings.GetValueOrDefault(name)?.Trim() ?? string.Empty;
}
