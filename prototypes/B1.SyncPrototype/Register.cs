namespace MedReminder.Prototypes.Sync;

// Last-writer-wins register that keeps every version (§4.2). The full
// history makes application commutative (a set of versions), gives the
// value "as of" any HLC for count-anchor evaluation, and backs the
// conflict review's one-tap restore.
public sealed class Register<T>
{
    private readonly SortedDictionary<Hlc, T> _versions = new();

    public bool HasValue => _versions.Count > 0;

    public int VersionCount => _versions.Count;

    public void Set(Hlc hlc, T value) => _versions[hlc] = value;

    public T? Current => _versions.Count == 0 ? default : _versions.Last().Value;

    public Hlc CurrentHlc => _versions.Count == 0 ? Hlc.Zero : _versions.Last().Key;

    // Latest version strictly before `asOf`; `null` means "no limit".
    public bool TryGetAsOf(Hlc? asOf, out T value)
    {
        value = default!;
        var found = false;
        foreach (var (hlc, v) in _versions)
        {
            if (asOf is { } limit && hlc >= limit) break;
            value = v;
            found = true;
        }
        return found;
    }

    public IEnumerable<KeyValuePair<Hlc, T>> Versions => _versions;
}
