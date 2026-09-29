namespace MedReminder.Domain.Sync;

// Pure hybrid-logical-clock rules (Kulkarni et al., "Logical Physical
// Clocks", 2014; docs/analysis/ANALYSIS-B1-MOBILE-SYNC.md §4.1). The
// caller keeps the last timestamp; these functions only compute the
// next one, so they are deterministic and testable.
//
// A timestamp issued by a device is always greater than every timestamp
// the device issued or received before, whatever its wall clock does:
// when the clock goes backwards, the physical part stays and the counter
// grows.
public static class HybridClock
{
    // Next timestamp for a local event. `last` is the greatest timestamp
    // this device has issued or received (null when there is none).
    public static HybridTimestamp Tick(HybridTimestamp? last, long nowMs, Guid deviceId)
    {
        if (last is not { } previous || nowMs > previous.PhysicalMs)
        {
            return new HybridTimestamp(nowMs, 0, deviceId);
        }
        return new HybridTimestamp(previous.PhysicalMs, checked(previous.Counter + 1), deviceId);
    }

    // Timestamp after receiving `remote`: greater than both `last` and
    // `remote`. Used by the apply step (Phase 3b).
    public static HybridTimestamp Receive(
        HybridTimestamp? last, HybridTimestamp remote, long nowMs, Guid deviceId)
    {
        var lastPhysical = last?.PhysicalMs ?? long.MinValue;
        var physical = Math.Max(nowMs, Math.Max(lastPhysical, remote.PhysicalMs));

        int counter;
        if (physical == lastPhysical && physical == remote.PhysicalMs)
            counter = checked(Math.Max(last!.Value.Counter, remote.Counter) + 1);
        else if (physical == lastPhysical)
            counter = checked(last!.Value.Counter + 1);
        else if (physical == remote.PhysicalMs)
            counter = checked(remote.Counter + 1);
        else
            counter = 0;

        return new HybridTimestamp(physical, counter, deviceId);
    }
}
