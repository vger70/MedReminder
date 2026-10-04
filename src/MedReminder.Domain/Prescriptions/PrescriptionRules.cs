namespace MedReminder.Domain.Prescriptions;

// Rules of the prescription lifecycle. Validity periods differ between
// countries and kinds of prescription, so the default only pre-fills
// "valid until" (an Italian electronic prescription is valid for 30
// days); the user can change it.
//
// Repeatable prescriptions follow the same approach: the number of
// dispensations and the validity are the user's entry, the defaults only
// pre-fill the form. No minimum interval between dispensations is
// enforced: such rules differ by country and are not verified here.
public static class PrescriptionRules
{
    public const int DefaultValidityDays = 30;

    // Days before "valid until" when the reminder to collect is due.
    public const int ReminderLeadDays = 3;

    public const int MaxPackages = 99;

    public const int MaxCodeLength = 64;

    // Upper bound of Prescription.Dispensations.
    public const int MaxDispensations = 12;

    // Pre-fills "valid until" when a prescription is marked repeatable.
    public const int DefaultRepeatableValidityMonths = 12;

    public static DateOnly DefaultValidUntil(DateOnly issuedOn)
        => issuedOn.AddDays(DefaultValidityDays - 1);

    public static DateOnly DefaultRepeatableValidUntil(DateOnly issuedOn)
        => issuedOn.AddMonths(DefaultRepeatableValidityMonths).AddDays(-1);

    // Dispensations still to collect: for a repeatable prescription the
    // allowed ones minus those recorded (never below zero, since two
    // devices may record more than allowed between syncs); for a single
    // prescription 1 until it is collected.
    public static int DispensationsLeft(Prescription prescription, int dispensationsCollected)
    {
        ArgumentNullException.ThrowIfNull(prescription);
        if (!prescription.IsRepeatable) return prescription.CollectedOn is null ? 1 : 0;
        return Math.Max(0, prescription.Dispensations!.Value - dispensationsCollected);
    }

    public static int DispensationsLeft(Prescription prescription, IReadOnlyCollection<PrescriptionDispensation> dispensations)
    {
        ArgumentNullException.ThrowIfNull(dispensations);
        return DispensationsLeft(prescription, dispensations.Count);
    }

    // An issued prescription not collected yet (for a repeatable one:
    // with dispensations left) whose last valid day is near: from
    // ReminderLeadDays before it to the day itself.
    public static bool ReminderDue(Prescription prescription, DateOnly today, int dispensationsCollected = 0)
    {
        ArgumentNullException.ThrowIfNull(prescription);
        return prescription.StatusOn(today, dispensationsCollected) == PrescriptionStatus.ToCollect
            && prescription.ValidUntil is { } until
            && today >= until.AddDays(-ReminderLeadDays);
    }

    // Null when the dates and numbers are consistent, else the reason.
    // `dispensations` are those the prescription would hold once saved.
    public static PrescriptionError? Validate(
        Prescription prescription, IReadOnlyCollection<PrescriptionDispensation>? dispensations = null)
    {
        ArgumentNullException.ThrowIfNull(prescription);
        var records = dispensations ?? [];
        if (prescription.RequestedOn is null && prescription.IssuedOn is null && prescription.CollectedOn is null
            && records.Count == 0)
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
        if (prescription.Dispensations is { } allowed && (allowed < 1 || allowed > MaxDispensations))
            return PrescriptionError.Dispensations;
        if (!prescription.IsRepeatable)
        {
            return records.Count > 0 ? PrescriptionError.DispensationsOnSingle : null;
        }
        if (prescription.CollectedOn is not null)
            return PrescriptionError.CollectedOnRepeatable;
        if (records.Count > prescription.Dispensations)
            return PrescriptionError.TooManyDispensations;
        foreach (var record in records)
        {
            if (prescription.IssuedOn is { } start && record.CollectedOn < start)
                return PrescriptionError.DispensationBeforeIssued;
            if (prescription.ValidUntil is { } end && record.CollectedOn > end)
                return PrescriptionError.DispensationAfterValidity;
            if (record.Packages is { } count && (count < 1 || count > MaxPackages))
                return PrescriptionError.Packages;
        }
        return null;
    }
}

// Member names are UI string keys (Ui.PrescriptionEditDialog.Error.<name>).
public enum PrescriptionError
{
    NoDate,
    IssuedBeforeRequested,
    ValidBeforeIssued,
    CollectedBeforeIssued,
    Packages,
    Code,
    Dispensations,
    DispensationsOnSingle,
    CollectedOnRepeatable,
    TooManyDispensations,
    DispensationBeforeIssued,
    DispensationAfterValidity,
}
