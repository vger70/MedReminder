namespace MedReminder.Domain.Calculations;

// The repetitions of a dose reminder while its slot is not recorded
// (mobile issue #128): repetition k (1..Count) fires at the slot instant
// plus Delay plus (k - 1) Interval. "Repeat" starts one interval after the
// slot; "delayed insistence" starts after a longer delay. Any intake of the
// slot (DoseSlots.Records) stops them; nothing else does.
public sealed record DoseRepetition
{
    // At most two digits, so the dedup key of a repetition (SlotKey: the
    // slot id and "#k") fits DoseReminderEvents.SlotKey (40 characters).
    public const int MaxCount = 99;

    public DoseRepetition(TimeSpan delay, TimeSpan interval, int count)
    {
        if (delay <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(delay));
        if (interval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(interval));
        if (count is < 1 or > MaxCount) throw new ArgumentOutOfRangeException(nameof(count));
        Delay = delay;
        Interval = interval;
        Count = count;
    }

    public TimeSpan Delay { get; }

    public TimeSpan Interval { get; }

    public int Count { get; }

    // Every `interval`, starting one interval after the slot.
    public static DoseRepetition Repeat(TimeSpan interval, int count) => new(interval, interval, count);

    // The instant of reminder k of a slot due at `slotAt`: the slot itself
    // for k = 0, repetition k for 1..Count.
    public DateTimeOffset At(DateTimeOffset slotAt, int k)
    {
        if (k < 0 || k > Count) throw new ArgumentOutOfRangeException(nameof(k));
        return k == 0 ? slotAt : slotAt + Delay + Interval * (k - 1);
    }

    // The last repetition due at `now` (0 when none is due yet: the slot's
    // own reminder).
    public int LatestDue(DateTimeOffset slotAt, DateTimeOffset now)
    {
        if (now < slotAt + Delay) return 0;
        // In long first: a short interval over a long span overflows int.
        var k = 1 + (now - slotAt - Delay).Ticks / Interval.Ticks;
        return (int)Math.Min(k, Count);
    }

    // The dedup key of the reminder k of a slot: the slot id alone for the
    // slot's own reminder (k = 0), as before repetitions.
    public static string SlotKey(Guid slotId, int k) => k == 0 ? slotId.ToString() : $"{slotId}#{k}";
}
