using System.Globalization;
using FinanceSentry.Core.Utils;
using FinanceSentry.Modules.CryptoSync.Application.Services;
using FinanceSentry.Modules.CryptoSync.Domain;
using FinanceSentry.Modules.CryptoSync.Domain.Exceptions;
using FinanceSentry.Modules.CryptoSync.Domain.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FinanceSentry.Modules.CryptoSync.Infrastructure.RevolutX;

/// <summary>
/// Revolut X behind the same seam as Binance (#472). The API secret this adapter receives is the
/// user's Ed25519 private key (PEM); the API key is the id Revolut X issued for its public half.
/// </summary>
public sealed class RevolutXAdapter(
    RevolutXHttpClient httpClient,
    RevolutXHoldingsAggregator aggregator,
    ILogger<RevolutXAdapter> logger,
    IOptions<RevolutXOptions> options) : ICryptoExchangeAdapter
{

    // The venue serves at most one week of fills per call.
    private const long WindowMs = 7L * 24 * 60 * 60 * 1000 - 2;

    // Half a year of windows: a walk that far behind finishes over the next runs.
    private const int MaxWindowsPerCall = 26;
    private const int MaxPagesPerWindow = 50;
    private const int PageLimit = 1000;
    private const string Usd = "USD";
    private const string BuySide = "buy";
    private const string SellSide = "sell";

    // The transactions endpoint serves at most 30 days per call; the walk widens each window by 1 ms on both sides.
    private const long FlowWindowMs = 30L * 24 * 60 * 60 * 1000 - 2;
    private const int MaxFlowWindowsPerCall = 12;
    private const int FlowPageLimit = 1900;
    private const string TransferTypes = "receive,send";
    private const string CompletedStatus = "completed";
    private const string PendingStatus = "pending";
    private const string ReceiveType = "receive";
    private const string SendType = "send";
    private const string RewardType = "reward";

    // A transfer pending longer than this stops holding the walk back, so a stuck row cannot pin it forever.
    private static readonly TimeSpan MaxPendingHold = TimeSpan.FromDays(30);

    private static readonly string[] UsdStablecoins = ["USDC", "USDT"];

    private readonly decimal _dustThresholdUsd = options.Value.DustThresholdUsd;

    public string ExchangeName => CryptoExchangeProvider.RevolutX;

    public bool TradeHistoryStartsAtConnect => true;

    /// <summary>A live, signed <c>GET /balances</c>: proves the key pair is registered and usable.</summary>
    public async Task ValidateCredentialsAsync(string apiKey, string apiSecret, CancellationToken ct = default)
    {
        var credentials = RevolutXSigner.ParseCredentials(apiKey, apiSecret);
        await httpClient.GetBalancesAsync(credentials, ct);
    }

    public async Task<IReadOnlyList<CryptoAssetBalance>> GetHoldingsAsync(
        string apiKey,
        string apiSecret,
        CancellationToken ct = default)
    {
        var credentials = RevolutXSigner.ParseCredentials(apiKey, apiSecret);

        var balancesTask = httpClient.GetBalancesAsync(credentials, ct);
        var currenciesTask = httpClient.GetCurrenciesAsync(credentials, ct);
        var tickersTask = httpClient.GetTickersAsync(credentials, ct);
        await Task.WhenAll(balancesTask, currenciesTask, tickersTask);

        var snapshot = aggregator.Aggregate(
            balancesTask.Result,
            currenciesTask.Result,
            tickersTask.Result.Data,
            _dustThresholdUsd);

        if (snapshot.UnratedFiat.Count > 0)
        {
            logger.LogWarning(
                "Revolut X fiat balances valued 1:1 because no FX rate is known for them: {Currencies}",
                string.Join(", ", snapshot.UnratedFiat));
        }

        if (snapshot.UnpricedAssets.Count > 0)
        {
            logger.LogWarning(
                "Revolut X assets skipped because no USD-convertible ticker prices them: {Assets}",
                string.Join(", ", snapshot.UnpricedAssets));
        }

        return snapshot.Holdings;
    }

    /// <summary>
    /// The user's fills for <paramref name="asset"/> since the cursor (or since connect, for a
    /// never-walked holding), up to <see cref="CryptoTradeWalk.AsOf"/>. Revolut X serves one pair
    /// per call and at most a one-week window, so this walks every pair that sells
    /// <paramref name="asset"/> for a USD-valued quote, window by window; history before connect is
    /// out of reach, which is what the forward ledger is for.
    ///
    /// <list type="bullet">
    /// <item><b>Pairs.</b> Only pairs quoted in USD, a USD stablecoin (at par) or a fiat currency
    /// <see cref="CurrencyConverter"/> knows. A fill on any other pair (a crypto quote) cannot be
    /// priced in USD, so it is not returned: the ledger sees its quantity as unpriced.</item>
    /// <item><b>Windows.</b> Each window is asked for one millisecond wider on both sides and the
    /// fills filtered back to it, so a fill on a boundary is counted exactly once whether the
    /// venue treats the bounds as inclusive or exclusive.</item>
    /// <item><b>Failure.</b> Any call failing throws: the cursor stays where it was.</item>
    /// </list>
    /// </summary>
    public async Task<CryptoTradePage> GetTradesAsync(
        string apiKey,
        string apiSecret,
        string asset,
        string? cursor,
        CryptoTradeWalk walk,
        CancellationToken ct = default)
    {
        var credentials = RevolutXSigner.ParseCredentials(apiKey, apiSecret);
        var baseAsset = asset.Trim().ToUpperInvariant();

        var asOfMs = ToUnixMs(walk.AsOf);
        var startMs = RevolutXTradeCursor.Parse(cursor) is { } walkedTo
            ? walkedTo + 1
            : ToUnixMs(walk.TrackedSince);

        if (startMs > asOfMs)
        {
            return new CryptoTradePage([], cursor ?? RevolutXTradeCursor.Format(startMs - 1));
        }

        var pairs = await httpClient.GetPairsAsync(credentials, ct);
        var symbols = pairs.Values
            .Where(p => string.Equals(p.Base, baseAsset, StringComparison.OrdinalIgnoreCase)
                && IsUsdValued(p.Quote))
            .Select(p => $"{p.Base.ToUpperInvariant()}-{p.Quote.ToUpperInvariant()}")
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        var fills = new Dictionary<string, CryptoTrade>(StringComparer.Ordinal);
        var windowStart = startMs;
        var windows = 0;
        var walkedToMs = startMs - 1;

        while (windowStart <= asOfMs && windows < MaxWindowsPerCall)
        {
            var windowEnd = Math.Min(windowStart + WindowMs - 1, asOfMs);

            foreach (var symbol in symbols)
            {
                await WalkWindowAsync(credentials, symbol, baseAsset, windowStart, windowEnd, fills, ct);
            }

            walkedToMs = windowEnd;
            windowStart = windowEnd + 1;
            windows++;
        }

        var trades = fills.Values
            .OrderBy(t => t.Timestamp)
            .ThenBy(t => t.TradeId, StringComparer.Ordinal)
            .ToList();

        return new CryptoTradePage(
            trades,
            RevolutXTradeCursor.Format(walkedToMs),
            IsComplete: walkedToMs >= asOfMs);
    }

    /// <summary>
    /// The account's completed <c>receive</c>, <c>send</c> and <c>reward</c> (staking: income, not a transfer) transactions created in the walk, window by window (one
    /// millisecond wider on both sides, filtered back, as the fills walk does). Each leg's account type is what makes a
    /// transfer readable: the leg that is not <c>revolut_x</c> is the counterparty (<c>revolut</c>: the owner's own
    /// Revolut app; <c>external_*</c>: outside it). The amount is the venue-side leg, credited for a receive and
    /// debited for a send. A coin transfer with an external crypto wallet also takes its chain hash, network and
    /// fee from its details (<see cref="ChainDetailsAsync"/>), so another venue's leg can be matched to it.
    /// </summary>
    public async Task<CryptoFlowPage> GetFlowsAsync(
        string apiKey,
        string apiSecret,
        CryptoFlowWalk walk,
        CancellationToken ct = default)
    {
        var credentials = RevolutXSigner.ParseCredentials(apiKey, apiSecret);
        var asOfMs = ToUnixMs(walk.AsOf);
        var windowStart = ToUnixMs(walk.From);
        var walkedToMs = windowStart - 1;
        var holdBelowMs = long.MaxValue;
        var holdSinceMs = ToUnixMs(walk.AsOf - MaxPendingHold);
        var completed = new Dictionary<string, RevolutXTransaction>(StringComparer.Ordinal);

        for (var windows = 0; windowStart <= asOfMs && windows < MaxFlowWindowsPerCall; windows++)
        {
            var windowEnd = Math.Min(windowStart + FlowWindowMs - 1, asOfMs);
            var rows = new List<RevolutXTransaction>();
            await foreach (var row in TransactionsInWindowAsync(credentials, TransferTypes, windowStart, windowEnd, ct))
            {
                rows.Add(row);
            }

            rows.AddRange(await RewardsInWindowAsync(credentials, windowStart, windowEnd, ct));

            foreach (var row in rows)
            {
                var status = row.Status?.Trim().ToLowerInvariant();
                if (status == CompletedStatus)
                {
                    completed.TryAdd(row.Id, row);
                }
                else if (status == PendingStatus && row.CreatedDateMs >= holdSinceMs)
                {
                    holdBelowMs = Math.Min(holdBelowMs, row.CreatedDateMs);
                }
            }

            walkedToMs = windowEnd;
            windowStart = windowEnd + 1;
        }

        IReadOnlyList<CryptoFlow> flows = completed.Count == 0 ? [] : await ToFlowsAsync(credentials, completed.Values, ct);
        var resumeMs = Math.Min(walkedToMs, holdBelowMs - 1);
        return new CryptoFlowPage(
            flows,
            DateTimeOffset.FromUnixTimeMilliseconds(Math.Max(resumeMs, ToUnixMs(walk.From) - 1)).UtcDateTime);
    }

    private async Task<List<RevolutXTransaction>> RewardsInWindowAsync(
        RevolutXCredentials credentials,
        long startMs,
        long endMs,
        CancellationToken ct)
    {
        var rows = new List<RevolutXTransaction>();
        try
        {
            await foreach (var row in TransactionsInWindowAsync(credentials, RewardType, startMs, endMs, ct))
            {
                rows.Add(row);
            }
        }
        catch (RevolutXException ex) when (ex.VenueStatusCode is >= 400 and < 500 and not 429)
        {
            logger.LogWarning(ex, "Skipping Revolut X rewards - the request was rejected.");
            return [];
        }

        return rows;
    }

    private async IAsyncEnumerable<RevolutXTransaction> TransactionsInWindowAsync(
        RevolutXCredentials credentials,
        string types,
        long startMs,
        long endMs,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        string? pageCursor = null;
        for (var page = 0; ; page++)
        {
            if (page == MaxPagesPerWindow)
            {
                throw new RevolutXException(
                    $"Revolut X returned more than {MaxPagesPerWindow} pages of transactions for one window.");
            }

            var response = await httpClient.GetTransactionsAsync(
                credentials, types, startMs - 1, endMs + 1, pageCursor, FlowPageLimit, ct);

            foreach (var row in response.Data ?? [])
            {
                if (!string.IsNullOrWhiteSpace(row.Id) && row.CreatedDateMs >= startMs && row.CreatedDateMs <= endMs)
                {
                    yield return row;
                }
            }

            pageCursor = response.Metadata?.NextCursor;
            if (string.IsNullOrEmpty(pageCursor))
            {
                yield break;
            }
        }
    }

    private async Task<IReadOnlyList<CryptoFlow>> ToFlowsAsync(
        RevolutXCredentials credentials,
        IEnumerable<RevolutXTransaction> rows,
        CancellationToken ct)
    {
        var currenciesTask = httpClient.GetCurrenciesAsync(credentials, ct);
        var tickersTask = httpClient.GetTickersAsync(credentials, ct);
        await Task.WhenAll(currenciesTask, tickersTask);

        var flows = new List<CryptoFlow>();
        foreach (var row in rows.OrderBy(r => r.CreatedDateMs).ThenBy(r => r.Id, StringComparer.Ordinal))
        {
            var type = row.Type?.Trim().ToLowerInvariant();
            var (own, other) = type switch
            {
                ReceiveType => (row.Destination, row.Source),
                SendType => (row.Source, row.Destination),
                // A reward has a destination leg only: the asset credited, in the Revolut X account.
                RewardType => (row.Destination, null),
                _ => (null, null),
            };
            var unit = own?.Currency?.Trim().ToUpperInvariant();
            var amount = ParseAmount(own?.Amount);

            if (own is null || string.IsNullOrEmpty(unit) || amount is not > 0m)
            {
                // A row the reader cannot value is left out rather than guessed; the sleeve's change then reads as
                // growth, which the warning makes findable.
                logger.LogWarning("Revolut X transaction {TransactionId} is unreadable and was skipped.", row.Id);
                continue;
            }

            var isFiat = RevolutXHoldingsAggregator.IsFiat(unit, currenciesTask.Result);
            var counterparty = other?.Account?.Type?.Trim().ToLowerInvariant();
            var chain = !isFiat && CryptoFlowClassifier.IsExternalWalletType(counterparty)
                ? await ChainDetailsAsync(credentials, row.Id, type == SendType, unit, amount.Value, ct)
                : null;
            flows.Add(new CryptoFlow(
                EventId: row.Id,
                ProviderType: type switch { ReceiveType => CryptoFlowTypes.Receive, RewardType => CryptoFlowTypes.Reward, _ => CryptoFlowTypes.Send },
                OccurredAt: DateTimeOffset.FromUnixTimeMilliseconds(row.ProcessedDateMs ?? row.CreatedDateMs).UtcDateTime,
                Amount: chain?.Amount ?? amount.Value,
                Unit: unit,
                IsFiat: isFiat,
                UnitPriceUsd: isFiat ? null : RevolutXHoldingsAggregator.UnitPriceUsd(unit, tickersTask.Result.Data),
                Fee: chain?.Fee,
                FeeUnit: chain?.Fee is null ? null : unit,
                CounterpartyRef: counterparty,
                Status: CompletedStatus,
                TxRef: chain?.TxRef,
                Network: chain?.Network));
        }

        return flows;
    }

    /// <summary>
    /// The chain hash and network of a transfer with an external crypto wallet (S2b), read from the transaction's
    /// details; the list leaves them out. A send's listed amount is what left the account, Revolut's own fee
    /// included: when the details name that fee in the sent unit, the amount becomes what went on chain and the
    /// fee is kept apart, the way a Binance withdrawal is recorded.
    /// </summary>
    private async Task<ChainDetails> ChainDetailsAsync(
        RevolutXCredentials credentials,
        string transactionId,
        bool isSend,
        string unit,
        decimal listedAmount,
        CancellationToken ct)
    {
        var details = await httpClient.GetTransactionDetailsAsync(credentials, transactionId, ct);
        var source = details.Source;
        var fee = isSend
            && string.Equals(source?.FeeCurrency?.Trim(), unit, StringComparison.OrdinalIgnoreCase)
            && ParseAmount(source?.Fee) is > 0m and var kept
            && kept < listedAmount
                ? kept
                : (decimal?)null;
        return new ChainDetails(
            fee is { } f ? listedAmount - f : listedAmount,
            fee,
            string.IsNullOrWhiteSpace(details.CryptoTransactionHash) ? null : details.CryptoTransactionHash.Trim(),
            string.IsNullOrWhiteSpace(details.Network) ? null : details.Network.Trim());
    }

    private sealed record ChainDetails(decimal Amount, decimal? Fee, string? TxRef, string? Network);

    private async Task WalkWindowAsync(
        RevolutXCredentials credentials,
        string symbol,
        string baseAsset,
        long startMs,
        long endMs,
        Dictionary<string, CryptoTrade> fills,
        CancellationToken ct)
    {
        string? pageCursor = null;
        for (var page = 0; ; page++)
        {
            if (page == MaxPagesPerWindow)
            {
                throw new RevolutXException(
                    $"Revolut X returned more than {MaxPagesPerWindow} pages of {symbol} fills for one window.");
            }

            var response = await httpClient.GetPrivateTradesAsync(
                credentials, symbol, startMs - 1, endMs + 1, pageCursor, PageLimit, ct);

            foreach (var row in response.Data ?? [])
            {
                if (row.TradeTimestampMs < startMs || row.TradeTimestampMs > endMs)
                {
                    continue;
                }

                if (ToTrade(row, symbol, baseAsset) is { } trade)
                {
                    fills.TryAdd(trade.TradeId, trade);
                }
            }

            pageCursor = response.Metadata?.NextCursor;
            if (string.IsNullOrEmpty(pageCursor))
            {
                return;
            }
        }
    }

    private CryptoTrade? ToTrade(RevolutXTrade row, string symbol, string baseAsset)
    {
        var quantity = ParseAmount(row.Quantity);
        var price = ParseAmount(row.Price);
        var quote = row.PriceCurrency?.Trim().ToUpperInvariant();
        var side = row.Side?.Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(row.TradeId)
            || quantity is not > 0m
            || price is not > 0m
            || string.IsNullOrEmpty(quote)
            || !IsUsdValued(quote)
            || (side is not (BuySide or SellSide))
            || (row.QuantityCurrency is { } baseOnFill
                && !string.Equals(baseOnFill.Trim(), baseAsset, StringComparison.OrdinalIgnoreCase)))
        {
            // A fill the ledger cannot price is left out rather than guessed; its quantity then
            // reaches the ledger as unpriced.
            logger.LogWarning(
                "Revolut X fill {TradeId} on {Symbol} is unreadable and was skipped.", row.TradeId, symbol);
            return null;
        }

        var priceUsd = ToUsd(price.Value, quote);
        return new CryptoTrade(
            TradeId: row.TradeId,
            Asset: baseAsset,
            QuoteAsset: quote,
            Quantity: quantity.Value,
            PriceUsd: priceUsd,
            QuoteQuantityUsd: ToUsd(quantity.Value * price.Value, quote),
            IsBuyer: side == BuySide,
            Timestamp: DateTimeOffset.FromUnixTimeMilliseconds(row.TradeTimestampMs).UtcDateTime);
    }

    /// <summary>USD as-is, a USD stablecoin at par, any other fiat through the FX table.</summary>
    private static bool IsUsdValued(string quote) =>
        string.Equals(quote, Usd, StringComparison.OrdinalIgnoreCase)
        || UsdStablecoins.Contains(quote, StringComparer.OrdinalIgnoreCase)
        || CurrencyConverter.IsKnown(quote);

    private static decimal ToUsd(decimal amount, string quote) =>
        string.Equals(quote, Usd, StringComparison.OrdinalIgnoreCase)
        || UsdStablecoins.Contains(quote, StringComparer.OrdinalIgnoreCase)
            ? amount
            : CurrencyConverter.ToUsd(amount, quote);

    private static long ToUnixMs(DateTime utc) =>
        new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeMilliseconds();

    // A plain decimal string only: no thousands separators, which would silently turn "1,5" into 15.
    private static decimal? ParseAmount(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
}
