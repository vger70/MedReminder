using System.Globalization;
using System.Text.Json;

namespace MedReminder.Application.Catalogue;

// The `latest.json` manifest published by .github/workflows/
// download_aifa.yaml next to the monthly AIFA archive
// (docs/analysis/ANALYSIS-CATALOGUE-REMOTE-FEED.md §2.1). `Sha256` and
// `Size` are optional: the client verifies them when present.
public sealed record CatalogueFeedManifest(
    string Version,
    string? File,
    DateTimeOffset? Generated,
    string? Sha256,
    long? Size)
{
    public string ExpectedFileName => CatalogueFeedManifestParser.FileNameFor(Version);
}

// Pure JSON parser for the feed manifest, kept in the Application layer
// (like GitHubReleaseParser) so it is testable without HTTP. Unknown
// fields such as `csv_count` are ignored.
public static class CatalogueFeedManifestParser
{
    public static string FileNameFor(string version) => $"aifa-{version}.zip";

    public static bool TryParse(string json, out CatalogueFeedManifest? manifest, out string? error)
    {
        ArgumentNullException.ThrowIfNull(json);
        manifest = null;

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            error = "Malformed manifest: " + ex.Message;
            return false;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                error = "The manifest is not a JSON object.";
                return false;
            }

            if (!TryGetString(root, "version", out var version) || !SnapshotVersion.IsYearMonth(version))
            {
                error = "The manifest has no valid 'version' (yyyymm).";
                return false;
            }

            string? file = null;
            if (root.TryGetProperty("file", out var fileElement) && fileElement.ValueKind != JsonValueKind.Null)
            {
                file = fileElement.ValueKind == JsonValueKind.String ? fileElement.GetString() : null;
                if (!string.Equals(file, FileNameFor(version!), StringComparison.Ordinal))
                {
                    error = $"The manifest 'file' does not match '{FileNameFor(version!)}'.";
                    return false;
                }
            }

            DateTimeOffset? generated = null;
            if (TryGetString(root, "generated", out var generatedText)
                && DateTimeOffset.TryParse(generatedText, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
            {
                generated = parsed;
            }

            string? sha256 = null;
            if (root.TryGetProperty("sha256", out var shaElement) && shaElement.ValueKind != JsonValueKind.Null)
            {
                sha256 = shaElement.ValueKind == JsonValueKind.String ? shaElement.GetString() : null;
                if (!IsSha256Hex(sha256))
                {
                    error = "The manifest 'sha256' is not 64 hexadecimal characters.";
                    return false;
                }
                sha256 = sha256!.ToLowerInvariant();
            }

            long? size = null;
            if (root.TryGetProperty("size", out var sizeElement) && sizeElement.ValueKind != JsonValueKind.Null)
            {
                if (sizeElement.ValueKind != JsonValueKind.Number || !sizeElement.TryGetInt64(out var value) || value <= 0)
                {
                    error = "The manifest 'size' is not a positive integer.";
                    return false;
                }
                size = value;
            }

            manifest = new CatalogueFeedManifest(version!, file, generated, sha256, size);
            error = null;
            return true;
        }
    }

    private static bool TryGetString(JsonElement root, string name, out string? value)
    {
        value = null;
        if (!root.TryGetProperty(name, out var element) || element.ValueKind != JsonValueKind.String)
        {
            return false;
        }
        value = element.GetString();
        return !string.IsNullOrEmpty(value);
    }

    private static bool IsSha256Hex(string? value)
    {
        if (value is null || value.Length != 64)
        {
            return false;
        }
        foreach (var c in value)
        {
            if (!char.IsAsciiHexDigit(c))
            {
                return false;
            }
        }
        return true;
    }
}
