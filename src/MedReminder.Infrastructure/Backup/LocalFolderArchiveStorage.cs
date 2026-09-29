using MedReminder.Application.Abstractions;
using MedReminder.Application.Export;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MedReminder.Infrastructure.Backup;

// IArchiveStorage over a local (or locally mounted) folder: the C.3+
// cloud-folder target (docs/analysis/ANALYSIS-C3PP-CLOUD-PROVIDERS.md
// §7.3). The user's OS-level sync agent (OneDrive, iCloud Drive,
// Dropbox, Google Drive Desktop, Nextcloud, …) does the actual upload.
// Id = absolute file path.
internal sealed class LocalFolderArchiveStorage : IArchiveStorage
{
    private readonly Func<string> _folder;
    private readonly string _tempDirectory;
    private readonly ILogger<LocalFolderArchiveStorage> _log;

    // The folder is read on every call: BackupSettings reloads on change,
    // so a folder picked in Settings applies without a restart.
    public LocalFolderArchiveStorage(
        IOptionsMonitor<BackupSettings> settings,
        ILogger<LocalFolderArchiveStorage> log)
        : this(() => settings.CurrentValue.CloudFolderDirectory, Path.GetTempPath(), log)
    {
    }

    internal LocalFolderArchiveStorage(
        Func<string> folder,
        string tempDirectory,
        ILogger<LocalFolderArchiveStorage> log)
    {
        _folder = folder;
        _tempDirectory = tempDirectory;
        _log = log;
    }

    public async Task<string> UploadAsync(
        Stream archive,
        string suggestedName,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(archive);

        await using (archive)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(suggestedName);
            if (!string.Equals(Path.GetFileName(suggestedName), suggestedName, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "The archive name must be a plain file name.", nameof(suggestedName));
            }

            var folder = _folder();
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            {
                _log.LogWarning(
                    "Archive storage folder {Directory} does not exist.", folder);
                throw new DirectoryNotFoundException(
                    $"Archive storage folder '{folder}' does not exist.");
            }

            var finalPath = Path.Combine(folder, suggestedName);
            var tempPath = Path.Combine(
                _tempDirectory, "MedReminder-upload-" + Guid.NewGuid().ToString("N") + ".tmp");

            try
            {
                await using (var temp = new FileStream(
                    tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                    bufferSize: 81920, useAsync: true))
                {
                    await archive.CopyToAsync(temp, ct);
                }

                // Temp-then-move (ANALYSIS-C3PLUS §4.1): the archive
                // only appears under its final name once fully written.
                // The rename is atomic only when %TEMP% and the folder
                // share a volume; otherwise File.Move copies then deletes.
                File.Move(tempPath, finalPath, overwrite: false);
            }
            finally
            {
                TryDeleteTemp(tempPath);
            }

            return finalPath;
        }
    }

    public Task<Stream> DownloadAsync(string id, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return Task.FromResult<Stream>(File.OpenRead(id));
    }

    public Task<IReadOnlyList<ArchiveInfo>> ListAsync(CancellationToken ct)
    {
        var folder = _folder();
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            return Task.FromResult<IReadOnlyList<ArchiveInfo>>(Array.Empty<ArchiveInfo>());
        }

        var archives = new List<ArchiveInfo>();
        foreach (var file in new DirectoryInfo(folder).EnumerateFiles("*" + ExportFormat.ArchiveExtension))
        {
            ct.ThrowIfCancellationRequested();

            // The pattern alone would also match longer extensions on
            // Windows (legacy 3-character extension rule).
            if (!string.Equals(file.Extension, ExportFormat.ArchiveExtension, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Last-write time, not creation time: copies (cross-volume
            // moves, sync agents materialising the file on another
            // device) reset the creation time but keep the last-write
            // time, which is what C.3+ retention has always compared.
            archives.Add(new ArchiveInfo(
                file.FullName,
                file.Name,
                new DateTimeOffset(file.LastWriteTimeUtc),
                file.Length));
        }

        archives.Sort((a, b) => b.CreatedAtUtc.CompareTo(a.CreatedAtUtc));
        return Task.FromResult<IReadOnlyList<ArchiveInfo>>(archives);
    }

    public Task DeleteAsync(string id, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (File.Exists(id))
        {
            File.Delete(id);
        }
        return Task.CompletedTask;
    }

    private static void TryDeleteTemp(string tempPath)
    {
        try
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
        catch
        {
            // A leftover file in %TEMP% is harmless; never mask the
            // original outcome of the upload.
        }
    }
}
