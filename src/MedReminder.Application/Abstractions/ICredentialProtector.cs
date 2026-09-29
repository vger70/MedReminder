namespace MedReminder.Application.Abstractions;

// Encrypts / decrypts local secrets (e.g. the SMTP password). The MVP
// implements this port via DPAPI CurrentUser on the Infrastructure
// side.
public interface ICredentialProtector
{
    string Protect(string plaintext);
    string Unprotect(string ciphertext);
}
