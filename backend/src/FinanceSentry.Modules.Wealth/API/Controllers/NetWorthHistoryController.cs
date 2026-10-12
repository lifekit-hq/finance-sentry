namespace FinanceSentry.Modules.Wealth.API.Controllers;

using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Core.Utils;
using FinanceSentry.Modules.Wealth.Application.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Authorize]
[Route("net-worth")]
public class NetWorthHistoryController(
    IQueryHandler<GetNetWorthHistoryQuery, NetWorthHistoryResponse> handler,
    IUserBaseCurrencyReader baseCurrencyReader) : ControllerBase
{
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

        // Snapshots are stored in USD; the profile's base currency is applied at the boundary.
        decimal Fx(decimal usd) => CurrencyConverter.FromUsd(usd, baseCurrency);
        decimal? FxOrNull(decimal? usd) => usd is null ? null : Fx(usd.Value);
        return Ok(result with
        {
            Snapshots = [.. result.Snapshots.Select(s => s with
            {
                BankingTotal = Fx(s.BankingTotal),
                BrokerageTotal = Fx(s.BrokerageTotal),
                CryptoTotal = Fx(s.CryptoTotal),
                TotalNetWorth = Fx(s.TotalNetWorth),
                CashTotal = FxOrNull(s.CashTotal),
                BrokerageInvested = FxOrNull(s.BrokerageInvested),
                CryptoInvested = FxOrNull(s.CryptoInvested),
                Currency = baseCurrency,
            })],
        });
    }
}
