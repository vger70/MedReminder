namespace MedReminder.Application.Monitoring;

// Process-wide gate that serializes ConsumptionCatchUp.RunAsync and
// MedicationMonitor.RunAsync.
//
// Both are started from two places: the MedicationMonitorHostedService
// tick and the "Check now" command in MainForm. Each caller opens its
// own DI scope and DbContext, so without a gate two catch-ups can read
// the same "last consumption day" before either commits and then both
// write the same automatic consumption days (double decrement). The
// same interleaving lets two monitor passes both see "not yet notified"
// and send the low-stock warning twice.
//
// A database unique constraint cannot replace this gate: manual
// intakes (RegisterIntake) legitimately write several Consumption
// movements for the same medicine and day, with the same OccurredAt.
//
// A process-wide lock is sufficient because the single-instance mutex
// guarantees one process per Windows session, and each profile's
// database is opened by that process only.
internal static class MonitoringGate
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
}
