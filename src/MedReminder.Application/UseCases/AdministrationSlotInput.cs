namespace MedReminder.Application.UseCases;

// DTO usato dai command (AddMedicine, UpdateMedicine) per portare gli
// slot dall'UI alla Application senza esporre l'entità di dominio.
// Order è derivato dall'indice nella lista quando il command viene
// eseguito — l'UI non deve calcolarlo manualmente.
public sealed record AdministrationSlotInput(
    decimal Dose,
    TimeOnly? Time,
    string? TimingLabel);
