using System.Reflection;
using FluentAssertions;
using MedReminder.Application.Abstractions;
using Xunit;

namespace MedReminder.Application.Tests.Architecture;

// B.1 Phase 3a guard (docs/analysis/ANALYSIS-B1-MOBILE-SYNC.md §7.2):
// every Application service that saves a unit of work also takes
// IOperationLog, so a new use case cannot forget to emit its operations.
// OperationEmissionTests checks what each one emits.
public class OperationEmissionGuardTests
{
    // Writers of derived rows and device-local rows only (§3.4, §4.2),
    // and the apply step: nothing they write is a new local operation.
    private static readonly HashSet<string> DeviceLocalWriters = new(StringComparer.Ordinal)
    {
        "ConsumptionCatchUp",  // derived ledger rows
        "MedicationMonitor",   // NotificationEvents
        "DoseReminderService", // DoseReminderEvents
        // The sync apply layer (§7.2): it writes what other devices
        // produced, which is already in their logs.
        "ApplyRemoteOperations",
        // Genesis register versions: the values every device starts from.
        "SyncGenesis",
        // Sync bookkeeping (Phase 3c): published marks and peer progress.
        "SyncEngine",
        "CreateSyncGroup",
        "ResetSyncGeneration",
    };

    [Fact]
    public void Every_writer_takes_the_operation_log()
    {
        var writers = typeof(IUnitOfWork).Assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract)
            .Where(t => t.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
                .Any(c => c.GetParameters().Any(p => p.ParameterType == typeof(IUnitOfWork))))
            .ToList();

        writers.Select(t => t.Name).Should().Contain(["AddMedicine", "RetractFact", "LinkMedicineToReferenceUseCase"]);
        DeviceLocalWriters.Should().BeSubsetOf(writers.Select(t => t.Name));

        var missing = writers
            .Where(t => !DeviceLocalWriters.Contains(t.Name))
            .Where(t => !t.GetConstructors().Any(c => c.GetParameters().Any(p => p.ParameterType == typeof(IOperationLog))))
            .Select(t => t.FullName);

        missing.Should().BeEmpty("a service that saves user facts must record them as sync operations");
    }
}
