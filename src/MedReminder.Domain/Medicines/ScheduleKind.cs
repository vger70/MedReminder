namespace MedReminder.Domain.Medicines;

// Discriminator of the therapy schedule stored on each
// MedicationScheduleHistory entry (see ANALYSIS-A1-REGIMENS.md §2).
//
// The integer values are persisted verbatim in
// MedicationScheduleHistories.ScheduleKind — they must stay stable
// across releases. FixedDaily = 0 is the default carried by pre-A1
// rows via SQLite's DEFAULT 0, so their read path continues to
// return exactly the legacy dose × administrations/day.
public enum ScheduleKind
{
    FixedDaily = 0,
    Weekly = 1,
    Cyclic = 2,
    Tapering = 3,
    Prn = 4,
}
