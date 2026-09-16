using FluentAssertions;
using MedReminder.Domain.Calculations;
using MedReminder.Domain.Medicines;
using MedReminder.Domain.Tests.Support;
using Xunit;

namespace MedReminder.Domain.Tests.Calculations;

// Verifica del nuovo overload di DailyConsumption che accetta gli slot
// di somministrazione: quando presenti, il consumo giornaliero è la
// somma delle dosi; quando assenti si ricade sul modello legacy
// dose × frequenza.
public class DailyConsumptionSlotTests
{
    private static readonly DateOnly Today = new(2026, 9, 15);

    [Fact]
    public void Slots_summed_when_present_ignoring_schedule_dose_and_freq()
    {
        var schedule = new[]
        {
            DomainFactory.Schedule(new DateOnly(2026, 9, 1), 1m, 2),   // legacy: 2 unità/giorno
        };
        var slots = new[]
        {
            Slot(dose: 0.5m, time: new TimeOnly(8, 0)),
            Slot(dose: 1m, time: new TimeOnly(14, 0)),
            Slot(dose: 0.5m, time: new TimeOnly(22, 0)),
        };

        DailyConsumption.RateOn(Today, schedule, slots).Should().Be(2m);
    }

    [Fact]
    public void Empty_slot_list_falls_back_to_legacy_schedule()
    {
        var schedule = new[] { DomainFactory.Schedule(new DateOnly(2026, 9, 1), 1m, 3) };
        DailyConsumption.RateOn(Today, schedule, Array.Empty<MedicationAdministrationSlot>())
            .Should().Be(3m);
    }

    [Fact]
    public void Null_slot_argument_falls_back_to_legacy_schedule()
    {
        var schedule = new[] { DomainFactory.Schedule(new DateOnly(2026, 9, 1), 1m, 2) };
        DailyConsumption.RateOn(Today, schedule, administrationSlots: null)
            .Should().Be(2m);
    }

    [Fact]
    public void Slots_with_zero_total_still_win_over_legacy_when_present()
    {
        // Contratto: se l'utente ha definito slot, la fonte di verità sono
        // gli slot — anche se il totale è zero (medicina in "pausa dosi"
        // pur senza sospensione formale). Legacy dose×freq NON viene usato.
        var schedule = new[] { DomainFactory.Schedule(new DateOnly(2026, 9, 1), 1m, 2) };
        var slots = new[] { Slot(dose: 0m, time: new TimeOnly(9, 0)) };

        DailyConsumption.RateOn(Today, schedule, slots).Should().Be(0m);
    }

    private static MedicationAdministrationSlot Slot(decimal dose, TimeOnly? time = null, string? label = null)
    {
        return new MedicationAdministrationSlot
        {
            MedicineId = DomainFactory.MedicineId,
            Dose = dose,
            Time = time,
            TimingLabel = label,
        };
    }
}
