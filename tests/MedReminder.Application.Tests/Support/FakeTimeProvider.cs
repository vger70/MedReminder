namespace MedReminder.Application.Tests.Support;

// TimeProvider di test: orologio manuale in UTC. LocalTimeZone forzato a
// UTC per rendere deterministica la conversione "local day" nei test —
// prevents the build machine's time zone from affecting the result.
internal sealed class FakeTimeProvider : TimeProvider
{
    private DateTimeOffset _utcNow;
    private readonly TimeZoneInfo _zone;

    // zone: a test that exercises local days and DST passes its own.
    public FakeTimeProvider(DateTimeOffset initial, TimeZoneInfo? zone = null)
    {
        _utcNow = initial.ToUniversalTime();
        _zone = zone ?? TimeZoneInfo.Utc;
    }

    public override DateTimeOffset GetUtcNow() => _utcNow;

    public override TimeZoneInfo LocalTimeZone => _zone;

    public void SetUtcNow(DateTimeOffset newNow) => _utcNow = newNow.ToUniversalTime();

    public void AdvanceBy(TimeSpan delta) => _utcNow = _utcNow + delta;
}
