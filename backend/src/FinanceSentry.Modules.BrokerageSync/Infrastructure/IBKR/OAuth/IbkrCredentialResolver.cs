using System.Security.Cryptography;
using FinanceSentry.Infrastructure.Encryption;
using FinanceSentry.Modules.BrokerageSync.Domain;
using FinanceSentry.Modules.BrokerageSync.Domain.Exceptions;

namespace FinanceSentry.Modules.BrokerageSync.Infrastructure.IBKR.OAuth;

public interface IIbkrCredentialResolver
{
    /// <summary>Decrypts the stored artifacts and parses the RSA keys into ready-to-use OAuth material.</summary>
    IbkrOAuthCredentials Resolve(IBKRCredential credential);
}

public sealed class IbkrCredentialResolver(ICredentialEncryptionService encryption) : IIbkrCredentialResolver
{
    public IbkrOAuthCredentials Resolve(IBKRCredential credential)
    {
        try
        {
            return ResolveStored(credential);
        }
        catch (Exception ex)
        {
            throw new BrokerCredentialUnreadableException("IBKR", ex);
        }
    }

    private IbkrOAuthCredentials ResolveStored(IBKRCredential credential)
    {
        var accessTokenSecret = encryption.Decrypt(
            credential.EncryptedAccessTokenSecret,
            credential.AccessTokenSecretIv,
            credential.AccessTokenSecretAuthTag,
            credential.KeyVersion);
        var signaturePem = encryption.Decrypt(
            credential.EncryptedSignatureKey,
            credential.SignatureKeyIv,
            credential.SignatureKeyAuthTag,
            credential.KeyVersion);
        var encryptionPem = encryption.Decrypt(
            credential.EncryptedEncryptionKey,
            credential.EncryptionKeyIv,
            credential.EncryptionKeyAuthTag,
            credential.KeyVersion);

        var signatureKey = RSA.Create();
        var encryptionKey = RSA.Create();
        try
        {
            signatureKey.ImportFromPem(signaturePem);
            encryptionKey.ImportFromPem(encryptionPem);
            var (prime, generator) = IbkrOAuthSigner.ParseDhParams(credential.DhParam);

            return new IbkrOAuthCredentials
            {
                UserId = credential.UserId,
                ConsumerKey = credential.ConsumerKey,
                AccessToken = credential.AccessToken,
                AccessTokenSecret = accessTokenSecret,
                SignatureKey = signatureKey,
                EncryptionKey = encryptionKey,
                DhPrime = prime,
                DhGenerator = generator,
            };
        }
        catch
        {
            signatureKey.Dispose();
            encryptionKey.Dispose();
            throw;
        }
    }
}
