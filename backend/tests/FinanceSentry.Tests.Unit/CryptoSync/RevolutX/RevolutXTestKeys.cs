namespace FinanceSentry.Tests.Unit.CryptoSync.RevolutX;

/// <summary>
/// Known-answer Ed25519 material. The key is RFC 8032 §7.1 TEST 1 — a published test vector, not a
/// secret — and the PEMs are assembled at runtime from its seed so no private-key block is
/// committed for scanners to trip on.
/// </summary>
internal static class RevolutXTestKeys
{
    /// <summary>RFC 8032 §7.1 TEST 1 secret key (the 32-byte seed).</summary>
    public const string Rfc8032Test1SeedHex = "9d61b19deffd5a60ba844af492ec2cc44449c5697b326919703bac031cae7f60";

    /// <summary>RFC 8032 §7.1 TEST 1 public key.</summary>
    public const string Rfc8032Test1PublicKeyHex = "d75a980182b10ab7d54bfed3c964073a0ee172f3daa62325af021a68f707511a";

    /// <summary>RFC 8032 §7.1 TEST 1 signature over the empty message.</summary>
    public const string Rfc8032Test1EmptyMessageSignatureHex =
        "e5564300c360ac729086e2cc806e828a84877f1eb8e5d974d873e065224901555fb8821590a33bacc61e39701cf9b46bd25bf5f0595bbe24655141438e7a100b";

    /// <summary>
    /// <c>openssl pkeyutl -sign -rawin</c> (OpenSSL 3.5) with the TEST 1 key over
    /// <c>1700000000000GET/api/1.0/balances</c> — an independent implementation's answer for a real
    /// Revolut X signing payload.
    /// </summary>
    public const string OpenSslBalancesSignatureBase64 =
        "9YsJiloOtqfqhHv2ZFDPOMcpJe8EThAc+E4gHYQlyMmDWx0CQRu7C4QOM22q6Nw66/XdmePlwC4iLxZ2TEEfDQ==";

    public const string OpenSslBalancesTimestamp = "1700000000000";

    // PKCS#8 PrivateKeyInfo prefixes for a 32-byte raw key (RFC 8410).
    private const string Ed25519Pkcs8PrefixHex = "302e020100300506032b657004220420";
    private const string X25519Pkcs8PrefixHex = "302e020100300506032b656e04220420";

    /// <summary>The TEST 1 key as the PKCS#8 PEM <c>openssl genpkey -algorithm ed25519</c> writes.</summary>
    public static string Ed25519PrivateKeyPem => Pem("PRIVATE KEY", Ed25519Pkcs8PrefixHex + Rfc8032Test1SeedHex);

    /// <summary>The TEST 1 public key (SubjectPublicKeyInfo PEM).</summary>
    public static string Ed25519PublicKeyPem =>
        Pem("PUBLIC KEY", "302a300506032b6570032100" + Rfc8032Test1PublicKeyHex);

    /// <summary>A well-formed PKCS#8 key that is X25519, not Ed25519.</summary>
    public static string X25519PrivateKeyPem => Pem("PRIVATE KEY", X25519Pkcs8PrefixHex + Rfc8032Test1SeedHex);

    private static string Pem(string label, string derHex) =>
        $"-----BEGIN {label}-----\n{Convert.ToBase64String(Convert.FromHexString(derHex))}\n-----END {label}-----\n";
}
