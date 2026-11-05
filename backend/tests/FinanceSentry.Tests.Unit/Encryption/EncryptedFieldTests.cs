namespace FinanceSentry.Tests.Unit.Encryption;

using System.Security.Cryptography;
using FinanceSentry.Infrastructure.Encryption;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

/// <summary>
/// The field-encryption primitive for declared profile data: one text column of <c>v{keyVersion}.{iv}.{tag}.{ciphertext}</c>,
/// the key version travelling with the value so rotation and a later key change both work.
/// </summary>
public sealed class EncryptedFieldTests
{
    private const string KeyV1 = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";
    private const string KeyV2 = "ZmVkY2JhOTg3NjU0MzIxMGZlZGNiYTk4NzY1NDMyMTA=";

    private static CredentialEncryptionService At(int current, params (int Version, string Key)[] keys) =>
        new(Options.Create(new EncryptionOptions
        {
            CurrentKeyVersion = current,
            Keys = keys.ToDictionary(k => k.Version, k => k.Key),
        }));

    private static readonly CredentialEncryptionService V1 = At(1, (1, KeyV1));
    private static readonly CredentialEncryptionService V2 = At(2, (1, KeyV1), (2, KeyV2));

    [Fact]
    public void Seal_ThenOpen_ReturnsThePlaintext()
    {
        EncryptedField.Open(V1, EncryptedField.Seal(V1, "Quarterly bonus")).Should().Be("Quarterly bonus");
    }

    [Fact]
    public void Seal_WritesTheStoredFormat_AndNeverThePlaintext()
    {
        var stored = EncryptedField.Seal(V2, "Quarterly bonus");

        stored.Should().StartWith("v2.");
        stored.Split('.').Should().HaveCount(4);
        stored.Should().NotContain("Quarterly");
        EncryptedField.KeyVersionOf(stored).Should().Be(2);
    }

    [Fact]
    public void Seal_IsRandomised_SoEqualPlaintextsDifferInStorage()
    {
        EncryptedField.Seal(V1, "same").Should().NotBe(EncryptedField.Seal(V1, "same"));
    }

    [Fact]
    public void AnEmptyString_IsStoredEmpty_AndReadsBackEmpty()
    {
        EncryptedField.Seal(V1, string.Empty).Should().BeEmpty();
        EncryptedField.Open(V1, string.Empty).Should().BeEmpty();
        EncryptedField.KeyVersionOf(string.Empty).Should().BeNull();
    }

    [Fact]
    public void AValueSealedUnderAnOlderKey_StillOpens_AfterTheCurrentKeyMovesOn()
    {
        var sealedUnderV1 = EncryptedField.Seal(V1, "kept readable");

        EncryptedField.KeyVersionOf(sealedUnderV1).Should().Be(1);
        EncryptedField.Open(V2, sealedUnderV1).Should().Be("kept readable");
    }

    [Fact]
    public void ReSealing_MovesAValueToTheCurrentKey()
    {
        var stored = EncryptedField.Seal(V1, "rotate me");

        var rotated = EncryptedField.Seal(V2, EncryptedField.Open(V2, stored));

        EncryptedField.KeyVersionOf(rotated).Should().Be(2);
        EncryptedField.Open(V2, rotated).Should().Be("rotate me");
    }

    [Fact]
    public void ATamperedValue_IsRejected_NotReadAsSomethingElse()
    {
        var parts = EncryptedField.Seal(V1, "do not alter").Split('.');
        var cipher = Convert.FromBase64String(parts[3]);
        cipher[0] ^= 0xFF;
        parts[3] = Convert.ToBase64String(cipher);

        var act = () => EncryptedField.Open(V1, string.Join('.', parts));

        act.Should().Throw<CryptographicException>();
    }

    [Theory]
    [InlineData("plain text, not sealed")]
    [InlineData("v.a.b.c")]
    [InlineData("vX.a.b.c")]
    [InlineData("v1.a.b")]
    public void AValueNotInTheStoredForm_Throws(string stored)
    {
        var act = () => EncryptedField.Open(V1, stored);

        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void LikePatternFor_MatchesOnlyThatKeyVersion()
    {
        EncryptedField.LikePatternFor(2).Should().Be("v2.%");
    }

    [Fact]
    public void StringConverter_RoundTrips_AndStoresCiphertext()
    {
        var converter = new EncryptedStringConverter(V1);

        var stored = (string)converter.ConvertToProvider("Acme annual bonus")!;

        stored.Should().StartWith("v1.").And.NotContain("Acme");
        converter.ConvertFromProvider(stored).Should().Be("Acme annual bonus");
    }

    [Theory]
    [InlineData("4500.00")]
    [InlineData("0.1")]
    [InlineData("-120.5")]
    [InlineData("79228162514264337593543950335")]
    public void DecimalConverter_RoundTripsExactly_KeepingScale(string literal)
    {
        var amount = decimal.Parse(literal, System.Globalization.CultureInfo.InvariantCulture);
        var converter = new EncryptedDecimalConverter(V1);

        var stored = (string)converter.ConvertToProvider(amount)!;

        stored.Should().StartWith("v1.").And.NotContain(literal);
        var back = (decimal)converter.ConvertFromProvider(stored)!;
        back.Should().Be(amount);
        back.ToString(System.Globalization.CultureInfo.InvariantCulture).Should().Be(literal);
    }
}
