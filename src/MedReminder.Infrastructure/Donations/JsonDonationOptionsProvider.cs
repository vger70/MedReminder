using System.Text.Json;
using System.Text.Json.Serialization;
using MedReminder.Application.Donations;
using MedReminder.Infrastructure.Storage;

namespace MedReminder.Infrastructure.Donations;

// Reads the donation configuration from assets/donations.settings.json,
// embedded in this assembly with the logical name
// "donations.settings.json" (see MedReminder.Infrastructure.csproj).
// Nothing is read from %LOCALAPPDATA%\MedReminder\ or from the install
// directory, so changing the payment links requires a new build. The
// original design (A6, docs/analysis/ANALYSIS-A6-DONATION-SUPPORT.md
// §3.2, §4.2) read an admin-managed file from the data folder. The
// file shape is a single top-level "Donations" object holding only
// public URLs, so it is plain JSON with no DPAPI.
//
// Tolerant by design: a missing resource, a missing "Donations"
// section or corrupted JSON all bind to DonationOptions with
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

    // Loads the embedded configuration.
    public static DonationOptions Load() =>
        Load(Path.Combine(AppDataPaths.GetAppDataDirectory(), SettingsFileName));

    // The path argument is currently ignored: the configuration always
    // comes from the embedded resource. Any failure yields a disabled
    // DonationOptions.
    public static DonationOptions Load(string path)
    {
        try
        {
            var assembly = typeof(JsonDonationOptionsProvider).Assembly;

            using var stream =
                typeof(JsonDonationOptionsProvider)
                .Assembly
                .GetManifestResourceStream(SettingsFileName);

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
