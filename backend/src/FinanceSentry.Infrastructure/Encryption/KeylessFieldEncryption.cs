namespace FinanceSentry.Infrastructure.Encryption;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

/// <summary>
/// The encryption service of a host that holds no key (the MCP host, issue #613). A context in such a host still maps
/// its field-encrypted columns, so the model is the same everywhere, but any attempt to seal or open one throws
/// <see cref="FieldKeyUnavailableException"/> instead of returning something wrong. A query that projects only the
/// plain columns never touches the converter and works as usual.
/// </summary>
public sealed class KeylessFieldEncryption : ICredentialEncryptionService
{
    public static readonly KeylessFieldEncryption Instance = new();

    private KeylessFieldEncryption() { }

    public EncryptionResult Encrypt(string plaintext) => throw new FieldKeyUnavailableException();

    public string Decrypt(byte[] ciphertext, byte[] iv, byte[] authTag, int keyVersion) => throw new FieldKeyUnavailableException();
}

/// <summary>An encrypted field was read or written on a host that holds no key.</summary>
public sealed class FieldKeyUnavailableException()
    : InvalidOperationException("This host holds no encryption key, so encrypted fields cannot be read or written here.");

/// <summary>A context whose model binds an encryption service (see <see cref="EncryptedFieldPropertyExtensions"/>).</summary>
public interface IFieldEncryptedContext
{
    /// <summary>The service the context's encrypted columns are bound to.</summary>
    ICredentialEncryptionService Encryption { get; }
}

/// <summary>
/// EF caches the model per context type, and an encrypted column's converter binds the encryption service when the model
/// is built. A process has one service, so the default cache is right there; a process that builds the same context with
/// different services (the test suite, which plays several hosts) would otherwise reuse the first model - and its key.
/// The key carries the service instance.
/// </summary>
public sealed class FieldEncryptionModelCacheKeyFactory : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime)
        => (context.GetType(), (context as IFieldEncryptedContext)?.Encryption, designTime);
}
