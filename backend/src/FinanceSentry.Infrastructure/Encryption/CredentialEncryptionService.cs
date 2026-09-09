namespace FinanceSentry.Infrastructure.Encryption;

using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

/// <summary>
/// AES-256-GCM credential encryption service.
///
/// Security properties:
/// - AES-256-GCM provides authenticated encryption (confidentiality + integrity).
/// - Each call to Encrypt() generates a fresh 12-byte IV — never reuse an IV.
/// - The 16-byte authentication tag is verified on Decrypt(); tampered data is rejected.
/// - Key is derived from a master key stored in environment config (never in source code).
/// - Key versioning enables rotation: old encrypted credentials retain their key version
///   so they can still be decrypted after rotation.
///
/// SC-007: Encrypt + Decrypt cycle must complete within 50ms on average.
/// </summary>
public class CredentialEncryptionService : ICredentialEncryptionService
{
    private readonly EncryptionOptions _options;

    // AES-256-GCM constants
    private const int IvSizeBytes = 12;      // 96-bit IV (recommended for GCM)
    private const int TagSizeBytes = 16;     // 128-bit authentication tag

    /// <summary>
    /// The key this service silently fell back to when <c>Encryption:Keys</c> was unconfigured
    /// (issue #493). It is committed to a public repository, so every credential encrypted under
    /// it must be treated as disclosed. It is named here so exactly one place spells it: the
    /// startup validator refuses it outside Development, and the Development fallback below is
    /// the ONE remaining caller.
    /// </summary>
    public const string DisclosedFallbackKeyBase64 = "dGVzdGtleS10ZXN0a2V5LXRlc3RrZXktdGVzdGtleTA=";

    public CredentialEncryptionService(IOptions<EncryptionOptions> options)
    {
        _options = options.Value;
        // No fallback here any more (#493). An unconfigured instance used to quietly encrypt
        // production credentials under DisclosedFallbackKeyBase64; it now throws, and
        // EncryptionOptionsValidator turns that into a startup failure outside Development
        // rather than a first-request one.
        ValidateOptions(_options);
    }

    /// <inheritdoc />
    public EncryptionResult Encrypt(string plaintext)
    {
        ArgumentException.ThrowIfNullOrEmpty(plaintext, nameof(plaintext));

        var key = GetKeyForVersion(_options.CurrentKeyVersion);
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);

        var iv = new byte[IvSizeBytes];
        RandomNumberGenerator.Fill(iv);

        var ciphertext = new byte[plaintextBytes.Length];
        var authTag = new byte[TagSizeBytes];

        using var aesGcm = new AesGcm(key, TagSizeBytes);
        aesGcm.Encrypt(iv, plaintextBytes, ciphertext, authTag);

        return new EncryptionResult(ciphertext, iv, authTag, _options.CurrentKeyVersion);
    }

    /// <inheritdoc />
    public string Decrypt(byte[] ciphertext, byte[] iv, byte[] authTag, int keyVersion)
    {
        ArgumentNullException.ThrowIfNull(ciphertext, nameof(ciphertext));
        ArgumentNullException.ThrowIfNull(iv, nameof(iv));
        ArgumentNullException.ThrowIfNull(authTag, nameof(authTag));

        if (iv.Length != IvSizeBytes)
            throw new ArgumentException($"IV must be {IvSizeBytes} bytes.", nameof(iv));
        if (authTag.Length != TagSizeBytes)
            throw new ArgumentException($"Auth tag must be {TagSizeBytes} bytes.", nameof(authTag));

        var key = GetKeyForVersion(keyVersion);
        var plaintext = new byte[ciphertext.Length];

        using var aesGcm = new AesGcm(key, TagSizeBytes);

        // AesGcm.Decrypt throws CryptographicException if tag verification fails.
        // Do NOT catch this — tampered data must be rejected by the caller.
        aesGcm.Decrypt(iv, ciphertext, authTag, plaintext);

        return Encoding.UTF8.GetString(plaintext);
    }

    private byte[] GetKeyForVersion(int version)
    {
        if (!_options.Keys.TryGetValue(version, out var base64Key))
            throw new InvalidOperationException($"Encryption key version {version} is not configured.");

        var key = Convert.FromBase64String(base64Key);

        if (key.Length != 32)
            throw new InvalidOperationException($"Key version {version} must be 32 bytes (256 bits) for AES-256.");

        return key;
    }

    private static void ValidateOptions(EncryptionOptions opts)
    {
        if (opts.Keys == null || opts.Keys.Count == 0)
            throw new InvalidOperationException("At least one encryption key must be configured.");

        if (!opts.Keys.ContainsKey(opts.CurrentKeyVersion))
            throw new InvalidOperationException(
                $"CurrentKeyVersion ({opts.CurrentKeyVersion}) is not present in the Keys dictionary.");
    }
}
