using FinanceSentry.Modules.CryptoSync.Domain;
using FinanceSentry.Modules.CryptoSync.Domain.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FinanceSentry.Modules.CryptoSync.Infrastructure.RevolutX;

/// <summary>
/// Revolut X behind the same seam as Binance (#472). The API secret this adapter receives is the
/// user's Ed25519 private key (PEM); the API key is the id Revolut X issued for its public half.
/// </summary>
public sealed class RevolutXAdapter(
    RevolutXHttpClient httpClient,
    RevolutXHoldingsAggregator aggregator,
    ILogger<RevolutXAdapter> logger,
    IConfiguration configuration) : ICryptoExchangeAdapter
{
    private const decimal DefaultDustThresholdUsd = 0.01m;

    private readonly decimal _dustThresholdUsd = decimal.TryParse(
        configuration["RevolutX:DustThresholdUsd"],
        System.Globalization.NumberStyles.Number,
        System.Globalization.CultureInfo.InvariantCulture,
        out var threshold) ? threshold : DefaultDustThresholdUsd;

    public string ExchangeName => CryptoExchangeProvider.RevolutX;

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

        if (snapshot.FiatBalancesExcluded.Count > 0)
        {
            logger.LogInformation(
                "Revolut X fiat balances not synced as holdings (venue cash lands with #472's follow-up): {Currencies}",
                string.Join(", ", snapshot.FiatBalancesExcluded));
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
    /// Trade ingestion is #472's second PR. Revolut X serves at most a one-week window of fills,
    /// so history before connect is unrecoverable either way and cost basis stays null for those
    /// lots. Until ingestion lands this reports no trades and leaves the cursor where it was, so
    /// the sync writes no cost basis rather than a guessed one.
    /// </summary>
    public Task<CryptoTradePage> GetTradesAsync(
        string apiKey,
        string apiSecret,
        string asset,
        string? cursor,
        CancellationToken ct = default) =>
        Task.FromResult(new CryptoTradePage([], cursor));
}
