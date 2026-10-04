namespace MedReminder.Domain.Catalogue;

// A shortage notice this device showed for a medicine (not replicated).
// One per medicine, package code and start date: a new shortage of the
// same package, with another start, gets a new notice.
public sealed class ShortageNoticeEvent
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required Guid MedicineId { get; init; }

    public required string Code { get; init; }

    public required DateOnly Start { get; init; }

    public required DateTimeOffset FiredAt { get; init; }
}
