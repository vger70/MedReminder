namespace MedReminder.Application.Export;

// Argon2id cost parameters, carried verbatim in manifest.json so the
// importer honours what the archive declares rather than a hard-coded
// assumption (docs/analysis/ANALYSIS-C3-EXPORT-IMPORT.md §3.1, §10 —
// "parameters live in the manifest, not in code"). A future build can
// strengthen the defaults without breaking older archives.
public sealed record Argon2Params
{
    // Number of passes over memory (Argon2id "time cost").
    public required int Iterations { get; init; }

    // Memory cost in KiB (Argon2id "memory cost").
    public required int MemoryKiB { get; init; }

    // Degree of parallelism (Argon2id "lanes").
    public required int Parallelism { get; init; }

    // Current first-cut defaults (§3.3, §12 item 1). Confirmed against
    // OWASP's Argon2id guidance: m = 64 MiB, t = 3, p = 1 is an
    // accepted configuration for interactive use.
    public static Argon2Params Default { get; } = new()
    {
        Iterations = 3,
        MemoryKiB = 65536,
        Parallelism = 1,
    };
}
