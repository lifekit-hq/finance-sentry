namespace FinanceSentry.Modules.BankSync.Application.Services;

using System.Collections.Concurrent;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Infrastructure.Encryption;
using FinanceSentry.Infrastructure.Logging;
using FinanceSentry.Modules.BankSync.Domain;
using FinanceSentry.Modules.BankSync.Domain.Events;
using FinanceSentry.Modules.BankSync.Domain.Interfaces;
using FinanceSentry.Modules.BankSync.Domain.Repositories;
using FinanceSentry.Modules.BankSync.Infrastructure.Monobank;
using FinanceSentry.Modules.BankSync.Infrastructure.TrueLayer;

/// <summary>
/// Result returned by a sync operation.
/// </summary>
public record SyncResult(
    bool Success,
    int TransactionCountFetched,
    int TransactionCountDeduped,
    string? ErrorCode,
    string? ErrorMessage);

/// <summary>
/// Drives the full transaction sync lifecycle for a single account:
/// create job → decrypt token → fetch from provider → deduplicate → persist → update account state.
/// Supports Monobank and TrueLayer (both timestamp-based) providers.
/// </summary>
public interface IScheduledSyncService
{
    Task<SyncResult> PerformFullSyncAsync(
        Guid accountId,
        CancellationToken ct = default,
        string? preAcquiredTrueLayerAccessToken = null);
}

/// <inheritdoc />
public class ScheduledSyncService(
    IBankAccountRepository accounts,
    ITransactionRepository transactions,
    ISyncJobRepository syncJobs,
    ICredentialEncryptionService encryption,
    ITransactionDeduplicationService dedup,
    IBankSyncLogger logger,
    IBankProviderFactory providerFactory,
    IMonobankCredentialRepository monobankCredentials,
    ITrueLayerConnectionRepository truelayerConnections,
    ITrueLayerClient truelayerClient,
    MonobankBalanceCache monobankBalanceCache,
    IAlertGeneratorService alerts,
    IUserAlertPreferencesReader userPreferences,
    IEventBus eventBus) : IScheduledSyncService
{
    private readonly IBankAccountRepository _accounts = accounts;
    private readonly ITransactionRepository _transactions = transactions;
    private readonly ISyncJobRepository _syncJobs = syncJobs;
    private readonly ICredentialEncryptionService _encryption = encryption;
    private readonly ITransactionDeduplicationService _dedup = dedup;
    private readonly IBankSyncLogger _logger = logger;
    private readonly IBankProviderFactory _providerFactory = providerFactory;
    private readonly IMonobankCredentialRepository _monobankCredentials = monobankCredentials;
    private readonly ITrueLayerConnectionRepository _truelayerConnections = truelayerConnections;
    private readonly ITrueLayerClient _truelayerClient = truelayerClient;
    private readonly MonobankBalanceCache _monobankBalanceCache = monobankBalanceCache;
    private readonly IAlertGeneratorService _alerts = alerts;
    private readonly IUserAlertPreferencesReader _userPreferences = userPreferences;
    private readonly IEventBus _eventBus = eventBus;

    /// <summary>
    /// Error codes that represent a transient, self-healing condition (provider rate-limit /
    /// 429). These must not mark the account failed or fire a SyncFailure alert — the next
    /// scheduled cycle retries and clears the state on its own.
    /// </summary>
    private static readonly HashSet<string> TransientErrorCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "MONOBANK_RATE_LIMITED",
        "RATE_LIMIT_EXCEEDED",
    };

    /// <inheritdoc />
    public async Task<SyncResult> PerformFullSyncAsync(
          Guid accountId,
          CancellationToken ct = default,
          string? preAcquiredTrueLayerAccessToken = null)
    {
        var startedAt = DateTime.UtcNow;

        var account = await _accounts.GetByIdAsync(accountId, ct);
        if (account == null)
            return new SyncResult(false, 0, 0, "ACCOUNT_NOT_FOUND", "Account not found.");

        var job = new SyncJob(accountId, account.UserId)
        {
            Status = "running",
            StartedAt = startedAt
        };
        await _syncJobs.AddAsync(job, ct);

        _logger.SyncStarted(job.CorrelationId ?? job.Id.ToString(), accountId);

        try
        {
            account.BeginSync();
            await _accounts.UpdateAsync(account, ct);

            SyncResult result;

            if (account.Provider == "monobank")
                result = await SyncMonobankAsync(account, job, startedAt, ct);
            else if (account.Provider == "truelayer")
                result = await SyncTrueLayerAsync(account, job, startedAt, ct, preAcquiredTrueLayerAccessToken);
            else
                throw new InvalidOperationException(
                    $"Unknown provider '{account.Provider}' for account {account.Id}.");

            await EvaluateAlertsAfterSuccessAsync(account, ct);
            await PublishSyncCompletedAsync(account, result, ct);

            return result;
        }
        catch (Exception ex)
        {
            var errorCode = ExtractErrorCode(ex.Message, account.Provider);
            var isTransient = errorCode is not null && TransientErrorCodes.Contains(errorCode);

            job.MarkFailed(ex.Message, errorCode);
            await _syncJobs.UpdateAsync(job, ct);

            try
            {
                var freshAccount = await _accounts.GetByIdAsync(accountId, ct);
                if (freshAccount != null)
                {
                    if (errorCode is "ITEM_LOGIN_REQUIRED" or "MONOBANK_TOKEN_INVALID")
                        freshAccount.MarkReauthRequired();
                    else if (isTransient && freshAccount.SyncStatus == "syncing")
                        freshAccount.MarkTransientRetry();
                    else if (freshAccount.SyncStatus == "syncing")
                        freshAccount.MarkFailed(errorCode);

                    await _accounts.UpdateAsync(freshAccount, ct);
                }
            }
            catch
            {
                // best-effort
            }

            _logger.SyncFailed(job.CorrelationId ?? job.Id.ToString(), accountId,
                errorCode ?? "UNKNOWN", ex.Message, job.RetryCount);

            // Transient throttling (provider 429) is self-healing — the next scheduled
            // cycle retries. Surfacing a "reconnect your credentials" alert here would be
            // a false alarm, so we skip it. A genuine failure still alerts the user.
            if (!isTransient)
                await EvaluateSyncFailureAlertAsync(account, errorCode, ct);

            return new SyncResult(false, 0, 0, errorCode, ex.Message);
        }
    }

    /// <summary>
    /// Publishes <see cref="AccountSyncCompletedEvent"/> after a successful sync. This is
    /// what drives the intraday net-worth snapshot refresh (FirstSyncSnapshotTrigger) —
    /// the event was defined and handled but never published, so the trigger was dead
    /// code and the day's snapshot stayed frozen at the 01:00 UTC backstop run.
    /// Best-effort: a downstream reaction must not fail the sync.
    /// </summary>
    private async Task PublishSyncCompletedAsync(Domain.BankAccount account, SyncResult result, CancellationToken ct)
    {
        try
        {
            await _eventBus.Publish(new AccountSyncCompletedEvent(
                account.Id, account.UserId, account.Provider, "success",
                result.TransactionCountFetched, account.CurrentBalance, null, null), ct);
        }
        catch
        {
            // best-effort
        }
    }

    private async Task EvaluateAlertsAfterSuccessAsync(Domain.BankAccount account, CancellationToken ct)
    {
        try
        {
            var prefs = await _userPreferences.GetAsync(account.UserId, ct);
            if (prefs is null) return;

            if (prefs.SyncFailureAlerts)
                await _alerts.ResolveSyncFailureAlertAsync(account.UserId, account.Provider, account.Id, ct);

            if (prefs.LowBalanceAlerts && account.CurrentBalance.HasValue)
            {
                var balance = account.CurrentBalance.Value;
                if (balance < prefs.LowBalanceThreshold)
                {
                    await _alerts.GenerateLowBalanceAlertAsync(
                        account.UserId, account.Id, account.BankName,
                        balance, prefs.LowBalanceThreshold, ct);
                }
                else
                {
                    await _alerts.ResolveLowBalanceAlertAsync(account.UserId, account.Id, ct);
                }
            }
        }
        catch
        {
            // best-effort: alert generation failure must not break sync
        }
    }

    private async Task EvaluateSyncFailureAlertAsync(Domain.BankAccount account, string? errorCode, CancellationToken ct)
    {
        try
        {
            var prefs = await _userPreferences.GetAsync(account.UserId, ct);
            if (prefs is null || !prefs.SyncFailureAlerts) return;

            await _alerts.GenerateSyncFailureAlertAsync(
                account.UserId, account.Provider, account.Id, account.BankName, errorCode, ct);
        }
        catch
        {
            // best-effort
        }
    }

    private async Task<SyncResult> SyncMonobankAsync(
        Domain.BankAccount account, SyncJob job, DateTime startedAt, CancellationToken ct)
    {
        if (account.MonobankCredentialId is null)
            throw new InvalidOperationException($"Monobank account {account.Id} has no credential id.");

        var cred = await _monobankCredentials.GetByIdAsync(account.MonobankCredentialId.Value, ct)
            ?? throw new InvalidOperationException($"Monobank credential {account.MonobankCredentialId} not found.");

        var plainToken = _encryption.Decrypt(cred.EncryptedToken, cred.Iv, cred.AuthTag, cred.KeyVersion);
        _logger.CredentialAccessed(job.CorrelationId ?? job.Id.ToString(), account.Id);

        var provider = _providerFactory.Resolve("monobank");

        // Per-account watermark, NOT cred.LastSyncAt: the credential is shared by every card
        // on the token, so a shared timestamp lets the first-synced card starve the others'
        // fetch windows (cards added later never received their initial history import).
        var since = account.LastTransactionSyncAt;
        var (candidates, _) = await provider.SyncTransactionsAsync(
            plainToken, account.ExternalAccountId, account.Id, account.UserId, since, ct);

        var entities = await PersistAndReconcileAsync(account.Id, candidates, ct);

        account.LastTransactionSyncAt = DateTime.UtcNow;

        // T031: update last sync timestamp on credential (kept for display/reaper purposes)
        cred.LastSyncAt = DateTime.UtcNow;
        await _monobankCredentials.UpdateAsync(cred, ct);

        // Refresh live balance from Monobank /personal/client-info. The endpoint
        // is rate-limited to one call per 60s per token, so we check the shared
        // MonobankBalanceCache first (primed by whichever sibling account synced
        // first this cycle) and only call the API when the cache is cold. On
        // rate-limit / transient failure we keep the prior balance rather than
        // zeroing it.
        var freshSelf = _monobankBalanceCache.TryGet(plainToken, account.ExternalAccountId);
        if (freshSelf is null)
        {
            try
            {
                var freshAccounts = await provider.GetAccountsAsync(plainToken, ct);
                foreach (var fa in freshAccounts)
                    _monobankBalanceCache.Set(plainToken, fa.ExternalAccountId, fa);
                freshSelf = freshAccounts.FirstOrDefault(a => a.ExternalAccountId == account.ExternalAccountId);
            }
            catch (Infrastructure.Monobank.MonobankException)
            {
                // Rate limit or transient — leave the prior balance in place.
            }
        }

        if (freshSelf is not null)
        {
            // Backfill / refresh the card product type (black/white/…) from client-info so
            // pre-existing accounts (connected before ProductType was captured) get grouped,
            // and heal the account type + credit limit so a card that gained (or always had)
            // a credit line flips to the liability convention instead of counting its limit
            // as own money.
            if (freshSelf.ProductType is { Length: > 0 } productType && account.ProductType != productType)
                account.ProductType = productType;
            if (!string.IsNullOrEmpty(freshSelf.AccountType) && account.AccountType != freshSelf.AccountType)
                account.AccountType = freshSelf.AccountType;
            account.CreditLimit = freshSelf.CreditLimit;
        }

        account.MarkActive(freshSelf?.CurrentBalance ?? account.CurrentBalance ?? 0m);
        await _accounts.UpdateAsync(account, ct);

        var lastTxDate = entities.Count > 0
            ? entities.Max(t => t.PostedDate ?? t.TransactionDate)
            : (DateTime?)null;

        job.MarkSuccess(candidates.Count, entities.Count, lastTxDate);
        await _syncJobs.UpdateAsync(job, ct);

        var durationMs = (long)(DateTime.UtcNow - startedAt).TotalMilliseconds;
        _logger.SyncCompleted(job.CorrelationId ?? job.Id.ToString(), account.Id,
            candidates.Count, entities.Count, durationMs);

        return new SyncResult(true, candidates.Count, entities.Count, null, null);
    }

    // Serializes the refresh-token exchange per TrueLayer connection. A connection can back several
    // accounts, each with its own scheduled sync job firing on the same cron; without this gate two
    // jobs could refresh the shared, rotating refresh_token concurrently — one wins, the other gets
    // invalid_grant and the rotated token is lost, bricking the connection.
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> TrueLayerRefreshLocks = new();

    private async Task<SyncResult> SyncTrueLayerAsync(
        Domain.BankAccount account, SyncJob job, DateTime startedAt, CancellationToken ct,
        string? preAcquiredAccessToken = null)
    {
        if (account.TrueLayerConnectionId is null)
            throw new InvalidOperationException($"TrueLayer account {account.Id} has no connection id.");

        var connectionId = account.TrueLayerConnectionId.Value;
        // At reconnect the caller passes the freshly-exchanged access token, which still carries an
        // active SCA session — the only token that can pull transaction history from strict banks
        // (e.g. AIB). A refreshed background token cannot, so fall back to it only when none given.
        var accessToken = preAcquiredAccessToken
            ?? await AcquireTrueLayerAccessTokenAsync(connectionId, job, account.Id, ct);

        var connection = await _truelayerConnections.GetByIdAsync(connectionId, ct)
            ?? throw new InvalidOperationException($"TrueLayer connection {connectionId} not found.");

        var provider = _providerFactory.Resolve("truelayer");

        // Pick up credit cards added (or newly supported) after the original consent —
        // without this, a card only ever appears through an explicit reconnect.
        await DiscoverTrueLayerCardsAsync(account, connectionId, accessToken, provider, ct);
        // Per-account watermark, NOT connection.LastSyncAt: a connection can back several
        // accounts, and a shared timestamp lets the first-synced account starve the others'
        // fetch windows (accounts added later never received their initial history import).
        var since = account.LastTransactionSyncAt;

        // Credit cards live behind the /data/v1/cards endpoint family — the accounts
        // endpoints return 404 for them, so both transactions and balance must route on it.
        var isCard = account.ProductType == TrueLayerAdapter.CardProductType;

        IReadOnlyList<Domain.Transaction> entities;
        int candidateCount;
        try
        {
            var (candidates, _) = isCard && provider is TrueLayerAdapter cardAdapter
                ? await cardAdapter.SyncCardTransactionsAsync(
                    accessToken, account.ExternalAccountId, account.Id, account.UserId, since, ct)
                : await provider.SyncTransactionsAsync(
                    accessToken, account.ExternalAccountId, account.Id, account.UserId, since, ct);

            entities = await PersistAndReconcileAsync(account.Id, candidates, ct);
            candidateCount = candidates.Count;

            account.LastTransactionSyncAt = DateTime.UtcNow;

            // Kept for display/reaper purposes; no longer drives fetch windows.
            connection.LastSyncAt = DateTime.UtcNow;
            await _truelayerConnections.UpdateAsync(connection, ct);
        }
        catch (Infrastructure.TrueLayer.TrueLayerException ex)
            when (ex.StatusCode == 403 && ex.Message.Contains("access_denied", StringComparison.OrdinalIgnoreCase))
        {
            // Strong Customer Authentication wall: some banks (notably AIB) deny background
            // transaction access made with a refreshed token — only a user-present flow can pull
            // history. Balance access still works, so degrade to a balance-only sync instead of
            // hard-failing, which would otherwise flap the account to "failed" every cycle.
            // LastSyncAt is deliberately not advanced so a later user-present sync can still
            // backfill transactions from the same point.
            entities = [];
            candidateCount = 0;
        }

        // Refresh the live balance — TrueLayer balances can move independently of
        // dedup'd transactions (e.g. an overdraft on AIB), so re-query. On failure or an
        // empty result keep the prior balance (matching the Monobank path) — zeroing it
        // would record a phantom drop in every consumer, net-worth snapshots included.
        decimal? latestBalance = null;
        try
        {
            if (isCard)
            {
                var cardBal = await _truelayerClient.GetCardBalanceAsync(accessToken, account.ExternalAccountId, ct);
                // Card "current" is the outstanding amount owed — exactly the stored
                // convention for credit accounts.
                latestBalance = cardBal?.Current;
                if (cardBal?.CreditLimit is not null)
                    account.CreditLimit = cardBal.CreditLimit;
            }
            else
            {
                var bal = await _truelayerClient.GetBalanceAsync(accessToken, account.ExternalAccountId, ct);
                latestBalance = bal?.Current;
            }
        }
        catch (Infrastructure.TrueLayer.TrueLayerException)
        {
            // Best-effort — keep the prior balance if the balance endpoint trips.
        }

        account.MarkActive(latestBalance ?? account.CurrentBalance ?? 0m);
        await _accounts.UpdateAsync(account, ct);

        var lastTxDate = entities.Count > 0
            ? entities.Max(t => t.PostedDate ?? t.TransactionDate)
            : (DateTime?)null;

        job.MarkSuccess(candidateCount, entities.Count, lastTxDate);
        await _syncJobs.UpdateAsync(job, ct);

        var durationMs = (long)(DateTime.UtcNow - startedAt).TotalMilliseconds;
        _logger.SyncCompleted(job.CorrelationId ?? job.Id.ToString(), account.Id,
            candidateCount, entities.Count, durationMs);

        return new SyncResult(true, candidateCount, entities.Count, null, null);
    }

    // Card discovery is one extra API call per connection; once a day is plenty — a new
    // card appearing within 24h (or instantly via reconnect) is fine. The stamp is claimed
    // before the call so parallel sibling-account syncs don't duplicate it.
    private static readonly ConcurrentDictionary<Guid, DateTime> CardDiscoveryStamps = new();
    private static readonly TimeSpan CardDiscoveryInterval = TimeSpan.FromHours(24);

    /// <summary>
    /// Lists the connection's credit cards (TrueLayer serves them under /data/v1/cards only)
    /// and creates a BankAccount for any card not yet known, under the same bank name so it
    /// groups with the institution's existing accounts. The recurring SyncScheduler picks the
    /// new account up on its next pass (every 10 minutes).
    /// </summary>
    private async Task DiscoverTrueLayerCardsAsync(
        Domain.BankAccount account, Guid connectionId, string accessToken,
        IBankProvider provider, CancellationToken ct)
    {
        if (provider is not TrueLayerAdapter adapter)
            return;

        var now = DateTime.UtcNow;
        var last = CardDiscoveryStamps.GetOrAdd(connectionId, DateTime.MinValue);
        if (now - last < CardDiscoveryInterval || !CardDiscoveryStamps.TryUpdate(connectionId, now, last))
            return;

        try
        {
            var cards = await adapter.GetCardsAsync(accessToken, ct);
            foreach (var card in cards)
            {
                var existing = await _accounts.GetByExternalAccountIdAsync(card.ExternalAccountId, ct);
                if (existing is not null)
                    continue;

                var cardAccount = new Domain.BankAccount(
                    userId: account.UserId,
                    externalAccountId: card.ExternalAccountId,
                    bankName: account.BankName,
                    accountType: "credit",
                    accountNumberLast4: card.AccountNumberLast4,
                    ownerName: string.Empty,
                    currency: card.Currency,
                    createdBy: account.UserId,
                    provider: "truelayer")
                {
                    TrueLayerConnectionId = connectionId,
                    CurrentBalance = card.CurrentBalance,
                    CreditLimit = card.CreditLimit,
                    ProductType = TrueLayerAdapter.CardProductType
                };

                await _accounts.AddAsync(cardAccount, ct);
            }
        }
        catch (Infrastructure.TrueLayer.TrueLayerException)
        {
            // Provider without card support (or a transient /cards failure) — not an error.
        }
    }

    /// <summary>
    /// Exchanges a connection's rotating refresh_token for a fresh access_token, serialized per
    /// connection. The rotated refresh_token is persisted <em>immediately</em> — before any transaction
    /// fetch — so a later sync failure cannot strand a consumed token and permanently brick the
    /// connection (the invalid_grant root cause). The connection is re-read inside the lock so parallel
    /// per-account jobs always refresh from the latest persisted token.
    /// </summary>
    private async Task<string> AcquireTrueLayerAccessTokenAsync(
        Guid connectionId, SyncJob job, Guid accountId, CancellationToken ct)
    {
        var gate = TrueLayerRefreshLocks.GetOrAdd(connectionId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            var connection = await _truelayerConnections.GetByIdAsync(connectionId, ct)
                ?? throw new InvalidOperationException($"TrueLayer connection {connectionId} not found.");

            var refreshToken = _encryption.Decrypt(
                connection.EncryptedRefreshToken, connection.Iv, connection.AuthTag, connection.KeyVersion);
            _logger.CredentialAccessed(job.CorrelationId ?? job.Id.ToString(), accountId);

            var tokenSet = await _truelayerClient.RefreshAccessTokenAsync(refreshToken, ct);

            // Persist the rotated refresh_token now — not after the sync — so nothing downstream can lose it.
            if (!string.IsNullOrEmpty(tokenSet.RefreshToken) && tokenSet.RefreshToken != refreshToken)
            {
                var encrypted = _encryption.Encrypt(tokenSet.RefreshToken);
                connection.SetRefreshToken(encrypted.Ciphertext, encrypted.Iv, encrypted.AuthTag, encrypted.KeyVersion);
                await _truelayerConnections.UpdateAsync(connection, ct);
            }

            return tokenSet.AccessToken;
        }
        finally
        {
            gate.Release();
        }
    }

    private static string? ExtractErrorCode(string message, string provider)
    {
        if (provider == "monobank")
        {
            if (message.Contains("MONOBANK_TOKEN_INVALID", StringComparison.OrdinalIgnoreCase))
                return "MONOBANK_TOKEN_INVALID";
            if (message.Contains("MONOBANK_RATE_LIMITED", StringComparison.OrdinalIgnoreCase))
                return "MONOBANK_RATE_LIMITED";
            return null;
        }

        if (provider == "truelayer")
        {
            // Expired/revoked consent is a re-consent condition, not a transient failure. Map it to the
            // canonical reauth code so the account is flagged for reconnection and the scheduler stops
            // retrying it every cycle (instead of logging errorCode=UNKNOWN forever).
            if (message.Contains("invalid_grant", StringComparison.OrdinalIgnoreCase)
                || message.Contains("consent has expired", StringComparison.OrdinalIgnoreCase)
                || message.Contains("has been revoked", StringComparison.OrdinalIgnoreCase))
                return "ITEM_LOGIN_REQUIRED";
        }

        string[] knownCodes =
        [
            "ITEM_LOGIN_REQUIRED",
            "RATE_LIMIT_EXCEEDED",
            "INVALID_REQUEST",
            "SERVER_ERROR",
            "INTERNAL_SERVER_ERROR",
            "INVALID_CREDENTIALS",
            "PRODUCT_NOT_READY"
        ];

        foreach (var code in knownCodes)
        {
            if (message.Contains(code, StringComparison.OrdinalIgnoreCase))
                return code;
        }

        return null;
    }

    /// <summary>
    /// Persists newly-synced candidates (deduplicated) and then retires any existing pending
    /// row that now has a settled/posted twin, so pending transactions don't linger as stale
    /// duplicates once they clear. See <see cref="PendingReconciler"/>.
    /// </summary>
    private async Task<IReadOnlyList<Domain.Transaction>> PersistAndReconcileAsync(
        Guid accountId, IEnumerable<TransactionCandidate> candidates, CancellationToken ct)
    {
        var candidateList = candidates as IReadOnlyList<TransactionCandidate> ?? candidates.ToList();
        var existingRows = (await _transactions.GetByAccountIdAsync(accountId, ct)).ToList();
        // Dedup against ALL hashes — including soft-deleted rows, which still occupy the unique
        // (AccountId, UniqueHash) index — not just the active rows above. Missing a soft-deleted
        // hash here re-inserts it and violates the constraint, poisoning the whole batch.
        var existingHashes = (await _transactions.GetAllUniqueHashesByAccountIdAsync(accountId, ct)).ToHashSet();

        // Settle in place: a Monobank hold keeps its date when it clears, so the settled
        // version hashes identically to the stored pending row. Dedup then discards the
        // settled copy and the row would stay IsPending forever — flip it here instead.
        var pendingByHash = existingRows
            .Where(t => t.IsPending)
            .ToDictionary(t => t.UniqueHash);
        if (pendingByHash.Count > 0)
        {
            var settled = false;
            foreach (var candidate in candidateList.Where(c => !c.IsPending))
            {
                var hash = _dedup.ComputeHash(
                    candidate.AccountId, candidate.Amount, candidate.HashDate, candidate.Description);
                if (hash is not null && pendingByHash.TryGetValue(hash, out var row))
                {
                    row.IsPending = false;
                    row.PostedDate = candidate.PostedDate ?? row.PostedDate ?? row.TransactionDate;
                    settled = true;
                }
            }
            if (settled)
                await _transactions.SaveChangesAsync(ct);
        }

        var entities = _dedup.FilterDuplicates(candidateList, existingHashes)
            .Select(_dedup.ToEntity)
            // Guard against duplicate hashes *within* a single provider batch (e.g. a pending and
            // booked copy that hash alike) — FilterDuplicates only compares against existing rows.
            .GroupBy(e => e.UniqueHash)
            .Select(g => g.First())
            .ToList();
        if (entities.Count > 0)
            await _transactions.AddRangeAsync(entities, ct);

        var stale = PendingReconciler.SelectStalePending(existingRows, entities);
        if (stale.Count > 0)
        {
            var now = DateTime.UtcNow;
            foreach (var t in stale)
            {
                t.IsActive = false;
                t.DeletedAt = now;
            }
            await _transactions.SaveChangesAsync(ct);
        }

        return entities;
    }
}
