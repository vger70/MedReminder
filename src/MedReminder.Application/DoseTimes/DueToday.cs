using MedReminder.Application.Abstractions;
using MedReminder.Domain.Calculations;
using MedReminder.Domain.Medicines;

namespace MedReminder.Application.DoseTimes;

// Today's doses already due at the current time
// (IntradayConsumption, docs/analysis/ANALYSIS-INTRADAY-CONSUMPTION.md
// §4): reads what the domain function needs besides the schedule. Shared
// by the main list and the stock-count dialog, so the count suggests
// the quantity the list subtracts. Load the settings once per screen
// with LoadSettingsAsync and pass them to each call.
public sealed class DueToday
{
    private readonly IMedicationIntakeRepository _intakes;
    private readonly IStockCountRepository _counts;
    private readonly IDoseTimePresetRepository? _presets;
    private readonly TimeProvider _clock;

    public DueToday(
        IMedicationIntakeRepository intakes,
        IStockCountRepository counts,
        TimeProvider clock,
        IDoseTimePresetRepository? presets = null)
    {
        _intakes = intakes;
        _counts = counts;
        _clock = clock;
        _presets = presets;
    }

    public async Task<DoseTimeSettings> LoadSettingsAsync(CancellationToken cancellationToken)
        => _presets is null
            ? DoseTimeSettings.BuiltIn
            : await new DoseTimeSettingsQuery(_presets).LoadAsync(cancellationToken);

    public DateTime LocalNow() => TimeZoneInfo.ConvertTime(_clock.GetUtcNow(), _clock.LocalTimeZone).DateTime;

    public async Task<decimal> ComputeAsync(
        Medicine medicine,
        IReadOnlyList<MedicationScheduleHistory> schedule,
        IReadOnlyList<MedicationSuspension> suspensions,
        IReadOnlyList<MedicationAdministrationSlot> slots,
        DoseTimeSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(medicine);
        ArgumentNullException.ThrowIfNull(settings);

        var now = LocalNow();
        var today = DateOnly.FromDateTime(now);

        // The ledger already booked today: an intake that handles the
        // day, or a count that materialized it.
        var booked = (await _intakes.ListForMedicineAsync(medicine.Id, cancellationToken))
                .Any(i => i.Day == today && !i.IsExtra)
            || (await _counts.ListForMedicineAsync(medicine.Id, cancellationToken))
                .Any(c => c.CountDay == today && c.MaterializesCountDay);

        return IntradayConsumption.DueSoFar(
            medicine, today, TimeOnly.FromDateTime(now), schedule, suspensions, slots, booked,
            id => settings.Find(id)?.Time, settings.Defaults);
    }
}
