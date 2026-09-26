using FinanceSentry.Modules.BrokerageSync.Domain.Repositories;
using FinanceSentry.Modules.BrokerageSync.Infrastructure.IBKR.Flex;
using Microsoft.Extensions.Logging;

namespace FinanceSentry.Modules.BrokerageSync.Application.Services;

public interface IIbkrFlexStatementFetcher
{
    /// <summary>
    /// Fetches the user's IBKR Flex statement, or <c>null</c> when the user has no active Flex
    /// credential. A missing credential is a normal, expected state — setting it up is a manual
    /// step a human takes in IBKR's account management — so this never throws or retries for
    /// that case, it just logs and returns.
    /// </summary>
    Task<FlexStatementXml?> FetchAsync(Guid userId, CancellationToken ct = default);
}

public sealed class IbkrFlexStatementFetcher(
    IIBKRFlexCredentialRepository credentialRepository,
    IIbkrFlexCredentialResolver credentialResolver,
    IIbkrFlexClient flexClient,
    ILogger<IbkrFlexStatementFetcher> logger) : IIbkrFlexStatementFetcher
{
    public async Task<FlexStatementXml?> FetchAsync(Guid userId, CancellationToken ct = default)
    {
        var credential = await credentialRepository.GetByUserIdAsync(userId, ct);
        if (credential is null || !credential.IsActive)
        {
            logger.LogInformation("No active IBKR Flex credential for user {UserId}; skipping Flex fetch.", userId);
            return null;
        }

        var credentials = credentialResolver.Resolve(credential);

        try
        {
            var statement = await flexClient.FetchStatementAsync(credentials, ct);
            credential.RecordUseSuccess();
            await credentialRepository.SaveChangesAsync(ct);
            return statement;
        }
        catch (Exception ex)
        {
            credential.RecordUseError(ex.Message);
            await credentialRepository.SaveChangesAsync(ct);
            throw;
        }
    }
}
