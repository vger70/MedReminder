namespace MedReminder.Application.Abstractions;

// Unità di lavoro: raggruppa più scritture in un singolo commit atomico.
// Per l'MVP è sufficiente SaveChangesAsync — EF Core garantisce di per
// sé la transazionalità del singolo SaveChanges.
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
