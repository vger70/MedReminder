namespace MedReminder.Application.UseCases;

// DTO used by commands (AddMedicine, UpdateMedicine) to carry the
// slots from the UI to the Application without exposing the domain
// entity. Order is derived from the list index when the command is
// executed — the UI does not have to compute it manually.
public sealed record AdministrationSlotInput(
    decimal Dose,
    TimeOnly? Time,
    string? TimingLabel);
