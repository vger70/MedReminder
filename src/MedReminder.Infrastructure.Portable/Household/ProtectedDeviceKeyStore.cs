using MedReminder.Application.Abstractions;

namespace MedReminder.Infrastructure.Household;

// IDeviceKeyStore in household\device.protected (household step H3b): the
// device's private key (PKCS#8, base64), protected with the host's
// credential protector (DPAPI CurrentUser on Windows). It survives a join,
// which replaces the household but not the device.
public sealed class ProtectedDeviceKeyStore : IDeviceKeyStore
{
    public const string FileName = "device.protected";

    private readonly ICredentialProtector _protector;
    private readonly string _path;

    public ProtectedDeviceKeyStore(ICredentialProtector protector, string directory)
    {
        ArgumentNullException.ThrowIfNull(protector);
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _protector = protector;
        _path = Path.Combine(directory, FileName);
    }

    public string? LoadPrivateKey()
        => File.Exists(_path) ? _protector.Unprotect(File.ReadAllText(_path)) : null;

    public void SavePrivateKey(string privateKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(privateKey);
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporary = _path + ".tmp";
        File.WriteAllText(temporary, _protector.Protect(privateKey));
        File.Move(temporary, _path, overwrite: true);
    }
}
