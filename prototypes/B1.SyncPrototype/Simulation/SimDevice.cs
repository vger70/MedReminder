namespace MedReminder.Prototypes.Sync.Simulation;

// One device: local replica, HLC, outbox, applied vector and causal
// buffer (§5.2, §5.3). Operations it authored stay in _ownSegments so it
// can rebuild after a bootstrap and re-publish a segment found missing.
public sealed class SimDevice
{
    private readonly List<Operation> _outbox = new();
    private readonly Dictionary<(Guid Device, int Seq), Segment> _buffer = new();
    private readonly List<Segment> _ownSegments = new();
    private int _nextSeq = 1;

    public SimDevice(string name, Func<long> physicalMs)
    {
        Name = name;
        Id = Guid.NewGuid();
        Clock = new HlcClock(Id, physicalMs);
    }

    public string Name { get; }
    public Guid Id { get; }
    public HlcClock Clock { get; }
    public Replica Replica { get; private set; } = new();
    public Dictionary<Guid, int> Applied { get; private set; } = new();
    public bool Online { get; set; } = true;
    public int Bootstraps { get; private set; }
    public int DuplicatesIgnored { get; private set; }

    public void Local(Operation op)
    {
        Replica.Apply(op);
        _outbox.Add(op);
    }

    public Hlc Next() => Clock.Now();

    public void Seal(SimCloud cloud)
    {
        if (_outbox.Count == 0) return;
        // Dependency vector at seal time (§5.2).
        var deps = new Dictionary<Guid, int>(Applied);
        deps.Remove(Id);
        var segment = new Segment(Id, _nextSeq, deps, _outbox.ToList());
        cloud.Upload(segment);
        _ownSegments.Add(segment);
        Applied[Id] = _nextSeq;
        _nextSeq++;
        _outbox.Clear();
    }

    // Returns true when anything new was applied.
    public bool Pull(SimCloud cloud, Random rng, long nowMs)
    {
        var progress = false;

        // Re-publish own segments that disappeared (tail truncation or
        // deletion before every peer applied them, §6.3).
        foreach (var own in _ownSegments)
        {
            var coveredByCheckpoint = cloud.Checkpoints.Any(c => c.Vector.TryGetValue(Id, out var v) && v >= own.Seq);
            if (!cloud.Exists(Id, own.Seq) && !coveredByCheckpoint)
                cloud.Upload(own);
        }

        foreach (var folder in cloud.Folders.Where(f => f != Id).OrderBy(_ => rng.Next()))
        {
            var have = Applied.GetValueOrDefault(folder);
            var available = cloud.List(folder).Where(s => s.Seq > have).ToList();
            var latestKnown = Math.Max(
                available.Count == 0 ? 0 : available.Max(s => s.Seq),
                cloud.Checkpoints.Select(c => c.Vector.GetValueOrDefault(folder)).DefaultIfEmpty(0).Max());
            if (latestKnown > have && !cloud.Exists(folder, have + 1))
            {
                // Needed segments were compacted away: rebuild from the
                // newest checkpoint (§5.6).
                Bootstrap(cloud);
                return true;
            }

            // Random order and random duplicates of the download.
            foreach (var s in available.OrderBy(_ => rng.Next()))
            {
                if (!_buffer.TryAdd((s.DeviceId, s.Seq), s)) DuplicatesIgnored++;
                if (rng.NextDouble() < 0.2 && !_buffer.TryAdd((s.DeviceId, s.Seq), s)) DuplicatesIgnored++;
            }
        }

        bool applied;
        do
        {
            applied = false;
            foreach (var key in _buffer.Keys.ToList())
            {
                var s = _buffer[key];
                var have = Applied.GetValueOrDefault(s.DeviceId);
                if (s.Seq <= have)
                {
                    _buffer.Remove(key);
                    DuplicatesIgnored++;
                    continue;
                }
                if (s.Seq != have + 1) continue;
                if (s.Dependencies.Any(d => d.Key != Id && Applied.GetValueOrDefault(d.Key) < d.Value)) continue;
                foreach (var op in s.Operations)
                {
                    Clock.Observe(op.Hlc);
                    if (!Replica.Apply(op)) DuplicatesIgnored++;
                }
                Applied[s.DeviceId] = s.Seq;
                _buffer.Remove(key);
                applied = true;
                progress = true;
            }
        } while (applied);

        cloud.PublishDeviceRecord(Id, Applied, nowMs);
        return progress;
    }

    public void WriteCheckpoint(SimCloud cloud, int number)
    {
        Seal(cloud);
        cloud.AddCheckpoint(new Checkpoint(Id, number, Replica.CloneAsCheckpoint(), new Dictionary<Guid, int>(Applied)));
    }

    // Join or rebuild: newest checkpoint, then own operations (sealed and
    // pending) re-applied idempotently.
    public void Bootstrap(SimCloud cloud)
    {
        Bootstraps++;
        // Not simply the newest checkpoint: one written by a device that
        // was still catching up may not cover segments already deleted.
        // Pick the newest checkpoint that reaches every folder's first
        // remaining segment.
        var checkpoint = cloud.Checkpoints
            .Reverse()
            .FirstOrDefault(c => cloud.Folders.All(f =>
            {
                var present = cloud.List(f);
                var needed = present.Count == 0
                    ? cloud.Checkpoints.Select(x => x.Vector.GetValueOrDefault(f)).DefaultIfEmpty(0).Max()
                    : present.Min(x => x.Seq) - 1;
                return c.Vector.GetValueOrDefault(f) >= needed;
            }))
            ?? throw new InvalidOperationException("No checkpoint covers the compacted segments.");
        Replica = checkpoint.State.CloneAsCheckpoint();
        Applied = new Dictionary<Guid, int>(checkpoint.Vector);
        _buffer.Clear();
        foreach (var own in _ownSegments)
            foreach (var op in own.Operations)
                Replica.Apply(op);
        foreach (var op in _outbox)
            Replica.Apply(op);
        if (_ownSegments.Count > 0)
            Applied[Id] = Math.Max(Applied.GetValueOrDefault(Id), _ownSegments[^1].Seq);
        foreach (var op in Replica.Log)
            Clock.Observe(op.Hlc);
    }
}
