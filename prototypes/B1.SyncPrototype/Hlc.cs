namespace MedReminder.Prototypes.Sync;

// Hybrid logical clock timestamp (ANALYSIS-B1-MOBILE-SYNC.md §4.1).
// Total order: physical milliseconds, then logical counter, then device
// id as the final tie-breaker, so every device orders any two
// timestamps the same way.
public readonly record struct Hlc(long PhysicalMs, int Counter, Guid DeviceId) : IComparable<Hlc>
{
    public static Hlc Zero { get; } = new(0, 0, Guid.Empty);

    public int CompareTo(Hlc other)
    {
        var c = PhysicalMs.CompareTo(other.PhysicalMs);
        if (c != 0) return c;
        c = Counter.CompareTo(other.Counter);
        if (c != 0) return c;
        return DeviceId.CompareTo(other.DeviceId);
    }

    public static bool operator <(Hlc a, Hlc b) => a.CompareTo(b) < 0;
    public static bool operator >(Hlc a, Hlc b) => a.CompareTo(b) > 0;
    public static bool operator <=(Hlc a, Hlc b) => a.CompareTo(b) <= 0;
    public static bool operator >=(Hlc a, Hlc b) => a.CompareTo(b) >= 0;

    public override string ToString() => $"{PhysicalMs}.{Counter}.{DeviceId:N}";
}

// Per-device HLC generator. Physical time comes from a delegate so the
// simulation can give each device its own clock skew.
public sealed class HlcClock
{
    private readonly Guid _deviceId;
    private readonly Func<long> _physicalMs;
    private long _lastPhysical;
    private int _lastCounter;

    public HlcClock(Guid deviceId, Func<long> physicalMs)
    {
        _deviceId = deviceId;
        _physicalMs = physicalMs;
    }

    public Hlc Now()
    {
        var pt = _physicalMs();
        if (pt > _lastPhysical)
        {
            _lastPhysical = pt;
            _lastCounter = 0;
        }
        else
        {
            _lastCounter++;
        }
        return new Hlc(_lastPhysical, _lastCounter, _deviceId);
    }

    // Standard HLC receive rule: never issue a timestamp at or below one
    // already observed, so local edits made after a merge win over the
    // values they overwrite.
    public void Observe(Hlc remote)
    {
        var pt = _physicalMs();
        var max = Math.Max(Math.Max(pt, _lastPhysical), remote.PhysicalMs);
        if (max == _lastPhysical && max == remote.PhysicalMs)
            _lastCounter = Math.Max(_lastCounter, remote.Counter) + 1;
        else if (max == _lastPhysical)
            _lastCounter++;
        else if (max == remote.PhysicalMs)
            _lastCounter = remote.Counter + 1;
        else
            _lastCounter = 0;
        _lastPhysical = max;
    }
}
