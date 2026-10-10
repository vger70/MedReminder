using MedReminder.Application.Abstractions;

namespace MedReminder.Application.Export;

// Listing and retention of the automatic snapshots held by an
// IArchiveStorage (C.3+ §3.3, C.3++ §7.5), shared by the desktop backup
// and the Android cloud backup (backlog B2-03), so both read and prune
// the same names the same way.
public static class CloudSnapshots
{
    // Most recent first. Reading every manifest would mean downloading
    // every archive, so the profile and the date come from the
    // CloudSnapshotName file name; device, source and version stay empty.
    // ArchivePath is the storage id. A name that does not match keeps
    // the storage's creation time and an empty profile.
    public static async Task<IReadOnlyList<CloudSnapshotInfo>> ListAsync(
        IArchiveStorage storage, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(storage);
        var results = new List<CloudSnapshotInfo>();
        foreach (var archive in await storage.ListAsync(cancellationToken))
        {
            var named = CloudSnapshotName.TryParse(archive.Name, out var profileId, out var created);
            results.Add(new CloudSnapshotInfo
            {
                ArchivePath = archive.Id,
                FileName = archive.Name,
                CreatedAtUtc = named ? created : archive.CreatedAtUtc,
                ProfileId = profileId,
            });
        }
        results.Sort((a, b) => b.CreatedAtUtc.CompareTo(a.CreatedAtUtc));
        return results;
    }

    // Deletes every archive older than retentionDays whose name matches
    // CloudSnapshotName; any other name (a hand-copied or manual export)
    // is left alone. Each archive is judged on its own age. profileIds,
    // when given, limits the pruning to those profiles' snapshots: a host
    // passes the profiles it has just backed up, so the old snapshots of
    // a profile whose backup keeps failing are not pruned away. A failed
    // delete does not stop the others. Returns the number deleted.
    public static async Task<int> PruneAsync(
        IArchiveStorage storage,
        int retentionDays,
        DateTimeOffset now,
        IReadOnlyCollection<string>? profileIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(storage);
        if (retentionDays <= 0) return 0;

        var cutoff = now.AddDays(-retentionDays);
        var archives = await storage.ListAsync(cancellationToken);

        var deleted = 0;
        foreach (var archive in archives)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!CloudSnapshotName.TryParse(archive.Name, out var profileId, out _)) continue;
            if (profileIds is not null && !profileIds.Contains(profileId, StringComparer.OrdinalIgnoreCase)) continue;
            if (archive.CreatedAtUtc >= cutoff) continue;

            try
            {
                await storage.DeleteAsync(archive.Id, cancellationToken);
                deleted++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // A locked file must not stop pruning the others.
            }
        }

        return deleted;
    }
}
