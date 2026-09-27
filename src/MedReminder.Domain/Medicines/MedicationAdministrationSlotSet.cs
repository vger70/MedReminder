namespace MedReminder.Domain.Medicines;

// One recorded version of a medicine's administration slots (B.1,
// docs/analysis/ANALYSIS-B1-MOBILE-SYNC.md §4.2): every change of the
// slots appends a set instead of replacing the rows, so a derivation
// can tell which slots applied on a past day. A set may be empty: that
// records "no slots, back to dose x frequency".
//
//   EffectiveFrom -> the medicine's StartDate for the set given at
//                    creation (or found by the boot patch), otherwise
//                    the day the change was made.
//   RecordedAt    -> recording instant. The current slots are those of
//                    the most recently recorded set, which is today's
//                    replace-all behavior. Phase 3 adds the hybrid
//                    logical clock.
public sealed class MedicationAdministrationSlotSet
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required Guid MedicineId { get; init; }

    public required DateOnly EffectiveFrom { get; init; }

    public required DateTimeOffset RecordedAt { get; init; }
}
