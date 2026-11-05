namespace FinanceSentry.Infrastructure.Encryption;

using System.Globalization;

/// <summary>
/// The stored form of one field-encrypted value: a single text column holding
/// <c>v{keyVersion}.{iv}.{tag}.{ciphertext}</c>, the last three base64. The key version travels with the value, so a
/// row stays readable after the current key moves on, and rotation can find the rows still on an older key without
/// decrypting them.
/// <para>
/// This is the primitive for declared profile data (income, goals, career amounts), stored as text rather than as the
/// <c>byte[]</c> columns the provider credentials use: <c>OwnedRowExporter</c> drops every <c>byte[]</c> column, so a
/// declared amount stored the way a credential is would vanish from the person's data download. Here the entity
/// property stays plaintext in code; the converter encrypts on write and decrypts on read, and the export writes the
/// decrypted value because the property is not a secret to its owner.
/// </para>
/// <para>
/// An empty string is stored as the empty string: that it is empty is not a secret, and AES-GCM over zero bytes is
/// refused by <see cref="ICredentialEncryptionService"/>.
/// </para>
/// </summary>
public static class EncryptedField
{
    private const char Separator = '.';
    private const char VersionPrefix = 'v';
    private const int PartCount = 4;

    /// <summary>Encrypts <paramref name="plaintext"/> under the service's current key.</summary>
    public static string Seal(ICredentialEncryptionService encryption, string plaintext)
    {
        ArgumentNullException.ThrowIfNull(encryption);
        ArgumentNullException.ThrowIfNull(plaintext);

        if (plaintext.Length == 0)
            return string.Empty;

        var sealedValue = encryption.Encrypt(plaintext);
        return string.Join(
            Separator,
            VersionPrefix + sealedValue.KeyVersion.ToString(CultureInfo.InvariantCulture),
            Convert.ToBase64String(sealedValue.Iv),
            Convert.ToBase64String(sealedValue.AuthTag),
            Convert.ToBase64String(sealedValue.Ciphertext));
    }

    /// <summary>
    /// Decrypts a stored value. Throws when the value is not in the stored form or its authentication tag fails -
    /// a field that cannot be read is a wrong key or tampered data, never an empty answer.
    /// </summary>
    public static string Open(ICredentialEncryptionService encryption, string stored)
    {
        ArgumentNullException.ThrowIfNull(encryption);
        ArgumentNullException.ThrowIfNull(stored);

        if (stored.Length == 0)
            return string.Empty;

        var parts = stored.Split(Separator);
        if (parts.Length != PartCount || !TryParseVersion(parts[0], out var keyVersion))
            throw new FormatException("The stored value is not an encrypted field (expected v{keyVersion}.{iv}.{tag}.{ciphertext}).");

        return encryption.Decrypt(
            Convert.FromBase64String(parts[3]),
            Convert.FromBase64String(parts[1]),
            Convert.FromBase64String(parts[2]),
            keyVersion);
    }

    /// <summary>The key version a stored value was sealed under, or null for the empty string (sealed under none).</summary>
    public static int? KeyVersionOf(string stored)
    {
        ArgumentNullException.ThrowIfNull(stored);
        if (stored.Length == 0)
            return null;

        var head = stored.Split(Separator, 2)[0];
        return TryParseVersion(head, out var version)
            ? version
            : throw new FormatException("The stored value is not an encrypted field.");
    }

    /// <summary>The SQL <c>LIKE</c> pattern matching every value sealed under <paramref name="keyVersion"/>.</summary>
    public static string LikePatternFor(int keyVersion)
        => VersionPrefix + keyVersion.ToString(CultureInfo.InvariantCulture) + Separator + "%";

    private static bool TryParseVersion(string head, out int version)
    {
        version = 0;
        return head.Length > 1
            && head[0] == VersionPrefix
            && int.TryParse(head.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out version);
    }
}
