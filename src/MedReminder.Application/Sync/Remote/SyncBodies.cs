using System.Buffers.Binary;
using System.Text.Json;
using MedReminder.Domain.Sync;

namespace MedReminder.Application.Sync.Remote;

// Content of a segment (docs/SYNC-FORMAT.md §4): the operations a device
// produced since its previous segment, in its local HLC order, and its
// applied vector when the segment was sealed (dependencies, §5.2).
public sealed record SegmentContent(
    IReadOnlyDictionary<Guid, int> Dependencies,
    IReadOnlyList<SegmentOperation> Operations)
{
    public byte[] ToBytes() => JsonSerializer.SerializeToUtf8Bytes(this, SyncFileCodec.Json);

    public static SegmentContent Parse(byte[] content)
        => JsonSerializer.Deserialize<SegmentContent>(content, SyncFileCodec.Json)
            ?? throw new InvalidDataException("Empty segment.");
}

public sealed record SegmentOperation(
    Guid Id,
    long PhysicalMs,
    int Counter,
    Guid DeviceId,
    string Type,
    int SchemaVersion,
    Guid MedicineId,
    Guid? EntityId,
    string Payload)
{
    public static SegmentOperation From(SyncOperation o)
        => new(o.Id, o.HlcPhysicalMs, o.HlcCounter, o.DeviceId, o.Type, o.SchemaVersion, o.MedicineId, o.EntityId, o.Payload);

    public SyncOperation ToOperation(int generation) => new()
    {
        Id = Id,
        HlcPhysicalMs = PhysicalMs,
        HlcCounter = Counter,
        DeviceId = DeviceId,
        Generation = generation,
        Type = Type,
        SchemaVersion = SchemaVersion,
        MedicineId = MedicineId,
        EntityId = EntityId,
        Payload = Payload,
    };
}

// Content of devices/<deviceId>.mrd (§5.1): what the other devices need
// to know about this one. Encrypted: the name may be personal.
public sealed record DeviceRecordContent(
    Guid DeviceId,
    string Name,
    string Platform,
    string AppVersion,
    int PublishedSeq,
    IReadOnlyDictionary<Guid, int> Applied,
    DateTimeOffset LastSeen)
{
    public byte[] ToBytes() => JsonSerializer.SerializeToUtf8Bytes(this, SyncFileCodec.Json);

    public static DeviceRecordContent Parse(byte[] content)
        => JsonSerializer.Deserialize<DeviceRecordContent>(content, SyncFileCodec.Json)
            ?? throw new InvalidDataException("Empty device record.");
}

// Content of a genesis or checkpoint: a small JSON part, then the
// database image (ISyncSnapshotStore).
//
//   uint32 LE JSON length | JSON { snapshotSchema } | image bytes
public sealed record SnapshotContent(int SnapshotSchema, byte[] Image)
{
    private sealed record Meta(int SnapshotSchema);

    public byte[] ToBytes()
    {
        var meta = JsonSerializer.SerializeToUtf8Bytes(new Meta(SnapshotSchema), SyncFileCodec.Json);
        var bytes = new byte[4 + meta.Length + Image.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)meta.Length);
        meta.CopyTo(bytes, 4);
        Image.CopyTo(bytes, 4 + meta.Length);
        return bytes;
    }

    public static SnapshotContent Parse(byte[] content)
    {
        var length = (int)BinaryPrimitives.ReadUInt32LittleEndian(content);
        var meta = JsonSerializer.Deserialize<Meta>(content.AsSpan(4, length), SyncFileCodec.Json)
            ?? throw new InvalidDataException("Empty snapshot metadata.");
        return new SnapshotContent(meta.SnapshotSchema, content[(4 + length)..]);
    }
}
