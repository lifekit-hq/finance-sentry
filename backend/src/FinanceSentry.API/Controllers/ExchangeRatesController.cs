namespace FinanceSentry.API.Controllers;

using FinanceSentry.Infrastructure.Fx;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

/// <summary>
/// Historical conversion rates, so a UAH-denominated cost can be shown in hard currency
/// as it actually stood on a past date rather than at today's rate.
/// </summary>
[ApiController]
[Authorize]
[Route("exchange-rates")]
public sealed class ExchangeRatesController(IHistoricalExchangeRateService rates) : ControllerBase
{
    /// <summary>Longest window a single request may ask for, to bound the upstream fetch.</summary>
    private const int MaxWindowDays = 366 * 15;

    [HttpGet("history")]
    public async Task<IActionResult> GetHistory(
        [FromQuery] string currency,
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(currency))
            return BadRequest(new { errorCode = "FX_CURRENCY_REQUIRED" });

        if (to < from)
            return BadRequest(new { errorCode = "FX_RANGE_INVALID" });

        if (to.DayNumber - from.DayNumber > MaxWindowDays)
            return BadRequest(new { errorCode = "FX_RANGE_TOO_LARGE" });

        var series = await rates.GetDailySeriesAsync(currency, from, to, ct);

        var points = series
            .OrderBy(kv => kv.Key)
            .Select(kv => new FxRatePoint(kv.Key, kv.Value, Invert(kv.Value)))
            .ToList();

        return Ok(new FxHistoryResponse(currency.ToUpperInvariant(), "USD", points));
    }

    /// <summary>Units of the foreign currency per 1 USD — the way a rate is usually quoted.</summary>
    private static decimal? Invert(decimal usdPerUnit) =>
        usdPerUnit > 0m ? Math.Round(1m / usdPerUnit, 4) : null;
}

public record FxRatePoint(DateOnly Date, decimal UsdPerUnit, decimal? UnitsPerUsd);

public record FxHistoryResponse(string Currency, string QuoteCurrency, IReadOnlyList<FxRatePoint> Points);
