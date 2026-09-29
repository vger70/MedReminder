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

    // Step H3a: replication of the household.

    // False when an operation with that id is already in the log.
    Task<bool> ExistsAsync(Guid operationId, CancellationToken cancellationToken);

    // This device's operations not yet in a published segment.
    Task<IReadOnlyList<HouseholdOperationRecord>> ListPendingAsync(Guid deviceId, CancellationToken cancellationToken);

    // Marks operations as published in segment seq (0: in the genesis).
    Task MarkPublishedAsync(IReadOnlyList<Guid> operationIds, int seq, CancellationToken cancellationToken);

    // Applied vector: the last segment applied from each other device.
    Task<IReadOnlyDictionary<Guid, int>> GetAppliedAsync(CancellationToken cancellationToken);

    Task SetAppliedAsync(Guid deviceId, int seq, CancellationToken cancellationToken);

    Task SaveIdentityAsync(HouseholdIdentity identity, CancellationToken cancellationToken);

    // Joining another household: the log, the registers and the applied
    // vector are emptied and the identity replaced.
    Task ResetAsync(HouseholdIdentity identity, CancellationToken cancellationToken);
}

// Step H3a: Storage is where the household group lives once published
// (null for a household of one that was never published); KeyVersion the
// household key in use; SegmentSeq the last segment this device published.
public sealed record HouseholdIdentity(
    Guid HouseholdId,
    Guid DeviceId,
    int Generation,
    int KeyVersion = 1,
    SyncTarget? Storage = null,
    string? DeviceName = null,
    int SegmentSeq = 0,
    // Step H4a: the end of the last household run that listed and applied
    // the household group; the master's lease counts from it.
    DateTimeOffset? LastSyncedAt = null);

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
