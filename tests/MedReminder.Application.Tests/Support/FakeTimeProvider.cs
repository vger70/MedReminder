namespace MedReminder.Application.Tests.Support;

// TimeProvider di test: orologio manuale in UTC. LocalTimeZone forzato a
// UTC per rendere deterministica la conversione "local day" nei test —
// evita che il fuso della macchina di build influenzi il risultato.
internal sealed class FakeTimeProvider : TimeProvider
{
    private DateTimeOffset _utcNow;

    public FakeTimeProvider(DateTimeOffset initial)
    {
        _utcNow = initial.ToUniversalTime();
    }

    public override DateTimeOffset GetUtcNow() => _utcNow;

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

    public void SetUtcNow(DateTimeOffset newNow) => _utcNow = newNow.ToUniversalTime();

    public void AdvanceBy(TimeSpan delta) => _utcNow = _utcNow + delta;
}
