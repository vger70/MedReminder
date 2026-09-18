using MedReminder.Domain.Catalogue;
using MedReminder.Domain.Notifications;

namespace MedReminder.Domain.Medicines;

// Aggregate root: represents a medicine tracked by the user.
//
// Public setters allow EF Core deserialization; the business rules that
// constrain state transitions (activation, schedule change, StockEpoch
// increment after a positive movement, etc.) live in the Application
// layer, not on this class. Derived calculations (remaining stock, ETA,
// notify?) live in MedReminder.Domain.Calculations.
public sealed class Medicine
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required string Name { get; set; }

    public string? ActiveIngredient { get; set; }

    public string? Package { get; set; }

    // Free-form unit code (spec §4). A suggested list ("tablets",
    // "capsules", "ml", ...) is kept on the UI side: no constraint here
    // so unusual units are not blocked.
    public required string Unit { get; set; }

    public decimal DosePerAdministration { get; set; }

    public int AdministrationsPerDay { get; set; }

    public DateOnly StartDate { get; set; }

    public DateOnly? EndDate { get; set; }

    // Warning threshold expressed as estimated days remaining (spec §8).
    // 0 effectively disables the low-stock notification.
    public int ThresholdDays { get; set; }

    public string? DoctorName { get; set; }

    public string? Notes { get; set; }

    public bool IsActive { get; set; } = true;

    // Incremented by the Application on every positive stock movement
    // (spec §8: after a new refill the warning cycle restarts).
    // NotificationEvents tied to the previous epoch no longer block the
    // current cycle.
    public int StockEpoch { get; set; } = 1;

    public NotificationChannels NotificationChannels { get; set; }
        = NotificationChannels.Windows;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    // Optional link to the reference catalogue (ANALYSIS-DRUG-
    // CATALOGUE.md §2.5). All three fields stay NULL for user-authored
    // medicines that were never linked. Free-text Name /
    // ActiveIngredient remain the authoritative display fields.

    // National code in the reference catalogue (AIC for IT, EMA
    // product number for EU, national code for other countries).
    public string? NationalCode { get; set; }

    // ATC code inherited from the linked reference row.
    public AtcCode? AtcCode { get; set; }

    // Weak FK to reference_medicines.id: not enforced by a physical
    // FK because the reference catalogue lives in independently-
    // refreshable snapshots and its rows may be swapped out.
    public Guid? LinkedReferenceMedicineId { get; set; }
}
