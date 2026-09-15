using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace MedReminder.Infrastructure.Persistence.ValueConverters;

// Il provider SQLite di EF Core 10 rifiuta ORDER BY e le aggregate
// (Max/Min) su DateTimeOffset (mappato di default a TEXT ISO 8601 con
// offset — non c'è una traduzione sicura verso SQL). Convertiamo quindi
// tutti i DateTimeOffset a INTEGER (long) contenenti gli UtcTicks:
// long si ordina e si aggrega senza problemi.
//
// Al ritorno DateTimeOffset è ricostruito con offset zero (UTC): chi
// vuole il "giorno locale" del movimento deve convertirlo esplicitamente
// tramite TimeProvider.LocalTimeZone (vedi StockMovementRepository).
internal static class DateTimeOffsetConverters
{
    public static readonly ValueConverter<DateTimeOffset, long> ToUtcTicks =
        new(
            dto => dto.UtcTicks,
            ticks => new DateTimeOffset(ticks, TimeSpan.Zero));

    public static readonly ValueConverter<DateTimeOffset?, long?> ToUtcTicksNullable =
        new(
            dto => dto.HasValue ? dto.Value.UtcTicks : (long?)null,
            ticks => ticks.HasValue ? new DateTimeOffset(ticks.Value, TimeSpan.Zero) : (DateTimeOffset?)null);
}
