namespace FinanceSentry.Infrastructure.Encryption;

/// <summary>
/// One store of encrypted credentials that key rotation must migrate (issue #493).
///
/// Rotation exists because the disclosed fallback key was in use in production: removing the
/// fallback stops NEW credentials being written under it, but every row already written stays
/// readable by anyone with the repository until it is re-encrypted. A store that holds encrypted
/// credentials and does not implement this interface is silently left on the old key, so a new
/// credential store must add an implementation in the same change that adds the store.
///
/// Contract for implementers:
/// <list type="bullet">
/// <item><b>Idempotent.</b> Select only rows whose key version differs from the target; a second
/// run over an already-rotated store does nothing and returns 0.</item>
/// <item><b>Atomic per row.</b> Persist each row's new ciphertext, IV, auth tag and key version
/// together. A row is never left with a new ciphertext and an old version (undecryptable) or an
/// old ciphertext and a new version (decryptable only by accident).</item>
/// <item><b>Fail loud.</b> A row that cannot be decrypted is a real problem — a wrong key or
/// tampered data — and must surface, not be skipped.</item>
/// </list>
/// </summary>
public interface ICredentialRotationTarget
{
    /// <summary>Store name for logs — e.g. <c>MonobankCredentials</c>.</summary>
    string Name { get; }

    /// <summary>
    /// Re-encrypt every row not already at <paramref name="targetKeyVersion"/>, returning how many
    /// rows were migrated.
    /// </summary>
    Task<int> RotateAsync(int targetKeyVersion, CancellationToken cancellationToken);
}
