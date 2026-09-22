using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;
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
    public const string SettingsFileName = AppDataPaths.DonationsSettingsFileName;

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
            var assembly = typeof(JsonDonationOptionsProvider).Assembly;

            using var stream = assembly.GetManifestResourceStream(AppDataPaths.DonationsSettingsFileName);

            if (stream is null)
                return new DonationOptions();

            using var reader = new StreamReader(stream);

            var json = reader.ReadToEnd();

            var root = JsonSerializer.Deserialize<DonationsFileRoot>(
                json,
                JsonOptions);

            return root?.Donations ?? new DonationOptions();
        }
        catch
        {
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
