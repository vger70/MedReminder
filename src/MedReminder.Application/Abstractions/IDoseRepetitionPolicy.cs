using MedReminder.Domain.Calculations;
using MedReminder.Domain.Medicines;

namespace MedReminder.Application.Abstractions;

// How a host repeats the dose reminders of a medicine until the slot is
// recorded (mobile issue #128); null for a single reminder. Optional: a host
// without it (the desktop) keeps one reminder per slot.
public interface IDoseRepetitionPolicy
{
    DoseRepetition? For(Medicine medicine);
}
