namespace MedReminder.Application.Catalogue;

// Tells a USB HID scanner burst apart from human typing by the
// average interval between keystrokes. Used only to decide whether an
// unterminated payload may be submitted on idle
// (docs/analysis/ANALYSIS-A2-BARCODE-SCAN.md §5B.2); an explicit
// Enter / Tab always submits regardless of speed.
//
// Timestamps are supplied by the caller (milliseconds from any
// monotonic clock) so the class stays deterministic under test.
public sealed class ScanBurstDetector
{
    private long _firstTimestamp;
    private long _lastTimestamp;
    private int _count;

    public int KeystrokeCount => _count;

    public void Record(long timestampMilliseconds)
    {
        if (_count == 0) _firstTimestamp = timestampMilliseconds;
        _lastTimestamp = timestampMilliseconds;
        _count++;
    }

    public void Reset()
    {
        _count = 0;
        _firstTimestamp = 0;
        _lastTimestamp = 0;
    }

    // True when at least `minimumKeystrokes` were recorded and their
    // average spacing does not exceed the bound.
    public bool IsBurst(int minimumKeystrokes, int maxAverageIntervalMilliseconds)
    {
        if (_count < Math.Max(2, minimumKeystrokes)) return false;
        var elapsed = _lastTimestamp - _firstTimestamp;
        return elapsed <= (long)maxAverageIntervalMilliseconds * (_count - 1);
    }
}
