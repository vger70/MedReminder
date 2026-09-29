using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace MedReminder.Infrastructure.Persistence.ValueConverters;

// The EF Core 10 SQLite provider refuses ORDER BY and aggregates
// (Max / Min) on DateTimeOffset (mapped by default to ISO 8601 TEXT
// with offset — no safe SQL translation exists). We therefore convert
// every DateTimeOffset to INTEGER (long) holding UtcTicks: long
// orders and aggregates without issue.
//
// On the way back, DateTimeOffset is reconstructed with a zero
// offset (UTC): callers that want the "local day" of the movement
// must convert it explicitly via TimeProvider.LocalTimeZone (see
// StockMovementRepository).
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
