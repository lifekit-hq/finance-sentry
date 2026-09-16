using System.Text;
using FinanceSentry.Modules.CryptoSync.Domain.Exceptions;
using FinanceSentry.Modules.CryptoSync.Infrastructure.RevolutX;
using FluentAssertions;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Xunit;

namespace FinanceSentry.Tests.Unit.CryptoSync.RevolutX;

public class RevolutXSignerTests
{
    [Fact]
    public void ParsePrivateKey_ReadsThePkcs8Pem_ToTheRfc8032Key()
    {
        var key = RevolutXSigner.ParsePrivateKey(RevolutXTestKeys.Ed25519PrivateKeyPem);

        Convert.ToHexStringLower(key.GeneratePublicKey().GetEncoded())
            .Should().Be(RevolutXTestKeys.Rfc8032Test1PublicKeyHex);
    }

    [Fact]
    public void Sign_MatchesTheRfc8032KnownAnswer()
    {
        var key = new Ed25519PrivateKeyParameters(Convert.FromHexString(RevolutXTestKeys.Rfc8032Test1SeedHex));

        var signature = RevolutXSigner.Sign(key, string.Empty);

        Convert.ToHexStringLower(Convert.FromBase64String(signature))
            .Should().Be(RevolutXTestKeys.Rfc8032Test1EmptyMessageSignatureHex);
    }

    [Fact]
    public void Sign_ARevolutXPayload_MatchesOpenSsl()
    {
        var key = RevolutXSigner.ParsePrivateKey(RevolutXTestKeys.Ed25519PrivateKeyPem);
        var message = RevolutXSigner.BuildMessage(
            RevolutXTestKeys.OpenSslBalancesTimestamp, "GET", "/api/1.0/balances", query: "", body: "");

        RevolutXSigner.Sign(key, message).Should().Be(RevolutXTestKeys.OpenSslBalancesSignatureBase64);
    }

    [Fact]
    public void Sign_VerifiesUnderThePublicKey()
    {
        var key = RevolutXSigner.ParsePrivateKey(RevolutXTestKeys.Ed25519PrivateKeyPem);
        const string message = "1765360896219POST/api/1.0/orders{\"symbol\":\"BTC-USD\"}";

        var signature = Convert.FromBase64String(RevolutXSigner.Sign(key, message));

        var verifier = new Ed25519Signer();
        verifier.Init(forSigning: false, key.GeneratePublicKey());
        var bytes = Encoding.UTF8.GetBytes(message);
        verifier.BlockUpdate(bytes, 0, bytes.Length);
        verifier.VerifySignature(signature).Should().BeTrue();
    }

    [Theory]
    [InlineData("1700000000000", "GET", "/api/1.0/balances", "", "", "1700000000000GET/api/1.0/balances")]
    [InlineData("1700000000000", "get", "/api/1.0/orders/active", "limit=10", "", "1700000000000GET/api/1.0/orders/activelimit=10")]
    [InlineData("1", "POST", "/api/1.0/orders", "", "{\"a\":1}", "1POST/api/1.0/orders{\"a\":1}")]
    public void BuildMessage_ConcatenatesWithoutSeparators_AndUppercasesTheMethod(
        string timestamp, string method, string path, string query, string body, string expected)
    {
        RevolutXSigner.BuildMessage(timestamp, method, path, query, body).Should().Be(expected);
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("")] // what a single-line <input> leaves after a multi-line paste
    [InlineData("\r\n")]
    public void ParsePrivateKey_ToleratesRewrittenLineBreaks(string lineBreak)
    {
        var pasted = RevolutXTestKeys.Ed25519PrivateKeyPem.Replace("\n", lineBreak);

        var key = RevolutXSigner.ParsePrivateKey(pasted);

        Convert.ToHexStringLower(key.GetEncoded()).Should().Be(RevolutXTestKeys.Rfc8032Test1SeedHex);
    }

    [Fact]
    public void ParsePrivateKey_PublicKeyPasted_SaysSo()
    {
        var act = () => RevolutXSigner.ParsePrivateKey(RevolutXTestKeys.Ed25519PublicKeyPem);

        act.Should().Throw<RevolutXException>().WithMessage("*public key*");
    }

    [Fact]
    public void ParsePrivateKey_NonEd25519Key_IsRejected()
    {
        var act = () => RevolutXSigner.ParsePrivateKey(RevolutXTestKeys.X25519PrivateKeyPem);

        act.Should().Throw<RevolutXException>().WithMessage("*not an Ed25519 key*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a key")]
    [InlineData("-----BEGIN OPENSSH PRIVATE KEY-----\nAAAA\n-----END OPENSSH PRIVATE KEY-----")]
    [InlineData("-----BEGIN PRIVATE KEY-----\n!!!not-base64!!!\n-----END PRIVATE KEY-----")]
    public void ParsePrivateKey_Garbage_IsA422_ThatNeverEchoesTheInput(string input)
    {
        var act = () => RevolutXSigner.ParsePrivateKey(input);

        var thrown = act.Should().Throw<RevolutXException>().Which;
        thrown.StatusCode.Should().Be(422);
        thrown.ErrorCode.Should().Be("INVALID_CREDENTIALS");
        if (input.Trim().Length > 0)
        {
            thrown.Message.Should().NotContain(input.Trim());
        }
    }

    [Fact]
    public void ParseCredentials_TrimsTheApiKey_AndRequiresIt()
    {
        RevolutXSigner.ParseCredentials("  abc  ", RevolutXTestKeys.Ed25519PrivateKeyPem).ApiKey.Should().Be("abc");

        var act = () => RevolutXSigner.ParseCredentials(" ", RevolutXTestKeys.Ed25519PrivateKeyPem);
        act.Should().Throw<RevolutXException>().WithMessage("*API key is required*");
    }
}
