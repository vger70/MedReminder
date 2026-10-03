using MedReminder.Domain.Stock;

namespace MedReminder.UI.Forms;

// Inputs shared by the dialogs that enter a package (docs/analysis/
// ANALYSIS-PACKAGE-EXPIRY.md §5.1): the printed expiry as month and
// year, and the in-use period after opening.
internal static class PackageInputs
{
    // Month and year only, as printed. Unchecked: no expiry entered.
    public static DateTimePicker ExpiryPicker(DateOnly? value, DateOnly today) => new()
    {
        Format = DateTimePickerFormat.Custom,
        CustomFormat = "MM/yyyy",
        ShowUpDown = true,
        ShowCheckBox = true,
        Checked = value is not null,
        Value = (value ?? today.AddYears(1)).ToDateTime(TimeOnly.MinValue),
        Width = 120,
        Dock = DockStyle.Left,
    };

    // The expiry the picker holds: the last day of the shown month, or
    // the original date when the month is unchanged (a scanned expiry
    // can carry a real day).
    public static DateOnly? ReadExpiry(DateTimePicker picker, DateOnly? original)
    {
        if (!picker.Checked) return null;
        var value = picker.Value;
        if (original is { } kept && kept.Year == value.Year && kept.Month == value.Month) return kept;
        return PackageExpiryRules.EndOfMonth(value.Year, value.Month);
    }

    // 0 means no in-use period.
    public static NumericUpDown UseWithinBox(int? value) => new()
    {
        Minimum = 0,
        Maximum = PackageExpiryRules.MaxUseWithinDays,
        Value = value ?? 0,
        Width = 80,
    };

    public static int? ReadUseWithin(NumericUpDown box) => box.Value == 0 ? null : (int)box.Value;

    public static TextBox BatchBox(string? value) => new()
    {
        Width = 160,
        Dock = DockStyle.Left,
        MaxLength = PackageExpiryRules.MaxBatchLength,
        Text = value ?? string.Empty,
    };
}
