using System.Text.Json;
using System.Text.Json.Nodes;
using MedReminder.Application.Abstractions;
using MedReminder.Application.GuidedSetup;
using MedReminder.Domain.Notifications;

namespace MedReminder.Infrastructure.Settings;

// profiles\<id>\ui.settings.json: UI preferences that belong to one
// profile rather than to the Windows user (docs/notes/
// EVOLUTION-PROPOSALS.md §3.2, docs/analysis/ANALYSIS-UI-MODERNIZATION.md
// §4.4). Shape:
//   { "TextSize": "Large", "Appearance": "Dark",
//     "MainWindow": { "X": 100, "Y": 80, "Width": 1200, "Height": 700, "Maximized": true },
//     "NavigationWidth": 300, "GuidedSetupShown": true,
//     "NewMedicineThresholdDays": 10, "NewMedicineChannels": "Both" }
// Text size and appearance are read once at boot, before the main
// window is created, so they are not part of the reloadable
// configuration; the main window placement and the width of its
// navigation pane (at 96 DPI) are written each time the window closes.
// The guided setup (docs/prompt/PROMPT-GUIDED-SETUP.md) writes whether
// it was shown on this device and the lead time and channels a new
// medicine starts with; none of them is replicated or exported. A
// missing, unreadable or unknown value reads as the default: a UI
// preference must never block the app from opening.
public static class ProfileUiSettingsFile
{
    public const string FileName = "ui.settings.json";

    private const string TextSizeProperty = "TextSize";
    private const string AppearanceProperty = "Appearance";
    private const string MainWindowProperty = "MainWindow";
    private const string NavigationWidthProperty = "NavigationWidth";
    private const string GuidedSetupShownProperty = "GuidedSetupShown";
    private const string NewMedicineThresholdDaysProperty = "NewMedicineThresholdDays";
    private const string NewMedicineChannelsProperty = "NewMedicineChannels";
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

    // Null when no width was saved or the saved one is not a positive
    // number: the pane then keeps its default width.
    public static int? ReadNavigationWidth(string profileDirectory)
        => ReadRoot(profileDirectory)?[NavigationWidthProperty] is JsonValue value
           && value.TryGetValue<int>(out var width)
           && width > 0
            ? width
            : null;

    public static void WriteNavigationWidth(string profileDirectory, int width)
        => Update(profileDirectory, root => root[NavigationWidthProperty] = width);

    // False when the guided setup never closed on this device for this
    // profile, or the value is not a boolean.
    public static bool ReadGuidedSetupShown(string profileDirectory)
        => ReadRoot(profileDirectory)?[GuidedSetupShownProperty] is JsonValue value
           && value.TryGetValue<bool>(out var shown)
           && shown;

    public static void WriteGuidedSetupShown(string profileDirectory)
        => Update(profileDirectory, root => root[GuidedSetupShownProperty] = true);

    // Null when the guided setup did not store them, or one of the two is
    // missing or out of range: the medicine dialog then keeps its own
    // defaults (NewMedicineDefaults.BuiltIn).
    public static NewMedicineDefaults? ReadNewMedicineDefaults(string profileDirectory)
    {
        var root = ReadRoot(profileDirectory);
        if (root?[NewMedicineThresholdDaysProperty] is not JsonValue days
            || !days.TryGetValue<int>(out var thresholdDays)) return null;
        if (root[NewMedicineChannelsProperty] is not JsonValue channelsValue
            || !channelsValue.TryGetValue<string>(out var channelsText)
            || int.TryParse(channelsText, out _)
            || !Enum.TryParse<NotificationChannels>(channelsText, ignoreCase: true, out var channels)) return null;
        return NewMedicineDefaults.TryCreate(thresholdDays, channels);
    }

    public static void WriteNewMedicineDefaults(string profileDirectory, NewMedicineDefaults defaults)
    {
        ArgumentNullException.ThrowIfNull(defaults);
        if (NewMedicineDefaults.TryCreate(defaults.ThresholdDays, defaults.Channels) is null)
            throw new ArgumentException("The lead time or the channels are out of range.", nameof(defaults));
        Update(profileDirectory, root =>
        {
            root[NewMedicineThresholdDaysProperty] = defaults.ThresholdDays;
            // The member name, not the number, as for the text size.
            root[NewMedicineChannelsProperty] = defaults.Channels.ToString();
        });
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
