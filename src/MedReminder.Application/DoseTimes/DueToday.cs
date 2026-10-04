using MedReminder.Application.Abstractions;
using MedReminder.Domain.Calculations;
using MedReminder.Domain.Medicines;

namespace MedReminder.Application.DoseTimes;

// What IntradayConsumption needs besides one medicine's schedule, read
// once per screen: the instant, the time-of-day settings and the
// medicines whose today the ledger already booked.
public sealed record DueTodayContext(
    DateTime LocalNow,
    DoseTimeSettings Settings,
    IReadOnlySet<Guid> BookedToday)
{
    public DateOnly Today => DateOnly.FromDateTime(LocalNow);
}

// Today's doses already due at the current time
// (IntradayConsumption, docs/analysis/ANALYSIS-INTRADAY-CONSUMPTION.md
// §4). Shared by the main list and the stock-count dialog, so the count
// suggests the quantity the list subtracts. LoadAsync runs three
// queries whatever the number of medicines; Compute is pure.
public sealed class DueToday
{
    private readonly IMedicationIntakeRepository _intakes;
    private readonly IStockCountRepository _counts;
    private readonly DoseTimeSettingsQuery? _settings;
    private readonly TimeProvider _clock;

    public DueToday(
        IMedicationIntakeRepository intakes,
        IStockCountRepository counts,
        TimeProvider clock,
        DoseTimeSettingsQuery? settings = null)
    {
        _intakes = intakes;
        _counts = counts;
        _clock = clock;
        _settings = settings;
    }

    public async Task<DueTodayContext> LoadAsync(CancellationToken cancellationToken)
    {
        var now = TimeZoneInfo.ConvertTime(_clock.GetUtcNow(), _clock.LocalTimeZone).DateTime;
        var today = DateOnly.FromDateTime(now);
        var settings = _settings is null ? DoseTimeSettings.BuiltIn : await _settings.LoadAsync(cancellationToken);

        // The ledger already booked today: an intake that handles the
        // day, or a count that materialized it.
        var booked = new HashSet<Guid>(await _intakes.ListMedicinesWithDayIntakeAsync(today, cancellationToken));
        booked.UnionWith(await _counts.ListMedicinesWithMaterializedCountAsync(today, cancellationToken));

        return new DueTodayContext(now, settings, booked);
    }

    public static decimal Compute(
        DueTodayContext context,
        Medicine medicine,
        IReadOnlyList<MedicationScheduleHistory> schedule,
        IReadOnlyList<MedicationSuspension> suspensions,
        IReadOnlyList<MedicationAdministrationSlot> slots)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(medicine);
        return IntradayConsumption.DueSoFar(
            medicine, context.Today, TimeOnly.FromDateTime(context.LocalNow), schedule, suspensions, slots,
            context.BookedToday.Contains(medicine.Id),
            id => context.Settings.Find(id)?.Time, context.Settings.Defaults);
    }
}
