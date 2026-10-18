namespace FinanceSentry.Modules.Wealth.Application.Services;

using FinanceSentry.Core.Connections;
using FinanceSentry.Core.Domain;
using FinanceSentry.Core.Headlines;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Core.Utils;
using FinanceSentry.Modules.Wealth.Application.Queries;
using FinanceSentry.Modules.Wealth.Domain.Services;
using Microsoft.Extensions.Options;

public class WealthAggregationService(
    IBankingAccountsReader bankingAccounts,
    IBankingTransactionReader bankingTransactions,
    ICryptoHoldingsReader? cryptoReader = null,
    IBrokerageHoldingsReader? brokerageReader = null,
    IBrokerageConnectionStatusReader? brokerageConnections = null,
    IEnumerable<IConnectionHealthReader>? healthReaders = null,
    IOptionsMonitor<ConnectionHealthOptions>? healthOptions = null,
    IUserAssetVisibilityReader? visibilityReader = null,
    IUserBaseCurrencyReader? baseCurrencyReader = null) : IWealthAggregationService
{
    private static readonly HashSet<string> AllowedCategories =
        new(StringComparer.OrdinalIgnoreCase) { "banking", "crypto", "brokerage", "other" };

    // Banking feeds sync roughly daily (not tick-by-tick like crypto/brokerage prices), so a
    // longer window before we call a bank account "stale". Matches the net-worth snapshot job.
    private static readonly TimeSpan BankingStaleThreshold = TimeSpan.FromHours(36);

    // A Dormant account is not ranked: it is quiet beside its working siblings (WorstSyncStatus).
    private static readonly IReadOnlyDictionary<string, int> SyncStatusPriority =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["reauth_required"] = 0,
            [ConnectionHealthSurface.ReconnectNeeded] = 0,
            ["failed"] = 1,
            [ConnectionHealthSurface.Retrying] = 2,
            ["stale"] = 3,
            ["pending"] = 4,
            ["syncing"] = 5,
            ["synced"] = 6,
            ["active"] = 6,
        };

    private static readonly HashSet<string> NotCurrentStatuses =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "stale", "failed", "reauth_required",
            ConnectionHealthSurface.Retrying, ConnectionHealthSurface.ReconnectNeeded,
        };

    private readonly IBankingAccountsReader _bankingAccounts = bankingAccounts ?? throw new ArgumentNullException(nameof(bankingAccounts));
    private readonly IBankingTransactionReader _bankingTransactions = bankingTransactions ?? throw new ArgumentNullException(nameof(bankingTransactions));
    private readonly ICryptoHoldingsReader? _cryptoReader = cryptoReader;
    private readonly IBrokerageHoldingsReader? _brokerageReader = brokerageReader;

    public async Task<WealthSummaryResponse> GetWealthSummaryAsync(
        Guid userId, string? category, string? provider, CancellationToken ct = default)
    {
        if (category is not null && !AllowedCategories.Contains(category))
            throw new ArgumentException($"Invalid category '{category}'.", nameof(category));

        var bankAccounts = await _bankingAccounts.GetAccountSummariesAsync(userId, ct);
        var labels = healthReaders is null || healthOptions is null
            ? ConnectionHealthLabels.None
            : await ConnectionHealthLabels.ReadAsync(healthReaders, healthOptions.CurrentValue, userId, ct);
        var prefs = await VisibilityAsync(userId, ct);

        IEnumerable<BankingAccountSummary> filtered = bankAccounts;

        if (provider is not null)
            filtered = filtered.Where(a => string.Equals(a.Provider, provider, StringComparison.OrdinalIgnoreCase));
        else if (category is not null)
            filtered = filtered.Where(a => ProviderCategoryMapper.GetCategory(a.Provider) == category);

        // The worst conversion behind any figure the total is summed from.
        var fx = filtered.Aggregate(FxStatus.Exact, (worst, a) => worst.Worst(a.FxStatus));

        var grouped = filtered
            .GroupBy(a => ProviderCategoryMapper.GetCategory(a.Provider))
            .Select(g =>
            {
                var summaries = g.ToList();
                var institutions = BuildBankingInstitutions(summaries, labels, prefs);
                return new CategorySummaryDto(
                    g.Key,
                    institutions.Sum(i => i.TotalInBaseCurrency),
                    institutions.Count,
                    institutions);
            })
            .ToList();

        if (_cryptoReader is not null && (category is null || category == "crypto")
            && (provider is null || ProviderCategoryMapper.GetCategory(provider) == "crypto"))
        {
            var holdings = (await _cryptoReader.GetHoldingsAsync(userId, ct))
                .Where(h => provider is null || string.Equals(h.Provider, provider, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (holdings.Count > 0)
            {
                fx = holdings.Aggregate(fx, (worst, h) => worst.Worst(h.FxStatus));
                var accounts = holdings.Select(h => new AccountBalanceDto(
                    Guid.Empty, DisplayNameFor(h.Provider), h.IsVenueFiat ? "cash" : "crypto", h.Asset, h.Provider.ToLowerInvariant(), "crypto",
                    h.Asset, h.FreeQuantity + h.LockedQuantity, h.UsdValue, "synced", h.SyncedAt,
                    Symbol: h.Asset))
                    .Select(a => Mark(a, prefs))
                    .ToList();

                var institutionsByProvider = holdings
                    .GroupBy(h => h.Provider, StringComparer.OrdinalIgnoreCase)
                    .Select(pg => new InstitutionDto(
                        InstitutionId: userId,
                        Provider: pg.Key.ToLowerInvariant(),
                        Name: DisplayNameFor(pg.Key),
                        Category: "crypto",
                        TotalInBaseCurrency: pg.Sum(h => h.UsdValue),
                        SyncStatus: labels.ForInstitution(pg.Key) ?? "synced",
                        LastSyncTimestamp: pg.Max(h => (DateTime?)h.SyncedAt),
                        LastSuccessfulSyncTimestamp: pg.Max(h => (DateTime?)h.SyncedAt),
                        Accounts: accounts.Where(a => string.Equals(a.Provider, pg.Key, StringComparison.OrdinalIgnoreCase)).ToList()))
                    .ToList();

                grouped.Add(new CategorySummaryDto("crypto", institutionsByProvider.Sum(i => i.TotalInBaseCurrency), institutionsByProvider.Count, institutionsByProvider));
            }
        }

        if (_brokerageReader is not null && (category is null || category == "brokerage"))
        {
            var holdings = (await _brokerageReader.GetHoldingsAsync(userId, ct))
                .Where(h => provider is null || string.Equals(h.Provider, provider, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (holdings.Count > 0)
            {
                var now = DateTime.UtcNow;
                fx = holdings.Aggregate(fx, (worst, h) => worst.Worst(h.FxStatus));
                var connectionStatuses = brokerageConnections is null
                    ? new Dictionary<string, string>()
                    : await brokerageConnections.GetStatusesAsync(userId, ct);

                var accounts = holdings.Select(h => new AccountBalanceDto(
                    Guid.Empty, AccountNameFor(h.Provider),
                    AssetClassNormalizer.Normalize(h.InstrumentType) == AssetClassNormalizer.Cash ? "cash" : "brokerage",
                    h.Symbol.Length >= 4 ? h.Symbol[..4] : h.Symbol,
                    h.Provider.ToLowerInvariant(), "brokerage", h.Symbol, h.Quantity, h.UsdValue,
                    BrokerageFreshness.IsStale(h.Provider, h.SyncedAt, now) ? "stale" : "synced", h.SyncedAt,
                    NativeCurrency: h.NativeValue is null ? null : h.Currency, NativeValue: h.NativeValue,
                    Symbol: h.Symbol))
                    .Select(a => Mark(a, prefs))
                    .ToList();

                var institutionsByProvider = holdings
                    .GroupBy(h => h.Provider, StringComparer.OrdinalIgnoreCase)
                    .Select(pg => new InstitutionDto(
                        InstitutionId: userId,
                        Provider: pg.Key.ToLowerInvariant(),
                        Name: DisplayNameFor(pg.Key),
                        Category: "brokerage",
                        TotalInBaseCurrency: pg.Sum(h => h.UsdValue),
                        SyncStatus: labels.ForInstitution(pg.Key)
                            ?? BrokerageSyncStatus(pg.Key, pg.Max(h => h.SyncedAt), now, connectionStatuses),
                        LastSyncTimestamp: pg.Max(h => (DateTime?)h.SyncedAt),
                        LastSuccessfulSyncTimestamp: pg.Max(h => (DateTime?)h.SyncedAt),
                        Accounts: accounts.Where(a => string.Equals(a.Provider, pg.Key, StringComparison.OrdinalIgnoreCase)).ToList()))
                    .ToList();

                grouped.Add(new CategorySummaryDto("brokerage", institutionsByProvider.Sum(i => i.TotalInBaseCurrency), institutionsByProvider.Count, institutionsByProvider));
            }
        }

        var total = grouped.Sum(c => c.TotalInBaseCurrency);
        return new WealthSummaryResponse(
            total, "USD", grouped,
            new AppliedFiltersDto(category, provider),
            BuildHeadline(total, grouped, fx, category, provider));
    }

    // Institutions that are not current (stale, failed, needing re-auth) make the total degraded, each named
    // as its own source. The oldest successful sync behind the total is its as-of.
    private static Headline BuildHeadline(
        decimal totalUsd, IReadOnlyList<CategorySummaryDto> categories, FxStatus fx, string? category, string? provider)
    {
        var institutions = categories.SelectMany(c => c.Institutions).ToList();
        var signals = new HeadlineSignals
        {
            StaleSources = [.. institutions
                .Where(i => NotCurrentStatuses.Contains(i.SyncStatus))
                .Select(i => i.Provider)
                .Distinct(StringComparer.OrdinalIgnoreCase)],
            Fx = fx,
        };
        DateTimeOffset? asOf = institutions.Min(i => i.LastSuccessfulSyncTimestamp) is { } oldest
            ? new DateTimeOffset(DateTime.SpecifyKind(oldest, DateTimeKind.Utc))
            : null;
        var scope = provider is not null ? HeadlineScope.Source(provider)
            : category is not null ? HeadlineScope.Sleeve(category)
            : HeadlineScope.Umbrella;

        return HeadlineFactory.Value(totalUsd, scope, DateOnly.FromDateTime(DateTime.UtcNow), signals, asOf);
    }

    private static IReadOnlyList<InstitutionDto> BuildBankingInstitutions(
        IReadOnlyList<BankingAccountSummary> summaries, ConnectionHealthLabels labels, UserAssetVisibility prefs)
    {
        var buckets = new Dictionary<string, List<BankingAccountSummary>>();
        foreach (var s in summaries)
        {
            var key = InstitutionKeyFor(s);
            if (!buckets.TryGetValue(key, out var list))
            {
                list = [];
                buckets[key] = list;
            }
            list.Add(s);
        }

        return [.. buckets.Values.Select(list =>
        {
            var first = list[0];
            var accountDtos = list.Select(a => new AccountBalanceDto(
                a.AccountId, a.BankName, a.AccountType, a.AccountNumberLast4,
                a.Provider, ProviderCategoryMapper.GetCategory(a.Provider),
                a.Currency, a.CurrentBalance, a.BalanceUsd,
                AccountSyncStatus(a, labels), a.LastSyncTimestamp,
                a.ProductType)).Select(a => Mark(a, prefs)).ToList();

            // Monobank returns one account per card × currency. Group them into physical cards
            // (black/white/…) with empty sub-accounts hidden, so the UI can nest currencies under
            // the card instead of showing a flat, mostly-empty list.
            var cards = string.Equals(first.Provider, "monobank", StringComparison.OrdinalIgnoreCase)
                ? BuildMonobankCards(accountDtos)
                : null;

            return new InstitutionDto(
                InstitutionId: InstitutionIdFor(first),
                Provider: first.Provider.ToLowerInvariant(),
                Name: first.BankName,
                Category: ProviderCategoryMapper.GetCategory(first.Provider),
                TotalInBaseCurrency: accountDtos.Sum(a => AccountBalanceMath.SignedForNetTotal(a.AccountType, a.BalanceInBaseCurrency ?? 0m)),
                SyncStatus: WorstSyncStatus(accountDtos.Select(a => a.SyncStatus)),
                LastSyncTimestamp: list.Max(a => a.LastSyncTimestamp),
                LastSuccessfulSyncTimestamp: list.Max(a => a.LastSuccessfulSyncTimestamp),
                Accounts: accountDtos,
                Cards: cards);
        }).OrderByDescending(i => i.TotalInBaseCurrency)];
    }

    private static string InstitutionKeyFor(BankingAccountSummary s)
    {
        var scoped = s.MonobankCredentialId ?? s.TrueLayerConnectionId ?? s.AccountId;
        return $"{s.Provider.ToLowerInvariant()}::{scoped:N}";
    }

    private static Guid InstitutionIdFor(BankingAccountSummary s)
        => s.MonobankCredentialId ?? s.TrueLayerConnectionId ?? s.AccountId;

    // The visibility preferences with the threshold in USD, the currency this service sums in: the base-currency
    // amount is converted once here, and the controller re-expresses the rows' values afterwards.
    private async Task<UserAssetVisibility> VisibilityAsync(Guid userId, CancellationToken ct)
    {
        var prefs = visibilityReader is null ? null : await visibilityReader.GetAsync(userId, ct);
        prefs ??= new UserAssetVisibility(HideEmptyBalances: true, HideSmallBalances: false, SmallBalanceThreshold: 1m);
        var baseCurrency = CurrencyConverter.ResolveBase(
            baseCurrencyReader is null ? null : await baseCurrencyReader.GetAsync(userId, ct));
        return prefs with { SmallBalanceThreshold = CurrencyConverter.ToUsd(prefs.SmallBalanceThreshold, baseCurrency) };
    }

    // Labels a row, never drops it. A row with a quantity but no USD value is unpriced: unknown, so never hidden.
    private static AccountBalanceDto Mark(AccountBalanceDto a, UserAssetVisibility prefs)
    {
        decimal? value = a.CurrentBalance is 0m ? 0m : a.BalanceInBaseCurrency is 0m ? null : a.BalanceInBaseCurrency;
        return a with { HiddenReason = AssetVisibility.Classify(value, AccountBalanceMath.IsLiability(a.AccountType), prefs) };
    }

    /// <summary>
    /// Groups Monobank currency sub-accounts into physical cards by product type. Every sub-account and card is
    /// returned; the empty ones carry <see cref="AccountBalanceDto.HiddenReason"/>. A missing product type (rows
    /// connected before it was captured) collapses into one "Card" group until the next sync
    /// backfills it.
    /// </summary>
    private static IReadOnlyList<CardGroupDto> BuildMonobankCards(IReadOnlyList<AccountBalanceDto> accounts)
    {
        return [.. accounts
            .GroupBy(a => a.ProductType ?? "card")
            .Select(g => new CardGroupDto(
                CardType: g.Key,
                DisplayName: FormatCardName(g.Key),
                TotalInBaseCurrency: g.Sum(a => AccountBalanceMath.SignedForNetTotal(a.AccountType, a.BalanceInBaseCurrency ?? 0m)),
                SyncStatus: WorstSyncStatus(g.Select(a => a.SyncStatus)),
                Accounts: [.. g]))
            .OrderByDescending(c => c.TotalInBaseCurrency)];
    }

    private static string FormatCardName(string productType) => productType.ToLowerInvariant() switch
    {
        "fop" => "FOP",
        "eaid" => "eAid",
        "card" => "Card",
        var s when s.Length > 0 => char.ToUpperInvariant(s[0]) + s[1..],
        _ => productType,
    };

    /// <summary>
    /// Downgrades a banking account that <i>looks</i> synced but hasn't had a successful sync
    /// within <see cref="BankingStaleThreshold"/> to "stale", so a lapsed connection (e.g. an
    /// expired TrueLayer consent) shows a frozen balance as stale rather than current. Leaves
    /// already-actionable states ("reauth_required", "failed") and in-progress ones untouched.
    /// </summary>
    private static string EffectiveBankingStatus(string status, DateTime? lastSuccessfulSync)
    {
        if (status is not ("synced" or "active"))
            return status;

        var isStale = lastSuccessfulSync is null
            || DateTime.UtcNow - lastSuccessfulSync.Value > BankingStaleThreshold;
        return isStale ? "stale" : status;
    }

    /// <summary>
    /// A bank account's status: its health label when its provider is listed in
    /// <see cref="ConnectionHealthOptions.ActiveProviders"/> and the connection is not Healthy, else the sync's own
    /// status aged by <see cref="EffectiveBankingStatus"/>. A listed provider's account that the policy holds Healthy
    /// shows no failure (one the policy does not count - our own, or a sync we cancelled - is not the person's to see),
    /// and its parked account reads as the reconnect the policy asked for.
    /// </summary>
    private static string AccountSyncStatus(BankingAccountSummary account, ConnectionHealthLabels labels)
    {
        if (labels.ForAccount(account.AccountId, account.Provider, account.MonobankCredentialId ?? account.TrueLayerConnectionId)
            is { } label)
        {
            return label;
        }

        var status = account.SyncStatus;
        if (labels.IsActive(account.Provider))
        {
            status = status switch
            {
                "failed" => "synced",
                "reauth_required" => ConnectionHealthSurface.ReconnectNeeded,
                _ => status,
            };
        }

        return EffectiveBankingStatus(status, account.LastSuccessfulSyncTimestamp);
    }

    // The most urgent status; a Dormant member counts only when every member is Dormant.
    public static string WorstSyncStatus(IEnumerable<string> statuses)
    {
        var all = statuses.ToList();
        var awake = all.Where(s => s != ConnectionHealthSurface.Dormant).ToList();
        if (awake.Count == 0)
            return all[0];

        var min = awake
            .Select(s => (Status: s, Priority: SyncStatusPriority.TryGetValue(s, out var p) ? p : int.MaxValue))
            .OrderBy(t => t.Priority)
            .First();
        return min.Status;
    }

    // A lapsed connection (Inzhur's session) outranks the holdings' age: the owner has to reconnect.
    private static string BrokerageSyncStatus(
        string provider, DateTime lastSyncedAt, DateTime now, IReadOnlyDictionary<string, string> connectionStatuses)
    {
        if (connectionStatuses.TryGetValue(provider, out var status) && status == "reauth_required")
            return status;
        return BrokerageFreshness.IsStale(provider, lastSyncedAt, now) ? "stale" : "synced";
    }

    private static string AccountNameFor(string provider)
        => string.Equals(provider, "ibkr", StringComparison.OrdinalIgnoreCase) ? "IBKR" : DisplayNameFor(provider);

    private static string DisplayNameFor(string provider) => provider.ToLowerInvariant() switch
    {
        "binance" => "Binance",
        "revolut_x" => "Revolut X",
        "ibkr" => "Interactive Brokers",
        "inzhur" => "Inzhur",
        _ => char.ToUpperInvariant(provider[0]) + provider[1..],
    };

    public async Task<TransactionSummaryResponse> GetTransactionSummaryAsync(
        Guid userId, DateOnly from, DateOnly to,
        string? category, string? provider, CancellationToken ct = default)
    {
        if (category is not null && !AllowedCategories.Contains(category))
            throw new ArgumentException($"Invalid category '{category}'.", nameof(category));

        var txList = await _bankingTransactions.GetTransactionsAsync(userId, from, to, ct);

        IEnumerable<BankingTransactionSummary> filtered = txList.Where(t => !t.IsPending);

        if (provider is not null)
            filtered = filtered.Where(t => string.Equals(t.Provider, provider, StringComparison.OrdinalIgnoreCase));
        else if (category is not null)
            filtered = filtered.Where(t => ProviderCategoryMapper.GetCategory(t.Provider) == category);

        var byCategory = filtered
            .GroupBy(t => ProviderCategoryMapper.GetCategory(t.Provider))
            .Select(g =>
            {
                // Sum AmountUsd — accounts span currencies (UAH/EUR/…); summing native Amount
                // would add hryvnia to euros as if both were dollars.
                var debits = g.Where(t => string.Equals(t.TransactionType, "debit", StringComparison.OrdinalIgnoreCase)).Sum(t => t.AmountUsd);
                var credits = g.Where(t => string.Equals(t.TransactionType, "credit", StringComparison.OrdinalIgnoreCase)).Sum(t => t.AmountUsd);
                return new TransactionCategoryDto(g.Key, debits, credits, credits - debits, g.Count());
            })
            .ToList();

        var totalDebits = byCategory.Sum(c => c.TotalDebits);
        var totalCredits = byCategory.Sum(c => c.TotalCredits);

        return new TransactionSummaryResponse(
            from.ToString("yyyy-MM-dd"), to.ToString("yyyy-MM-dd"),
            totalDebits, totalCredits, totalCredits - totalDebits,
            byCategory, new AppliedFiltersDto(category, provider));
    }
}
