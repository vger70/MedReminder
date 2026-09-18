using System.Globalization;
using System.Text;

namespace MedReminder.Infrastructure.Catalogue;

// Deterministic text normalisation shared by the importer (writes
// commercial_name_norm / name_norm) and the query service (normalises
// user prefixes before running LIKE). Two calls with the same input
// always produce the same output, regardless of the current culture.
//
// The transformation is: strip diacritics (NFD then drop
// NonSpacingMark chars), lowercase (invariant), trim runs of
// whitespace to a single space, and trim the ends.
public static class CatalogueTextNormalizer
{
    public static string Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        var decomposed = input.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        var lastWasSpace = true;

        foreach (var ch in decomposed)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsWhiteSpace(ch))
            {
                if (!lastWasSpace)
                {
                    builder.Append(' ');
                    lastWasSpace = true;
                }
                continue;
            }

            builder.Append(char.ToLowerInvariant(ch));
            lastWasSpace = false;
        }

        // Trim trailing single space, if any.
        if (builder.Length > 0 && builder[^1] == ' ')
        {
            builder.Length--;
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }
}
