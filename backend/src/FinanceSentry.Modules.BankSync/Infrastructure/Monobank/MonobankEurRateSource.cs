namespace FinanceSentry.Modules.BankSync.Infrastructure.Monobank;

using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FinanceSentry.Modules.BankSync.Application.Services;
using Microsoft.Extensions.Logging;

/// <summary>
/// Monobank's public currency endpoint (<c>/bank/currency</c>, no token), pair EUR (978) to UAH (980), field
/// <c>rateBuy</c>. The endpoint is rate limited to one call a minute and the bank updates it slowly, so the answer is
/// cached for <see cref="CacheTtl"/>; when a refresh fails the last answer stays in use, and its publication time (shown
/// to the user) says how old it is.
/// </summary>
public sealed class MonobankEurRateSource(
    IHttpClientFactory httpClientFactory, TimeProvider timeProvider, ILogger<MonobankEurRateSource> logger) : IEurUahRateSource
{
    public const string HttpClientName = "monobank-public";

    public static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    private const int EurCode = 978;
    private const int UahCode = 980;

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private EurUahBuyRate? _cached;
    private DateTimeOffset _fetchedAt = DateTimeOffset.MinValue;

    public async Task<EurUahBuyRate?> GetAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var now = timeProvider.GetUtcNow();
            if (_cached is not null && now - _fetchedAt < CacheTtl) return _cached;

            // Stamped before the call: a failing endpoint is retried after one TTL, not on every request.
            _fetchedAt = now;
            try
            {
                var fetched = await FetchAsync(cancellationToken);
                if (fetched is not null) _cached = fetched;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                logger.LogWarning(ex, "Monobank currency endpoint unavailable; using the last known EUR rate if any.");
            }

            return _cached;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<EurUahBuyRate?> FetchAsync(CancellationToken ct)
    {
        using var http = httpClientFactory.CreateClient(HttpClientName);
        var rows = await http.GetFromJsonAsync<List<CurrencyRow>>("/bank/currency", JsonOpts, ct);
        var row = rows?.FirstOrDefault(r => r.CurrencyCodeA == EurCode && r.CurrencyCodeB == UahCode);
        return row is { RateBuy: > 0m }
            ? new EurUahBuyRate(row.RateBuy.Value, DateTimeOffset.FromUnixTimeSeconds(row.Date))
            : null;
    }

    private sealed record CurrencyRow(
        [property: JsonPropertyName("currencyCodeA")] int CurrencyCodeA,
        [property: JsonPropertyName("currencyCodeB")] int CurrencyCodeB,
        [property: JsonPropertyName("date")] long Date,
        [property: JsonPropertyName("rateBuy")] decimal? RateBuy);
}
