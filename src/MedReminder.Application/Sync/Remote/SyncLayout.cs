using System.Globalization;

namespace MedReminder.Application.Sync.Remote;

// Paths of the remote storage (docs/SYNC-FORMAT.md §2,
// docs/analysis/ANALYSIS-B1-MOBILE-SYNC.md §5.1). One folder per sync
// group; names are random ids and counters only (R4).
//
//   <groupId>/group.json
//   <groupId>/key.<keyVersion>.wrap
//   <groupId>/genesis/<generation>.mrg
//   <groupId>/checkpoints/<generation>/<deviceId>-<n>.mrc
//   <groupId>/ops/<generation>/<deviceId>/<seq>.mrs
//   <groupId>/devices/<deviceId>.mrd
//   <groupId>/pairing/<deviceId>.mrp
//
// The current generation is the highest genesis present and the current
// key version the highest wrap present: each of those files has one
// writer, so group.json never changes after it is created (R5). Phase
// 4c: a generation has one key version, the one its genesis is sealed
// with (SyncKeys); a key rotation starts a new generation.
public static class SyncLayout
{
    public static string Group(Guid groupId) => Id(groupId);

    public static string GroupFile(Guid groupId) => $"{Id(groupId)}/group.json";

    public static string KeyWrap(Guid groupId, int keyVersion) => $"{Id(groupId)}/key.{Num(keyVersion)}.wrap";

    public static string GenesisFolder(Guid groupId) => $"{Id(groupId)}/genesis/";

    public static string Genesis(Guid groupId, int generation) => $"{Id(groupId)}/genesis/{Num(generation)}.mrg";

    public static string CheckpointFolder(Guid groupId, int generation) => $"{Id(groupId)}/checkpoints/{Num(generation)}/";

    public static string Checkpoint(Guid groupId, int generation, Guid deviceId, int n)
        => $"{CheckpointFolder(groupId, generation)}{Id(deviceId)}-{Num(n)}.mrc";

    public static string OpsFolder(Guid groupId, int generation) => $"{Id(groupId)}/ops/{Num(generation)}/";

    public static string Segment(Guid groupId, int generation, Guid deviceId, int seq)
        => $"{OpsFolder(groupId, generation)}{Id(deviceId)}/{Num(seq)}.mrs";

    public static string DevicesFolder(Guid groupId) => $"{Id(groupId)}/devices/";

    public static string Device(Guid groupId, Guid deviceId) => $"{DevicesFolder(groupId)}{Id(deviceId)}.mrd";

    // Phase 4c: the pairing offer a device shows as a QR code, one per
    // device, removed when the offer ends.
    public static string Pairing(Guid groupId, Guid deviceId) => $"{Id(groupId)}/pairing/{Id(deviceId)}.mrp";

    // "<groupId>/ops/<g>/<deviceId>/<seq>.mrs" -> (deviceId, seq).
    public static bool TryParseSegment(string path, out Guid deviceId, out int seq)
    {
        deviceId = default;
        seq = 0;
        var parts = path.Split('/');
        return parts.Length == 5 && parts[1] == "ops" && parts[4].EndsWith(".mrs", StringComparison.Ordinal)
            && Guid.TryParseExact(parts[3], "N", out deviceId)
            && int.TryParse(parts[4][..^4], NumberStyles.None, CultureInfo.InvariantCulture, out seq);
    }

    // "<groupId>/checkpoints/<g>/<deviceId>-<n>.mrc" -> (deviceId, n).
    public static bool TryParseCheckpoint(string path, out Guid deviceId, out int n)
    {
        deviceId = default;
        n = 0;
        var name = path[(path.LastIndexOf('/') + 1)..];
        var dash = name.IndexOf('-');
        return name.EndsWith(".mrc", StringComparison.Ordinal) && dash == 32
            && Guid.TryParseExact(name[..dash], "N", out deviceId)
            && int.TryParse(name[(dash + 1)..^4], NumberStyles.None, CultureInfo.InvariantCulture, out n);
    }

    // "<groupId>/genesis/<g>.mrg" or "<groupId>/key.<v>.wrap" -> number.
    public static bool TryParseNumber(string path, string prefix, string suffix, out int value)
    {
        value = 0;
        var name = path[(path.LastIndexOf('/') + 1)..];
        return name.StartsWith(prefix, StringComparison.Ordinal) && name.EndsWith(suffix, StringComparison.Ordinal)
            && int.TryParse(name[prefix.Length..^suffix.Length], NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }

    private static string Id(Guid id) => id.ToString("N");

    private static string Num(int value) => value.ToString(CultureInfo.InvariantCulture);
}
