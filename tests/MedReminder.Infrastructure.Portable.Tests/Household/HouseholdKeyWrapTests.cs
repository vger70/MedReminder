using System.Security.Cryptography;
using FluentAssertions;
using MedReminder.Application.Household;
using MedReminder.Infrastructure.Export;
using Xunit;

namespace MedReminder.Infrastructure.Tests.Household;

// Household step H3b: ECDH P-256 + HKDF + AES-GCM wrap for a device or the
// recovery key.
public sealed class HouseholdKeyWrapTests
{
    private readonly ArchiveCipher _cipher = new();
    private static readonly byte[] Secret = RandomNumberGenerator.GetBytes(32);
    private const string Purpose = "MedReminder.Household.Grant|test";

    [Fact]
    public void Only_the_private_key_and_purpose_open_a_wrapped_secret()
    {
        var (privateKey, publicKey) = HouseholdKeyWrap.CreateKeyPair();
        var (otherPrivate, _) = HouseholdKeyWrap.CreateKeyPair();

        var wrapped = HouseholdKeyWrap.Wrap(_cipher, publicKey, Secret, Purpose);

        HouseholdKeyWrap.Unwrap(_cipher, privateKey, wrapped, Purpose).Should().Equal(Secret);
        FluentActions.Invoking(() => HouseholdKeyWrap.Unwrap(_cipher, otherPrivate, wrapped, Purpose))
            .Should().Throw<CryptographicException>();
        FluentActions.Invoking(() => HouseholdKeyWrap.Unwrap(_cipher, privateKey, wrapped, Purpose + "|other"))
            .Should().Throw<CryptographicException>();
    }

    [Fact]
    public void A_wrapped_secret_is_new_each_time_and_has_no_colon()
    {
        var (_, publicKey) = HouseholdKeyWrap.CreateKeyPair();

        var first = HouseholdKeyWrap.Wrap(_cipher, publicKey, Secret, Purpose);
        var second = HouseholdKeyWrap.Wrap(_cipher, publicKey, Secret, Purpose);

        first.Should().NotBe(second, "a fresh ephemeral key and nonce each time");
        first.Should().NotContain(":").And.StartWith("1.");
    }

    [Fact]
    public void An_altered_wrapped_secret_does_not_open()
    {
        var (privateKey, publicKey) = HouseholdKeyWrap.CreateKeyPair();
        var parts = HouseholdKeyWrap.Wrap(_cipher, publicKey, Secret, Purpose).Split('.');
        parts[4] = (parts[4][0] == 'A' ? "B" : "A") + parts[4][1..];

        FluentActions.Invoking(() => HouseholdKeyWrap.Unwrap(_cipher, privateKey, string.Join('.', parts), Purpose))
            .Should().Throw<CryptographicException>();
    }

    [Fact]
    public void The_public_key_is_derived_from_the_private_key()
    {
        var (privateKey, publicKey) = HouseholdKeyWrap.CreateKeyPair();

        HouseholdKeyWrap.PublicKeyOf(privateKey).Should().Be(publicKey);
    }
}
