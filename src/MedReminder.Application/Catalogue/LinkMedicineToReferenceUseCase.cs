using MedReminder.Application.Abstractions;
using MedReminder.Domain.Catalogue;
using MedReminder.Domain.Medicines;

namespace MedReminder.Application.Catalogue;

// Associates a user-authored Medicine with a ReferenceMedicine row
// picked from the catalogue. Populates NationalCode, AtcCode,
// LinkedReferenceMedicineId, and — only if the user had left it
// blank — ActiveIngredient (concatenating the reference names).
// Never overwrites free-text notes, dose, schedule, threshold,
// suspension state or the medicine's stock epoch.
public sealed class LinkMedicineToReferenceUseCase
{
    private readonly IMedicineRepository _medicines;
    private readonly IReferenceCatalogueQueryService _query;
    private readonly IUnitOfWork _uow;
    private readonly TimeProvider _clock;

    public LinkMedicineToReferenceUseCase(
        IMedicineRepository medicines,
        IReferenceCatalogueQueryService query,
        IUnitOfWork uow,
        TimeProvider clock)
    {
        _medicines = medicines;
        _query = query;
        _uow = uow;
        _clock = clock;
    }

    public async Task<LinkResult> ExecuteAsync(
        Guid medicineId,
        CountryCode referenceCountry,
        string nationalCode,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(nationalCode))
        {
            throw new ArgumentException("National code is required.", nameof(nationalCode));
        }

        var medicine = await _medicines.GetAsync(medicineId, cancellationToken)
            ?? throw new InvalidOperationException($"Medicine {medicineId} not found.");

        var reference = await _query.GetByNationalCodeAsync(referenceCountry, nationalCode.Trim(), cancellationToken);
        if (reference is null)
        {
            return LinkResult.ReferenceNotFound;
        }

        medicine.NationalCode = reference.NationalCode;
        medicine.LinkedReferenceMedicineId = reference.Id;

        var atc = reference.ActiveIngredients
            .Select(a => a.Atc)
            .FirstOrDefault(a => a.HasValue);
        medicine.AtcCode = atc;

        if (string.IsNullOrWhiteSpace(medicine.ActiveIngredient)
            && reference.ActiveIngredients.Count > 0)
        {
            medicine.ActiveIngredient = string.Join(
                " / ",
                reference.ActiveIngredients.Select(a => a.Name));
        }

        medicine.UpdatedAt = _clock.GetUtcNow();

        await _medicines.UpdateAsync(medicine, cancellationToken);
        _ = await _uow.SaveChangesAsync(cancellationToken);

        return LinkResult.Linked;
    }
}

public enum LinkResult
{
    Linked = 0,
    ReferenceNotFound = 1,
}
