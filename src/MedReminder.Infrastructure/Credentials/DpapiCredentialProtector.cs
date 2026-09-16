using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using MedReminder.Application.Abstractions;

namespace MedReminder.Infrastructure.Credentials;

// Cifratura simmetrica user-scoped via DPAPI (ANALYSIS §1.1 punto 11).
// Il ciphertext è vincolato all'utente Windows corrente: non è
// riproducibile su un altro account/macchina, il che è esattamente ciò
// che serve per una app single-user desktop.
//
// L'output Protect è base64 di un blob DPAPI; Unprotect è idempotente
// e case-sensitive sul ciphertext. Non aggiungiamo entropia extra:
// se la vogliamo (per legare al processo o a un pin), la aggiungeremo
// dietro un flag futuro senza rompere il formato del file.
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
