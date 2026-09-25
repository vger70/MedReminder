namespace MedReminder.Application.Abstractions;

/// <summary>
/// Metadata of one encrypted .mrz archive held by an
/// <see cref="IArchiveStorage"/> backend
/// (docs/analysis/ANALYSIS-C3PP-CLOUD-PROVIDERS.md §7.2).
/// </summary>
/// <param name="Id">
/// Opaque, backend-specific identifier (a local file path, a Graph item
/// id, a Dropbox path, …). Callers must never parse or construct an
/// <c>Id</c>: only pass back values returned by
/// <see cref="IArchiveStorage.ListAsync"/> or
/// <see cref="IArchiveStorage.UploadAsync"/>.
/// </param>
/// <param name="Name">Display name, typically the file name.</param>
/// <param name="CreatedAtUtc">
/// When the archive was stored. Backends pick the timestamp that best
/// survives their own copy / sync semantics; retention compares against it.
/// </param>
/// <param name="SizeBytes">Archive size in bytes.</param>
public sealed record ArchiveInfo(
    string Id,
    string Name,
    DateTimeOffset CreatedAtUtc,
    long SizeBytes);
