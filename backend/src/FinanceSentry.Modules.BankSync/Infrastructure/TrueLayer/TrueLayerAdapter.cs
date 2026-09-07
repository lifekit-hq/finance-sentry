namespace FinanceSentry.Modules.BankSync.Infrastructure.TrueLayer;

using FinanceSentry.Core.Domain;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.BankSync.Application.Services;
using FinanceSentry.Modules.BankSync.Application.Services.CategoryMapping;
using FinanceSentry.Modules.BankSync.Domain.Interfaces;

/// <summary>
/// Implements IBankProvider for TrueLayer. The credential argument on every method
/// is an OAuth access_token — the caller (ScheduledSyncService) is responsible for
/// exchanging the per-connection refresh_token for a fresh access_token before
/// invoking this adapter.
/// </summary>
public class TrueLayerAdapter(
    ITrueLayerClient client,
    TrueLayerCategoryMapper categoryMapper,
    ITransactionCategorizer categorizer,
    IActiveSubscriptionsReader activeSubscriptions) : IBankProvider
{
    /// <summary>
    /// ProductType marker distinguishing a /data/v1/cards credit card from a regular
    /// /data/v1/accounts account — the two live behind different endpoint families, so
    /// every later balance/transaction call must route on it.
    /// </summary>
    public const string CardProductType = "card";

    private const int InitialSyncWindowDays = 90;

    /// <summary>
    /// Trailing overlap re-fetched on every incremental sync. A transaction that settles
    /// keeps its original timestamp, so a pure watermark fetch never re-observes the
    /// posted version once the watermark passes it — the stored pending row would linger
    /// forever. Re-reading the last few days lets the pending reconciler (and same-hash
    /// settle-in-place) clear it; dedup makes the overlap idempotent.
    /// </summary>
    private const int ResyncLookbackDays = 7;

    private readonly TrueLayerCategoryMapper _categoryMapper = categoryMapper;
    private readonly ITransactionCategorizer _categorizer = categorizer;
    private readonly IActiveSubscriptionsReader _activeSubscriptions = activeSubscriptions;

    public string ProviderName => "truelayer";

    public async Task<IReadOnlyList<BankAccountInfo>> GetAccountsAsync(string credential, CancellationToken ct = default)
    {
        var accounts = await client.ListAccountsAsync(credential, ct);
        var result = new List<BankAccountInfo>(accounts.Count);

        foreach (var a in accounts)
        {
            decimal? currentBalance = null;
            try
            {
                var balance = await client.GetBalanceAsync(credential, a.AccountId, ct);
                currentBalance = balance?.Current;
            }
            catch (TrueLayerException)
            {
                // Balance is best-effort; don't fail account creation on a single 4xx.
            }

            result.Add(new BankAccountInfo(
                ExternalAccountId: a.AccountId,
                Name: !string.IsNullOrWhiteSpace(a.DisplayName) ? a.DisplayName : a.ProviderName,
                AccountType: a.AccountType,
                AccountNumberLast4: a.AccountNumberLast4,
                CurrentBalance: currentBalance,
                Currency: a.Currency,
                OwnerName: string.Empty));
        }

        return result;
    }

    /// <summary>
    /// Lists the connection's credit cards (from /data/v1/cards) as account infos with the
    /// liability convention: AccountType "credit", CurrentBalance = amount owed, ProductType
    /// <see cref="CardProductType"/>. Providers without card support throw — callers treat
    /// that as "no cards".
    /// </summary>
    public async Task<IReadOnlyList<BankAccountInfo>> GetCardsAsync(string credential, CancellationToken ct = default)
    {
        var cards = await client.ListCardsAsync(credential, ct);
        var result = new List<BankAccountInfo>(cards.Count);

        foreach (var c in cards)
        {
            decimal? owed = null;
            decimal? creditLimit = null;
            try
            {
                var balance = await client.GetCardBalanceAsync(credential, c.AccountId, ct);
                owed = balance?.Current;
                creditLimit = balance?.CreditLimit;
            }
            catch (TrueLayerException)
            {
                // Balance is best-effort; don't fail card discovery on a single 4xx.
            }

            result.Add(new BankAccountInfo(
                ExternalAccountId: c.AccountId,
                Name: !string.IsNullOrWhiteSpace(c.DisplayName) ? c.DisplayName : c.ProviderName,
                AccountType: "credit",
                AccountNumberLast4: c.AccountNumberLast4,
                CurrentBalance: owed,
                Currency: c.Currency,
                OwnerName: string.Empty,
                ProductType: CardProductType,
                CreditLimit: creditLimit));
        }

        return result;
    }

    public Task<(IReadOnlyList<TransactionCandidate> Candidates, DateTime? NextSyncFrom)> SyncTransactionsAsync(
        string credential, string externalAccountId, Guid accountId, Guid userId,
        DateTime? since, CancellationToken ct = default)
        => SyncCoreAsync(credential, externalAccountId, accountId, userId, since, isCard: false, ct);

    /// <summary>
    /// Same sync flow as <see cref="SyncTransactionsAsync"/> but against the card endpoint
    /// family (/data/v1/cards/{id}/transactions[/pending]).
    /// </summary>
    public Task<(IReadOnlyList<TransactionCandidate> Candidates, DateTime? NextSyncFrom)> SyncCardTransactionsAsync(
        string credential, string externalAccountId, Guid accountId, Guid userId,
        DateTime? since, CancellationToken ct = default)
        => SyncCoreAsync(credential, externalAccountId, accountId, userId, since, isCard: true, ct);

    private async Task<(IReadOnlyList<TransactionCandidate> Candidates, DateTime? NextSyncFrom)> SyncCoreAsync(
        string credential, string externalAccountId, Guid accountId, Guid userId,
        DateTime? since, bool isCard, CancellationToken ct)
    {
        // Read once for the whole sync: the loan rule matches each row against the user's active
        // repayment plans, so a per-row lookup would be one query per transaction.
        var installmentPlans = await _activeSubscriptions.GetActiveInstallmentPlansAsync(userId, ct);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var overlapFrom = today.AddDays(-ResyncLookbackDays);
        var watermarkFrom = since.HasValue
            ? DateOnly.FromDateTime(since.Value.Date)
            : today.AddDays(-InitialSyncWindowDays);
        var from = watermarkFrom < overlapFrom ? watermarkFrom : overlapFrom;

        var booked = isCard
            ? await client.GetCardTransactionsAsync(credential, externalAccountId, from, today, ct)
            : await client.GetTransactionsAsync(credential, externalAccountId, from, today, ct);
        var pending = isCard
            ? await client.GetCardPendingTransactionsAsync(credential, externalAccountId, ct)
            : await client.GetPendingTransactionsAsync(credential, externalAccountId, ct);

        var candidates = booked
            .Concat(pending)
            .Select(MapTransaction)
            .ToList();

        return (candidates, DateTime.UtcNow);

        TransactionCandidate MapTransaction(TrueLayerTransaction t)
        {
            var amount = Math.Abs(t.Amount);
            var txType = t.Amount < 0 || t.TransactionType == "debit" ? "debit" : "credit";

            // TrueLayer's own classification enters the shared ladder as the provider category —
            // many EU banks return it empty, and the ladder's description rules then decide.
            var category = _categorizer.Categorize(
                new CategorizationSignals(
                    Description: t.Description,
                    MerchantName: t.MerchantName,
                    TransactionType: txType,
                    Amount: amount,
                    ProviderCategory: _categoryMapper.Map(t.Classification)),
                installmentPlans)
                ?? CategoryKeys.Uncategorized;

            // Persist the raw classification (when present) so a later re-map is traceable.
            var sourceCategory = TrueLayerCategoryMapper.ToSourceCategory(t.Classification);

            return new TransactionCandidate(
                AccountId: accountId,
                UserId: userId,
                Amount: amount,
                TransactionDate: t.Timestamp,
                PostedDate: t.IsPending ? null : t.Timestamp,
                Description: t.Description,
                IsPending: t.IsPending,
                TransactionType: txType,
                MerchantName: t.MerchantName,
                MerchantCategory: category,
                SourceCategory: sourceCategory);
        }
    }

    public Task DisconnectAsync(string credential, CancellationToken ct = default)
        => Task.CompletedTask;
}
