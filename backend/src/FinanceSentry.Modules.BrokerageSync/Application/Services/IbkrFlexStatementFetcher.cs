using FinanceSentry.Modules.BrokerageSync.Domain.Exceptions;
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
    /// that case, it just logs and returns. Any other failure is recorded on the credential as its
    /// last error and rethrown; with <paramref name="statementNotAvailableIsExpected"/> IBKR answering that
    /// no statement is available yet (<see cref="IbkrFlexException.IsStatementNotAvailable"/>) is rethrown
    /// without being recorded, for the scheduled run that routinely meets it.
    /// </summary>
    Task<FlexStatementXml?> FetchAsync(
        Guid userId,
        FlexStatementWindow? window = null,
        CancellationToken ct = default,
        bool statementNotAvailableIsExpected = false);
}

public sealed class IbkrFlexStatementFetcher(
    IIBKRFlexCredentialRepository credentialRepository,
    IIbkrFlexCredentialResolver credentialResolver,
    IIbkrFlexClient flexClient,
    ILogger<IbkrFlexStatementFetcher> logger) : IIbkrFlexStatementFetcher
{
    public async Task<FlexStatementXml?> FetchAsync(
        Guid userId,
        FlexStatementWindow? window = null,
        CancellationToken ct = default,
        bool statementNotAvailableIsExpected = false)
    {
        var credential = await credentialRepository.GetByUserIdUnscopedAsync(userId, ct);
        if (credential is null || !credential.IsActive)
        {
            logger.LogInformation("No active IBKR Flex credential for user {UserId}; skipping Flex fetch.", userId);
            return null;
        }

        var credentials = credentialResolver.Resolve(credential);

        try
        {
            var statement = await flexClient.FetchStatementAsync(credentials, window, ct);
            credential.RecordUseSuccess();
            await credentialRepository.SaveChangesAsync(ct);
            return statement;
        }
        catch (Exception ex) when (!(statementNotAvailableIsExpected && ex is IbkrFlexException { IsStatementNotAvailable: true }))
        {
            credential.RecordUseError(ex.Message);
            await credentialRepository.SaveChangesAsync(ct);
            throw;
        }
    }
}
