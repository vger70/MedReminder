using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using MedReminder.Application.Abstractions;

namespace MedReminder.Infrastructure.Credentials;

// User-scoped symmetric encryption via DPAPI (ANALYSIS §1.1 item 11).
// The ciphertext is bound to the current Windows user: it cannot be
// reproduced on another account or machine, which is exactly what a
// single-user desktop app needs.
//
// Protect's output is base64 of a DPAPI blob; Unprotect is idempotent
// and case-sensitive on the ciphertext. No extra entropy is added:
// if we want some (to bind to the process or to a PIN), we will add
// it behind a future flag without breaking the file format.
[SupportedOSPlatform("windows")]
internal sealed class DpapiCredentialProtector : ICredentialProtector
{
    public string Protect(string plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        var bytes = Encoding.UTF8.GetBytes(plaintext);
        var protectedBytes = ProtectedData.Protect(bytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(protectedBytes);
    }

    public string Unprotect(string ciphertext)
    {
        ArgumentException.ThrowIfNullOrEmpty(ciphertext);
        var protectedBytes = Convert.FromBase64String(ciphertext);
        var bytes = ProtectedData.Unprotect(protectedBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(bytes);
    }
}
