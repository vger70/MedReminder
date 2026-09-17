namespace MedReminder.UI.Presentation;

// Row of the main grid (spec §13). Flat structure optimized for
// DataGridView data binding; the status / color rules live in
// MainForm, which formats the cell per row.
internal sealed class MedicineListItem
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal CurrentStock { get; set; }
    public decimal DailyRate { get; set; }
    public int? DaysRemaining { get; set; }
    public DateOnly? EstimatedRunOutDate { get; set; }
    public int ThresholdDays { get; set; }
    public bool IsSuspended { get; set; }
    public MedicineRowStatus Status { get; set; }

    // Pre-formatted values for the grid — avoid replicating the
    // conversions in every cell-formatting event.
    public string StockDisplay => $"{CurrentStock:0.##} {Unit}".TrimEnd();
    public string DailyRateDisplay => DailyRate <= 0m ? "—" : $"{DailyRate:0.##}/day";
    public string DaysRemainingDisplay => DaysRemaining is null ? "—" : DaysRemaining.Value.ToString();
    public string EtaDisplay => EstimatedRunOutDate?.ToString("d") ?? "—";
    // Set by MedicineOverviewLoader based on the current language of
    // ILocalizationService — avoids injecting the service into a
    // data-binding DTO.
    public string StatusDisplay { get; set; } = string.Empty;
}

internal enum MedicineRowStatus
{
    Ok = 0,
    Warning = 1,
    Empty = 2,
    Suspended = 3,
}
