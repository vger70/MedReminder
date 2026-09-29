namespace MedReminder.Domain.Sync;

// Last-writer-wins resolution of one register (B.1 Phase 3b,
// docs/analysis/ANALYSIS-B1-MOBILE-SYNC.md §4.2, §4.5). Pure: the result
// depends only on the set of versions, never on the order they arrived
// in, so every device holding the same versions agrees.
//
// Winner: the version with the greatest HLC.
//
// Concurrency: a device writes a register only over the versions it has
// seen, and records the greatest one as the base of its write. Hybrid
// clocks make every version it had seen older than its write. So of two
// versions, the later one was written without seeing the earlier one
// exactly when its base is older than the earlier one (or it had none):
// they are concurrent. Two writes of one device are never concurrent.
//
// Conflicts (product owner, 2026-09-27: only concurrent writes): every
// version concurrent with the winner lost to it. A later write by a
// device that had seen both clears them, since it is concurrent with
// neither.
public static class RegisterMerge
{
    public sealed record Version(HybridTimestamp Timestamp, HybridTimestamp? Base, string? Value);

    public static Version? Winner(IEnumerable<Version> versions)
    {
        ArgumentNullException.ThrowIfNull(versions);
        return versions.MaxBy(v => v.Timestamp);
    }

    public static bool AreConcurrent(Version a, Version b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        if (a.Timestamp == b.Timestamp) return false;
        var (earlier, later) = a.Timestamp < b.Timestamp ? (a, b) : (b, a);
        return later.Base is not { } seen || seen < earlier.Timestamp;
    }

    // The versions that lost to the winner without its writer having
    // seen them, in HLC order.
    public static IReadOnlyList<Version> Losers(IReadOnlyCollection<Version> versions)
    {
        ArgumentNullException.ThrowIfNull(versions);
        var winner = Winner(versions);
        if (winner is null) return [];
        return [.. versions
            .Where(v => v.Timestamp != winner.Timestamp && AreConcurrent(v, winner))
            .OrderBy(v => v.Timestamp)];
    }
}
