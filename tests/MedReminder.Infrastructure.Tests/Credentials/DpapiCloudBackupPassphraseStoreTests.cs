using System.Security.Cryptography;
using FluentAssertions;
using MedReminder.Infrastructure.Credentials;
using Xunit;

namespace MedReminder.Infrastructure.Tests.Credentials;

// Round-trip and tampering coverage for the C.3+ automatic-cloud-backup
// passphrase store (docs/analysis/ANALYSIS-C3PLUS-CLOUD-BACKUP.md §3.5).
// Uses a temporary file so the user's real cloud-backup.protected is
// never touched.
public class DpapiCloudBackupPassphraseStoreTests : IDisposable
{
    private readonly string _tempFile;

    public DpapiCloudBackupPassphraseStoreTests()
    {
        _tempFile = Path.Combine(
            Path.GetTempPath(),
            $"medreminder-cloud-passphrase-test-{Guid.NewGuid():N}.protected");
    }

    public void Dispose()
    {
        if (File.Exists(_tempFile)) File.Delete(_tempFile);
    }

    [Fact]
    public void Set_then_get_round_trips_and_encrypts_on_disk()
    {
        var sut = new DpapiCloudBackupPassphraseStore(_tempFile);

        sut.HasPassphrase.Should().BeFalse();
        sut.SetPassphrase("correct horse battery staple".ToCharArray());
        sut.HasPassphrase.Should().BeTrue();

        var stored = File.ReadAllText(_tempFile);
        stored.Should().NotContain("correct horse battery staple");

        var read = sut.GetPassphrase();
        read.Should().NotBeNull();
        new string(read!).Should().Be("correct horse battery staple");
    }

    [Fact]
    public void Clear_removes_the_protected_file()
    {
        var sut = new DpapiCloudBackupPassphraseStore(_tempFile);
        sut.SetPassphrase("temp-passphrase".ToCharArray());

        sut.Clear();

        File.Exists(_tempFile).Should().BeFalse();
        sut.HasPassphrase.Should().BeFalse();
        sut.GetPassphrase().Should().BeNull();
    }

    [Fact]
    public void Tampered_blob_returns_null_on_read()
    {
        var sut = new DpapiCloudBackupPassphraseStore(_tempFile);
        sut.SetPassphrase("legit-passphrase".ToCharArray());

        // Corrupt the base64 payload — DPAPI must refuse to decrypt.
        var raw = Convert.FromBase64String(File.ReadAllText(_tempFile).Trim());
        raw[^1] ^= 0xFF;
        File.WriteAllText(_tempFile, Convert.ToBase64String(raw));

        sut.HasPassphrase.Should().BeTrue();
        sut.GetPassphrase().Should().BeNull();
    }

    [Fact]
    public void Empty_passphrase_is_rejected()
    {
        var sut = new DpapiCloudBackupPassphraseStore(_tempFile);
        Action act = () => sut.SetPassphrase(Array.Empty<char>());
        act.Should().Throw<ArgumentException>();
    }
}
