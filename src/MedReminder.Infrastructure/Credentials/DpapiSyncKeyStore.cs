using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text.Json;
using MedReminder.Application.Abstractions;

namespace MedReminder.Infrastructure.Credentials;

// Group key of the current profile's sync group, protected with DPAPI
// CurrentUser in profiles\<id>\sync.protected (B.1 Phase 3c,
// docs/analysis/ANALYSIS-B1-MOBILE-SYNC.md §5.4, §7.3). Only the key of
// the version in use is kept; rotation is Phase 4.
[SupportedOSPlatform("windows")]
internal sealed class DpapiSyncKeyStore : ISyncKeyStore
{
    public const string FileName = "sync.protected";

    private sealed record Stored(Guid GroupId, int KeyVersion, string Protected);

    private readonly string _path;

    public DpapiSyncKeyStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = path;
    }

    public byte[]? Load(Guid groupId, int keyVersion)
    {
        if (!File.Exists(_path)) return null;
        var stored = JsonSerializer.Deserialize<Stored>(File.ReadAllText(_path));
        if (stored is null || stored.GroupId != groupId || stored.KeyVersion != keyVersion) return null;
        return ProtectedData.Unprotect(
            Convert.FromBase64String(stored.Protected), optionalEntropy: null, DataProtectionScope.CurrentUser);
    }

    public void Save(Guid groupId, int keyVersion, byte[] key)
    {
        ArgumentNullException.ThrowIfNull(key);
        var protectedKey = ProtectedData.Protect(key, optionalEntropy: null, DataProtectionScope.CurrentUser);
        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(new Stored(groupId, keyVersion, Convert.ToBase64String(protectedKey))));
        File.Move(temp, _path, overwrite: true);
    }

    public void Clear()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }
}
