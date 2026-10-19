namespace FinanceSentry.Modules.Wealth.API.Controllers;

using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Headlines;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Core.Utils;
using FinanceSentry.Infrastructure.Fx;
using FinanceSentry.Modules.Wealth.Application.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Authorize]
[Route("net-worth")]
public class NetWorthBridgeController(
    IQueryHandler<GetNetWorthBridgeQuery, NetWorthBridgeResponse> handler,
    IUserBaseCurrencyReader baseCurrencyReader,
    IHistoricalExchangeRateService rates) : ControllerBase
{
    private readonly IHistoricalExchangeRateService _rates = rates ?? throw new ArgumentNullException(nameof(rates));
    private readonly IUserBaseCurrencyReader _baseCurrencyReader = baseCurrencyReader ?? throw new ArgumentNullException(nameof(baseCurrencyReader));
    private readonly IQueryHandler<GetNetWorthBridgeQuery, NetWorthBridgeResponse> _handler
        = handler ?? throw new ArgumentNullException(nameof(handler));

    /// <summary>
    /// The net-worth bridge (M5) between the first and last snapshot dated <paramref name="from"/> through
    /// <paramref name="to"/>, the window <c>GET /net-worth/history</c> takes: <c>GET /api/v1/net-worth/bridge</c>.
    /// </summary>
    [HttpGet("bridge")]
    public async Task<IActionResult> GetBridge(
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null,
        CancellationToken ct = default)
    {
        var userId = User.RequireUserId();
        var result = await _handler.Handle(new GetNetWorthBridgeQuery(userId, from, to), ct);
        var baseCurrency = CurrencyConverter.ResolveBase(await _baseCurrencyReader.GetAsync(userId, ct));
        if (baseCurrency == "USD" || result is not { From: { } opening, To: { } closing, StartValue: { } start })
            return Ok(result);

        // Each end is shown at its own date's rate, as the history rows are; every line between them at the closing
        // rate. The opening and closing rates differ, so the FX line also carries the start value's re-statement
        // (start × (closing factor − opening factor)): the lines still sum to the converted End − Start.
        var published = await _rates.GetPublishedSeriesAsync(baseCurrency, opening, closing, ct);
        var (openingFactor, openingStatus) = Factor(published, baseCurrency, opening);
        var (closingFactor, closingStatus) = Factor(published, baseCurrency, closing);
        decimal? AtClosing(decimal? usd) => usd * closingFactor;
        var assessment = HeadlineQualityMapper.WithFx(
            new HeadlineAssessment(result.Quality, result.Reasons), amountAvailable: true, openingStatus.Worst(closingStatus));

        return Ok(result with
        {
            StartValue = start * openingFactor,
            EndValue = AtClosing(result.EndValue),
            Savings = AtClosing(result.Savings),
            InvestmentReturn = AtClosing(result.InvestmentReturn),
            Market = AtClosing(result.Market),
            Income = AtClosing(result.Income),
            Fees = AtClosing(result.Fees),
            Taxes = AtClosing(result.Taxes),
            FxTranslation = AtClosing(result.FxTranslation) + (start * (closingFactor - openingFactor)),
            Revaluation = AtClosing(result.Revaluation),
            NewlyLinked = AtClosing(result.NewlyLinked),
            Unlinked = AtClosing(result.Unlinked),
            Residual = AtClosing(result.Residual),
            Quality = assessment.Quality,
            Reasons = assessment.Reasons,
            Currency = baseCurrency,
        });
    }

    // Base-currency units per USD on a date: the rate the feed published for it (stored as USD per unit of the base),
    // else today's rate, named as such for a past date. A date of today takes today's live rate.
    private static (decimal Factor, FxStatus Status) Factor(
        IReadOnlyDictionary<DateOnly, decimal> published, string baseCurrency, DateOnly date)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (date < today && PublishedRateLookup.Resolve(published, date) is { Rate: > 0m } rate)
            return (1m / rate.Rate, rate.Status);

        var live = CurrencyConverter.ConvertFromUsd(1m, baseCurrency);
        return (live.Amount, date < today ? live.Status.Worst(FxStatus.TodayRate) : live.Status);
    }
}
