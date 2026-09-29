using MedReminder.Application.Abstractions;

namespace MedReminder.Infrastructure.Sync;

// ISyncTransport over a folder (B.1 Phase 3c, docs/analysis/
// ANALYSIS-B1-MOBILE-SYNC.md §5.8): a folder that a third-party client
// (OneDrive, Google Drive, Dropbox desktop) or a NAS share keeps in sync
// between PCs. The folder is chosen by the user, like the cloud-backup
// folder of C.3+.
//
// A file is written under a temporary name starting with '.' and then
// renamed, so other devices and the sync client never see it half
// written; List skips those names. A create never replaces a file.
public sealed class LocalFolderSyncTransport : ISyncTransport
{
    private const string TempPrefix = ".tmp-";

    private readonly string _root;

    public LocalFolderSyncTransport(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        _root = Path.GetFullPath(root);
    }

    public Task<IReadOnlyList<string>> ListAsync(string prefix, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        if (!Directory.Exists(_root)) return Task.FromResult<IReadOnlyList<string>>([]);
        IReadOnlyList<string> files = [.. Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories)
            .Where(f => !Path.GetFileName(f).StartsWith('.'))
            .Select(f => Path.GetRelativePath(_root, f).Replace('\\', '/'))
            .Where(p => p.StartsWith(prefix, StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)];
        return Task.FromResult(files);
    }

    public async Task<byte[]?> ReadAsync(string path, CancellationToken cancellationToken)
    {
        var full = Resolve(path);
        try
        {
            return await File.ReadAllBytesAsync(full, cancellationToken);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
    }

    public async Task<bool> CreateAsync(string path, byte[] content, CancellationToken cancellationToken)
    {
        var full = Resolve(path);
        if (File.Exists(full)) return false;
        var temp = await WriteTempAsync(full, content, cancellationToken);
        try
        {
            File.Move(temp, full, overwrite: false);
            return true;
        }
        catch (IOException) when (File.Exists(full))
        {
            File.Delete(temp);
            return false;
        }
    }

    public async Task WriteAsync(string path, byte[] content, CancellationToken cancellationToken)
    {
        var full = Resolve(path);
        var temp = await WriteTempAsync(full, content, cancellationToken);
        File.Move(temp, full, overwrite: true);
    }

    public Task DeleteAsync(string path, CancellationToken cancellationToken)
    {
        var full = Resolve(path);
        if (File.Exists(full)) File.Delete(full);
        return Task.CompletedTask;
    }

    private static async Task<string> WriteTempAsync(string full, byte[] content, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        var temp = Path.Combine(Path.GetDirectoryName(full)!, TempPrefix + Guid.NewGuid().ToString("N"));
        await File.WriteAllBytesAsync(temp, content, cancellationToken);
        return temp;
    }

    // Relative paths only, never outside the root.
    private string Resolve(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var full = Path.GetFullPath(Path.Combine(_root, path.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new ArgumentException("The path leaves the sync folder.", nameof(path));
        return full;
    }
}
