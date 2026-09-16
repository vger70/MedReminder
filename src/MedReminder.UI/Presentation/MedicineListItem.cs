namespace MedReminder.UI.Presentation;

// Riga della griglia principale (spec §13). Struttura piatta ottimizzata
// per DataGridView data-binding; le regole di stato/colore vivono nel
// MainForm che formatta la cella per riga.
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

    // Formattazioni pronte per il grid — evitano di replicare le
    // conversioni in ogni cell-formatting event.
    public string StockDisplay => $"{CurrentStock:0.##} {Unit}".TrimEnd();
    public string DailyRateDisplay => DailyRate <= 0m ? "—" : $"{DailyRate:0.##}/gg";
    public string DaysRemainingDisplay => DaysRemaining is null ? "—" : DaysRemaining.Value.ToString();
    public string EtaDisplay => EstimatedRunOutDate?.ToString("d") ?? "—";
    // Impostato dal MedicineOverviewLoader in base alla lingua corrente
    // dell'ILocalizationService — evita di iniettare il service in un DTO
    // di databinding.
    public string StatusDisplay { get; set; } = string.Empty;
}

internal enum MedicineRowStatus
{
    Ok = 0,
    Warning = 1,
    Empty = 2,
    Suspended = 3,
}
