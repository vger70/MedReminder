using System.Text.Json;
using MedReminder.Application.Abstractions;
using MedReminder.Application.Sync.Remote;

namespace MedReminder.Application.Household.Remote;

// Files of a household group (household feature, step H3a;
// docs/analysis/ANALYSIS-HOUSEHOLD-MASTER-DEVICE.md §5.1). The layout is
// the one of a profile group (SyncLayout) except the cleartext file,
// household.json instead of group.json: apps that list group.json, up to
// 2.10 included, never see a household. Key wrap, genesis, segments and
// device records use the envelope and header of docs/SYNC-FORMAT.md, with
// the household id as group id.
public sealed record HouseholdFile(string Format, int FormatVersion, Guid HouseholdId)
{
    public const string FormatName = "MedReminder.Household";

    public static string PathOf(Guid householdId) => $"{householdId:N}/household.json";

    public static HouseholdFile Create(Guid householdId) => new(FormatName, SyncFileCodec.FormatVersion, householdId);

    public byte[] ToBytes() => JsonSerializer.SerializeToUtf8Bytes(this, SyncFileCodec.Json);

    public static HouseholdFile Parse(byte[] content)
    {
        var file = JsonSerializer.Deserialize<HouseholdFile>(content, SyncFileCodec.Json)
            ?? throw new InvalidDataException("Empty household.json.");
        if (file.Format != FormatName) throw new InvalidDataException("Not a MedReminder household.");
        if (file.FormatVersion > SyncFileCodec.FormatVersion)
            throw new NotSupportedException($"Household format {file.FormatVersion} is not supported.");
        return file;
    }

    // Households present in the storage.
    public static async Task<IReadOnlyList<Guid>> ListAsync(ISyncTransport transport, CancellationToken ct)
        => [.. (await transport.ListAsync(string.Empty, ct))
            .Where(p => p.EndsWith("/household.json", StringComparison.Ordinal))
            .Select(p => Guid.TryParseExact(p[..p.IndexOf('/')], "N", out var id) ? id : Guid.Empty)
            .Where(id => id != Guid.Empty)];
}

// Content of a household segment, and of the household image (genesis):
// operations in HLC order. The household is small (profiles and settings
// change rarely), so the image is the whole log and there are no
// checkpoints.
public sealed record HouseholdSegmentContent(
    IReadOnlyDictionary<Guid, int> Dependencies,
    IReadOnlyList<HouseholdSegmentOperation> Operations)
{
    public const int ContentVersion = 1;

    public byte[] ToBytes() => JsonSerializer.SerializeToUtf8Bytes(this, SyncFileCodec.Json);

    public static HouseholdSegmentContent Parse(byte[] content)
        => JsonSerializer.Deserialize<HouseholdSegmentContent>(content, SyncFileCodec.Json)
            ?? throw new InvalidDataException("Empty household segment.");
}

public sealed record HouseholdSegmentOperation(
    Guid Id,
    long PhysicalMs,
    int Counter,
    Guid DeviceId,
    string Type,
    int SchemaVersion,
    string ProfileId,
    string Payload);
