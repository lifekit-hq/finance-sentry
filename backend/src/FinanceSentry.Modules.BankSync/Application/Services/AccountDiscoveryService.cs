namespace FinanceSentry.Modules.BankSync.Application.Services;

using FinanceSentry.Infrastructure.Encryption;
using FinanceSentry.Modules.BankSync.Domain;
using FinanceSentry.Modules.BankSync.Domain.Repositories;
using FinanceSentry.Modules.BankSync.Infrastructure.Jobs;
using FinanceSentry.Modules.BankSync.Infrastructure.Monobank;
using FinanceSentry.Modules.BankSync.Infrastructure.TrueLayer;
using Hangfire;
using Microsoft.Extensions.Logging;

/// <summary>
/// Re-lists provider accounts for every existing connection and creates <see cref="BankAccount"/>
/// rows for entries not yet known, keyed on the provider account id
/// (<see cref="BankAccount.ExternalAccountId"/>). Run by <c>SyncScheduler</c> before it fans out
/// per-account sync jobs, so a newly discovered account is synced on the same scheduled run
/// without the user reconnecting. A listing failure on one connection is logged and skipped —
/// it never aborts discovery for the other connections.
///
/// Existing accounts are never touched here: this only adds rows. On SCA-strict banks (e.g. AIB)
/// a discovered account stays balance-only until a user-present reconnect re-establishes the SCA
/// session needed to pull transaction history in the background (see the inline-sync comment in
/// <c>FinalizeTrueLayerConnectCommand</c>).
/// </summary>
public interface IAccountDiscoveryService
{
    Task<int> DiscoverNewAccountsAsync(CancellationToken ct = default);
}

/// <inheritdoc />
public class AccountDiscoveryService(
    ITrueLayerConnectionRepository trueLayerConnections,
    ITrueLayerClient trueLayerClient,
    ITrueLayerTokenRefreshService trueLayerTokenRefresh,
    IMonobankCredentialRepository monobankCredentials,
    IMonobankAdapter monobankAdapter,
    MonobankBalanceCache monobankBalanceCache,
    ICredentialEncryptionService encryption,
    IBankAccountRepository accounts,
    IBackgroundJobClient backgroundJobs,
    ILogger<AccountDiscoveryService> logger) : IAccountDiscoveryService
{
    public async Task<int> DiscoverNewAccountsAsync(CancellationToken ct = default)
    {
        var created = await DiscoverTrueLayerAsync(ct);
        created += await DiscoverMonobankAsync(ct);
        return created;
    }

    private async Task<int> DiscoverTrueLayerAsync(CancellationToken ct)
    {
        var linkedConnections = await trueLayerConnections.GetAllLinkedAsync(ct);
        var created = 0;

        foreach (var connection in linkedConnections)
        {
            try
            {
                var accessToken = await trueLayerTokenRefresh.AcquireAccessTokenAsync(connection.Id, ct);
                created += await DiscoverTrueLayerConnectionAsync(connection, accessToken, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "TrueLayer account discovery failed for connection {ConnectionId}; skipping this connection.",
                    connection.Id);
            }
        }

        return created;
    }

    private async Task<int> DiscoverTrueLayerConnectionAsync(
        TrueLayerConnection connection, string accessToken, CancellationToken ct)
    {
        var created = 0;

        var providerAccounts = await trueLayerClient.ListAccountsAsync(accessToken, ct);
        foreach (var pa in providerAccounts)
        {
            if (await accounts.ExistsByExternalAccountIdAsync(pa.AccountId, ct))
                continue;

            decimal? currentBalance = null;
            try
            {
                var bal = await trueLayerClient.GetBalanceAsync(accessToken, pa.AccountId, ct);
                currentBalance = bal?.Current;
            }
            catch (TrueLayerException)
            {
                // Best-effort: skip balance, account is still usable.
            }

            var account = TrueLayerAccountFactory.CreateAccount(connection, pa, currentBalance);
            await accounts.AddAsync(account, ct);
            backgroundJobs.Enqueue<ScheduledSyncJob>(job => job.ExecuteSyncAsync(account.Id));
            created++;
        }

        IReadOnlyList<TrueLayerAccountInfo> providerCards = [];
        try
        {
            providerCards = await trueLayerClient.ListCardsAsync(accessToken, ct);
        }
        catch (TrueLayerException ex)
        {
            logger.LogInformation(
                "TrueLayer /cards unavailable for connection {ConnectionId}: {Error}",
                connection.Id, ex.Message);
        }

        foreach (var card in providerCards)
        {
            if (await accounts.ExistsByExternalAccountIdAsync(card.AccountId, ct))
                continue;

            decimal? owed = null;
            decimal? creditLimit = null;
            try
            {
                var bal = await trueLayerClient.GetCardBalanceAsync(accessToken, card.AccountId, ct);
                owed = bal?.Current;
                creditLimit = bal?.CreditLimit;
            }
            catch (TrueLayerException)
            {
                // Best-effort: skip balance, card is still usable.
            }

            var cardAccount = TrueLayerAccountFactory.CreateCardAccount(connection, card, owed, creditLimit);
            await accounts.AddAsync(cardAccount, ct);
            backgroundJobs.Enqueue<ScheduledSyncJob>(job => job.ExecuteSyncAsync(cardAccount.Id));
            created++;
        }

        return created;
    }

    private async Task<int> DiscoverMonobankAsync(CancellationToken ct)
    {
        var credentials = await monobankCredentials.GetAllAsync(ct);
        var created = 0;

        foreach (var credential in credentials)
        {
            try
            {
                var token = encryption.Decrypt(
                    credential.EncryptedToken, credential.Iv, credential.AuthTag, credential.KeyVersion);
                var clientInfo = await monobankAdapter.GetClientInfoAsync(token, ct);

                foreach (var pa in clientInfo.Accounts)
                {
                    monobankBalanceCache.Set(token, pa.Id, MonobankAdapter.ToBankAccountInfo(pa, clientInfo.Name));

                    if (await accounts.ExistsByExternalAccountIdAsync(pa.Id, ct))
                        continue;

                    var account = MonobankAccountFactory.CreateAccount(credential.UserId, credential.Id, pa);
                    await accounts.AddAsync(account, ct);
                    backgroundJobs.Enqueue<ScheduledSyncJob>(job => job.ExecuteSyncAsync(account.Id));
                    created++;
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Monobank account discovery failed for credential {CredentialId}; skipping this connection.",
                    credential.Id);
            }
        }

        return created;
    }
}
