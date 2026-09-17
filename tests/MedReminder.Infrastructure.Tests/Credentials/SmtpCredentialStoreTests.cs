using FluentAssertions;
using MedReminder.Infrastructure.Credentials;
using Xunit;

namespace MedReminder.Infrastructure.Tests.Credentials;

// Verifies that the store encrypts the payload before writing it to disk and
// lo restituisca correttamente in chiaro alla lettura. Usa un file
// temporary file so we do not touch the user's real configuration.
public class SmtpCredentialStoreTests : IDisposable
{
    private readonly string _tempFile;

    public SmtpCredentialStoreTests()
    {
        _tempFile = Path.Combine(
            Path.GetTempPath(),
            $"medreminder-smtp-test-{Guid.NewGuid():N}.protected");
    }

    public void Dispose()
    {
        if (File.Exists(_tempFile)) File.Delete(_tempFile);
    }

    [Fact]
    public void Set_then_get_returns_original_password_and_encrypts_on_disk()
    {
        var protector = new DpapiCredentialProtector();
        var sut = new SmtpCredentialStore(protector, _tempFile);

        sut.HasPassword.Should().BeFalse();
        sut.SetPassword("P@ssw0rd!");
        sut.HasPassword.Should().BeTrue();

        var stored = File.ReadAllText(_tempFile);
        stored.Should().NotContain("P@ssw0rd!");   // must be encrypted

        sut.GetPassword().Should().Be("P@ssw0rd!");
    }

    [Fact]
    public void Clear_removes_credentials_file()
    {
        var protector = new DpapiCredentialProtector();
        var sut = new SmtpCredentialStore(protector, _tempFile);
        sut.SetPassword("temp");

        sut.Clear();

        File.Exists(_tempFile).Should().BeFalse();
        sut.HasPassword.Should().BeFalse();
        sut.GetPassword().Should().BeNull();
    }

    [Fact]
    public void Get_returns_null_when_no_password_stored()
    {
        var protector = new DpapiCredentialProtector();
        var sut = new SmtpCredentialStore(protector, _tempFile);

        sut.GetPassword().Should().BeNull();
    }
}
