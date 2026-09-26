using System.Text.Json;
using System.Text.Json.Serialization;

namespace MedReminder.Infrastructure.Export;

// Single source of truth for how the archive's JSON is serialized
// (docs/analysis/ANALYSIS-C3-EXPORT-IMPORT.md §4.1 step 5): UTF-8,
// no BOM, camelCase property names, ISO-8601 timestamps
// (System.Text.Json's default for DateTimeOffset). Used for both
// manifest.json and payload.json so the on-disk shape matches
// docs/EXPORT-FORMAT.md exactly.
internal static class ExportJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        // Omit null shared sections rather than emitting "prop": null
        // noise; the payload arrays are always written.
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}
