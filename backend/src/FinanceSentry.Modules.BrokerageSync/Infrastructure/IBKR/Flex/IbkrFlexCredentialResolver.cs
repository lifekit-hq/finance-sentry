using FinanceSentry.Infrastructure.Encryption;
using FinanceSentry.Modules.BrokerageSync.Domain;

namespace FinanceSentry.Modules.BrokerageSync.Infrastructure.IBKR.Flex;

public interface IIbkrFlexCredentialResolver
{
    IbkrFlexCredentials Resolve(IBKRFlexCredential credential);
}

public sealed class IbkrFlexCredentialResolver(ICredentialEncryptionService encryption) : IIbkrFlexCredentialResolver
{
    public IbkrFlexCredentials Resolve(IBKRFlexCredential credential)
    {
        var token = encryption.Decrypt(
            credential.EncryptedToken, credential.TokenIv, credential.TokenAuthTag, credential.KeyVersion);
        return new IbkrFlexCredentials(credential.UserId, token, credential.QueryId);
    }
}
