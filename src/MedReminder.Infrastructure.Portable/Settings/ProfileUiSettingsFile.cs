using System.Text.Json;
using MedReminder.Application.Abstractions;

namespace MedReminder.Infrastructure.Settings;

// profiles\<id>\ui.settings.json: UI preferences that belong to one
// profile rather than to the Windows user (docs/notes/
// EVOLUTION-PROPOSALS.md §3.2, docs/analysis/ANALYSIS-UI-MODERNIZATION.md
// §4.4). Shape: { "TextSize": "Large", "Appearance": "Dark" }.
// Read once at boot, before the main window is created, so it is not
// part of the reloadable configuration. A missing, unreadable or
// unknown value reads as the default: a UI preference must never block
// the app from opening.
public static class ProfileUiSettingsFile
{
    public const string FileName = "ui.settings.json";

    private const string TextSizeProperty = "TextSize";
    private const string AppearanceProperty = "Appearance";
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    public static string PathFor(string profileDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileDirectory);
        return Path.Combine(profileDirectory, FileName);
    }

    public static TextSize ReadTextSize(string profileDirectory)
        => TextSizes.Parse(ReadProperty(profileDirectory, TextSizeProperty));

    public static AppearanceMode ReadAppearance(string profileDirectory)
        => AppearanceModes.Parse(ReadProperty(profileDirectory, AppearanceProperty));

    public static void WriteTextSize(string profileDirectory, TextSize size)
        => Write(profileDirectory, size, ReadAppearance(profileDirectory));

    public static void WriteAppearance(string profileDirectory, AppearanceMode mode)
        => Write(profileDirectory, ReadTextSize(profileDirectory), mode);

    // Writes both preferences through a temporary file so a crash never
    // leaves a truncated file behind.
    public static void Write(string profileDirectory, TextSize size, AppearanceMode mode)
    {
        var path = PathFor(profileDirectory);
        Directory.CreateDirectory(profileDirectory);
        var payload = new Dictionary<string, string>
        {
            [TextSizeProperty] = size.ToString(),
            [AppearanceProperty] = mode.ToString(),
        };
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(payload, WriteOptions));
        File.Move(tmp, path, overwrite: true);
    }

    private static string? ReadProperty(string profileDirectory, string name)
    {
        var path = PathFor(profileDirectory);
        try
        {
            if (!File.Exists(path)) return null;
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (document.RootElement.ValueKind != JsonValueKind.Object) return null;
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)
                    && property.Value.ValueKind == JsonValueKind.String)
                {
                    return property.Value.GetString();
                }
            }
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }
}
