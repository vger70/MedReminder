namespace MedReminder.Application.Export;

// The decrypted archive body (payload.json), one array per entity type
// plus a Shared sub-object for the opt-in non-DB files
// (docs/analysis/ANALYSIS-C3-EXPORT-IMPORT.md §3.2). These are DTOs,
// not the EF Core entities: dependent rows carry the parent's Id
// explicitly, there are no navigation properties, and enums / value
// objects are projected to their documented wire form (strings). The
// public, documented shape is docs/EXPORT-FORMAT.md.
public sealed class ExportPayload
{
    // Entity-model version (§3.2). Must be <= CurrentSchemaVersion to
    // import (§4.3).
    public int SchemaVersion { get; set; } = ExportFormat.CurrentSchemaVersion;

    // Descriptive header for the exported profile. Not restored as a
    // DB row (the profile registry is out of scope for a single-
    // profile export, §3.4) — carried for the import info panel and
    // human inspection.
    public ExportedProfileInfo Profile { get; set; } = new();

    public IList<ExportedMedicine> Medicines { get; set; } = new List<ExportedMedicine>();
    public IList<ExportedStockMovement> StockMovements { get; set; } = new List<ExportedStockMovement>();
    public IList<ExportedScheduleHistory> MedicationScheduleHistory { get; set; } = new List<ExportedScheduleHistory>();
    public IList<ExportedAdministrationSlot> MedicationAdministrationSlots { get; set; } = new List<ExportedAdministrationSlot>();
    public IList<ExportedSuspension> MedicationSuspensions { get; set; } = new List<ExportedSuspension>();
    public IList<ExportedIntake> MedicationIntakes { get; set; } = new List<ExportedIntake>();
    public IList<ExportedNotificationEvent> NotificationEvents { get; set; } = new List<ExportedNotificationEvent>();
    public IList<ExportedDoseReminderEvent> DoseReminderEvents { get; set; } = new List<ExportedDoseReminderEvent>();

    // Schema version 2 (B.1 Phase 2b). Absent from version 1 archives,
    // which the importer maps to Legacy rows (docs/EXPORT-FORMAT.md §5).
    public IList<ExportedAdministrationSlotSet> MedicationAdministrationSlotSets { get; set; } = new List<ExportedAdministrationSlotSet>();
    public IList<ExportedStockCount> StockCounts { get; set; } = new List<ExportedStockCount>();

    // Null when the profile has no cutoff (created after the patch).
    public ExportedLedgerCutoff? LedgerCutoff { get; set; }

    // Prescription lifecycle (EVOLUTION-PROPOSALS-2 §3.2). Additive field
    // of schema version 2: archives without it import with none.
    public IList<ExportedPrescription> Prescriptions { get; set; } = new List<ExportedPrescription>();

    // Administrative deadlines (EVOLUTION-PROPOSALS-2 §3.6). Additive
    // field of schema version 2: archives without it import with none.
    public IList<ExportedDeadline> Deadlines { get; set; } = new List<ExportedDeadline>();

    // Packages and their expiry (ANALYSIS-PACKAGE-EXPIRY.md §7.2).
    // Additive field of schema version 2: archives without it import with
    // none.
    public IList<ExportedStockPackage> StockPackages { get; set; } = new List<ExportedStockPackage>();

    // Time-of-day presets and per-day default times
    // (ANALYSIS-INTRADAY-CONSUMPTION.md §6). Additive fields of schema
    // version 2: archives without them import with the built-ins.
    // Null in an archive written before the presets: its slots are then
    // linked to the presets on first use (SlotPresetBackfill).
    public IList<ExportedDoseTimePreset>? DoseTimePresets { get; set; }
    public IList<ExportedDoseTimeDefault> DoseTimeDefaults { get; set; } = new List<ExportedDoseTimeDefault>();

    // Per-profile notification settings (§3.2). Travels implicitly
    // with the profile.
    public ExportedNotificationSettings? NotificationSettings { get; set; }

    // Opt-in shared files (§3.4).
    public ExportedShared Shared { get; set; } = new();
}

public sealed class ExportedProfileInfo
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class ExportedMedicine
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? ActiveIngredient { get; set; }
    public string? Package { get; set; }
    public string Unit { get; set; } = string.Empty;
    public decimal DosePerAdministration { get; set; }
    public int AdministrationsPerDay { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public int ThresholdDays { get; set; }
    public string? DoctorName { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; }
    public int StockEpoch { get; set; }

    // NotificationChannels flags enum, serialized as its string form.
    public string NotificationChannels { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public bool RemindOnDose { get; set; }

    // Optional catalogue link (all null for unlinked medicines).
    public string? NationalCode { get; set; }

    // 7-char ATC code as plain text, or null.
    public string? AtcCode { get; set; }

    public Guid? LinkedReferenceMedicineId { get; set; }
}

public sealed class ExportedStockMovement
{
    public Guid Id { get; set; }
    public Guid MedicineId { get; set; }
    public DateTimeOffset OccurredAt { get; set; }

    // StockMovementKind, serialized as its string form.
    public string Kind { get; set; } = string.Empty;

    public decimal QuantityDelta { get; set; }
    public int StockEpoch { get; set; }
    public string? Notes { get; set; }

    // StockMovementOrigin as its string form. Null in schema version 1
    // archives: imported as Legacy.
    public string? Origin { get; set; }
}

public sealed class ExportedScheduleHistory
{
    public Guid Id { get; set; }
    public Guid MedicineId { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public decimal DosePerAdministration { get; set; }
    public int AdministrationsPerDay { get; set; }

    // ScheduleKind, serialized as its string form.
    public string ScheduleKind { get; set; } = string.Empty;

    public string? SchedulePayload { get; set; }
}

public sealed class ExportedAdministrationSlot
{
    public Guid Id { get; set; }
    public Guid MedicineId { get; set; }

    // Slot set the row belongs to. Null in schema version 1 archives:
    // the importer groups the medicine's slots into one set.
    public Guid? SetId { get; set; }

    public decimal Dose { get; set; }
    public TimeOnly? Time { get; set; }
    public string? TimingLabel { get; set; }
    public int Order { get; set; }

    // Additive field (docs/EXPORT-FORMAT.md §5): null in an archive
    // written before it, whose slots import as scheduled and are then
    // corrected once (AsNeededSlotBackfill).
    public bool? IsAsNeeded { get; set; }

    // Additive field: archives without it import with null.
    public Guid? PresetId { get; set; }
}

public sealed class ExportedAdministrationSlotSet
{
    public Guid Id { get; set; }
    public Guid MedicineId { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
}

public sealed class ExportedStockCount
{
    public Guid Id { get; set; }
    public Guid MedicineId { get; set; }
    public DateOnly CountDay { get; set; }
    public decimal CountedQuantity { get; set; }
    public decimal TakenToday { get; set; }
    public int ThresholdAtCount { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
}

public sealed class ExportedLedgerCutoff
{
    public DateOnly CutoffDay { get; set; }
    public DateTimeOffset FrozenAt { get; set; }
}

public sealed class ExportedSuspension
{
    public Guid Id { get; set; }
    public Guid MedicineId { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public string? Reason { get; set; }
}

public sealed class ExportedIntake
{
    public Guid Id { get; set; }
    public Guid MedicineId { get; set; }
    public DateOnly Day { get; set; }
    public DateTimeOffset? ScheduledAt { get; set; }
    public DateTimeOffset? ActualAt { get; set; }
    public decimal Quantity { get; set; }

    // IntakeStatus, serialized as its string form.
    public string Status { get; set; } = string.Empty;

    public string? Notes { get; set; }

    // Additive field (docs/EXPORT-FORMAT.md §5): archives without it
    // import with false.
    public bool IsExtra { get; set; }
}

public sealed class ExportedNotificationEvent
{
    public Guid Id { get; set; }
    public Guid MedicineId { get; set; }
    public int StockEpoch { get; set; }
    public DateTimeOffset TriggeredAt { get; set; }

    // NotificationChannels, serialized as its string form.
    public string Channel { get; set; } = string.Empty;

    public int DaysRemainingAtSend { get; set; }
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }

    // Warning stage (1 or 2). Additive field: archives without it
    // import as the first stage.
    public int Stage { get; set; } = 1;
}

public sealed class ExportedDoseReminderEvent
{
    public Guid Id { get; set; }
    public Guid MedicineId { get; set; }
    public string SlotKey { get; set; } = string.Empty;
    public DateOnly LocalDate { get; set; }
    public DateTimeOffset FiredAt { get; set; }

    // NotificationChannels, serialized as its string form.
    public string Channel { get; set; } = string.Empty;
}

public sealed class ExportedNotificationSettings
{
    public string ToAddress { get; set; } = string.Empty;
    public string CaregiverAddress { get; set; } = string.Empty;

    // Added with the prescription request feature. Optional: archives
    // produced before it lack the field and import with "" (§5).
    public string DoctorAddress { get; set; } = string.Empty;

    // Caregiver options (EVOLUTION-PROPOSALS-2 §3.8). Additive: archives
    // without them import with "" (every kind copied, no digest). The day
    // of the last digest is not exported.
    public string CaregiverEmails { get; set; } = string.Empty;
    public string CaregiverDigest { get; set; } = string.Empty;

    // Lead days of the package expiry notices (ANALYSIS-PACKAGE-EXPIRY.md
    // §4.6). Additive: archives without them import with "" (defaults).
    public string PackageExpiryLeadDays { get; set; } = string.Empty;
    public string PackageInUseLeadDays { get; set; } = string.Empty;
}

// Opt-in non-DB files (§3.4). A section is null unless the user opted
// in and the source file existed at export time.
public sealed class ExportedShared
{
    public ExportedUserSettings? UserSettings { get; set; }
    public ExportedBackupSettings? BackupSettings { get; set; }
    public ExportedSmtpSettings? SmtpSettings { get; set; }

    // Base64 of the AES-GCM ciphertext of the SMTP password, keyed
    // with the archive key (§3.4). Null unless the user opted the
    // password in and one was stored. The password is NEVER emitted
    // in the clear.
    public ExportedProtectedSecret? SmtpPasswordEncrypted { get; set; }
}

public sealed class ExportedUserSettings
{
    public string Language { get; set; } = "en";
    public string ReferenceCountry { get; set; } = "IT";
    public bool CheckForUpdatesOnStartup { get; set; } = true;
}

public sealed class ExportedBackupSettings
{
    public bool Enabled { get; set; }
    public string Directory { get; set; } = string.Empty;
    public string PreferredTime { get; set; } = "03:00";
    public int RetentionDays { get; set; } = 30;

    // C.3+ (docs/analysis/ANALYSIS-C3PLUS-CLOUD-BACKUP.md §3.1) —
    // additive fields; older archives that omit them deserialize to
    // the defaults.
    public bool CloudFolderEnabled { get; set; }
    public string CloudFolderDirectory { get; set; } = string.Empty;
    public int CloudFolderRetention { get; set; } = 30;
}

public sealed class ExportedSmtpSettings
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public bool UseStartTls { get; set; } = true;
    public string Username { get; set; } = string.Empty;
    public string FromAddress { get; set; } = string.Empty;
    public string FromDisplayName { get; set; } = "MedReminder";
    public int TimeoutSeconds { get; set; } = 30;
}

// A secret re-encrypted with the archive key (AES-GCM). Its own nonce
// and tag travel with it so it can be decrypted independently of the
// payload envelope (§3.4).
public sealed class ExportedProtectedSecret
{
    public string NonceBase64 { get; set; } = string.Empty;
    public string TagBase64 { get; set; } = string.Empty;
    public string CiphertextBase64 { get; set; } = string.Empty;
}

public sealed class ExportedPrescription
{
    public Guid Id { get; set; }
    public Guid MedicineId { get; set; }
    public DateOnly? RequestedOn { get; set; }
    public DateOnly? IssuedOn { get; set; }
    public string? Code { get; set; }
    public int? Packages { get; set; }
    public DateOnly? ValidUntil { get; set; }
    public DateOnly? CollectedOn { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class ExportedDoseTimePreset
{
    public Guid Id { get; set; }
    public string? BuiltInKey { get; set; }
    public string? Label { get; set; }
    public TimeOnly? Time { get; set; }
    public bool IsAsNeeded { get; set; }
    public int Order { get; set; }
    public bool IsHidden { get; set; }
}

public sealed class ExportedDoseTimeDefault
{
    public int AdministrationsPerDay { get; set; }
    // "HH:mm" values separated by ';'.
    public string Times { get; set; } = string.Empty;
}

public sealed class ExportedStockPackage
{
    public Guid Id { get; set; }
    public Guid MedicineId { get; set; }
    public Guid? MovementId { get; set; }
    public decimal Quantity { get; set; }
    public DateOnly? ExpiresOn { get; set; }
    public int? UseWithinDays { get; set; }
    public DateOnly? OpenedOn { get; set; }
    public string? Batch { get; set; }
    public DateOnly? ClosedOn { get; set; }
    // PackageClosure member name; null for an open package.
    public string? Closure { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class ExportedDeadline
{
    public Guid Id { get; set; }
    public Guid? MedicineId { get; set; }
    // DeadlineKind member name.
    public string Kind { get; set; } = string.Empty;
    public string? Label { get; set; }
    public DateOnly DueOn { get; set; }
    public int LeadDays { get; set; }
    public int? RepeatMonths { get; set; }
    // NotificationChannels member name.
    public string Channels { get; set; } = string.Empty;
    public DateOnly? DoneOn { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
