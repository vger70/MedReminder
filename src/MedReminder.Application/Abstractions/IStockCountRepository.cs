using MedReminder.Domain.Stock;

namespace MedReminder.Application.Abstractions;

// Stock-count facts (B.1 Phase 2c-2), written by ReconcileStock.
public interface IStockCountRepository
{
    // In recording order.
    Task<IReadOnlyList<StockCount>> ListForMedicineAsync(
        Guid medicineId, CancellationToken cancellationToken);

    // Medicines with a count on `day` that materialized the day's
    // consumption. One query for the whole list.
    Task<IReadOnlyList<Guid>> ListMedicinesWithMaterializedCountAsync(DateOnly day, CancellationToken cancellationToken);

    Task AddAsync(StockCount count, CancellationToken cancellationToken);

    // Retraction of a mistaken count (RetractFact, B.1 Phase 2d).
    Task RemoveAsync(StockCount count, CancellationToken cancellationToken);
}
