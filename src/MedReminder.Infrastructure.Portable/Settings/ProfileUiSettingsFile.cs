using System.Text.Json;
using MedReminder.Application.Abstractions;

namespace MedReminder.Infrastructure.Settings;

// profiles\<id>\ui.settings.json: UI preferences that belong to one
// profile rather than to the Windows user (docs/notes/
// EVOLUTION-PROPOSALS.md §3.2). Shape: { "TextSize": "Large" }.
// Read once at boot, before the main window is created, so it is not
// part of the reloadable configuration. A missing, unreadable or
// unknown value reads as Normal: a UI preference must never block the
// app from opening.
public static class ProfileUiSettingsFile
{
    public const string FileName = "ui.settings.json";

    private const string TextSizeProperty = "TextSize";
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    public static string PathFor(string profileDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileDirectory);
        return Path.Combine(profileDirectory, FileName);
    }

    public static TextSize ReadTextSize(string profileDirectory)
    {
        var path = PathFor(profileDirectory);
        try
        {
            if (!File.Exists(path)) return TextSize.Normal;
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (document.RootElement.ValueKind != JsonValueKind.Object) return TextSize.Normal;
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (string.Equals(property.Name, TextSizeProperty, StringComparison.OrdinalIgnoreCase)
                    && property.Value.ValueKind == JsonValueKind.String)
                {
                    return TextSizes.Parse(property.Value.GetString());
                }
            }
            return TextSize.Normal;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return TextSize.Normal;
        }
    }

    // Writes through a temporary file so a crash never leaves a
    // truncated file behind.
    public static void WriteTextSize(string profileDirectory, TextSize size)
    {
        var path = PathFor(profileDirectory);
        Directory.CreateDirectory(profileDirectory);
        var payload = new Dictionary<string, string> { [TextSizeProperty] = size.ToString() };
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(payload, WriteOptions));
        File.Move(tmp, path, overwrite: true);
    }
}
