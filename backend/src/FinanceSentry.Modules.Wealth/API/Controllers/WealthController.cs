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
[Route("wealth")]
public class WealthController(
    IQueryHandler<GetWealthSummaryQuery, WealthSummaryResponse> wealthSummaryHandler,
    IQueryHandler<GetTransactionSummaryQuery, TransactionSummaryResponse> txSummaryHandler,
    IQueryHandler<GetFireProjectionQuery, FireProjectionResponse> fireProjectionHandler,
    IUserBaseCurrencyReader baseCurrencyReader) : ControllerBase
{
    private readonly IUserBaseCurrencyReader _baseCurrencyReader = baseCurrencyReader ?? throw new ArgumentNullException(nameof(baseCurrencyReader));
    private readonly IQueryHandler<GetWealthSummaryQuery, WealthSummaryResponse> _wealthSummaryHandler = wealthSummaryHandler ?? throw new ArgumentNullException(nameof(wealthSummaryHandler));
    private readonly IQueryHandler<GetTransactionSummaryQuery, TransactionSummaryResponse> _txSummaryHandler = txSummaryHandler ?? throw new ArgumentNullException(nameof(txSummaryHandler));
    private readonly IQueryHandler<GetFireProjectionQuery, FireProjectionResponse> _fireProjectionHandler = fireProjectionHandler ?? throw new ArgumentNullException(nameof(fireProjectionHandler));

    private static readonly HashSet<string> AllowedCategories =
        new(StringComparer.OrdinalIgnoreCase) { "banking", "crypto", "brokerage", "other" };

    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary(
        [FromQuery] string? category,
        [FromQuery] string? provider,
        CancellationToken ct)
    {
        if (category is not null && !AllowedCategories.Contains(category))
            return BadRequest(new { error = "Invalid category value. Allowed: banking, crypto, brokerage, other.", errorCode = "INVALID_FILTER" });

        var userId = User.RequireUserId();
        var result = await _wealthSummaryHandler.Handle(new GetWealthSummaryQuery(userId, category, provider), ct);
        var baseCurrency = CurrencyConverter.ResolveBase(await _baseCurrencyReader.GetAsync(userId, ct));
        return Ok(InBaseCurrency(result, baseCurrency));
    }

    // The aggregation speaks USD; the profile's base currency is applied once, here. USD (or an
    // unset profile) is an identity, so nobody without a setting sees a change.
    private static WealthSummaryResponse InBaseCurrency(WealthSummaryResponse summary, string baseCurrency)
    {
        if (baseCurrency == "USD")
            return summary with { BaseCurrency = baseCurrency };

        decimal Fx(decimal usd) => CurrencyConverter.FromUsd(usd, baseCurrency);
        AccountBalanceDto Account(AccountBalanceDto a) =>
            a with { BalanceInBaseCurrency = a.BalanceInBaseCurrency is { } v ? Fx(v) : null };
        InstitutionDto Institution(InstitutionDto i) => i with
        {
            TotalInBaseCurrency = Fx(i.TotalInBaseCurrency),
            Accounts = [.. i.Accounts.Select(Account)],
            Cards = i.Cards?.Select(c => c with
            {
                TotalInBaseCurrency = Fx(c.TotalInBaseCurrency),
                Accounts = [.. c.Accounts.Select(Account)],
            }).ToList(),
        };

        return summary with
        {
            TotalNetWorth = Fx(summary.TotalNetWorth),
            BaseCurrency = baseCurrency,
            Categories = [.. summary.Categories.Select(c => c with
            {
                TotalInBaseCurrency = Fx(c.TotalInBaseCurrency),
                Institutions = [.. c.Institutions.Select(Institution)],
            })],
        };
    }

    [HttpGet("transactions/summary")]
    public async Task<IActionResult> GetTransactionSummary(
        [FromQuery] string? from,
        [FromQuery] string? to,
        [FromQuery] string? category,
        [FromQuery] string? provider,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to))
            return BadRequest(new { error = "Query parameters 'from' and 'to' are required.", errorCode = "MISSING_DATE_RANGE" });

        if (!DateOnly.TryParseExact(from, "yyyy-MM-dd", out var fromDate)
         || !DateOnly.TryParseExact(to, "yyyy-MM-dd", out var toDate))
            return BadRequest(new { error = "Parameters 'from' and 'to' must be in yyyy-MM-dd format.", errorCode = "INVALID_DATE_RANGE" });

        if (fromDate > toDate)
            return BadRequest(new { error = "Parameter 'from' must be less than or equal to 'to'.", errorCode = "INVALID_DATE_RANGE" });

        if (category is not null && !AllowedCategories.Contains(category))
            return BadRequest(new { error = "Invalid category value. Allowed: banking, crypto, brokerage, other.", errorCode = "INVALID_FILTER" });

        var result = await _txSummaryHandler.Handle(
            new GetTransactionSummaryQuery(User.RequireUserId(), fromDate, toDate, category, provider), ct);
        return Ok(result);
    }

    [HttpGet("fire")]
    public async Task<IActionResult> GetFireProjection(CancellationToken ct)
    {
        var result = await _fireProjectionHandler.Handle(new GetFireProjectionQuery(User.RequireUserId()), ct);
        return Ok(result);
    }
}
