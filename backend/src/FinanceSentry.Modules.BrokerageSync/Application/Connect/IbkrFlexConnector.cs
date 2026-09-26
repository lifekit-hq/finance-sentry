using FinanceSentry.Infrastructure.Encryption;
using FinanceSentry.Modules.BrokerageSync.Domain;
using FinanceSentry.Modules.BrokerageSync.Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace FinanceSentry.Modules.BrokerageSync.Application.Connect;

public sealed class IbkrFlexConnector(
    IIBKRFlexCredentialRepository credentialRepository,
    ICredentialEncryptionService encryption,
    ILogger<IbkrFlexConnector> logger) : IIbkrFlexConnector
{
    public async Task ConnectAsync(Guid userId, ConnectIbkrFlexArtifacts artifacts, CancellationToken ct)
    {
        var existing = await credentialRepository.GetByUserIdAsync(userId, ct);
        var token = encryption.Encrypt(artifacts.Token);

        if (existing is not null)
        {
            existing.Replace(artifacts.QueryId, token.Ciphertext, token.Iv, token.AuthTag, token.KeyVersion);
            credentialRepository.Update(existing);
        }
        else
        {
            var credential = new IBKRFlexCredential(
                userId, artifacts.QueryId, token.Ciphertext, token.Iv, token.AuthTag, token.KeyVersion);
            await credentialRepository.AddAsync(credential, ct);
        }

        await credentialRepository.SaveChangesAsync(ct);
        logger.LogInformation("IBKR Flex credential persisted for user {UserId}", userId);
    }
}
