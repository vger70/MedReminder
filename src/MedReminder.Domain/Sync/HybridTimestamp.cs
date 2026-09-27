namespace MedReminder.Domain.Sync;

// Hybrid logical clock timestamp (docs/analysis/ANALYSIS-B1-MOBILE-SYNC.md
// §4.1): wall-clock milliseconds, a logical counter for events inside
// the same millisecond or while the wall clock is behind, and the id of
// the device that issued it.
//
// The order is total and identical on every device: physical time, then
// counter, then device id. The device id is compared as its lowercase
// 32-digit hex string, ordinally, so the tie-break never depends on
// Guid.CompareTo or on how a store sorts GUID values.
public readonly record struct HybridTimestamp(long PhysicalMs, int Counter, Guid DeviceId)
    : IComparable<HybridTimestamp>
{
    public int CompareTo(HybridTimestamp other)
    {
        var byPhysical = PhysicalMs.CompareTo(other.PhysicalMs);
        if (byPhysical != 0) return byPhysical;
        var byCounter = Counter.CompareTo(other.Counter);
        if (byCounter != 0) return byCounter;
        return string.CompareOrdinal(DeviceId.ToString("N"), other.DeviceId.ToString("N"));
    }

    public static bool operator <(HybridTimestamp left, HybridTimestamp right) => left.CompareTo(right) < 0;
    public static bool operator >(HybridTimestamp left, HybridTimestamp right) => left.CompareTo(right) > 0;
    public static bool operator <=(HybridTimestamp left, HybridTimestamp right) => left.CompareTo(right) <= 0;
    public static bool operator >=(HybridTimestamp left, HybridTimestamp right) => left.CompareTo(right) >= 0;

    public override string ToString() => $"{PhysicalMs}.{Counter}@{DeviceId:N}";
}
