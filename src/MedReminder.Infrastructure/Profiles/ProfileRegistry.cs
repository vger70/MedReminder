using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using MedReminder.Application.Abstractions;

namespace MedReminder.Infrastructure.Profiles;

// JSON persistence of the multi-user registry
// (docs/ANALYSIS-MULTI-USER.md §2.2, §8.3).
//
// File format (SchemaVersion 1):
//   {
//     "SchemaVersion": 1,
//     "ActiveProfileId": "default",
//     "Profiles": [
//       { "Id": "...", "DisplayName": "...", "Role": "admin",
//         "CreatedAt": "...", "LastUsedAt": "...",
//         "PinHash": null, "PinSalt": null, "PinIterations": 0 }
//     ]
//   }
//
// Atomicity: every write goes through a .tmp file + File.Move
// (overwrite), the same pattern used by BackupStateStore. A crash
// in the middle of a write leaves the previous file intact.
//
// Tolerance: an unknown role value ("root", "superuser", empty, …)
// deserializes to User. This is a fail-safe by design — we never
// accidentally promote a profile to admin because of a typo or a
// future schema drift.
//
// Concurrency: single-instance mutex Local\... already prevents two
// processes from mutating the file from the same Windows session
// (§10.1). The internal lock guards concurrent calls from within
// the same process (the UI thread and background services).
internal sealed class ProfileRegistry : IProfileRegistry
{
    private const int CurrentSchemaVersion = 1;
    private const int PinIterations = 100_000;
    private const int PinSaltBytes = 16;
    private const int PinHashBytes = 32;

    // System.Text.Json options are shared: writer indents to keep the
    // file human-readable (someone may need to hand-edit it in a
    // recovery scenario), the reader ignores unknown properties and
    // is case-insensitive so a hand-edited file with slightly wrong
    // casing still loads.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    private readonly string _registryPath;
    private readonly string _profilesRootDirectory;
    private readonly TimeProvider _clock;
    private readonly object _sync = new();

    // The two paths are passed in explicitly (instead of read from
    // AppDataPaths) so unit tests can exercise the registry against
    // a temporary directory. Production wiring builds them from
    // AppDataPaths.GetProfilesRegistryPath() /
    // AppDataPaths.GetProfilesRootDirectory().
    public ProfileRegistry(
        string registryPath,
        string profilesRootDirectory,
        TimeProvider clock)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registryPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(profilesRootDirectory);
        ArgumentNullException.ThrowIfNull(clock);
        _registryPath = registryPath;
        _profilesRootDirectory = profilesRootDirectory;
        _clock = clock;
    }

    public IReadOnlyList<Profile> ListProfiles()
    {
        lock (_sync)
        {
            var doc = LoadOrEmpty();
            return doc.Profiles.Select(ToProfile).ToArray();
        }
    }

    public Profile? GetById(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        lock (_sync)
        {
            var doc = LoadOrEmpty();
            var entry = doc.Profiles.FirstOrDefault(p =>
                string.Equals(p.Id, id, StringComparison.Ordinal));
            return entry is null ? null : ToProfile(entry);
        }
    }

    public string? ActiveProfileIdHint
    {
        get
        {
            lock (_sync)
            {
                var doc = LoadOrEmpty();
                return string.IsNullOrWhiteSpace(doc.ActiveProfileId)
                    ? null
                    : doc.ActiveProfileId;
            }
        }
    }

    // One-shot seeding hook reserved for the V1→V2 migration
    // (docs/ANALYSIS-MULTI-USER.md §5.2 step 6). Writes the initial
    // profiles.json with a single entry whose Id is the literal
    // "default" (later profiles use Guids) and Role forced to Admin
    // (§1.1a — the sole existing V1 user carries admin rights into
    // V2). Refuses if the registry is already populated: the
    // migrator is idempotent at the boot level (§5.1) and must not
    // be called twice.
    internal Profile SeedFromV1Migration(string id, string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        lock (_sync)
        {
            var doc = LoadOrEmpty();
            if (doc.Profiles.Count > 0)
            {
                throw new InvalidOperationException(
                    "Cannot seed a non-empty registry.");
            }

            var now = _clock.GetUtcNow();
            var entry = new ProfileEntry
            {
                Id = id,
                DisplayName = displayName.Trim(),
                Role = RoleToString(ProfileRole.Admin),
                CreatedAt = now,
                LastUsedAt = now,
                PinHash = null,
                PinSalt = null,
                PinIterations = 0,
            };
            doc.Profiles.Add(entry);
            doc.ActiveProfileId = entry.Id;
            Save(doc);
            return ToProfile(entry);
        }
    }

    public Profile Create(string displayName, ProfileRole role)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        lock (_sync)
        {
            var doc = LoadOrEmpty();
            var now = _clock.GetUtcNow();

            // First profile in an empty registry is forced to Admin
            // (§1.1a): there must always be one admin, and the
            // first-run wizard has nothing to compare against.
            var effectiveRole = doc.Profiles.Count == 0 ? ProfileRole.Admin : role;

            var entry = new ProfileEntry
            {
                Id = Guid.NewGuid().ToString("N"),
                DisplayName = displayName.Trim(),
                Role = RoleToString(effectiveRole),
                CreatedAt = now,
                LastUsedAt = now,
                PinHash = null,
                PinSalt = null,
                PinIterations = 0,
            };
            doc.Profiles.Add(entry);

            // First profile also becomes the active hint so an
            // immediate restart lands on it without a picker.
            if (string.IsNullOrWhiteSpace(doc.ActiveProfileId))
            {
                doc.ActiveProfileId = entry.Id;
            }

            Save(doc);
            return ToProfile(entry);
        }
    }

    public void Rename(string id, string newDisplayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(newDisplayName);
        lock (_sync)
        {
            var doc = LoadOrEmpty();
            var entry = FindOrThrow(doc, id);
            entry.DisplayName = newDisplayName.Trim();
            Save(doc);
        }
    }

    public void Delete(string id, bool deleteData)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        lock (_sync)
        {
            var doc = LoadOrEmpty();
            var entry = FindOrThrow(doc, id);

            // Invariant: at least one admin must remain (§2.2).
            if (StringEqualsAdmin(entry.Role))
            {
                var adminsAfter = doc.Profiles.Count(p =>
                    !string.Equals(p.Id, id, StringComparison.Ordinal)
                    && StringEqualsAdmin(p.Role));
                if (adminsAfter == 0)
                {
                    throw new InvalidOperationException(
                        "Cannot delete the last admin profile.");
                }
            }

            doc.Profiles.Remove(entry);

            if (string.Equals(doc.ActiveProfileId, id, StringComparison.Ordinal))
            {
                // Clearing the hint on deletion of the hinted profile:
                // the boot flow will pick another one or show the
                // picker.
                doc.ActiveProfileId = doc.Profiles.Count > 0
                    ? doc.Profiles[0].Id
                    : null;
            }

            Save(doc);

            if (deleteData)
            {
                var directory = Path.Combine(_profilesRootDirectory, id);
                if (Directory.Exists(directory))
                {
                    try
                    {
                        Directory.Delete(directory, recursive: true);
                    }
                    catch
                    {
                        // Best-effort disk cleanup: the registry entry
                        // is already gone, so a stuck file (SQLite
                        // handle still open on Windows) is manually
                        // recoverable — do not fail the whole
                        // operation.
                    }
                }
            }
        }
    }

    public void SetActiveProfileHint(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        lock (_sync)
        {
            var doc = LoadOrEmpty();
            _ = FindOrThrow(doc, id);
            doc.ActiveProfileId = id;

            // Bump LastUsedAt so the picker orders by "recently used"
            // when the user next opens it.
            var entry = doc.Profiles.First(p =>
                string.Equals(p.Id, id, StringComparison.Ordinal));
            entry.LastUsedAt = _clock.GetUtcNow();

            Save(doc);
        }
    }

    public void SetPin(string id, string? pin)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        lock (_sync)
        {
            var doc = LoadOrEmpty();
            var entry = FindOrThrow(doc, id);

            if (string.IsNullOrEmpty(pin))
            {
                entry.PinHash = null;
                entry.PinSalt = null;
                entry.PinIterations = 0;
            }
            else
            {
                var salt = new byte[PinSaltBytes];
                RandomNumberGenerator.Fill(salt);
                var hash = Pbkdf2(pin, salt, PinIterations);
                entry.PinHash = Convert.ToBase64String(hash);
                entry.PinSalt = Convert.ToBase64String(salt);
                entry.PinIterations = PinIterations;
            }

            Save(doc);
        }
    }

    public bool VerifyPin(string id, string pin)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (string.IsNullOrEmpty(pin)) return false;

        lock (_sync)
        {
            var doc = LoadOrEmpty();
            var entry = FindOrThrow(doc, id);

            if (string.IsNullOrEmpty(entry.PinHash)
                || string.IsNullOrEmpty(entry.PinSalt)
                || entry.PinIterations <= 0)
            {
                return false;
            }

            byte[] expected;
            byte[] salt;
            try
            {
                expected = Convert.FromBase64String(entry.PinHash);
                salt = Convert.FromBase64String(entry.PinSalt);
            }
            catch (FormatException)
            {
                return false;
            }

            var actual = Pbkdf2(pin, salt, entry.PinIterations);

            // Constant-time compare: the PIN is friction, not
            // security (§8.2), but there is no reason to leak timing
            // information about the stored hash.
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
    }

    public bool HasPin(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        lock (_sync)
        {
            var doc = LoadOrEmpty();
            var entry = FindOrThrow(doc, id);
            return !string.IsNullOrEmpty(entry.PinHash)
                && !string.IsNullOrEmpty(entry.PinSalt)
                && entry.PinIterations > 0;
        }
    }

    // ---- internal helpers ----

    private RegistryDocument LoadOrEmpty()
    {
        if (!File.Exists(_registryPath))
        {
            return new RegistryDocument
            {
                SchemaVersion = CurrentSchemaVersion,
                ActiveProfileId = null,
                Profiles = new List<ProfileEntry>(),
            };
        }

        try
        {
            var json = File.ReadAllText(_registryPath);
            if (string.IsNullOrWhiteSpace(json))
            {
                return new RegistryDocument
                {
                    SchemaVersion = CurrentSchemaVersion,
                    ActiveProfileId = null,
                    Profiles = new List<ProfileEntry>(),
                };
            }
            var doc = JsonSerializer.Deserialize<RegistryDocument>(json, JsonOptions)
                      ?? new RegistryDocument();
            doc.Profiles ??= new List<ProfileEntry>();
            return doc;
        }
        catch (JsonException)
        {
            // A corrupt file must not lock the user out of the app.
            // The boot flow will treat this as "no registry" and
            // trigger the first-run wizard. The corrupt file stays on
            // disk untouched — a subsequent Save overwrites it
            // atomically.
            return new RegistryDocument
            {
                SchemaVersion = CurrentSchemaVersion,
                ActiveProfileId = null,
                Profiles = new List<ProfileEntry>(),
            };
        }
    }

    private void Save(RegistryDocument doc)
    {
        doc.SchemaVersion = CurrentSchemaVersion;
        Directory.CreateDirectory(Path.GetDirectoryName(_registryPath)!);
        var json = JsonSerializer.Serialize(doc, JsonOptions);
        var tmp = _registryPath + ".tmp";
        File.WriteAllText(tmp, json);
        File.Move(tmp, _registryPath, overwrite: true);
    }

    private static ProfileEntry FindOrThrow(RegistryDocument doc, string id)
    {
        var entry = doc.Profiles.FirstOrDefault(p =>
            string.Equals(p.Id, id, StringComparison.Ordinal));
        if (entry is null)
        {
            throw new InvalidOperationException($"Profile '{id}' not found.");
        }
        return entry;
    }

    private static Profile ToProfile(ProfileEntry entry) => new(
        entry.Id,
        entry.DisplayName,
        ParseRole(entry.Role),
        entry.CreatedAt,
        entry.LastUsedAt,
        !string.IsNullOrEmpty(entry.PinHash)
            && !string.IsNullOrEmpty(entry.PinSalt)
            && entry.PinIterations > 0);

    private static string RoleToString(ProfileRole role) => role switch
    {
        ProfileRole.Admin => "admin",
        _ => "user",
    };

    private static ProfileRole ParseRole(string? raw)
    {
        // Tolerant: anything but the literal "admin" (case-insensitive)
        // is treated as user — see class-level notes.
        if (string.IsNullOrWhiteSpace(raw)) return ProfileRole.User;
        return string.Equals(raw, "admin", StringComparison.OrdinalIgnoreCase)
            ? ProfileRole.Admin
            : ProfileRole.User;
    }

    private static bool StringEqualsAdmin(string? raw) =>
        !string.IsNullOrWhiteSpace(raw)
        && string.Equals(raw, "admin", StringComparison.OrdinalIgnoreCase);

    private static byte[] Pbkdf2(string pin, byte[] salt, int iterations)
    {
        return Rfc2898DeriveBytes.Pbkdf2(
            pin,
            salt,
            iterations,
            HashAlgorithmName.SHA256,
            PinHashBytes);
    }

    // ---- JSON DTOs (internal) ----

    private sealed class RegistryDocument
    {
        public int SchemaVersion { get; set; } = CurrentSchemaVersion;
        public string? ActiveProfileId { get; set; }
        public List<ProfileEntry> Profiles { get; set; } = new();
    }

    private sealed class ProfileEntry
    {
        public string Id { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Role { get; set; } = "user";
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset LastUsedAt { get; set; }
        public string? PinHash { get; set; }
        public string? PinSalt { get; set; }
        public int PinIterations { get; set; }
    }
}
