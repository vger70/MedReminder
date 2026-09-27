namespace MedReminder.Application;

// Process-wide gate that serializes every write to the profile database
// made by the Application layer: all use cases (UseCases/, RetractFact,
// LinkMedicineToReferenceUseCase), ConsumptionCatchUp and
// MedicationMonitor. From Phase 3b the sync apply step runs under it too.
//
// Why every use case (B.1 Phase 3a, docs/analysis/
// ANALYSIS-B1-MOBILE-SYNC.md §7.4): a use case reads, decides and saves
// in its own DI scope and DbContext. Anything else committing in
// between (a catch-up deriving the ledger, a sync apply changing the
// same medicine) makes the decision stale. The hybrid clock of the
// operation log also relies on it: the next timestamp is read from the
// last committed one.
//
// Until Phase 3a the gate was MonitoringGate and covered only the
// catch-up, the monitor, RegisterIntake, ReconcileStock and RetractFact.
// The reasons it was introduced still hold: two catch-ups (tick and
// "Check now") could book the same automatic consumption twice, and two
// monitor passes could both send the same low-stock warning. A database
// unique constraint cannot replace it: manual intakes legitimately
// write several Consumption movements for the same medicine and day.
//
// Not reentrant: code running under the gate must not call another
// gated entry point. A process-wide lock is sufficient because the
// single-instance mutex guarantees one process per Windows session, and
// each profile's database is opened by that process only.
internal static class WriteGate
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public static async Task<T> RunExclusiveAsync<T>(
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);
        await Gate.WaitAsync(cancellationToken);
        try
        {
            return await action(cancellationToken);
        }
        finally
        {
            Gate.Release();
        }
    }

    public static Task RunExclusiveAsync(
        Func<CancellationToken, Task> action,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);
        return RunExclusiveAsync(async ct =>
        {
            await action(ct);
            return true;
        }, cancellationToken);
    }
}
