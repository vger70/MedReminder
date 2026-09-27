using MedReminder.Application.Abstractions;
using MedReminder.Application.Export;

namespace MedReminder.Infrastructure.Cloud.OneDrive;

// IArchiveStorage over the backups/ folder of the OneDrive app folder
// (C.3++ Phase 2, docs/analysis/ANALYSIS-C3PP-CLOUD-PROVIDERS.md §7.4;
// B.1 Phase 4a). The archives are the C.3 .mrz files, encrypted before
// they get here. Id = file name. Large archives go through an upload
// session under a temporary name, then a rename (spike S6 C10b), so a
// partial archive is never listed.
//
// "Target not available" (IArchiveStorage contract) is a signed-out
// account here: uploads throw DirectoryNotFoundException, listing is
// empty, so the backup host skips the run as it does for a missing
// folder.
public sealed class OneDriveArchiveStorage : IArchiveStorage
{
    public const string Folder = "backups";

    private readonly OneDriveClient _client;

    public OneDriveArchiveStorage(OneDriveClient client)
    {
        _client = client;
    }

    public async Task<string> UploadAsync(Stream archive, string suggestedName, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(archive);
        await using (archive)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(suggestedName);
            if (suggestedName.Contains('/') || suggestedName.Contains('\\') || suggestedName.StartsWith('.'))
            {
                throw new ArgumentException("The archive name must be a plain file name.", nameof(suggestedName));
            }

            Stream source = archive;
            MemoryStream? copy = null;
            if (!archive.CanSeek)
            {
                copy = new MemoryStream();
                await archive.CopyToAsync(copy, ct);
                copy.Position = 0;
                source = copy;
            }
            try
            {
                var length = source.Length - source.Position;
                DriveItem? stored;
                try
                {
                    stored = await _client.UploadAsync($"{Folder}/{suggestedName}", source, length, replace: false, ct);
                }
                catch (CloudSignInRequiredException ex)
                {
                    throw new DirectoryNotFoundException("OneDrive is not signed in on this device.", ex);
                }
                if (stored is null)
                {
                    throw new IOException($"An archive named '{suggestedName}' already exists.");
                }
                return suggestedName;
            }
            finally
            {
                copy?.Dispose();
            }
        }
    }

    public async Task<Stream> DownloadAsync(string id, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return await _client.OpenReadAsync($"{Folder}/{id}", ct)
            ?? throw new FileNotFoundException("The archive no longer exists.", id);
    }

    public async Task<IReadOnlyList<ArchiveInfo>> ListAsync(CancellationToken ct)
    {
        IReadOnlyList<DriveItem> children;
        try
        {
            children = await _client.ListChildrenAsync(Folder, ct);
        }
        catch (CloudSignInRequiredException)
        {
            return [];
        }
        var archives = children
            .Where(i => !i.IsFolder && i.Name is { } name && !name.StartsWith('.')
                && name.EndsWith(ExportFormat.ArchiveExtension, StringComparison.OrdinalIgnoreCase))
            .Select(i => new ArchiveInfo(i.Name!, i.Name!, i.LastModified ?? DateTimeOffset.MinValue, i.Size))
            .ToList();
        archives.Sort((a, b) => b.CreatedAtUtc.CompareTo(a.CreatedAtUtc));
        return archives;
    }

    public async Task DeleteAsync(string id, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        await _client.DeleteAsync($"{Folder}/{id}", ct);
    }
}
