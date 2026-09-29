namespace MedReminder.Domain.Catalogue;

// A pharmacologically-active substance sourced from the reference
// catalogue (e.g. "PARACETAMOLO"). The identity carries the country
// so that different national naming conventions do not collide.
public sealed class ReferenceActiveIngredient
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required CountryCode Country { get; init; }

    public required string Name { get; init; }

    public AtcCode? Atc { get; init; }
}
