using FluentAssertions;
using MedReminder.Infrastructure.Credentials;
using Xunit;

namespace MedReminder.Infrastructure.Tests.Credentials;

// Il progetto Infrastructure.Tests è net10.0-windows: la classe compila
// only on Windows. At runtime DPAPI CurrentUser requires a real
// user context — the assumption for the build machine.
public class DpapiCredentialProtectorTests
{
    [Fact]
    public void Protect_then_unprotect_returns_original_plaintext()
    {
        var sut = new DpapiCredentialProtector();
        const string original = "s3cr3t-p@ssword-😀-1234567890";

        var ciphertext = sut.Protect(original);
        ciphertext.Should().NotBeNullOrEmpty();
        ciphertext.Should().NotContain(original);

        var roundTrip = sut.Unprotect(ciphertext);
        roundTrip.Should().Be(original);
    }

    [Fact]
    public void Two_invocations_produce_different_ciphertexts_for_same_plaintext()
    {
        // DPAPI inietta entropia interna: due Protect sullo stesso
        // plaintext devono produrre ciphertext diversi.
        var sut = new DpapiCredentialProtector();
        const string plaintext = "same-value";

        var a = sut.Protect(plaintext);
        var b = sut.Protect(plaintext);

        a.Should().NotBe(b);
        sut.Unprotect(a).Should().Be(plaintext);
        sut.Unprotect(b).Should().Be(plaintext);
    }
}
