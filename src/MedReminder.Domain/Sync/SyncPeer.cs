namespace MedReminder.Domain.Sync;

// Sync progress per device of the group (table SyncPeers, B.1 Phase 3c,
// docs/analysis/ANALYSIS-B1-MOBILE-SYNC.md §5.3, §7.3).
//
// For another device, Seq is the last of its segments applied here, in
// order: the entries of all other devices form this device's applied
// vector. For this device, Seq is the last segment it published, and the
// checkpoint fields say when it last wrote one.
public sealed class SyncPeer
{
    public required Guid DeviceId { get; init; }

    public required int Generation { get; set; }

    public int Seq { get; set; }

    // This device only: checkpoints written, and the size of the
    // operation log at the last one.
    public int Checkpoints { get; set; }

    public long CheckpointOperations { get; set; }
}
