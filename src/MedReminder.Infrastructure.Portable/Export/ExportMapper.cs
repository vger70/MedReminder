using MedReminder.Application.Export;
using MedReminder.Domain.Catalogue;
using MedReminder.Domain.Medicines;
using MedReminder.Domain.Notifications;
using MedReminder.Domain.Prescriptions;
using MedReminder.Domain.Stock;

namespace MedReminder.Infrastructure.Export;

// Pure, allocation-only translation between the EF Core domain entities
// and the versioned export DTOs (docs/analysis/ANALYSIS-C3-EXPORT-
// IMPORT.md §3.2). Enums and value objects are projected to the
// documented wire form (enum names as strings, AtcCode as plain text)
// and back. No I/O, no EF Core context — kept separate so it is easy
// to reason about and to unit-test field-by-field round-trips.
internal static class ExportMapper
{
    public static ExportedMedicine ToDto(Medicine m) => new()
    {
        Id = m.Id,
        Name = m.Name,
        ActiveIngredient = m.ActiveIngredient,
        Package = m.Package,
        Unit = m.Unit,
        DosePerAdministration = m.DosePerAdministration,
        AdministrationsPerDay = m.AdministrationsPerDay,
        StartDate = m.StartDate,
        EndDate = m.EndDate,
        ThresholdDays = m.ThresholdDays,
        DoctorName = m.DoctorName,
        Notes = m.Notes,
        IsActive = m.IsActive,
        StockEpoch = m.StockEpoch,
        NotificationChannels = m.NotificationChannels.ToString(),
        CreatedAt = m.CreatedAt,
        UpdatedAt = m.UpdatedAt,
        RemindOnDose = m.RemindOnDose,
        NationalCode = m.NationalCode,
        AtcCode = m.AtcCode?.Value,
        LinkedReferenceMedicineId = m.LinkedReferenceMedicineId,
    };

    public static Medicine ToEntity(ExportedMedicine d) => new()
    {
        Id = d.Id,
        Name = d.Name,
        ActiveIngredient = d.ActiveIngredient,
        Package = d.Package,
        Unit = d.Unit,
        DosePerAdministration = d.DosePerAdministration,
        AdministrationsPerDay = d.AdministrationsPerDay,
        StartDate = d.StartDate,
        EndDate = d.EndDate,
        ThresholdDays = d.ThresholdDays,
        DoctorName = d.DoctorName,
        Notes = d.Notes,
        IsActive = d.IsActive,
        StockEpoch = d.StockEpoch,
        NotificationChannels = ParseEnum<NotificationChannels>(d.NotificationChannels),
        CreatedAt = d.CreatedAt,
        UpdatedAt = d.UpdatedAt,
        RemindOnDose = d.RemindOnDose,
        NationalCode = d.NationalCode,
        AtcCode = string.IsNullOrEmpty(d.AtcCode)
            ? null
            : MedReminder.Domain.Catalogue.AtcCode.Parse(d.AtcCode),
        LinkedReferenceMedicineId = d.LinkedReferenceMedicineId,
    };

    public static ExportedStockMovement ToDto(StockMovement m) => new()
    {
        Id = m.Id,
        MedicineId = m.MedicineId,
        OccurredAt = m.OccurredAt,
        Kind = m.Kind.ToString(),
        QuantityDelta = m.QuantityDelta,
        StockEpoch = m.StockEpoch,
        Notes = m.Notes,
        Origin = m.Origin.ToString(),
    };

    // A missing origin (schema version 1) is Legacy: the row predates
    // the facts / derived split (docs/EXPORT-FORMAT.md §5).
    public static StockMovement ToEntity(ExportedStockMovement d) => new()
    {
        Id = d.Id,
        MedicineId = d.MedicineId,
        OccurredAt = d.OccurredAt,
        Kind = ParseEnum<StockMovementKind>(d.Kind),
        QuantityDelta = d.QuantityDelta,
        StockEpoch = d.StockEpoch,
        Notes = d.Notes,
        Origin = d.Origin is null
            ? StockMovementOrigin.Legacy
            : ParseEnum<StockMovementOrigin>(d.Origin),
    };

    public static ExportedScheduleHistory ToDto(MedicationScheduleHistory s) => new()
    {
        Id = s.Id,
        MedicineId = s.MedicineId,
        EffectiveFrom = s.EffectiveFrom,
        DosePerAdministration = s.DosePerAdministration,
        AdministrationsPerDay = s.AdministrationsPerDay,
        ScheduleKind = s.ScheduleKind.ToString(),
        SchedulePayload = s.SchedulePayload,
    };

    public static MedicationScheduleHistory ToEntity(ExportedScheduleHistory d) => new()
    {
        Id = d.Id,
        MedicineId = d.MedicineId,
        EffectiveFrom = d.EffectiveFrom,
        DosePerAdministration = d.DosePerAdministration,
        AdministrationsPerDay = d.AdministrationsPerDay,
        ScheduleKind = ParseEnum<ScheduleKind>(d.ScheduleKind),
        SchedulePayload = d.SchedulePayload,
    };

    public static ExportedAdministrationSlot ToDto(MedicationAdministrationSlot s) => new()
    {
        Id = s.Id,
        MedicineId = s.MedicineId,
        SetId = s.SetId,
        Dose = s.Dose,
        Time = s.Time,
        TimingLabel = s.TimingLabel,
        Order = s.Order,
    };

    // SetId is resolved by ExportPayloadUpgrader before mapping; a
    // null here would be a bug, not a version 1 archive.
    public static MedicationAdministrationSlot ToEntity(ExportedAdministrationSlot d) => new()
    {
        Id = d.Id,
        MedicineId = d.MedicineId,
        SetId = d.SetId ?? throw new InvalidOperationException($"Slot {d.Id} has no slot set."),
        Dose = d.Dose,
        Time = d.Time,
        TimingLabel = d.TimingLabel,
        Order = d.Order,
    };

    public static ExportedAdministrationSlotSet ToDto(MedicationAdministrationSlotSet s) => new()
    {
        Id = s.Id,
        MedicineId = s.MedicineId,
        EffectiveFrom = s.EffectiveFrom,
        RecordedAt = s.RecordedAt,
    };

    public static MedicationAdministrationSlotSet ToEntity(ExportedAdministrationSlotSet d) => new()
    {
        Id = d.Id,
        MedicineId = d.MedicineId,
        EffectiveFrom = d.EffectiveFrom,
        RecordedAt = d.RecordedAt,
    };

    public static ExportedStockCount ToDto(StockCount c) => new()
    {
        Id = c.Id,
        MedicineId = c.MedicineId,
        CountDay = c.CountDay,
        CountedQuantity = c.CountedQuantity,
        TakenToday = c.TakenToday,
        ThresholdAtCount = c.ThresholdAtCount,
        RecordedAt = c.RecordedAt,
    };

    public static StockCount ToEntity(ExportedStockCount d) => new()
    {
        Id = d.Id,
        MedicineId = d.MedicineId,
        CountDay = d.CountDay,
        CountedQuantity = d.CountedQuantity,
        TakenToday = d.TakenToday,
        ThresholdAtCount = d.ThresholdAtCount,
        RecordedAt = d.RecordedAt,
    };

    public static ExportedLedgerCutoff ToDto(LedgerCutoff c) => new()
    {
        CutoffDay = c.CutoffDay,
        FrozenAt = c.FrozenAt,
    };

    public static LedgerCutoff ToEntity(ExportedLedgerCutoff d) => new()
    {
        CutoffDay = d.CutoffDay,
        FrozenAt = d.FrozenAt,
    };

    public static ExportedSuspension ToDto(MedicationSuspension s) => new()
    {
        Id = s.Id,
        MedicineId = s.MedicineId,
        StartDate = s.StartDate,
        EndDate = s.EndDate,
        Reason = s.Reason,
    };

    public static MedicationSuspension ToEntity(ExportedSuspension d) => new()
    {
        Id = d.Id,
        MedicineId = d.MedicineId,
        StartDate = d.StartDate,
        EndDate = d.EndDate,
        Reason = d.Reason,
    };

    public static ExportedIntake ToDto(MedicationIntake i) => new()
    {
        Id = i.Id,
        MedicineId = i.MedicineId,
        Day = i.Day,
        ScheduledAt = i.ScheduledAt,
        ActualAt = i.ActualAt,
        Quantity = i.Quantity,
        Status = i.Status.ToString(),
        Notes = i.Notes,
    };

    public static MedicationIntake ToEntity(ExportedIntake d) => new()
    {
        Id = d.Id,
        MedicineId = d.MedicineId,
        Day = d.Day,
        ScheduledAt = d.ScheduledAt,
        ActualAt = d.ActualAt,
        Quantity = d.Quantity,
        Status = ParseEnum<IntakeStatus>(d.Status),
        Notes = d.Notes,
    };

    public static ExportedNotificationEvent ToDto(NotificationEvent e) => new()
    {
        Id = e.Id,
        MedicineId = e.MedicineId,
        StockEpoch = e.StockEpoch,
        TriggeredAt = e.TriggeredAt,
        Channel = e.Channel.ToString(),
        DaysRemainingAtSend = e.DaysRemainingAtSend,
        Success = e.Success,
        ErrorMessage = e.ErrorMessage,
        Stage = e.Stage,
    };

    public static NotificationEvent ToEntity(ExportedNotificationEvent d) => new()
    {
        Id = d.Id,
        MedicineId = d.MedicineId,
        StockEpoch = d.StockEpoch,
        TriggeredAt = d.TriggeredAt,
        Channel = ParseEnum<NotificationChannels>(d.Channel),
        DaysRemainingAtSend = d.DaysRemainingAtSend,
        Success = d.Success,
        ErrorMessage = d.ErrorMessage,
        Stage = d.Stage < 1 ? 1 : d.Stage,
    };

    public static ExportedDoseReminderEvent ToDto(DoseReminderEvent e) => new()
    {
        Id = e.Id,
        MedicineId = e.MedicineId,
        SlotKey = e.SlotKey,
        LocalDate = e.LocalDate,
        FiredAt = e.FiredAt,
        Channel = e.Channel.ToString(),
    };

    public static DoseReminderEvent ToEntity(ExportedDoseReminderEvent d) => new()
    {
        Id = d.Id,
        MedicineId = d.MedicineId,
        SlotKey = d.SlotKey,
        LocalDate = d.LocalDate,
        FiredAt = d.FiredAt,
        Channel = ParseEnum<NotificationChannels>(d.Channel),
    };

    // Enum names are the documented wire form. Parsing is
    // case-sensitive on purpose — the export always writes the exact
    // enum name, and a mismatch signals a corrupt / hand-edited
    // payload rather than a benign casing difference.
    private static TEnum ParseEnum<TEnum>(string value) where TEnum : struct, Enum
        => Enum.Parse<TEnum>(value);

    public static ExportedPrescription ToDto(Prescription p) => new()
    {
        Id = p.Id,
        MedicineId = p.MedicineId,
        RequestedOn = p.RequestedOn,
        IssuedOn = p.IssuedOn,
        Code = p.Code,
        Packages = p.Packages,
        ValidUntil = p.ValidUntil,
        CollectedOn = p.CollectedOn,
        RecordedAt = p.RecordedAt,
        UpdatedAt = p.UpdatedAt,
    };

    public static Prescription ToEntity(ExportedPrescription d) => new()
    {
        Id = d.Id,
        MedicineId = d.MedicineId,
        RequestedOn = d.RequestedOn,
        IssuedOn = d.IssuedOn,
        Code = d.Code,
        Packages = d.Packages,
        ValidUntil = d.ValidUntil,
        CollectedOn = d.CollectedOn,
        RecordedAt = d.RecordedAt,
        UpdatedAt = d.UpdatedAt,
    };
}
