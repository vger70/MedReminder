using System.Reflection;
using MedReminder.Domain.Catalogue;

namespace MedReminder.Infrastructure.Catalogue;

// Surfaces reference-catalogue snapshots shipped as embedded resources
// under `Assets/Catalogue/<country>/aifa-<yyyymm>.<ext>` (or the
// corresponding EMA path). Stub in M1: no snapshot is actually
// embedded yet — M2 lands the first real AIFA snapshot. Until then,
// TryOpen returns false for every country and the DI-registered
// importer sits idle.
public sealed class EmbeddedSnapshotProvider
{
    private readonly Assembly _assembly;

    public EmbeddedSnapshotProvider(Assembly? assembly = null)
    {
        _assembly = assembly ?? typeof(EmbeddedSnapshotProvider).Assembly;
    }

    // Attempts to open the current snapshot embedded for `country`.
    // Returns false when no snapshot for that country is bundled in
    // this build. `snapshotVersion` is derived from the resource name
    // (e.g. "aifa-202609.zip" → "202609").
    public bool TryOpen(
        CountryCode country,
        out Stream snapshot,
        out string snapshotVersion)
    {
        snapshot = Stream.Null;
        snapshotVersion = string.Empty;

        var prefix = $"MedReminder.Infrastructure.Assets.Catalogue.{country.Value.ToLowerInvariant()}.";
        var resourceName = _assembly.GetManifestResourceNames()
            .FirstOrDefault(name => name.StartsWith(prefix, StringComparison.Ordinal));
        if (resourceName is null)
        {
            return false;
        }

        var opened = _assembly.GetManifestResourceStream(resourceName);
        if (opened is null)
        {
            return false;
        }

        snapshot = opened;
        snapshotVersion = ExtractVersion(resourceName, prefix);
        return true;
    }

    private static string ExtractVersion(string resourceName, string prefix)
    {
        var tail = resourceName[prefix.Length..];
        var dot = tail.IndexOf('.');
        var withoutExtension = dot >= 0 ? tail[..dot] : tail;
        var dash = withoutExtension.LastIndexOf('-');
        return dash >= 0 ? withoutExtension[(dash + 1)..] : withoutExtension;
    }
}
