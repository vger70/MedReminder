using System.Text.Json;
using System.Text.Json.Serialization;
using MedReminder.Application.Donations;
using MedReminder.Infrastructure.Storage;

namespace MedReminder.Infrastructure.Donations;

// Reads the donation configuration from the shared, admin-managed
// donations.settings.json at the %LOCALAPPDATA%\MedReminder\ root (A6,
// docs/ANALYSIS-A6-DONATION-SUPPORT.md §3.2, §4.2). The file shape
// mirrors the other shared settings — a single top-level "Donations"
// object — and holds only public URLs, so it is plain JSON with no
// DPAPI.
//
// Tolerant by design: a missing file, a missing "Donations" section,
// an empty file or a corrupted file all bind to DonationOptions with
// Enabled = false, which silently turns the whole feature off. Never
// throws to the caller.
public sealed class JsonDonationOptionsProvider
{
    public const string SettingsFileName = "donations.settings.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    // Loads from the standard shared location.
    public DonationOptions Load() =>
        Load(Path.Combine(AppDataPaths.GetAppDataDirectory(), SettingsFileName));

    // Loads from an explicit path (used by tests). Any failure yields a
    // disabled DonationOptions.
    public DonationOptions Load(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return new DonationOptions();
            }
            var json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json))
            {
                return new DonationOptions();
            }
            var root = JsonSerializer.Deserialize<DonationsFileRoot>(json, JsonOptions);
            return root?.Donations ?? new DonationOptions();
        }
        catch
        {
            // Corrupted or unreadable: feature stays silently off.
            return new DonationOptions();
        }
    }

    // Envelope matching the on-disk shape: { "Donations": { ... } }.
    private sealed class DonationsFileRoot
    {
        [JsonPropertyName("Donations")]
        public DonationOptions? Donations { get; set; }
    }
}
