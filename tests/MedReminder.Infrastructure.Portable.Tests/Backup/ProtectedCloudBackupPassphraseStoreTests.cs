using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using MedReminder.Application.Abstractions;
using MedReminder.Infrastructure.Backup;
using Xunit;

namespace MedReminder.Infrastructure.Tests.Backup;

// The cloud backup passphrase behind the host's credential protector
// (Android backlog B2-03): stored protected, read back, cleared, and
// unreadable when the protector cannot open it.
public sealed class ProtectedCloudBackupPassphraseStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mr-passphrase-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void Round_trip_stores_only_the_protected_form()
    {
        var store = new ProtectedCloudBackupPassphraseStore(new ReversingProtector(), _root);
        store.HasPassphrase.Should().BeFalse();

        store.SetPassphrase("correct horse battery".ToCharArray());

        store.HasPassphrase.Should().BeTrue();
        store.GetPassphrase().Should().Equal("correct horse battery".ToCharArray());
        File.ReadAllText(Path.Combine(_root, ProtectedCloudBackupPassphraseStore.FileName))
            .Should().NotContain("correct horse battery");
        Directory.EnumerateFiles(_root, "*.tmp").Should().BeEmpty();
    }

    [Fact]
    public void Clear_removes_the_passphrase()
    {
        var store = new ProtectedCloudBackupPassphraseStore(new ReversingProtector(), _root);
        store.SetPassphrase("correct horse battery".ToCharArray());

        store.Clear();
        store.Clear();

        store.HasPassphrase.Should().BeFalse();
        store.GetPassphrase().Should().BeNull();
    }

    [Fact]
    public void A_passphrase_the_protector_cannot_open_reads_as_none()
    {
        new ProtectedCloudBackupPassphraseStore(new ReversingProtector(), _root)
            .SetPassphrase("correct horse battery".ToCharArray());

        new ProtectedCloudBackupPassphraseStore(new FailingProtector(), _root).GetPassphrase().Should().BeNull();
    }

    [Fact]
    public void An_empty_or_blank_passphrase_is_rejected()
    {
        var store = new ProtectedCloudBackupPassphraseStore(new ReversingProtector(), _root);

        store.Invoking(s => s.SetPassphrase([])).Should().Throw<ArgumentException>();
        store.Invoking(s => s.SetPassphrase("   ".ToCharArray())).Should().Throw<ArgumentException>();
        store.HasPassphrase.Should().BeFalse();
    }

    private sealed class ReversingProtector : ICredentialProtector
    {
        public string Protect(string plaintext)
            => Convert.ToBase64String(Encoding.UTF8.GetBytes(new string(plaintext.Reverse().ToArray())));

        public string Unprotect(string ciphertext)
            => new(Encoding.UTF8.GetString(Convert.FromBase64String(ciphertext)).Reverse().ToArray());
    }

    private sealed class FailingProtector : ICredentialProtector
    {
        public string Protect(string plaintext) => throw new CryptographicException("no key");

        public string Unprotect(string ciphertext) => throw new CryptographicException("no key");
    }
}
