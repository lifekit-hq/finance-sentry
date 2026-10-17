namespace FinanceSentry.Modules.BankSync.Application.Services;

using FinanceSentry.Infrastructure.Encryption;
using FinanceSentry.Modules.BankSync.Application.Connections;
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
/// Each connection's listing is one attempt on that connection's health (connection health, Option B, S2): a
/// listing that works records a success, a failed one is classified and recorded through
/// <see cref="IBankConnectionHealth"/>, which raises the alert the policy decides once the provider is listed in
/// <c>ConnectionHealth:ActiveProviders</c> (in shadow mode it only records). For a listed provider the listing also
/// keeps the Dormant cadence: a Dormant connection is listed at most once per <c>ConnectionHealth:DormantSyncInterval</c>.
///
/// Existing accounts have their provider identifiers refreshed (IBAN, PANs, last four, card expiry), and the same read
/// drives the account lifecycle (<see cref="IAccountLifecycle"/>): an account the list stops naming is marked absent,
/// retired once its own health also says Dormant/ResourceGone, and restored if the list names it again; a new account
/// whose IBAN equals a failing or absent account's is stored as its reissue. A failed read changes none of it. On SCA-strict banks (e.g. AIB)
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
    IBankConnectionHealth connectionHealth,
    IAccountLifecycle lifecycle,
    ILogger<AccountDiscoveryService> logger,
    TimeProvider? clock = null) : IAccountDiscoveryService
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    /// <summary>What a Monobank connection is called in its alert: the bank name its accounts carry.</summary>
    private const string MonobankBankName = "Monobank";

    public async Task<int> DiscoverNewAccountsAsync(CancellationToken ct = default)
    {
        var created = await DiscoverTrueLayerAsync(ct);
        created += await DiscoverMonobankAsync(ct);
        return created;
    }

    private async Task<int> DiscoverTrueLayerAsync(CancellationToken ct)
    {
        var linkedConnections = await trueLayerConnections.GetAllLinkedUnscopedAsync(ct);
        var created = 0;

        foreach (var connection in linkedConnections)
        {
            var health = new BankConnectionRef(
                TrueLayerFailureClassifier.ProviderName, connection.Id, connection.UserId, connection.ProviderDisplayName);
            if (connectionHealth.DormantPausedUntil(health, connection.Health) is not null)
                continue;

            try
            {
                var accessToken = await trueLayerTokenRefresh.AcquireAccessTokenAsync(connection.Id, ct);
                created += await DiscoverTrueLayerConnectionAsync(connection, accessToken, ct);
                await connectionHealth.RecordConnectionSuccessAsync(health, ct);
            }
            catch (TrueLayerException ex) when (IsRefreshRejected(ex))
            {
                // Recorded first: the expiry below saves the tracked row, which then carries the new health too.
                await connectionHealth.RecordConnectionFailureAsync(health, ex, ct);
                await ExpireConnectionAsync(connection.Id, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "TrueLayer account discovery failed for connection {ConnectionId}; skipping this connection.",
                    connection.Id);
                await connectionHealth.RecordConnectionFailureAsync(health, ex, ct);
            }
        }

        return created;
    }

    // A rejected refresh means the consent is gone and only a user-present reconnect can restore it;
    // TrueLayerFailureClassifier reads the same body as a definitive credential loss.
    private static bool IsRefreshRejected(TrueLayerException ex)
        => ex.Message.Contains("invalid_grant", StringComparison.OrdinalIgnoreCase);

    // Applies the credential-expiry policy: the connection leaves LINKED (so discovery stops retrying
    // it) and its accounts go to reauth_required, which the existing reconnect prompt keys on.
    private async Task ExpireConnectionAsync(Guid connectionId, CancellationToken ct)
    {
        try
        {
            var connection = await trueLayerConnections.GetByIdUnscopedAsync(connectionId, ct);
            if (connection is null)
                return;

            logger.LogWarning(
                "TrueLayer refresh rejected (invalid_grant) for connection {ConnectionId}; marking it expired and its accounts reauth_required.",
                connectionId);

            var connectionAccounts = (await accounts.GetByUserIdUnscopedAsync(connection.UserId, ct))
                .Where(a => a.TrueLayerConnectionId == connectionId);
            foreach (var account in connectionAccounts)
            {
                account.MarkReauthRequired();
                await accounts.UpdateAsync(account, ct);
            }

            connection.MarkExpired();
            await trueLayerConnections.UpdateAsync(connection, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Could not mark TrueLayer connection {ConnectionId} expired; skipping this connection.",
                connectionId);
        }
    }

    private async Task<int> DiscoverTrueLayerConnectionAsync(
        TrueLayerConnection connection, string accessToken, CancellationToken ct)
    {
        var created = 0;
        var known = await KnownAccountsAsync(connection.UserId, ct);
        var held = known.Values.Where(a => a.TrueLayerConnectionId == connection.Id).ToList();

        var providerAccounts = await trueLayerClient.ListAccountsAsync(accessToken, ct);
        var listedIds = providerAccounts.Select(a => a.AccountId).ToHashSet();
        foreach (var pa in providerAccounts)
        {
            if (await RefreshExistingAsync(known, pa.AccountId, pa.Iban, null, pa.AccountNumberLast4, null, ct))
                continue;
            if (await accounts.ExistsByExternalAccountIdUnscopedAsync(pa.AccountId, ct))
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
            await lifecycle.AddDiscoveredAsync(account, held, listedIds, ct);
            backgroundJobs.Enqueue<ScheduledSyncJob>(job => job.ExecuteSyncAsync(account.Id));
            created++;
        }

        IReadOnlyList<TrueLayerAccountInfo> providerCards = [];
        var cardsListed = true;
        try
        {
            providerCards = await trueLayerClient.ListCardsAsync(accessToken, ct);
        }
        catch (TrueLayerException ex)
        {
            cardsListed = false;
            logger.LogInformation(
                "TrueLayer /cards unavailable for connection {ConnectionId}: {Error}",
                connection.Id, ex.Message);
        }

        listedIds.UnionWith(providerCards.Select(c => c.AccountId));
        foreach (var card in providerCards)
        {
            if (await RefreshExistingAsync(known, card.AccountId, null, null, card.AccountNumberLast4, card.ValidTo, ct))
                continue;
            if (await accounts.ExistsByExternalAccountIdUnscopedAsync(card.AccountId, ct))
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
            await lifecycle.AddDiscoveredAsync(cardAccount, held, listedIds, ct);
            backgroundJobs.Enqueue<ScheduledSyncJob>(job => job.ExecuteSyncAsync(cardAccount.Id));
            created++;
        }

        // A failed /cards read would make every card look absent, so the lifecycle skips it.
        if (cardsListed)
            await lifecycle.ReconcileAsync(held, listedIds, ct);

        return created;
    }

    private async Task<int> DiscoverMonobankAsync(CancellationToken ct)
    {
        var credentials = await monobankCredentials.GetAllUnscopedAsync(ct);
        var created = 0;

        foreach (var credential in credentials)
        {
            var health = new BankConnectionRef(
                MonobankFailureClassifier.ProviderName, credential.Id, credential.UserId, MonobankBankName);
            if (connectionHealth.DormantPausedUntil(health, credential.Health) is not null)
                continue;

            try
            {
                var token = CredentialUnreadableException.Decrypt(
                    encryption, credential.EncryptedToken, credential.Iv, credential.AuthTag, credential.KeyVersion);
                if (monobankBalanceCache.HasFreshEntries(token))
                {
                    logger.LogInformation(
                        "Monobank client-info fetched recently for credential {CredentialId}; deferring discovery to the next run.",
                        credential.Id);
                    continue;
                }

                var clientInfo = await monobankAdapter.GetClientInfoAsync(token, ct);
                await connectionHealth.RecordConnectionSuccessAsync(health, ct);

                var known = await KnownAccountsAsync(credential.UserId, ct);
                var held = known.Values.Where(a => a.MonobankCredentialId == credential.Id).ToList();
                var listedIds = clientInfo.Accounts.Select(a => a.Id).ToHashSet();
                foreach (var pa in clientInfo.Accounts)
                {
                    monobankBalanceCache.Set(token, pa.Id, MonobankAdapter.ToBankAccountInfo(pa, clientInfo.Name));

                    if (await RefreshExistingAsync(known, pa.Id, pa.Iban, pa.MaskedPans, LastFour(pa.MaskedPan), null, ct))
                        continue;
                    if (await accounts.ExistsByExternalAccountIdUnscopedAsync(pa.Id, ct))
                        continue;

                    var account = MonobankAccountFactory.CreateAccount(credential.UserId, credential.Id, pa);
                    await lifecycle.AddDiscoveredAsync(account, held, listedIds, ct);
                    backgroundJobs.Enqueue<ScheduledSyncJob>(job => job.ExecuteSyncAsync(account.Id));
                    created++;
                }

                await lifecycle.ReconcileAsync(held, listedIds, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Monobank account discovery failed for credential {CredentialId}; skipping this connection.",
                    credential.Id);
                await connectionHealth.RecordConnectionFailureAsync(health, ex, ct);
            }
        }

        return created;
    }

    private async Task<Dictionary<string, BankAccount>> KnownAccountsAsync(Guid userId, CancellationToken ct)
        => (await accounts.GetByUserIdUnscopedAsync(userId, ct)).ToDictionary(a => a.ExternalAccountId);

    // "0000" is the placeholder a missing PAN maps to; it must not overwrite a real last four.
    private static string? LastFour(string maskedPan)
        => maskedPan.Length >= 4 && maskedPan[^4..] != "0000" ? maskedPan[^4..] : null;

    /// <summary>Refreshes a row the provider still lists; true when the listed id is one we already hold.</summary>
    private async Task<bool> RefreshExistingAsync(
        Dictionary<string, BankAccount> known, string externalId, string? iban, string[]? pans, string? last4,
        string? validTo, CancellationToken ct)
    {
        if (!known.TryGetValue(externalId, out var account))
            return false;

        var previousLast4 = account.AccountNumberLast4;
        if (account.RefreshProviderIdentifiers(iban, pans, last4 == "0000" ? null : last4, validTo,
                _clock.GetUtcNow()))
        {
            try
            {
                await accounts.SaveProviderIdentifiersUnscopedAsync(account, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Could not store provider identifiers for account {AccountId}.", account.Id);
                return true;
            }

            if (account.AccountNumberLast4 != previousLast4)
                logger.LogInformation(
                    "Account {AccountId} ({Provider}) card digits changed ···{Previous} -> ···{Current}.",
                    account.Id, account.Provider, previousLast4, account.AccountNumberLast4);
        }

        return true;
    }
}
