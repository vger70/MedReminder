namespace MedReminder.Domain.Prescriptions;

// Rules of the prescription lifecycle. Validity periods differ between
// countries and kinds of prescription, so the default only pre-fills
// "valid until" (an Italian electronic prescription is valid for 30
// days); the user can change it.
public static class PrescriptionRules
{
    public const int DefaultValidityDays = 30;

    // Days before "valid until" when the reminder to collect is due.
    public const int ReminderLeadDays = 3;

    public const int MaxPackages = 99;

    public const int MaxCodeLength = 64;

    public static DateOnly DefaultValidUntil(DateOnly issuedOn)
        => issuedOn.AddDays(DefaultValidityDays - 1);

    // An issued prescription not collected yet whose last valid day is
    // near: from ReminderLeadDays before it to the day itself.
    public static bool ReminderDue(Prescription prescription, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(prescription);
        return prescription.StatusOn(today) == PrescriptionStatus.ToCollect
            && prescription.ValidUntil is { } until
            && today >= until.AddDays(-ReminderLeadDays);
    }

    // Null when the dates and numbers are consistent, else the reason.
    public static PrescriptionError? Validate(Prescription prescription)
    {
        ArgumentNullException.ThrowIfNull(prescription);
        if (prescription.RequestedOn is null && prescription.IssuedOn is null && prescription.CollectedOn is null)
            return PrescriptionError.NoDate;
        if (prescription.RequestedOn is { } requested && prescription.IssuedOn is { } issued && issued < requested)
            return PrescriptionError.IssuedBeforeRequested;
        if (prescription.IssuedOn is { } from && prescription.ValidUntil is { } until && until < from)
            return PrescriptionError.ValidBeforeIssued;
        if (prescription.IssuedOn is { } issuedOn && prescription.CollectedOn is { } collected && collected < issuedOn)
            return PrescriptionError.CollectedBeforeIssued;
        if (prescription.Packages is { } packages && (packages < 1 || packages > MaxPackages))
            return PrescriptionError.Packages;
        if (prescription.Code is { Length: > MaxCodeLength })
            return PrescriptionError.Code;
        return null;
    }
}

public enum PrescriptionError
{
    NoDate,
    IssuedBeforeRequested,
    ValidBeforeIssued,
    CollectedBeforeIssued,
    Packages,
    Code,
}
