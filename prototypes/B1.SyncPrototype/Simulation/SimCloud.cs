namespace MedReminder.Prototypes.Sync.Simulation;

// Encrypted segment of one device (§5.2). Encryption is out of scope for
// S9; the header fields that the design binds as associated data are
// kept so the apply rules can use them.
public sealed record Segment(Guid DeviceId, int Seq, IReadOnlyDictionary<Guid, int> Dependencies, IReadOnlyList<Operation> Operations);

public sealed record Checkpoint(Guid Author, int Number, Replica State, IReadOnlyDictionary<Guid, int> Vector);

public sealed record DeviceRecord(IReadOnlyDictionary<Guid, int> AppliedVector, long LastSeenMs);

// In-memory stand-in for the user's cloud folder (§5.1). One folder per
// device; create-only uploads, so no file ever has two writers (R5).
public sealed class SimCloud
{
    private readonly Dictionary<Guid, SortedDictionary<int, Segment>> _folders = new();
    private readonly Dictionary<Guid, DeviceRecord> _devices = new();
    private readonly List<Checkpoint> _checkpoints = new();

    public int SegmentsDeleted { get; private set; }

    public IReadOnlyList<Checkpoint> Checkpoints => _checkpoints;

    public IReadOnlyDictionary<Guid, DeviceRecord> DeviceRecords => _devices;

    public void Upload(Segment segment)
    {
        var folder = Folder(segment.DeviceId);
        if (!folder.TryAdd(segment.Seq, segment))
            throw new InvalidOperationException($"Segment {segment.DeviceId}/{segment.Seq} already exists.");
    }

    public IEnumerable<Guid> Folders => _folders.Keys;

    public IReadOnlyCollection<Segment> List(Guid deviceId) => Folder(deviceId).Values;

    public bool Exists(Guid deviceId, int seq) => Folder(deviceId).ContainsKey(seq);

    public void PublishDeviceRecord(Guid deviceId, IReadOnlyDictionary<Guid, int> vector, long nowMs)
        => _devices[deviceId] = new DeviceRecord(new Dictionary<Guid, int>(vector), nowMs);

    public void AddCheckpoint(Checkpoint checkpoint) => _checkpoints.Add(checkpoint);

    // §5.6: a segment is deleted when a checkpoint covers it and every
    // active (not stale) device has applied it.
    public void CollectGarbage(long nowMs, long staleAfterMs)
    {
        if (_checkpoints.Count == 0) return;
        var active = _devices.Values.Where(d => nowMs - d.LastSeenMs <= staleAfterMs).ToList();
        foreach (var (deviceId, folder) in _folders)
        {
            foreach (var seq in folder.Keys.ToList())
            {
                var covered = _checkpoints.Any(c => c.Vector.TryGetValue(deviceId, out var v) && v >= seq);
                var applied = active.All(d => d.AppliedVector.TryGetValue(deviceId, out var v) && v >= seq);
                if (covered && applied)
                {
                    folder.Remove(seq);
                    SegmentsDeleted++;
                }
            }
        }
    }

    private SortedDictionary<int, Segment> Folder(Guid deviceId)
    {
        if (!_folders.TryGetValue(deviceId, out var folder))
        {
            folder = new SortedDictionary<int, Segment>();
            _folders[deviceId] = folder;
        }
        return folder;
    }
}
