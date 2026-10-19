using System.Globalization;
using System.Net;
using FinanceSentry.Modules.CryptoSync.Domain;
using FinanceSentry.Modules.CryptoSync.Domain.Exceptions;
using FinanceSentry.Modules.CryptoSync.Domain.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FinanceSentry.Modules.CryptoSync.Infrastructure.Binance;

/// <summary>
/// HTTP orchestration only. Fans out the four wallet endpoints + price call in
/// parallel, tolerates permission failures on optional sources, and hands the
/// raw responses to <see cref="BinanceHoldingsAggregator"/> to produce the
/// per-asset balance list.
/// </summary>
public sealed class BinanceAdapter : ICryptoExchangeAdapter
{
    private readonly BinanceHttpClient _httpClient;
    private readonly BinanceHoldingsAggregator _aggregator;
    private readonly ILogger<BinanceAdapter> _logger;
    private readonly decimal _dustThresholdUsd;

    private static readonly string[] QuoteCandidates = ["USDT", "USDC", "FDUSD", "BUSD"];

    // Stablecoins are the quote side of every pair we walk; they have no USD-pair history of their own.
    private static readonly HashSet<string> NoTradeHistoryAssets = new(StringComparer.OrdinalIgnoreCase)
    {
        "USDT", "USDC", "BUSD", "FDUSD", "DAI",
    };

    // fiat/orders weighs UID 90000 per call against a 180000-per-minute UID budget (Binance's request-weight docs);
    // the weight of fiat/payments is unverified. A run therefore spends one fiat call, rotating through four lanes
    // (orders in, payments in, orders out, payments out) over one day-long window that the cursor leaves only after
    // every lane has read it; a walk further behind catches up over the next runs.
    private const long FlowWindowMs = 24L * 60 * 60 * 1000;
    private const int FlowLanes = 4;
    private const long DayMs = 24L * 60 * 60 * 1000;
    private const int FlowPageRows = 500;
    private const int MaxFlowPages = 20;
    private const int FiatIn = 0;
    private const int FiatOut = 1;
    private const string FiatSuccessCode = "000000";
    private const int EarnedPageRows = 100;
    private const int MaxEarnedPages = 20;
    private const int DividendRows = 500;
    private const string Bnb = "BNB";
    private const int RejectedKeyCode = -2015;
    private static readonly string[] FlexibleRewardTypes = ["BONUS", "REALTIME", "REWARDS"];

    private static readonly TimeSpan MaxPendingHold = TimeSpan.FromDays(30);

    // capital/deposit/hisrec and capital/withdraw/history take windows under 90 days. A run reads one such window of
    // both (deposits weigh IP 1, withdrawals UID 18000 of the 180000-per-minute budget).
    private const long ChainWindowMs = 89L * 24 * 60 * 60 * 1000;
    private const int ChainPageRows = 1000;
    private const int MaxChainPages = 20;
    private const int DepositSuccess = 1;
    private const int DepositCreditedLocked = 6;
    private const int WithdrawalCompleted = 6;
    private const int InternalTransferType = 1;
    private const string WithdrawTimeFormat = "yyyy-MM-dd HH:mm:ss";

    /// <summary>The counterparty a chain leg records: a transfer over a chain, or one with another Binance account.</summary>
    public const string OnChainCounterparty = "on_chain";

    /// <inheritdoc cref="OnChainCounterparty"/>
    public const string InternalCounterparty = "binance_internal";

    private static readonly HashSet<int> PendingDepositStatuses = [0, 8];
    private static readonly HashSet<int> PendingWithdrawalStatuses = [0, 2, 4];

    private static readonly HashSet<string> CompletedFiatStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Successful", "Finished", "Completed",
    };

    private static readonly HashSet<string> PendingFiatStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Processing", "Pending",
    };

    public string ExchangeName => CryptoExchangeProvider.Binance;

    public bool TradeHistoryStartsAtConnect => false;

    public bool WalksChainFlows => true;

    public BinanceAdapter(
        BinanceHttpClient httpClient,
        BinanceHoldingsAggregator aggregator,
        ILogger<BinanceAdapter> logger,
        IConfiguration configuration)
    {
        _httpClient = httpClient;
        _aggregator = aggregator;
        _logger = logger;
        _dustThresholdUsd = decimal.TryParse(
            configuration["Binance:DustThresholdUsd"],
            out var threshold) ? threshold : 0.01m;
    }

    public async Task ValidateCredentialsAsync(string apiKey, string apiSecret, CancellationToken ct = default)
    {
        await _httpClient.GetAccountAsync(apiKey, apiSecret, ct);
    }

    public async Task<IReadOnlyList<CryptoAssetBalance>> GetHoldingsAsync(
        string apiKey,
        string apiSecret,
        CancellationToken ct = default)
    {
        // Spot is the source of truth for credential health — fail loudly here.
        var spotTask = _httpClient.GetAccountAsync(apiKey, apiSecret, ct);
        var pricesTask = _httpClient.GetAllPricesAsync(ct);

        // Funding + Earn require additional permissions on the API key (Read-Only
        // is enough but the user may have scoped the key narrower). Treat as
        // best-effort: log and continue if any one of these is rejected.
        var fundingTask = SafeFetchAsync(
            () => _httpClient.GetFundingAssetsAsync(apiKey, apiSecret, ct),
            "Funding wallet",
            (IReadOnlyList<BinanceFundingAsset>)Array.Empty<BinanceFundingAsset>());

        var flexibleEarnTask = SafeFetchAsync(
            () => _httpClient.GetFlexibleEarnPositionsAsync(apiKey, apiSecret, ct),
            "Simple Earn (flexible)",
            new BinanceEarnPage<BinanceFlexibleEarnPosition>([], 0));

        var lockedEarnTask = SafeFetchAsync(
            () => _httpClient.GetLockedEarnPositionsAsync(apiKey, apiSecret, ct),
            "Simple Earn (locked)",
            new BinanceEarnPage<BinanceLockedEarnPosition>([], 0));

        await Task.WhenAll(spotTask, pricesTask, fundingTask, flexibleEarnTask, lockedEarnTask);

        return _aggregator.Aggregate(
            spotTask.Result,
            fundingTask.Result,
            flexibleEarnTask.Result,
            lockedEarnTask.Result,
            pricesTask.Result,
            _dustThresholdUsd);
    }

    public async Task<CryptoTradePage> GetTradesAsync(
        string apiKey,
        string apiSecret,
        string asset,
        string? cursor,
        CryptoTradeWalk walk,
        CancellationToken ct = default)
    {
        const int pageLimit = 1000;
        const int maxPages = 20;

        if (NoTradeHistoryAssets.Contains(asset))
        {
            return new CryptoTradePage([], cursor);
        }

        var nextFromIds = BinanceTradeCursor.Parse(cursor, QuoteCandidates);
        var allTrades = new List<CryptoTrade>();
        var feePrices = new Dictionary<(string Asset, long Day), decimal?>();
        var stopped = false;

        foreach (var quote in QuoteCandidates)
        {
            if (stopped) break;

            var symbol = $"{asset.ToUpperInvariant()}{quote}";
            var fromId = nextFromIds.GetValueOrDefault(quote);
            for (var page = 0; page < maxPages && !stopped; page++)
            {
                IReadOnlyList<BinanceTradeRow> rows;
                try
                {
                    rows = await _httpClient.GetMyTradesAsync(apiKey, apiSecret, symbol, fromId, pageLimit, ct);
                }
                catch (BinanceException ex)
                {
                    _logger.LogDebug(ex, "Binance trade history for {Symbol} unavailable (likely no such pair).", symbol);
                    break;
                }

                // fromId is inclusive, so a row below it was already counted by an earlier run.
                var fresh = rows.Where(r => r.Id >= fromId).OrderBy(r => r.Id).ToList();
                long? lastConsumedId = null;
                foreach (var r in fresh)
                {
                    var price = decimal.Parse(r.Price, CultureInfo.InvariantCulture);
                    var fee = ParseAmount(r.Commission);
                    var feeAsset = string.IsNullOrWhiteSpace(r.CommissionAsset) ? null : r.CommissionAsset.Trim().ToUpperInvariant();
                    var hasFee = fee is > 0m && feeAsset is not null;
                    decimal? feeUsd = null;
                    if (hasFee)
                    {
                        try
                        {
                            feeUsd = await FeeUsdAsync(fee!.Value, feeAsset!, asset, price, r.TimeMs, feePrices, ct);
                        }
                        catch (BinanceException ex)
                        {
                            _logger.LogWarning(
                                ex,
                                "Binance price for fee asset {Asset} failed on {Symbol} fill {TradeId}; the walk stops here and resumes from this fill next sync.",
                                feeAsset, symbol, r.Id);
                            stopped = true;
                            break;
                        }
                    }

                    allTrades.Add(new CryptoTrade(
                        TradeId: r.Id.ToString(CultureInfo.InvariantCulture),
                        Asset: asset.ToUpperInvariant(),
                        QuoteAsset: quote,
                        Quantity: decimal.Parse(r.Quantity, CultureInfo.InvariantCulture),
                        PriceUsd: price,
                        QuoteQuantityUsd: decimal.Parse(r.QuoteQuantity, CultureInfo.InvariantCulture),
                        IsBuyer: r.IsBuyer,
                        Timestamp: DateTimeOffset.FromUnixTimeMilliseconds(r.TimeMs).UtcDateTime,
                        Fee: hasFee ? fee : null,
                        FeeAsset: hasFee ? feeAsset : null,
                        FeeUsd: feeUsd));
                    lastConsumedId = r.Id;
                }

                if (lastConsumedId is { } lastId)
                {
                    // Ids are per symbol: the next run resumes this pair just past its last valued fill.
                    fromId = lastId + 1;
                    nextFromIds[quote] = fromId;
                }

                if (rows.Count < pageLimit) break;
            }
        }

        var trades = allTrades
            .OrderBy(t => t.Timestamp)
            .ThenBy(t => t.QuoteAsset, StringComparer.Ordinal)
            .ThenBy(t => long.Parse(t.TradeId, CultureInfo.InvariantCulture))
            .ToList();

        return new CryptoTradePage(trades, BinanceTradeCursor.Format(nextFromIds), IsComplete: !stopped);
    }

    /// <summary>
    /// A fill's commission in USD at the fill's own time: a stablecoin at par, the traded asset at the fill's price, any
    /// other asset (typically BNB) at the close of its daily <c>klines</c> candle for the fill's UTC date. Null when that asset has no
    /// USDT pair (HTTP 400, invalid symbol), so the fee stays unvalued instead of being priced at today's rate; any other
    /// failure (rate limit, 5xx) propagates so the walk retries it rather than storing the fee unvalued for good.
    /// </summary>
    private async Task<decimal?> FeeUsdAsync(
        decimal fee,
        string feeAsset,
        string tradedAsset,
        decimal fillPriceUsd,
        long timeMs,
        Dictionary<(string Asset, long Day), decimal?> cache,
        CancellationToken ct)
    {
        if (BinanceHoldingsAggregator.IsStablecoin(feeAsset))
        {
            return fee;
        }

        if (string.Equals(feeAsset, tradedAsset, StringComparison.OrdinalIgnoreCase))
        {
            return fee * fillPriceUsd;
        }

        var key = (feeAsset, timeMs / DayMs);
        if (!cache.TryGetValue(key, out var unitPrice))
        {
            try
            {
                unitPrice = await _httpClient.GetDailyClosePriceAsync($"{feeAsset}USDT", key.Item2 * DayMs, ct);
            }
            catch (BinanceException ex) when (ex.VenueStatusCode == (int)HttpStatusCode.BadRequest)
            {
                _logger.LogDebug(ex, "Binance price for fee asset {Asset} unavailable; the commission stays unvalued.", feeAsset);
                unitPrice = null;
            }

            cache[key] = unitPrice;
        }

        return unitPrice is { } p ? fee * p : null;
    }

    /// <summary>
    /// One lane's completed fiat legs created in a day-long window from <see cref="CryptoFlowWalk.From"/>: deposits and
    /// withdrawals (<c>fiat/orders</c>) or crypto bought or sold for fiat (<c>fiat/payments</c>), each with its fee. A
    /// deposit is the amount the bank or card paid (<c>indicatedAmount</c>), a withdrawal the amount paid out after the
    /// fee (<c>amount</c>), a buy or sell its fiat side (<c>sourceAmount</c>); the method is the counterparty. Unlike
    /// the holdings sources there is no fallback: a refused call throws, so the walk resumes from the same point.
    /// The cursor moves a window on only after the last lane has read it; a pending leg in any lane restarts the
    /// lanes on the same window, so it is read again once it settles.
    /// </summary>
    // shortcut: P2P and Pay legs are not read; on-chain legs are, by GetChainFlowsAsync on a cursor of their own.
    public async Task<CryptoFlowPage> GetFlowsAsync(
        string apiKey,
        string apiSecret,
        CryptoFlowWalk walk,
        CancellationToken ct = default)
    {
        var beginMs = ToUnixMs(walk.From);
        var endMs = beginMs + FlowWindowMs - 1;
        if (endMs > ToUnixMs(walk.AsOf))
        {
            return new CryptoFlowPage([], walk.From.AddMilliseconds(-1), walk.Lane);
        }

        var type = walk.Lane < FlowLanes / 2 ? FiatIn : FiatOut;
        var flows = new List<CryptoFlow>();
        var held = false;
        var holdSinceMs = ToUnixMs(walk.AsOf - MaxPendingHold);

        if (walk.Lane % 2 == 0)
        {
            var orders = await ReadFiatPagesAsync(
                (page, c) => _httpClient.GetFiatOrdersAsync(apiKey, apiSecret, type, beginMs, endMs, page, FlowPageRows, c), ct);
            foreach (var order in orders)
            {
                held |= IsHeld(holdSinceMs, order.Status, order.CreateTimeMs);
                if (IsCompleted(order.Status)
                    && FiatOrderFlow(order, type) is { } flow)
                {
                    flows.Add(flow);
                }
            }
        }
        else
        {
            var payments = await ReadFiatPagesAsync(
                (page, c) => _httpClient.GetFiatPaymentsAsync(apiKey, apiSecret, type, beginMs, endMs, page, FlowPageRows, c), ct);
            foreach (var payment in payments)
            {
                held |= IsHeld(holdSinceMs, payment.Status, payment.CreateTimeMs);
                if (IsCompleted(payment.Status)
                    && FiatPaymentFlow(payment, type) is { } flow)
                {
                    flows.Add(flow);
                }
            }
        }

        var ordered = flows.OrderBy(f => f.OccurredAt).ThenBy(f => f.EventId, StringComparer.Ordinal).ToList();
        var stayAt = walk.From.AddMilliseconds(-1);
        if (held)
        {
            return new CryptoFlowPage(ordered, stayAt);
        }

        if (walk.Lane != FlowLanes - 1)
        {
            return new CryptoFlowPage(ordered, stayAt, walk.Lane + 1);
        }

        // The window's last read also takes the cheap earn, distribution and dust history, once per window.
        ordered.AddRange(await ReadEarnedAsync(apiKey, apiSecret, beginMs, endMs, ct));
        return new CryptoFlowPage(
            ordered.OrderBy(f => f.OccurredAt).ThenBy(f => f.EventId, StringComparer.Ordinal).ToList(),
            DateTimeOffset.FromUnixTimeMilliseconds(endMs).UtcDateTime);
    }

    /// <summary>
    /// What the venue paid or converted in a window: Simple Earn rewards (flexible, locked) and distributions or
    /// airdrops (<c>assetDividend</c>) as income, dust conversions to BNB as their service charge. Each source is
    /// optional (a key scoped narrower than reading is rejected with a 401, or a 403 that names code -2015): a rejected
    /// source is skipped with a warning, which only leaves that income inside the market remainder, never in contributions. A throttled,
    /// failed or unreachable call throws so the window is read again. A crypto leg carries the venue price taken now.
    /// </summary>
    private async Task<List<CryptoFlow>> ReadEarnedAsync(string apiKey, string apiSecret, long beginMs, long endMs, CancellationToken ct)
    {
        var earned = new List<(string Id, string Type, long TimeMs, string? Asset, string? Amount, string? Fee)>();

        foreach (var type in FlexibleRewardTypes)
        {
            foreach (var row in await SafeReadAsync(
                "Simple Earn flexible rewards",
                () => ReadEarnPagesAsync((page, c) => _httpClient.GetFlexibleRewardsAsync(
                    apiKey, apiSecret, type, beginMs, endMs, page, EarnedPageRows, c), ct)))
            {
                earned.Add(($"{row.ProjectId}:{row.Type ?? type}:{row.TimeMs}", CryptoFlowTypes.Reward, row.TimeMs, row.Asset, row.Rewards, null));
            }
        }

        foreach (var row in await SafeReadAsync(
            "Simple Earn locked rewards",
            () => ReadEarnPagesAsync((page, c) => _httpClient.GetLockedRewardsAsync(
                apiKey, apiSecret, beginMs, endMs, page, EarnedPageRows, c), ct)))
        {
            earned.Add(($"{row.PositionId}:{row.TimeMs}", CryptoFlowTypes.Reward, row.TimeMs, row.Asset, row.Amount, null));
        }

        // shortcut: a day holds at most DividendRows distributions, and Earn payouts that also show here (undocumented)
        // are told apart by their text, so a payout worded otherwise would count twice; check against a real row.
        var dividends = await SafeReadAsync("asset dividends", async () =>
            (await _httpClient.GetAssetDividendsAsync(apiKey, apiSecret, beginMs, endMs, DividendRows, ct)).Rows ?? []);
        foreach (var row in dividends.Where(d => d.Info?.Contains("Earn", StringComparison.OrdinalIgnoreCase) != true))
        {
            earned.Add(((row.TranId ?? row.Id).ToString(CultureInfo.InvariantCulture), CryptoFlowTypes.Airdrop, row.TimeMs, row.Asset, row.Amount, null));
        }

        foreach (var row in await SafeReadAsync(
            "dust conversions",
            async () => (await _httpClient.GetDribbletAsync(apiKey, apiSecret, beginMs, endMs, ct)).Dribblets ?? []))
        {
            earned.Add((row.TransId.ToString(CultureInfo.InvariantCulture), CryptoFlowTypes.DustConversion, row.OperateTimeMs,
                Bnb, row.TransferedAmount, row.ServiceChargeAmount));
        }

        if (earned.Count == 0)
        {
            return [];
        }

        var priceMap = BinanceHoldingsAggregator.PriceMap(await _httpClient.GetAllPricesAsync(ct));
        var flows = new List<CryptoFlow>();
        foreach (var (id, type, timeMs, asset, amount, fee) in earned)
        {
            var unit = asset?.Trim().ToUpperInvariant();
            var quantity = ParseAmount(amount);
            var feeAmount = ParseAmount(fee);
            if (string.IsNullOrEmpty(unit) || quantity is not > 0m && feeAmount is not > 0m)
            {
                _logger.LogWarning("Binance {ProviderType} {EventId} is unreadable and was skipped.", type, id);
                continue;
            }

            flows.Add(new CryptoFlow(
                EventId: id,
                ProviderType: type,
                OccurredAt: DateTimeOffset.FromUnixTimeMilliseconds(timeMs).UtcDateTime,
                Amount: quantity ?? 0m,
                Unit: unit,
                IsFiat: false,
                UnitPriceUsd: BinanceHoldingsAggregator.UnitPriceUsd(unit, priceMap),
                Fee: feeAmount is > 0m ? feeAmount : null,
                FeeUnit: feeAmount is > 0m ? unit : null,
                CounterpartyRef: null,
                Status: "completed"));
        }

        return flows;
    }

    private static async Task<IReadOnlyList<T>> ReadEarnPagesAsync<T>(
        Func<int, CancellationToken, Task<BinanceEarnPage<T>>> fetch,
        CancellationToken ct)
    {
        var rows = new List<T>();
        for (var page = 1; ; page++)
        {
            if (page > MaxEarnedPages)
            {
                throw new BinanceException($"Binance returned more than {MaxEarnedPages} pages of earn rewards for one window.");
            }

            var response = await fetch(page, ct);
            rows.AddRange(response.Rows);
            if (response.Rows.Count < EarnedPageRows || rows.Count >= response.Total)
            {
                return rows;
            }
        }
    }

    private async Task<IReadOnlyList<T>> SafeReadAsync<T>(string label, Func<Task<IReadOnlyList<T>>> read)
    {
        try
        {
            return await read();
        }
        catch (BinanceException ex) when (ex.VenueStatusCode == (int)HttpStatusCode.Unauthorized
            || ex is { VenueStatusCode: (int)HttpStatusCode.Forbidden, BinanceErrorCode: RejectedKeyCode })
        {
            _logger.LogWarning(ex, "Skipping Binance source '{Source}' - request was rejected (likely missing API-key permission).", label);
            return [];
        }
    }

    /// <summary>
    /// The completed coin deposits and withdrawals (S2b) in one window of under 90 days from
    /// <see cref="CryptoFlowWalk.From"/>, both read every run. A deposit counts once credited (status 1, or 6 =
    /// credited but not yet withdrawable) at the amount credited, dated when credited; a withdrawal once completed
    /// (status 6) at <c>amount</c> with <c>transactionFee</c> as its fee, dated when applied (the balance leaves
    /// then). The chain hash and network are kept for cross-venue matching, and the counterparty says whether the
    /// coin crossed a chain (<see cref="OnChainCounterparty"/>) or came from or went to another Binance account
    /// (<see cref="InternalCounterparty"/>). A pending leg stops the cursor before it, so it is read again once it
    /// settles. Every crypto leg is priced at read time, as Revolut X's are.
    /// </summary>
    // shortcut: whether withdraw/history's amount is net of transactionFee is unverified; read as net (what left for
    // the address), so the fee is what the sleeve paid on top. A matched pair's fee is the sent less the received
    // either way, so only an unmatched withdrawal's split between outflow and fee depends on it.
    public async Task<CryptoFlowPage> GetChainFlowsAsync(
        string apiKey,
        string apiSecret,
        CryptoFlowWalk walk,
        CancellationToken ct = default)
    {
        var beginMs = ToUnixMs(walk.From);
        var asOfMs = ToUnixMs(walk.AsOf);
        if (beginMs > asOfMs)
        {
            return new CryptoFlowPage([], walk.From.AddMilliseconds(-1));
        }

        var endMs = Math.Min(beginMs + ChainWindowMs - 1, asOfMs);
        var depositsTask = ReadChainPagesAsync(
            (offset, c) => _httpClient.GetCoinDepositsAsync(apiKey, apiSecret, beginMs, endMs, offset, ChainPageRows, c), ct);
        var withdrawalsTask = ReadChainPagesAsync(
            (offset, c) => _httpClient.GetCoinWithdrawalsAsync(apiKey, apiSecret, beginMs, endMs, offset, ChainPageRows, c), ct);
        await Task.WhenAll(depositsTask, withdrawalsTask);

        var holdSinceMs = ToUnixMs(walk.AsOf - MaxPendingHold);
        var holdBelowMs = long.MaxValue;
        var completed = new List<ChainLeg>();
        foreach (var deposit in depositsTask.Result)
        {
            if (deposit.Status is DepositSuccess or DepositCreditedLocked)
            {
                completed.Add(new ChainLeg(
                    deposit.Id, CryptoFlowTypes.CryptoDeposit, deposit.CompleteTimeMs ?? deposit.InsertTimeMs, deposit.Amount,
                    deposit.Coin, null, deposit.TxId, deposit.Network, deposit.TransferType, deposit.Status));
            }
            else if (PendingDepositStatuses.Contains(deposit.Status) && deposit.InsertTimeMs >= holdSinceMs)
            {
                holdBelowMs = Math.Min(holdBelowMs, deposit.InsertTimeMs);
            }
        }

        foreach (var withdrawal in withdrawalsTask.Result)
        {
            var appliedMs = ParseWithdrawTime(withdrawal.ApplyTime);
            if (withdrawal.Status == WithdrawalCompleted && appliedMs is { } at)
            {
                completed.Add(new ChainLeg(
                    withdrawal.Id, CryptoFlowTypes.CryptoWithdrawal, at, withdrawal.Amount, withdrawal.Coin,
                    withdrawal.TransactionFee, withdrawal.TxId, withdrawal.Network, withdrawal.TransferType, withdrawal.Status));
            }
            else if (PendingWithdrawalStatuses.Contains(withdrawal.Status) && (appliedMs ?? asOfMs) >= holdSinceMs)
            {
                holdBelowMs = Math.Min(holdBelowMs, appliedMs ?? beginMs);
            }
        }

        var priceMap = completed.Count == 0
            ? []
            : BinanceHoldingsAggregator.PriceMap(await _httpClient.GetAllPricesAsync(ct));
        var flows = new List<CryptoFlow>();
        foreach (var leg in completed.OrderBy(l => l.AtMs).ThenBy(l => l.Id ?? string.Empty, StringComparer.Ordinal))
        {
            var unit = leg.Coin?.Trim().ToUpperInvariant();
            var amount = ParseAmount(leg.Amount);
            if (string.IsNullOrWhiteSpace(leg.Id) || amount is not > 0m || string.IsNullOrEmpty(unit))
            {
                _logger.LogWarning("Binance {ProviderType} {EventId} is unreadable and was skipped.", leg.Type, leg.Id);
                continue;
            }

            var fee = ParseAmount(leg.Fee);
            flows.Add(new CryptoFlow(
                EventId: leg.Id.Trim(),
                ProviderType: leg.Type,
                OccurredAt: DateTimeOffset.FromUnixTimeMilliseconds(leg.AtMs).UtcDateTime,
                Amount: amount.Value,
                Unit: unit,
                IsFiat: false,
                UnitPriceUsd: BinanceHoldingsAggregator.UnitPriceUsd(unit, priceMap),
                Fee: fee is > 0m ? fee : null,
                FeeUnit: fee is > 0m ? unit : null,
                CounterpartyRef: leg.TransferType == InternalTransferType ? InternalCounterparty : OnChainCounterparty,
                Status: leg.Status.ToString(CultureInfo.InvariantCulture),
                TxRef: string.IsNullOrWhiteSpace(leg.TxId) ? null : leg.TxId.Trim(),
                Network: string.IsNullOrWhiteSpace(leg.Network) ? null : leg.Network.Trim()));
        }

        var resumeMs = Math.Min(endMs, holdBelowMs - 1);
        return new CryptoFlowPage(flows, DateTimeOffset.FromUnixTimeMilliseconds(Math.Max(resumeMs, beginMs - 1)).UtcDateTime);
    }

    private static async Task<IReadOnlyList<T>> ReadChainPagesAsync<T>(
        Func<int, CancellationToken, Task<IReadOnlyList<T>>> fetch,
        CancellationToken ct)
    {
        var rows = new List<T>();
        for (var page = 0; ; page++)
        {
            if (page == MaxChainPages)
            {
                throw new BinanceException($"Binance returned more than {MaxChainPages} pages of coin history for one window.");
            }

            var data = await fetch(page * ChainPageRows, ct);
            rows.AddRange(data);
            if (data.Count < ChainPageRows)
            {
                return rows;
            }
        }
    }

    private readonly record struct ChainLeg(
        string? Id,
        string Type,
        long AtMs,
        string? Amount,
        string? Coin,
        string? Fee,
        string? TxId,
        string? Network,
        int TransferType,
        int Status);

    private static long? ParseWithdrawTime(string? value) =>
        DateTime.TryParseExact(
            value, WithdrawTimeFormat, CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var at)
            ? ToUnixMs(at)
            : null;

    private static async Task<IReadOnlyList<T>> ReadFiatPagesAsync<T>(
        Func<int, CancellationToken, Task<BinanceFiatPage<T>>> fetch,
        CancellationToken ct)
    {
        var rows = new List<T>();
        for (var page = 1; ; page++)
        {
            if (page > MaxFlowPages)
            {
                throw new BinanceException($"Binance returned more than {MaxFlowPages} pages of fiat history for one window.");
            }

            var response = await fetch(page, ct);
            if (!response.Success || !string.Equals(response.Code, FiatSuccessCode, StringComparison.Ordinal))
            {
                throw new BinanceException(response.Message ?? $"Binance fiat history refused with code {response.Code}.");
            }

            var data = response.Data ?? [];
            rows.AddRange(data);
            if (data.Count < FlowPageRows || rows.Count >= response.Total)
            {
                return rows;
            }
        }
    }

    private static bool IsCompleted(string? status) => status is not null && CompletedFiatStatuses.Contains(status.Trim());

    // A pending leg holds the walk on its window, so it is read again once it settles; one pending longer than
    // MaxPendingHold no longer does, so a stuck row cannot pin the walk.
    private static bool IsHeld(long holdSinceMs, string? status, long createTimeMs) =>
        status is not null && PendingFiatStatuses.Contains(status.Trim()) && createTimeMs >= holdSinceMs;

    private CryptoFlow? FiatOrderFlow(BinanceFiatOrder order, int type)
    {
        var amount = ParseAmount(type == FiatIn ? order.IndicatedAmount : order.Amount);
        return FiatFlow(
            order.OrderNo,
            type == FiatIn ? CryptoFlowTypes.FiatDeposit : CryptoFlowTypes.FiatWithdrawal,
            order.CreateTimeMs,
            amount,
            order.FiatCurrency,
            order.TotalFee,
            order.Method,
            order.Status);
    }

    private CryptoFlow? FiatPaymentFlow(BinanceFiatPayment payment, int type) =>
        FiatFlow(
            payment.OrderNo,
            type == FiatIn ? CryptoFlowTypes.CardBuy : CryptoFlowTypes.CardSell,
            payment.CreateTimeMs,
            ParseAmount(payment.SourceAmount),
            payment.FiatCurrency,
            payment.TotalFee,
            payment.PaymentMethod,
            payment.Status);

    private CryptoFlow? FiatFlow(
        string? eventId,
        string providerType,
        long createTimeMs,
        decimal? amount,
        string? currency,
        string? fee,
        string? counterparty,
        string? status)
    {
        var unit = currency?.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(eventId) || amount is not > 0m || string.IsNullOrEmpty(unit))
        {
            // Left out rather than guessed; the warning makes the gap findable.
            _logger.LogWarning("Binance {ProviderType} {EventId} is unreadable and was skipped.", providerType, eventId);
            return null;
        }

        var feeAmount = ParseAmount(fee);
        return new CryptoFlow(
            EventId: eventId,
            ProviderType: providerType,
            OccurredAt: DateTimeOffset.FromUnixTimeMilliseconds(createTimeMs).UtcDateTime,
            Amount: amount.Value,
            Unit: unit,
            IsFiat: true,
            UnitPriceUsd: null,
            Fee: feeAmount is > 0m ? feeAmount : null,
            FeeUnit: feeAmount is > 0m ? unit : null,
            CounterpartyRef: string.IsNullOrWhiteSpace(counterparty) ? null : counterparty.Trim(),
            Status: status?.Trim() ?? string.Empty);
    }

    private static long ToUnixMs(DateTime utc) =>
        new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeMilliseconds();

    // A plain decimal string only, invariant culture.
    private static decimal? ParseAmount(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    private async Task<T> SafeFetchAsync<T>(Func<Task<T>> fetcher, string label, T fallback)
    {
        try
        {
            return await fetcher();
        }
        catch (BinanceException ex)
        {
            _logger.LogWarning(
                ex,
                "Skipping Binance source '{Source}' — request was rejected (likely missing API-key permission). Sync continues without this data.",
                label);
            return fallback;
        }
    }
}
