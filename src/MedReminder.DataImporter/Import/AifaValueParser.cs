using System.Globalization;

namespace MedReminder.DataImporter.Import;

public static class AifaValueParser
{
    public static string? GetAtcParent(string? code) => code?.Length switch
    {
        1 => null,
        3 => code[..1],
        4 => code[..3],
        5 => code[..4],
        7 => code[..5],
        _ => null
    };

    public static bool TryParseUnambiguousQuantity(string? value, out decimal quantity)
    {
        quantity = default;
        if (string.IsNullOrWhiteSpace(value)) return false;
        return decimal.TryParse(value.Trim().Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out quantity);
    }

    public static bool IsNotAvailableIngredient(string? value) => string.Equals(value?.Trim(), "N.D.", StringComparison.OrdinalIgnoreCase);
}
