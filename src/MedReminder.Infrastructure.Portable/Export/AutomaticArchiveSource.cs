using System.Security.Cryptography;
using System.Text;
using MedReminder.Application.Export;

namespace MedReminder.Infrastructure.Export;

// The origin marker of an automatic cloud snapshot (C.3+,
// docs/analysis/ANALYSIS-C3PLUS-CLOUD-BACKUP.md §3.6): source "automatic"
// and the SHA-256 of the device name, never the name itself. One
// definition for the Windows ExportService and the portable ProfileArchive.
public static class AutomaticArchiveSource
{
    public const string Source = "automatic";

    public static ManifestDevice Device(string deviceName, string profileId)
        => new()
        {
            HostNameSha256 = HashDeviceName(deviceName),
            ProfileId = profileId,
        };

    public static string HashDeviceName(string? deviceName)
    {
        var normalized = (deviceName ?? string.Empty).Trim();
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
