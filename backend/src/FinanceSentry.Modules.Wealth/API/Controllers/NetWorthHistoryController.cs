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
public class NetWorthHistoryController(
    IQueryHandler<GetNetWorthHistoryQuery, NetWorthHistoryResponse> handler,
    IUserBaseCurrencyReader baseCurrencyReader,
    IHistoricalExchangeRateService rates) : ControllerBase
{
    private readonly IHistoricalExchangeRateService _rates = rates ?? throw new ArgumentNullException(nameof(rates));
    private readonly IUserBaseCurrencyReader _baseCurrencyReader = baseCurrencyReader ?? throw new ArgumentNullException(nameof(baseCurrencyReader));
    private readonly IQueryHandler<GetNetWorthHistoryQuery, NetWorthHistoryResponse> _handler
        = handler ?? throw new ArgumentNullException(nameof(handler));

    [HttpGet("history")]
    public async Task<IActionResult> GetHistory(
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null,
        CancellationToken ct = default)
    {
        var userId = User.RequireUserId();
        var result = await _handler.Handle(new GetNetWorthHistoryQuery(userId, from, to), ct);
        var baseCurrency = CurrencyConverter.ResolveBase(await _baseCurrencyReader.GetAsync(userId, ct));
        if (baseCurrency == "USD")
            return Ok(result);

        // Snapshots are stored in USD; the profile's base currency is applied at the boundary at the rate
        // published for each row's own date (C4), looked up once for the whole window. A row no feed
        // published a rate for within the lookback falls back to today's rate and says so; a row dated today
        // is shown at today's live rate, as the dashboard hero is.
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var todayStatus = CurrencyConverter.ConvertFromUsd(1m, baseCurrency).Status;
        var pastDates = result.Snapshots.Where(s => s.SnapshotDate < today).Select(s => s.SnapshotDate).ToList();
        var published = pastDates.Count == 0
            ? new Dictionary<DateOnly, decimal>()
            : await _rates.GetPublishedSeriesAsync(baseCurrency, pastDates.Min(), pastDates.Max(), ct);

        (NetWorthSnapshotDto Row, FxStatus Status) Convert(NetWorthSnapshotDto s)
        {
            Func<decimal, decimal> fx;
            FxStatus status;
            if (s.SnapshotDate < today && PublishedRateLookup.Resolve(published, s.SnapshotDate) is { } dated)
            {
                // The stored rate is USD per unit of the base currency, so USD / rate is the base amount.
                fx = usd => usd / dated.Rate;
                status = dated.Status;
            }
            else
            {
                fx = usd => CurrencyConverter.FromUsd(usd, baseCurrency);
                status = s.SnapshotDate < today ? todayStatus.Worst(FxStatus.TodayRate) : todayStatus;
            }

            var added = status == FxStatus.NearestPublished ? FxReasons.NearestPublished : status.ToReason();
            decimal? FxOrNull(decimal? usd) => usd is null ? null : fx(usd.Value);
            return (s with
            {
                BankingTotal = fx(s.BankingTotal),
                BrokerageTotal = fx(s.BrokerageTotal),
                CryptoTotal = fx(s.CryptoTotal),
                TotalNetWorth = fx(s.TotalNetWorth),
                CashTotal = FxOrNull(s.CashTotal),
                BrokerageInvested = FxOrNull(s.BrokerageInvested),
                CryptoInvested = FxOrNull(s.CryptoInvested),
                Currency = baseCurrency,
                FxReasons = Reasons(s.FxReasons, added),
            }, status);
        }

        var converted = result.Snapshots.Select(Convert).ToList();

        // The change is read off the converted end rows, so it always equals what the rows show.
        Headline? change = null;
        if (result.ChangeHeadline is { } h)
        {
            change = converted.Count == 0
                ? h
                : HeadlineFactory.ChangeInBaseCurrency(
                    h,
                    converted[0].Row.TotalNetWorth,
                    converted[^1].Row.TotalNetWorth,
                    baseCurrency,
                    converted[0].Status.Worst(converted[^1].Status));
        }

        return Ok(result with { Snapshots = [.. converted.Select(c => c.Row)], ChangeHeadline = change });
    }

    /// <summary>The row's stored reasons plus the one its display conversion added: the single worst reason.</summary>
    private static IReadOnlyList<string>? Reasons(IReadOnlyList<string>? carried, string? added)
    {
        if (added is null)
            return carried;

        var worst = added;
        foreach (var reason in carried ?? [])
            worst = FxReasonOrder.Worse(worst, reason);
        return [worst!];
    }
}
