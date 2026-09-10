namespace FinanceSentry.Tests.Unit.Encryption;

using System.Diagnostics;
using System.Security.Cryptography;
using FinanceSentry.Infrastructure.Encryption;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

/// <summary>
/// Unit tests for CredentialEncryptionService (T101).
///
/// Validates:
/// - Encrypt → Decrypt roundtrip produces original plaintext
/// - Authentication tag rejects tampered ciphertext
/// - No plaintext in returned EncryptionResult
/// - SC-007: Average encrypt+decrypt cycle &lt; 50ms (benchmark over 1000 iterations)
/// </summary>
public class CredentialEncryptionServiceTests
{
    private readonly CredentialEncryptionService _sut;

    // 32-byte key (256 bits) encoded as Base64 — for tests only, never use in production
    private const string TestKeyBase64 = "dGVzdC1vbmx5LWtleS1ub3QtdGhlLWxlYWtlZC1vbmU="; // 32 bytes

    public CredentialEncryptionServiceTests()
    {
        var options = Options.Create(new EncryptionOptions
        {
            CurrentKeyVersion = 1,
            Keys = new Dictionary<int, string> { [1] = TestKeyBase64 }
        });

        _sut = new CredentialEncryptionService(options);
    }

    [Fact]
    public void Encrypt_ThenDecrypt_ReturnsOriginalPlaintext()
    {
        const string original = "access-sandbox-abc123-provider-token";

        var result = _sut.Encrypt(original);
        var decrypted = _sut.Decrypt(result.Ciphertext, result.Iv, result.AuthTag, result.KeyVersion);

        decrypted.Should().Be(original);
    }

    [Fact]
    public void Encrypt_ProducesUniqueCiphertextEachCall_DueTo_FreshIv()
    {
        const string plaintext = "same-token-value";

        var r1 = _sut.Encrypt(plaintext);
        var r2 = _sut.Encrypt(plaintext);

        // IVs must differ (fresh random IV each time)
        r1.Iv.Should().NotBeEquivalentTo(r2.Iv);
        // Ciphertexts should differ because IVs differ
        r1.Ciphertext.Should().NotBeEquivalentTo(r2.Ciphertext);
    }

    [Fact]
    public void EncryptionResult_ContainsNoPlaintext()
    {
        const string plaintext = "secret-access-token-do-not-log";

        var result = _sut.Encrypt(plaintext);

        // The returned struct must not contain plaintext in any field
        result.Ciphertext.Should().NotBeEquivalentTo(System.Text.Encoding.UTF8.GetBytes(plaintext),
            "ciphertext must not equal the plaintext bytes");
        result.KeyVersion.Should().BeGreaterThan(0);
        result.Iv.Should().HaveCount(12, "IV must be exactly 12 bytes for AES-GCM");
        result.AuthTag.Should().HaveCount(16, "Auth tag must be 16 bytes");
    }

    [Fact]
    public void Decrypt_ThrowsCryptographicException_WhenCiphertextTampered()
    {
        const string plaintext = "legit-token";
        var result = _sut.Encrypt(plaintext);

        // Flip one byte in ciphertext to simulate tampering
        var tampered = (byte[])result.Ciphertext.Clone();
        tampered[0] ^= 0xFF;

        var act = () => _sut.Decrypt(tampered, result.Iv, result.AuthTag, result.KeyVersion);

        act.Should().Throw<CryptographicException>("AES-GCM must reject tampered ciphertext");
    }

    [Fact]
    public void Decrypt_ThrowsCryptographicException_WhenAuthTagTampered()
    {
        const string plaintext = "legit-token";
        var result = _sut.Encrypt(plaintext);

        var tamperedTag = (byte[])result.AuthTag.Clone();
        tamperedTag[0] ^= 0xFF;

        var act = () => _sut.Decrypt(result.Ciphertext, result.Iv, tamperedTag, result.KeyVersion);

        act.Should().Throw<CryptographicException>("AES-GCM must reject invalid auth tag");
    }

    [Fact]
    public void Decrypt_ThrowsArgumentException_WhenIvWrongLength()
    {
        var act = () => _sut.Decrypt(new byte[32], new byte[8], new byte[16], 1);

        act.Should().Throw<ArgumentException>().WithMessage("*IV must be 12 bytes*");
    }

    [Fact]
    public void Encrypt_ThrowsArgumentException_WhenPlaintextEmpty()
    {
        var act = () => _sut.Encrypt(string.Empty);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("short")]
    [InlineData("access-sandbox-abc123xyz-provider-long-token-value")]
    [InlineData("access-sandbox-abc123xyz-1234567890-abcdef-ghijkl-mnopqr")]
    public void Encrypt_Decrypt_Works_ForVariousTokenLengths(string token)
    {
        var result = _sut.Encrypt(token);
        var decrypted = _sut.Decrypt(result.Ciphertext, result.Iv, result.AuthTag, result.KeyVersion);

        decrypted.Should().Be(token);
    }

    /// <summary>
    /// SC-007: Encryption + Decryption combined must average &lt; 50ms per cycle.
    /// Runs 1000 iterations and asserts average duration is below threshold.
    /// </summary>
    [Fact]
    public void EncryptDecrypt_AverageCycle_IsUnder50Ms_SC007()
    {
        const string token = "access-sandbox-performance-test-token-value";
        const int iterations = 1000;
        const double maxAverageMs = 50.0;

        var sw = Stopwatch.StartNew();

        for (int i = 0; i < iterations; i++)
        {
            var result = _sut.Encrypt(token);
            _ = _sut.Decrypt(result.Ciphertext, result.Iv, result.AuthTag, result.KeyVersion);
        }

        sw.Stop();

        var averageMs = sw.Elapsed.TotalMilliseconds / iterations;

        averageMs.Should().BeLessThan(maxAverageMs,
            $"SC-007 requires encryption+decryption to average < {maxAverageMs}ms per cycle. " +
            $"Actual average: {averageMs:F3}ms over {iterations} iterations.");
    }
}
