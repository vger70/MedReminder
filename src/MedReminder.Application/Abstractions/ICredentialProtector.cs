namespace MedReminder.Application.Abstractions;

// Cifratura/decifratura di segreti locali (es. password SMTP). L'MVP
// implementa questa porta via DPAPI CurrentUser lato Infrastructure.
public interface ICredentialProtector
{
    string Protect(string plaintext);
    string Unprotect(string ciphertext);
}
