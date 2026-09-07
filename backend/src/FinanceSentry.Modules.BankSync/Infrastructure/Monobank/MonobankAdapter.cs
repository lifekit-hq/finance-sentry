namespace FinanceSentry.Modules.BankSync.Infrastructure.Monobank;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.BankSync.Application.Services;
using FinanceSentry.Modules.BankSync.Application.Services.CategoryMapping;
using FinanceSentry.Modules.BankSync.Domain.Interfaces;
using FinanceSentry.Modules.BankSync.Infrastructure.Categorization;

public class MonobankAdapter(
    MonobankHttpClient client,
    ICategoryResolver categoryResolver,
    IActiveSubscriptionsReader activeSubscriptions) : IMonobankAdapter, IBankProvider
{
    private readonly MonobankHttpClient _client = client;
    private readonly ICategoryResolver _categoryResolver = categoryResolver;
    private readonly IActiveSubscriptionsReader _activeSubscriptions = activeSubscriptions;

    /// <summary>Monobank rejects statement ranges longer than 31 days (+1h) with a 400.</summary>
    private const int MaxStatementWindowDays = 31;

    /// <summary>How far back the first-ever import of an account reaches.</summary>
    private const int InitialImportDays = 90;

    /// <summary>
    /// Trailing overlap re-fetched on every incremental sync. A hold keeps its original
    /// timestamp when it settles, so a pure watermark fetch never re-observes the settled
    /// version once the watermark passes it — the stored row would stay pending forever.
    /// Re-reading the last few days lets settle-in-place flip cleared holds; dedup makes
    /// the overlap idempotent.
    /// </summary>
    private const int ResyncLookbackDays = 7;

    public string ProviderName => "monobank";

    public async Task<IReadOnlyList<MonobankAccountInfo>> ConnectAsync(
        string token, CancellationToken ct = default)
    {
        var info = await _client.GetClientInfoAsync(token, ct);
        return info.Accounts;
    }

    public async Task<IReadOnlyList<MonobankAccountInfo>> GetAccountsAsync(
        string token, CancellationToken ct = default)
    {
        var info = await _client.GetClientInfoAsync(token, ct);
        return info.Accounts;
    }

    public Task<IReadOnlyList<MonobankTransaction>> GetStatementsAsync(
        string token, string accountId, DateTimeOffset from, DateTimeOffset to,
        CancellationToken ct = default)
        => _client.GetStatementsAsync(token, accountId, from, to, ct);

    public Task SetWebhookAsync(string token, string url, CancellationToken ct = default)
        => _client.SetWebhookAsync(token, url, ct);

    // ── IBankProvider ─────────────────────────────────────────────────────────

    async Task<IReadOnlyList<BankAccountInfo>> IBankProvider.GetAccountsAsync(
        string credential, CancellationToken ct)
    {
        var info = await _client.GetClientInfoAsync(credential, ct);
        return info.Accounts.Select(a => new BankAccountInfo(
            ExternalAccountId: a.Id,
            Name: a.Name,
            AccountType: a.Type,
            AccountNumberLast4: a.MaskedPan.Length >= 4
                ? a.MaskedPan[^4..] : a.MaskedPan.PadLeft(4, '0'),
            CurrentBalance: MonobankHttpClient.ToStoredBalance(a.Balance, a.CreditLimit),
            Currency: MonobankHttpClient.MapCurrency(a.CurrencyCode),
            OwnerName: info.Name,
            ProductType: a.ProductType,
            CreditLimit: a.CreditLimit > 0
                ? MonobankHttpClient.KopecksToDecimal(a.CreditLimit) : null)).ToList();
    }

    public async Task<(IReadOnlyList<TransactionCandidate> Candidates, DateTime? NextSyncFrom)> SyncTransactionsAsync(
        string credential, string externalAccountId, Guid accountId, Guid userId,
        DateTime? since, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var candidates = new List<TransactionCandidate>();
        var installmentPlans = await _activeSubscriptions.GetActiveInstallmentPlansAsync(userId, ct);

        var overlapStart = now.AddDays(-ResyncLookbackDays);
        var watermarkStart = since.HasValue
            ? new DateTimeOffset(since.Value.AddSeconds(1), TimeSpan.Zero)
            : now.AddDays(-InitialImportDays);
        var start = watermarkStart < overlapStart ? watermarkStart : overlapStart;

        // The statement endpoint rejects ranges longer than 31 days with a 400, so any
        // span — the 90-day initial import or an incremental catch-up after a long gap —
        // must be fetched as consecutive ≤31-day windows.
        for (var from = start; from < now; from = from.AddDays(MaxStatementWindowDays))
        {
            var to = from.AddDays(MaxStatementWindowDays) < now ? from.AddDays(MaxStatementWindowDays) : now;
            var txns = await _client.GetStatementsAsync(credential, externalAccountId, from, to, ct);
            candidates.AddRange(MapTransactions(txns, accountId, userId, installmentPlans));
        }

        return (candidates, DateTime.UtcNow);
    }

    Task IBankProvider.DisconnectAsync(string credential, CancellationToken ct)
        => Task.CompletedTask;

    public async Task<IReadOnlyList<TransactionCandidate>> GetCandidatesAsync(
        string token, string externalAccountId, Guid accountId, Guid userId,
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        var installmentPlans = await _activeSubscriptions.GetActiveInstallmentPlansAsync(userId, ct);
        var txns = await _client.GetStatementsAsync(token, externalAccountId, from, to, ct);
        return MapTransactions(txns, accountId, userId, installmentPlans).ToList();
    }

    private IEnumerable<TransactionCandidate> MapTransactions(
        IReadOnlyList<MonobankTransaction> txns, Guid accountId, Guid userId,
        IReadOnlyList<ActiveInstallmentPlan> installmentPlans)
    {
        return txns.Select(t =>
        {
            var amount = MonobankHttpClient.KopecksToDecimal(Math.Abs(t.Amount));
            var txType = t.Amount < 0 ? "debit" : "credit";
            var txDate = DateTimeOffset.FromUnixTimeSeconds(t.Time).UtcDateTime;
            return new TransactionCandidate(
                AccountId: accountId,
                UserId: userId,
                Amount: amount,
                TransactionDate: txDate,
                PostedDate: txDate,
                Description: t.Description,
                IsPending: t.Hold,
                TransactionType: txType,
                MerchantName: t.CounterName,
                MerchantCategory: ResolveCategory(t, txType, amount, installmentPlans),
                Mcc: t.MCC);
        });
    }

    // The runtime-editable keyword bridge stays in front so an admin keeps the last word on a
    // wording. Then loan/installment repayments: they carry the wire-transfer MCC 4829 and
    // would otherwise vanish into TRANSFER_OUT, and unlike a keyword the rule also reaches the
    // mortgage, whose description is a bare masked card number (#553). Then the
    // directional-transfer description (savings-jar "Поповнення «…»" / "З банки «…»" —
    // Monobank tags jars with the charity MCC 8398), then the MCC map for everything else.
    private string ResolveCategory(
        MonobankTransaction t, string transactionType, decimal amount,
        IReadOnlyList<ActiveInstallmentPlan> installmentPlans) =>
        _categoryResolver.TryResolveKeyword(t.Description)
        ?? LoanRepaymentClassifier.Resolve(
            transactionType, t.CounterName, t.Description, amount, t.MCC, installmentPlans)
        ?? TransferDescriptionClassifier.Resolve(t.Description)
        ?? _categoryResolver.ResolveMcc(t.MCC);
}
