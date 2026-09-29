using MedReminder.Application.Abstractions;

namespace MedReminder.Application.Tests.Support;

// IProfileRegistry with the invariants of ProfileRegistry (first profile
// admin, last admin kept) and a fake PIN hash: "hash-of-<pin>".
internal sealed class InMemoryProfileRegistry : IProfileRegistry
{
    private readonly List<Entry> _entries = new();

    public string? ActiveProfileIdHint => null;

    public string Add(string id, string name, ProfileRole role, string? pin = null)
    {
        _entries.Add(new Entry { Id = id, Name = name, Role = role, Pin = pin });
        return id;
    }

    public IReadOnlyList<Profile> ListProfiles() => [.. _entries.Select(ToProfile)];

    public Profile? GetById(string id) => _entries.FirstOrDefault(e => e.Id == id) is { } e ? ToProfile(e) : null;

    public Profile Create(string displayName, ProfileRole role)
    {
        var entry = new Entry
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = displayName.Trim(),
            Role = _entries.Count == 0 ? ProfileRole.Admin : role,
        };
        _entries.Add(entry);
        return ToProfile(entry);
    }

    public void Rename(string id, string newDisplayName) => Find(id).Name = newDisplayName.Trim();

    public void Delete(string id, bool deleteData)
    {
        var entry = Find(id);
        if (entry.Role == ProfileRole.Admin && _entries.Count(e => e.Role == ProfileRole.Admin) <= 1)
            throw new InvalidOperationException("Cannot delete the last admin profile.");
        _entries.Remove(entry);
    }

    public void SetActiveProfileHint(string id) { }

    public void SetPin(string id, string? pin) => Find(id).Pin = string.IsNullOrEmpty(pin) ? null : pin;

    public bool VerifyPin(string id, string pin) => Find(id).Pin == pin;

    public void SetRole(string id, ProfileRole role)
    {
        var entry = Find(id);
        if (entry.Role == role) return;
        if (role != ProfileRole.Admin && !_entries.Any(e => e.Id != id && e.Role == ProfileRole.Admin))
            throw new InvalidOperationException("Cannot demote the last admin profile.");
        entry.Role = role;
    }

    public ProfilePinHash? GetPinHash(string id)
        => Find(id).Pin is { } pin ? new ProfilePinHash("hash-of-" + pin, "salt", 100_000) : null;

    public bool HasPin(string id) => Find(id).Pin is not null;

    private Entry Find(string id)
        => _entries.FirstOrDefault(e => e.Id == id) ?? throw new InvalidOperationException("Unknown profile.");

    private static Profile ToProfile(Entry e)
        => new(e.Id, e.Name, e.Role, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, e.Pin is not null);

    private sealed class Entry
    {
        public required string Id { get; init; }
        public required string Name { get; set; }
        public ProfileRole Role { get; set; }
        public string? Pin { get; set; }
    }
}

internal sealed class FixedCurrentProfile(string id, ProfileRole role) : ICurrentProfile
{
    public string Id => id;
    public string DisplayName => id;
    public ProfileRole Role => role;
    public bool IsAdmin => role == ProfileRole.Admin;
    public string DataDirectory => string.Empty;
    public string DatabasePath => string.Empty;
    public string NotificationSettingsPath => string.Empty;
}
