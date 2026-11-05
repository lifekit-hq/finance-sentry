namespace FinanceSentry.Modules.Wealth.API.Controllers;

using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Core.Utils;
using FinanceSentry.Modules.Wealth.Application.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

/// <summary>
/// The brokerage sleeve's realized and unrealized P&amp;L on its net deposited, the portfolio header's gain figures
/// (issue 982): <c>GET /api/v1/wealth/brokerage/pnl</c>. Computed in USD; shown in the profile's base currency at
/// today's rate, as the growth split is.
/// </summary>
[ApiController]
[Authorize]
[Route("wealth/brokerage/pnl")]
public class BrokeragePnlController(
    IQueryHandler<GetBrokeragePnlQuery, BrokeragePnlResponse> handler,
    IUserBaseCurrencyReader baseCurrencyReader) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var userId = User.RequireUserId();
        var result = await handler.Handle(new GetBrokeragePnlQuery(userId), ct);
        var baseCurrency = CurrencyConverter.ResolveBase(await baseCurrencyReader.GetAsync(userId, ct));
        return Ok(InBaseCurrency(result, baseCurrency));
    }

    private static BrokeragePnlResponse InBaseCurrency(BrokeragePnlResponse pnl, string baseCurrency)
    {
        if (baseCurrency == "USD")
        {
            return pnl;
        }

        decimal? Fx(decimal? usd) => usd is { } v ? CurrencyConverter.FromUsd(v, baseCurrency) : null;
        return pnl with
        {
            NetDepositedUsd = Fx(pnl.NetDepositedUsd),
            Realized = pnl.Realized with { Usd = Fx(pnl.Realized.Usd) },
            Unrealized = pnl.Unrealized with { Usd = Fx(pnl.Unrealized.Usd) },
            Currency = baseCurrency,
        };
    }
}
