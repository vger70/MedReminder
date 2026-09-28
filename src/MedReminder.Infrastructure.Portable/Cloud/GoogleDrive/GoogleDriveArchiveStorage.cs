using MedReminder.Application.Abstractions;
using MedReminder.Application.Export;

namespace MedReminder.Infrastructure.Cloud.GoogleDrive;

// IArchiveStorage over a visible "MedReminder/backups" folder in the
// user's My Drive (C.3++ Phase 2, B.1 Phase 4b; product owner, 2026-09-28:
// backups visible, sync hidden). The archives are the C.3 .mrz files,
// encrypted before they get here. Id = Drive file id.
//
// With the drive.file scope the app sees only what it created, so its
// folders are found by a public property rather than by name (a user's
// own "MedReminder" folder is never touched). The folders are created on
// the first upload; if two exist (created twice before the listing
// caught up) the oldest is used, as everywhere on Drive (GoogleFile.Winner).
// Large archives go through a resumable upload, which is not listed
// before it completes (spike S7 C6b).
//
// A session that needs a new sign-in reaches the caller as
// CloudSignInRequiredException from every operation, as for OneDrive.
public sealed class GoogleDriveArchiveStorage : IArchiveStorage
{
    public const string RootFolderName = "MedReminder";
    public const string BackupsFolderName = "backups";
    public const string FolderProperty = "mrfolder";

    private readonly GoogleDriveClient _client;

    public GoogleDriveArchiveStorage(GoogleDriveClient client)
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
                var folder = await FolderAsync(create: true, ct);
                var existing = await _client.QueryAsync(
                    $"{GoogleDriveClient.Literal(folder!)} in parents and name = {GoogleDriveClient.Literal(suggestedName)} and trashed = false",
                    "drive", ct);
                if (existing.Count > 0) throw new IOException($"An archive named '{suggestedName}' already exists.");

                var id = await _client.GenerateIdAsync("drive", ct);
                var stored = await _client.CreateAsync(id, suggestedName, folder!, new Dictionary<string, string>(),
                    source, source.Length - source.Position, ct);
                return stored.Id;
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
        return await _client.OpenReadAsync(id, ct)
            ?? throw new FileNotFoundException("The archive no longer exists.", id);
    }

    public async Task<IReadOnlyList<ArchiveInfo>> ListAsync(CancellationToken ct)
    {
        var folder = await FolderAsync(create: false, ct);
        if (folder is null) return [];
        var archives = (await _client.QueryAsync($"{GoogleDriveClient.Literal(folder)} in parents and trashed = false", "drive", ct))
            .Where(f => !f.IsFolder && !f.Name.StartsWith('.')
                && f.Name.EndsWith(ExportFormat.ArchiveExtension, StringComparison.OrdinalIgnoreCase))
            .Select(f => new ArchiveInfo(f.Id, f.Name, f.CreatedTime, f.Size))
            .ToList();
        archives.Sort((a, b) => b.CreatedAtUtc.CompareTo(a.CreatedAtUtc));
        return archives;
    }

    public async Task DeleteAsync(string id, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        await _client.DeleteAsync(id, ct);
    }

    // The backups folder id; null when it does not exist and create is false.
    private async Task<string?> FolderAsync(bool create, CancellationToken ct)
    {
        var backups = await FindFolderAsync(BackupsFolderName, ct);
        if (backups is not null || !create) return backups;

        var root = await FindFolderAsync("root", ct)
            ?? (await _client.CreateFolderAsync(RootFolderName, "root",
                new Dictionary<string, string> { [FolderProperty] = "root" }, ct)).Id;
        return (await _client.CreateFolderAsync(BackupsFolderName, root,
            new Dictionary<string, string> { [FolderProperty] = BackupsFolderName }, ct)).Id;
    }

    private async Task<string?> FindFolderAsync(string role, CancellationToken ct)
        => GoogleFile.Winner(await _client.QueryAsync(
            $"{GoogleDriveClient.PropertyClause(FolderProperty, role)} and mimeType = {GoogleDriveClient.Literal(GoogleDriveClient.FolderMimeType)} and trashed = false",
            "drive", ct))?.Id;
}
