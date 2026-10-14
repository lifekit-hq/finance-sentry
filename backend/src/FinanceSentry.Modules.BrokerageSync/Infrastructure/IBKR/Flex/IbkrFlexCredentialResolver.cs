using FinanceSentry.Infrastructure.Encryption;
using FinanceSentry.Modules.BrokerageSync.Domain;
using FinanceSentry.Modules.BrokerageSync.Domain.Exceptions;

namespace FinanceSentry.Modules.BrokerageSync.Infrastructure.IBKR.Flex;

public interface IIbkrFlexCredentialResolver
{
    IbkrFlexCredentials Resolve(IBKRFlexCredential credential);
}

public sealed class IbkrFlexCredentialResolver(ICredentialEncryptionService encryption) : IIbkrFlexCredentialResolver
{
    public IbkrFlexCredentials Resolve(IBKRFlexCredential credential)
    {
        string token;
        try
        {
            token = encryption.Decrypt(
                credential.EncryptedToken, credential.TokenIv, credential.TokenAuthTag, credential.KeyVersion);
        }
        catch (Exception ex)
        {
            throw new BrokerCredentialUnreadableException("IBKR Flex", ex);
        }

        return new IbkrFlexCredentials(credential.UserId, token, credential.QueryId);
    }
}
