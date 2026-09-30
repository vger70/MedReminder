using System.Text.Json;
using MedReminder.Application.Abstractions;

namespace MedReminder.Infrastructure.Household;

// IHouseholdKeyStore in household\household.protected (household step
// H3a): the household id, the key version and the key, protected with the
// device's credential protector (DPAPI CurrentUser on Windows). One key at
// a time: a new version or another household replaces it.
public sealed class ProtectedHouseholdKeyStore : IHouseholdKeyStore
{
    public const string FileName = "household.protected";

    private readonly ICredentialProtector _protector;
    private readonly string _path;

    public ProtectedHouseholdKeyStore(ICredentialProtector protector, string directory)
    {
        ArgumentNullException.ThrowIfNull(protector);
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _protector = protector;
        _path = Path.Combine(directory, FileName);
    }

    public byte[]? Load(Guid householdId, int keyVersion)
    {
        if (!File.Exists(_path)) return null;
        var stored = JsonSerializer.Deserialize<StoredKey>(File.ReadAllText(_path));
        if (stored is null || stored.HouseholdId != householdId || stored.KeyVersion != keyVersion) return null;
        return Convert.FromBase64String(_protector.Unprotect(stored.Key));
    }

    public void Save(Guid householdId, int keyVersion, byte[] key)
    {
        ArgumentNullException.ThrowIfNull(key);
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporary = _path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(
            new StoredKey(householdId, keyVersion, _protector.Protect(Convert.ToBase64String(key)))));
        File.Move(temporary, _path, overwrite: true);
    }

    public void Clear()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }

    private sealed record StoredKey(Guid HouseholdId, int KeyVersion, string Key);
}
