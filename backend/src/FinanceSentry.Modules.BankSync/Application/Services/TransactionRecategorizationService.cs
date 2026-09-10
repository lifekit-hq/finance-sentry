namespace FinanceSentry.Modules.BankSync.Application.Services;

using FinanceSentry.Core.Domain;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Infrastructure.Encryption;
using FinanceSentry.Modules.BankSync.Application.Services.CategoryMapping;
using FinanceSentry.Modules.BankSync.Domain;
using FinanceSentry.Modules.BankSync.Domain.Interfaces;
using FinanceSentry.Modules.BankSync.Domain.Repositories;
using FinanceSentry.Modules.BankSync.Infrastructure.Monobank;
using FinanceSentry.Modules.BankSync.Infrastructure.TrueLayer;
using Microsoft.Extensions.Logging;

/// <summary>Outcome of a re-categorization pass for one user.</summary>
public record RecategorizationResult(int Examined, int ReResolved, int ReFetchedUpdated, int StillUncategorized);

/// <summary>
/// Recategorizes a user's already-stored transactions after the category-taxonomy overhaul.
/// Rows ingested before the overhaul carry no raw MCC/PFC signal, so they cannot be
/// re-resolved in place — they need a provider re-fetch. Rows ingested afterwards keep the
/// raw signal and are re-resolved for free (also picks up later MCC-map edits).
/// </summary>
public interface ITransactionRecategorizationService
{
    Task<RecategorizationResult> RecategorizeUserAsync(Guid userId, CancellationToken ct = default);
}

/// <inheritdoc />
public class TransactionRecategorizationService(
    IBankAccountRepository accounts,
    ITransactionRepository transactions,
    ICredentialEncryptionService encryption,
    ITransactionDeduplicationService dedup,
    IBankProviderFactory providerFactory,
    IMonobankCredentialRepository monobankCredentials,
    IMonobankAdapter monobankAdapter,
    ITrueLayerConnectionRepository truelayerConnections,
    ITrueLayerClient truelayerClient,
    ITransactionCategorizer categorizer,
    TrueLayerCategoryMapper categoryMapper,
    IActiveSubscriptionsReader activeSubscriptions,
    ILogger<TransactionRecategorizationService> logger) : ITransactionRecategorizationService
{
    // Monobank statement API caps each request at 31 days and ~1 request per 60s per token.
    private const int MonobankWindowDays = 31;
    private static readonly TimeSpan MonobankThrottle = TimeSpan.FromSeconds(61);

    private readonly IBankAccountRepository _accounts = accounts;
    private readonly ITransactionRepository _transactions = transactions;
    private readonly ICredentialEncryptionService _encryption = encryption;
    private readonly ITransactionDeduplicationService _dedup = dedup;
    private readonly IBankProviderFactory _providerFactory = providerFactory;
    private readonly IMonobankCredentialRepository _monobankCredentials = monobankCredentials;
    private readonly IMonobankAdapter _monobankAdapter = monobankAdapter;
    private readonly ITrueLayerConnectionRepository _truelayerConnections = truelayerConnections;
    private readonly ITrueLayerClient _truelayerClient = truelayerClient;
    private readonly ITransactionCategorizer _categorizer = categorizer;
    private readonly TrueLayerCategoryMapper _categoryMapper = categoryMapper;
    private readonly IActiveSubscriptionsReader _activeSubscriptions = activeSubscriptions;
    private readonly ILogger<TransactionRecategorizationService> _logger = logger;

    // Guarantees ≥ MonobankThrottle spacing between statement calls across the whole run.
    private bool _monobankCalled;

    public async Task<RecategorizationResult> RecategorizeUserAsync(Guid userId, CancellationToken ct = default)
    {
        var userAccounts = (await _accounts.GetByUserIdAsync(userId, ct)).ToList();
        var userTransactions = (await _transactions.GetByUserIdAsync(userId, ct)).ToList();

        // Read once for the whole pass: the loan rule matches each row against the user's
        // active repayment plans, so a per-row lookup would be one query per transaction.
        var installmentPlans = await _activeSubscriptions.GetActiveInstallmentPlansAsync(userId, ct);

        var reResolved = ReResolveFromStoredSignal(userTransactions, installmentPlans);
        var reFetched = await ReFetchMissingSignalAsync(userAccounts, userTransactions, ct);

        await _transactions.SaveChangesAsync(ct);

        var stillUncategorized = userTransactions.Count(t =>
            t.MerchantCategory is null || t.MerchantCategory == CategoryKeys.Uncategorized);

        _logger.LogInformation(
            "Recategorization for user {UserId}: examined {Examined}, re-resolved {ReResolved}, re-fetched {ReFetched}, still uncategorized {Still}.",
            userId, userTransactions.Count, reResolved, reFetched, stillUncategorized);

        return new RecategorizationResult(userTransactions.Count, reResolved, reFetched, stillUncategorized);
    }

    /// <summary>Pass 1 — cheap in-process re-resolve for rows that already carry a raw signal.</summary>
    private int ReResolveFromStoredSignal(
        IReadOnlyList<Transaction> txns, IReadOnlyList<ActiveInstallmentPlan> installmentPlans)
    {
        var updated = 0;
        foreach (var t in txns)
        {
            var resolved = ResolveFromRaw(t, installmentPlans);
            if (resolved is not null && resolved != t.MerchantCategory)
            {
                t.MerchantCategory = resolved;
                updated++;
            }
        }
        return updated;
    }

    /// <summary>
    /// Pass 2 — provider re-fetch for rows with no stored raw signal (pre-overhaul rows).
    /// Monobank walks 31-day windows from each account's oldest uncategorized row to now
    /// (rate-limited); TrueLayer covers its ~90-day window.
    /// </summary>
    private async Task<int> ReFetchMissingSignalAsync(
        IReadOnlyList<BankAccount> userAccounts, IReadOnlyList<Transaction> txns, CancellationToken ct)
    {
        // Rows still lacking any structured signal AND not rescued by description matching in
        // pass 1. Description-matched rows already carry a real category, so skip the re-fetch.
        var missingByAccount = txns
            .Where(t => t.Mcc is null && string.IsNullOrWhiteSpace(t.SourceCategory))
            .Where(t => t.MerchantCategory is null || t.MerchantCategory == CategoryKeys.Uncategorized)
            .GroupBy(t => t.AccountId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var updated = 0;
        foreach (var account in userAccounts)
        {
            if (!missingByAccount.TryGetValue(account.Id, out var rows) || rows.Count == 0)
                continue;

            IReadOnlyList<TransactionCandidate> candidates;
            try
            {
                candidates = await FetchCandidatesAsync(account, rows, ct);
            }
            catch (Exception ex)
            {
                // Best-effort per account — one provider failing must not abort the rest.
                _logger.LogWarning(ex, "Recategorization re-fetch failed for account {AccountId} ({Provider}).",
                    account.Id, account.Provider);
                continue;
            }

            var byHash = new Dictionary<string, TransactionCandidate>();
            foreach (var c in candidates)
                byHash[_dedup.ComputeHash(c.AccountId, c.Amount, c.HashDate, c.Description)] = c;

            foreach (var t in rows)
            {
                if (!byHash.TryGetValue(t.UniqueHash, out var c))
                    continue;

                t.MerchantCategory = c.MerchantCategory;
                t.SourceCategory = c.SourceCategory;
                t.Mcc = c.Mcc;
                updated++;
            }
        }
        return updated;
    }

    // The same ladder the ingest adapters run, so a backfill can only ever confirm or correct an
    // ingest decision — never reverse one (#553). A null means no rule claimed the row, which
    // leaves it eligible for a provider re-fetch in pass 2.
    //
    // SourceCategory is written by the TrueLayer adapter alone and holds the provider's raw
    // wording, so it goes through the same mapper ingest used; handing the ladder the raw string
    // would fail canonical-key validation and silently drop the provider rung on this path only.
    private string? ResolveFromRaw(Transaction t, IReadOnlyList<ActiveInstallmentPlan> installmentPlans) =>
        _categorizer.Categorize(
            new CategorizationSignals(
                Description: t.Description,
                MerchantName: t.MerchantName,
                TransactionType: t.TransactionType,
                Amount: t.Amount,
                Mcc: t.Mcc,
                ProviderCategory: _categoryMapper.MapStored(t.SourceCategory)),
            installmentPlans);

    private async Task<IReadOnlyList<TransactionCandidate>> FetchCandidatesAsync(
        BankAccount account, IReadOnlyList<Transaction> rows, CancellationToken ct)
    {
        return account.Provider switch
        {
            "monobank" => await FetchMonobankAsync(account, rows, ct),
            "truelayer" => await FetchTrueLayerAsync(account, ct),
            _ => throw new InvalidOperationException(
                $"Unknown provider '{account.Provider}' for account {account.Id}."),
        };
    }

    private async Task<IReadOnlyList<TransactionCandidate>> FetchMonobankAsync(
        BankAccount account, IReadOnlyList<Transaction> rows, CancellationToken ct)
    {
        if (account.MonobankCredentialId is null)
            throw new InvalidOperationException($"Monobank account {account.Id} has no credential id.");

        var cred = await _monobankCredentials.GetByIdAsync(account.MonobankCredentialId.Value, ct)
            ?? throw new InvalidOperationException($"Monobank credential {account.MonobankCredentialId} not found.");
        var token = _encryption.Decrypt(cred.EncryptedToken, cred.Iv, cred.AuthTag, cred.KeyVersion);

        // Cover the full span of this account's uncategorized rows, oldest → now, in ≤31-day
        // windows (Monobank's per-request cap), spaced to respect the per-token rate limit.
        var oldest = rows.Min(t => t.PostedDate ?? t.TransactionDate);
        var windowStart = new DateTimeOffset(DateTime.SpecifyKind(oldest.AddDays(-1), DateTimeKind.Utc));
        var now = DateTimeOffset.UtcNow;

        var all = new List<TransactionCandidate>();
        while (windowStart < now)
        {
            var windowEnd = windowStart.AddDays(MonobankWindowDays);
            if (windowEnd > now)
                windowEnd = now;

            if (_monobankCalled)
                await Task.Delay(MonobankThrottle, ct);
            _monobankCalled = true;

            var candidates = await _monobankAdapter.GetCandidatesAsync(
                token, account.ExternalAccountId, account.Id, account.UserId, windowStart, windowEnd, ct);
            all.AddRange(candidates);

            _logger.LogInformation(
                "Monobank window {From:yyyy-MM-dd}..{To:yyyy-MM-dd} for account {AccountId}: {Count} transactions.",
                windowStart, windowEnd, account.Id, candidates.Count);

            windowStart = windowEnd;
        }
        return all;
    }

    private async Task<IReadOnlyList<TransactionCandidate>> FetchTrueLayerAsync(BankAccount account, CancellationToken ct)
    {
        if (account.TrueLayerConnectionId is null)
            throw new InvalidOperationException($"TrueLayer account {account.Id} has no connection id.");

        var connection = await _truelayerConnections.GetByIdAsync(account.TrueLayerConnectionId.Value, ct)
            ?? throw new InvalidOperationException($"TrueLayer connection {account.TrueLayerConnectionId} not found.");
        var refreshToken = _encryption.Decrypt(
            connection.EncryptedRefreshToken, connection.Iv, connection.AuthTag, connection.KeyVersion);

        var tokenSet = await _truelayerClient.RefreshAccessTokenAsync(refreshToken, ct);

        // TrueLayer rotates refresh tokens — persist the new one so later syncs keep working.
        if (!string.IsNullOrEmpty(tokenSet.RefreshToken) && tokenSet.RefreshToken != refreshToken)
        {
            var encrypted = _encryption.Encrypt(tokenSet.RefreshToken);
            connection.SetRefreshToken(encrypted.Ciphertext, encrypted.Iv, encrypted.AuthTag, encrypted.KeyVersion);
            await _truelayerConnections.UpdateAsync(connection, ct);
        }

        var (candidates, _) = await _providerFactory.Resolve("truelayer")
            .SyncTransactionsAsync(tokenSet.AccessToken, account.ExternalAccountId, account.Id, account.UserId, null, ct);
        return candidates;
    }
}
