using MedReminder.Domain.Sync;

namespace MedReminder.Application.Abstractions;

// Local store of the household (household feature, step H2;
// docs/analysis/ANALYSIS-HOUSEHOLD-MASTER-DEVICE.md §5.3): the operation
// log and the register versions, under
// %LOCALAPPDATA%\MedReminder\household\. One store per installation,
// whatever profile is open.
public interface IHouseholdStore
{
    // The identity of this installation in its household, created on
    // first use: a household of one, with this device.
    Task<HouseholdIdentity> EnsureCreatedAsync(CancellationToken cancellationToken);

    // The greatest timestamp in the log; null when the log is empty.
    Task<HybridTimestamp?> GetLatestTimestampAsync(CancellationToken cancellationToken);

    // The operation and the register versions it writes, in one
    // transaction.
    Task AppendAsync(HouseholdOperationRecord operation, IReadOnlyList<HouseholdRegisterVersion> writes,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<HouseholdRegisterVersion>> ListRegistersAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<HouseholdOperationRecord>> ListOperationsAsync(CancellationToken cancellationToken);
}

public sealed record HouseholdIdentity(Guid HouseholdId, Guid DeviceId, int Generation);

public sealed record HouseholdOperationRecord(
    Guid Id,
    HybridTimestamp Timestamp,
    int Generation,
    string Type,
    int SchemaVersion,
    string ProfileId,
    string Payload);

public sealed record HouseholdRegisterVersion(
    string ProfileId,
    string Register,
    HybridTimestamp Version,
    string? Value);
