namespace MedReminder.Application.Abstractions;

/// <summary>
/// Delivers and retrieves encrypted .mrz archives to / from a storage
/// backend (docs/analysis/ANALYSIS-C3PP-CLOUD-PROVIDERS.md §7.2).
/// Implementations must be thread-safe. The content is always an
/// AES-GCM-encrypted archive produced by the C.3 export; the storage
/// layer never decrypts it.
/// </summary>
public interface IArchiveStorage
{
    /// <summary>
    /// Stores the archive under <paramref name="suggestedName"/>. The
    /// stream position is at 0 on entry; the implementation disposes the
    /// stream on exit. A failed upload must leave no visible archive.
    /// Returns the opaque <see cref="ArchiveInfo.Id"/> of the stored archive.
    /// </summary>
    /// <exception cref="DirectoryNotFoundException">
    /// The storage target is not available; callers treat this as
    /// "skip this run", not as a failure.
    /// </exception>
    Task<string> UploadAsync(
        Stream archive,
        string suggestedName,
        CancellationToken ct);

    /// <summary>
    /// Opens the archive identified by <paramref name="id"/>. The returned
    /// stream is owned by the caller and must be disposed.
    /// </summary>
    Task<Stream> DownloadAsync(string id, CancellationToken ct);

    /// <summary>
    /// Lists the stored archives, newest first. Returns an empty list when
    /// the storage target is not available.
    /// </summary>
    Task<IReadOnlyList<ArchiveInfo>> ListAsync(CancellationToken ct);

    /// <summary>
    /// Deletes the archive identified by <paramref name="id"/>. Deleting an
    /// archive that no longer exists is a no-op.
    /// </summary>
    Task DeleteAsync(string id, CancellationToken ct);
}
