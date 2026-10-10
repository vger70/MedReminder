using MedReminder.Application.Abstractions;

namespace MedReminder.Infrastructure.Tests.Support;

// An IArchiveStorage in memory, with a creation time per archive and
// hooks to make an upload or a delete fail.
internal sealed class MemoryArchiveStorage : IArchiveStorage
{
    private readonly Dictionary<string, (string Name, DateTimeOffset Created, byte[] Bytes)> _archives = new();
    private int _next;

    public Func<string, Exception?> FailUpload { get; set; } = _ => null;

    public Func<string, Exception?> FailDelete { get; set; } = _ => null;

    public DateTimeOffset UploadTime { get; set; } = DateTimeOffset.UnixEpoch;

    public IReadOnlyCollection<string> Names => _archives.Values.Select(a => a.Name).ToList();

    public string Add(string name, DateTimeOffset created, byte[]? bytes = null)
    {
        var id = "id-" + _next++;
        _archives[id] = (name, created, bytes ?? []);
        return id;
    }

    public byte[] Bytes(string id) => _archives[id].Bytes;

    public async Task<string> UploadAsync(Stream archive, string suggestedName, CancellationToken ct)
    {
        await using (archive)
        {
            if (FailUpload(suggestedName) is { } failure) throw failure;
            using var copy = new MemoryStream();
            await archive.CopyToAsync(copy, ct);
            return Add(suggestedName, UploadTime, copy.ToArray());
        }
    }

    public Task<Stream> DownloadAsync(string id, CancellationToken ct)
        => Task.FromResult<Stream>(new MemoryStream(_archives[id].Bytes, writable: false));

    public Task<IReadOnlyList<ArchiveInfo>> ListAsync(CancellationToken ct)
        => Task.FromResult<IReadOnlyList<ArchiveInfo>>(_archives
            .Select(a => new ArchiveInfo(a.Key, a.Value.Name, a.Value.Created, a.Value.Bytes.Length))
            .ToList());

    public Task DeleteAsync(string id, CancellationToken ct)
    {
        if (FailDelete(_archives[id].Name) is { } failure) throw failure;
        _archives.Remove(id);
        return Task.CompletedTask;
    }
}
