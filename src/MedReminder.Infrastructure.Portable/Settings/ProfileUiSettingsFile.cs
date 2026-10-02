using System.Text.Json;
using System.Text.Json.Nodes;
using MedReminder.Application.Abstractions;

namespace MedReminder.Infrastructure.Settings;

// profiles\<id>\ui.settings.json: UI preferences that belong to one
// profile rather than to the Windows user (docs/notes/
// EVOLUTION-PROPOSALS.md §3.2, docs/analysis/ANALYSIS-UI-MODERNIZATION.md
// §4.4). Shape:
//   { "TextSize": "Large", "Appearance": "Dark",
//     "MainWindow": { "X": 100, "Y": 80, "Width": 1200, "Height": 700, "Maximized": true } }
// Text size and appearance are read once at boot, before the main
// window is created, so they are not part of the reloadable
// configuration; the main window placement is written each time the
// window closes. A missing, unreadable or unknown value reads as the
// default: a UI preference must never block the app from opening.
public static class ProfileUiSettingsFile
{
    public const string FileName = "ui.settings.json";

    private const string TextSizeProperty = "TextSize";
    private const string AppearanceProperty = "Appearance";
    private const string MainWindowProperty = "MainWindow";
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };
    private static readonly JsonNodeOptions NodeOptions = new() { PropertyNameCaseInsensitive = true };

    public static string PathFor(string profileDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileDirectory);
        return Path.Combine(profileDirectory, FileName);
    }

    public static TextSize ReadTextSize(string profileDirectory)
        => TextSizes.Parse(ReadString(profileDirectory, TextSizeProperty));

    public static AppearanceMode ReadAppearance(string profileDirectory)
        => AppearanceModes.Parse(ReadString(profileDirectory, AppearanceProperty));

    // Null when no placement was saved or the saved one is damaged or
    // has no usable size: the window then opens at its default size.
    public static MainWindowPlacement? ReadMainWindow(string profileDirectory)
    {
        if (ReadRoot(profileDirectory)?[MainWindowProperty] is not JsonObject window) return null;
        try
        {
            var placement = new MainWindowPlacement(
                window["X"]!.GetValue<int>(),
                window["Y"]!.GetValue<int>(),
                window["Width"]!.GetValue<int>(),
                window["Height"]!.GetValue<int>(),
                window["Maximized"]?.GetValue<bool>() ?? false);
            return placement.Width > 0 && placement.Height > 0 ? placement : null;
        }
        catch (Exception ex) when (ex is NullReferenceException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    public static void WriteTextSize(string profileDirectory, TextSize size)
        => Update(profileDirectory, root => root[TextSizeProperty] = size.ToString());

    public static void WriteAppearance(string profileDirectory, AppearanceMode mode)
        => Update(profileDirectory, root => root[AppearanceProperty] = mode.ToString());

    public static void Write(string profileDirectory, TextSize size, AppearanceMode mode)
        => Update(profileDirectory, root =>
        {
            root[TextSizeProperty] = size.ToString();
            root[AppearanceProperty] = mode.ToString();
        });

    public static void WriteMainWindow(string profileDirectory, MainWindowPlacement placement)
    {
        ArgumentNullException.ThrowIfNull(placement);
        Update(profileDirectory, root => root[MainWindowProperty] = new JsonObject
        {
            ["X"] = placement.X,
            ["Y"] = placement.Y,
            ["Width"] = placement.Width,
            ["Height"] = placement.Height,
            ["Maximized"] = placement.Maximized,
        });
    }

    // Changes some properties and keeps the others, through a temporary
    // file so a crash never leaves a truncated file behind. A damaged
    // file is replaced by one holding only the new values.
    private static void Update(string profileDirectory, Action<JsonObject> change)
    {
        var path = PathFor(profileDirectory);
        Directory.CreateDirectory(profileDirectory);
        var root = ReadRoot(profileDirectory) ?? new JsonObject(NodeOptions);
        change(root);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, root.ToJsonString(WriteOptions));
        File.Move(tmp, path, overwrite: true);
    }

    private static string? ReadString(string profileDirectory, string name)
        => ReadRoot(profileDirectory)?[name] is JsonValue value
           && value.TryGetValue<string>(out var text)
            ? text
            : null;

    private static JsonObject? ReadRoot(string profileDirectory)
    {
        var path = PathFor(profileDirectory);
        try
        {
            if (!File.Exists(path)) return null;
            if (JsonNode.Parse(File.ReadAllText(path), NodeOptions) is not JsonObject root) return null;
            // A JsonObject builds its dictionary lazily: touch it here so a
            // duplicate property fails inside this catch.
            _ = root.Count;
            return root;
        }
        // ArgumentException: two properties whose names differ only in case.
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            return null;
        }
    }
}
