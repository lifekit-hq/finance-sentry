namespace FinanceSentry.Modules.Wealth.API.Controllers;

using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Headlines;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Core.Measures;
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
    IQueryHandler<GetSleeveGrowthQuery, SleeveGrowthResponse> sleeveGrowthHandler,
    IQueryHandler<GetNetWorthProjectionQuery, NetWorthProjectionResponse> netWorthProjectionHandler,
    IQueryHandler<GetMonthlyIncomeLensQuery, MonthlyIncomeLensResponse> monthlyIncomeLensHandler,
    IQueryHandler<GetCashflowProfileQuery, CashflowProfileResponse> cashflowProfileHandler,
    IQueryHandler<GetGoalsProgressQuery, GoalsProgressResponse> goalsProgressHandler,
    IUserBaseCurrencyReader baseCurrencyReader) : ControllerBase
{
    private readonly IQueryHandler<GetCashflowProfileQuery, CashflowProfileResponse> _cashflowProfileHandler = cashflowProfileHandler ?? throw new ArgumentNullException(nameof(cashflowProfileHandler));
    private readonly IQueryHandler<GetGoalsProgressQuery, GoalsProgressResponse> _goalsProgressHandler = goalsProgressHandler ?? throw new ArgumentNullException(nameof(goalsProgressHandler));
    private readonly IQueryHandler<GetMonthlyIncomeLensQuery, MonthlyIncomeLensResponse> _monthlyIncomeLensHandler = monthlyIncomeLensHandler ?? throw new ArgumentNullException(nameof(monthlyIncomeLensHandler));
    private readonly IQueryHandler<GetNetWorthProjectionQuery, NetWorthProjectionResponse> _netWorthProjectionHandler = netWorthProjectionHandler ?? throw new ArgumentNullException(nameof(netWorthProjectionHandler));
    private readonly IQueryHandler<GetSleeveGrowthQuery, SleeveGrowthResponse> _sleeveGrowthHandler = sleeveGrowthHandler ?? throw new ArgumentNullException(nameof(sleeveGrowthHandler));
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
            Headline = summary.Headline is { } h ? HeadlineFactory.InBaseCurrency(h, baseCurrency, h.Window.To) : null,
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
        var userId = User.RequireUserId();
        var result = await _fireProjectionHandler.Handle(new GetFireProjectionQuery(userId), ct);
        var baseCurrency = CurrencyConverter.ResolveBase(await _baseCurrencyReader.GetAsync(userId, ct));
        return Ok(InBaseCurrency(result, baseCurrency));
    }

    // Same rule as the summary: the projection speaks USD, the base currency is applied once, here.
    // Rates, months and dates are untouched; only the four money amounts are re-expressed.
    private static FireProjectionResponse InBaseCurrency(FireProjectionResponse fire, string baseCurrency)
    {
        if (baseCurrency == "USD")
            return fire with { BaseCurrency = baseCurrency };

        decimal Fx(decimal usd) => CurrencyConverter.FromUsd(usd, baseCurrency);
        return fire with
        {
            Target = Fx(fire.Target),
            CurrentNetWorth = Fx(fire.CurrentNetWorth),
            MonthlySavings = Fx(fire.MonthlySavings),
            AnnualSpend = Fx(fire.AnnualSpend),
            BaseCurrency = baseCurrency,
        };
    }

    /// <summary>
    /// The twelve-month net-worth projection (M10): a range at the shared low / central / high return assumption,
    /// with the inputs that produced it: <c>GET /api/v1/wealth/projection</c>.
    /// </summary>
    [HttpGet("projection")]
    public async Task<IActionResult> GetNetWorthProjection(CancellationToken ct)
    {
        var userId = User.RequireUserId();
        var result = await _netWorthProjectionHandler.Handle(new GetNetWorthProjectionQuery(userId), ct);
        var baseCurrency = CurrencyConverter.ResolveBase(await _baseCurrencyReader.GetAsync(userId, ct));
        return Ok(InBaseCurrency(result, baseCurrency));
    }

    private static NetWorthProjectionResponse InBaseCurrency(NetWorthProjectionResponse projection, string baseCurrency)
    {
        if (baseCurrency == "USD")
            return projection with { BaseCurrency = baseCurrency };

        decimal Fx(decimal usd) => CurrencyConverter.FromUsd(usd, baseCurrency);
        return projection with
        {
            CurrentNetWorth = Fx(projection.CurrentNetWorth),
            MonthlySavings = Fx(projection.MonthlySavings),
            ProjectedNetWorth = projection.ProjectedNetWorth is { } range
                ? new ProjectionRange(Fx(range.Low), Fx(range.Central), Fx(range.High))
                : null,
            BaseCurrency = baseCurrency,
        };
    }

    /// <summary>
    /// Each IPS goal tracked against net worth plus savings, at the shared projection assumptions (#454 S2), context
    /// only: <c>GET /api/v1/wealth/goals</c>.
    /// </summary>
    [HttpGet("goals")]
    public async Task<IActionResult> GetGoalsProgress(CancellationToken ct)
    {
        var userId = User.RequireUserId();
        var result = await _goalsProgressHandler.Handle(new GetGoalsProgressQuery(userId), ct);
        var baseCurrency = CurrencyConverter.ResolveBase(await _baseCurrencyReader.GetAsync(userId, ct));
        return Ok(InBaseCurrency(result, baseCurrency));
    }

    // Months, counts and the return range are untouched; every amount is re-expressed.
    private static GoalsProgressResponse InBaseCurrency(GoalsProgressResponse goals, string baseCurrency)
    {
        if (baseCurrency == "USD")
            return goals with { BaseCurrency = baseCurrency };

        decimal? Fx(decimal? usd) => usd is { } v ? CurrencyConverter.FromUsd(v, baseCurrency) : null;
        ProjectionRange? FxRange(ProjectionRange? range) => range is { } r
            ? new ProjectionRange(Fx(r.Low)!.Value, Fx(r.Central)!.Value, Fx(r.High)!.Value)
            : null;

        return goals with
        {
            CurrentNetWorth = Fx(goals.CurrentNetWorth),
            MonthlySavings = Fx(goals.MonthlySavings),
            Goals = [.. goals.Goals.Select(g => g with
            {
                TargetAmount = Fx(g.TargetAmount),
                RequiredMonthlySaving = FxRange(g.RequiredMonthlySaving),
                ProjectedAtTargetDate = FxRange(g.ProjectedAtTargetDate),
            })],
            ContributionPlan = goals.ContributionPlan is { } plan
                ? plan with
                {
                    PlannedAmount = Fx(plan.PlannedAmount)!.Value,
                    PlannedMonthly = Fx(plan.PlannedMonthly),
                    ActualMonthlySavings = Fx(plan.ActualMonthlySavings),
                    Difference = Fx(plan.Difference),
                }
                : null,
            BaseCurrency = baseCurrency,
        };
    }

    /// <summary>
    /// The monthly-income lens (P5): net worth drawn at the shared safe withdrawal rate (C8), as a monthly amount:
    /// <c>GET /api/v1/wealth/income-lens</c>.
    /// </summary>
    [HttpGet("income-lens")]
    public async Task<IActionResult> GetMonthlyIncomeLens(CancellationToken ct)
    {
        var userId = User.RequireUserId();
        var result = await _monthlyIncomeLensHandler.Handle(new GetMonthlyIncomeLensQuery(userId), ct);
        var baseCurrency = CurrencyConverter.ResolveBase(await _baseCurrencyReader.GetAsync(userId, ct));
        return Ok(InBaseCurrency(result, baseCurrency));
    }

    // The rate is untouched; only the two money amounts are re-expressed.
    private static MonthlyIncomeLensResponse InBaseCurrency(MonthlyIncomeLensResponse lens, string baseCurrency)
    {
        if (baseCurrency == "USD")
            return lens with { BaseCurrency = baseCurrency };

        decimal Fx(decimal usd) => CurrencyConverter.FromUsd(usd, baseCurrency);
        return lens with
        {
            NetWorth = Fx(lens.NetWorth),
            MonthlyIncome = lens.MonthlyIncome is { } income ? Fx(income) : null,
            BaseCurrency = baseCurrency,
        };
    }

    /// <summary>
    /// Burn, savings rate and runway over the trailing complete months (#454 S1), context only:
    /// <c>GET /api/v1/wealth/cashflow-profile</c>.
    /// </summary>
    [HttpGet("cashflow-profile")]
    public async Task<IActionResult> GetCashflowProfile(CancellationToken ct)
    {
        var userId = User.RequireUserId();
        var result = await _cashflowProfileHandler.Handle(new GetCashflowProfileQuery(userId), ct);
        var baseCurrency = CurrencyConverter.ResolveBase(await _baseCurrencyReader.GetAsync(userId, ct));
        return Ok(InBaseCurrency(result, baseCurrency));
    }

    // The rate, the shares and the month counts are untouched; only the two monthly amounts are re-expressed.
    private static CashflowProfileResponse InBaseCurrency(CashflowProfileResponse profile, string baseCurrency)
    {
        if (baseCurrency == "USD")
            return profile with { BaseCurrency = baseCurrency };

        decimal? Fx(decimal? usd) => usd is { } v ? CurrencyConverter.FromUsd(v, baseCurrency) : null;
        return profile with
        {
            IncomeMonthlyMedian = Fx(profile.IncomeMonthlyMedian),
            BurnMonthlyMedian = Fx(profile.BurnMonthlyMedian),
            BaseCurrency = baseCurrency,
        };
    }

    /// <summary>
    /// Contributions vs growth for the brokerage sleeve since its value series began (M2, with Modified Dietz as
    /// the percent): <c>GET /api/v1/wealth/brokerage/growth</c>.
    /// </summary>
    [HttpGet("brokerage/growth")]
    public Task<IActionResult> GetBrokerageGrowth(CancellationToken ct) => GetSleeveGrowth(WealthSourceIds.Brokerage, ct);

    /// <summary>
    /// Contributions vs growth for the crypto sleeve (the venues), the same split as the brokerage one:
    /// <c>GET /api/v1/wealth/crypto/growth</c>.
    /// </summary>
    [HttpGet("crypto/growth")]
    public Task<IActionResult> GetCryptoGrowth(CancellationToken ct) => GetSleeveGrowth(WealthSourceIds.Crypto, ct);

    private async Task<IActionResult> GetSleeveGrowth(string sleeve, CancellationToken ct)
    {
        var userId = User.RequireUserId();
        var result = await _sleeveGrowthHandler.Handle(new GetSleeveGrowthQuery(userId, sleeve), ct);
        var baseCurrency = CurrencyConverter.ResolveBase(await _baseCurrencyReader.GetAsync(userId, ct));
        return Ok(InBaseCurrency(result, baseCurrency));
    }

    // Computed in USD over dated flows; shown in the base currency at today's rate, as the summary is. The headline
    // names that rate (FX_TODAY_RATE) for a window reaching into the past; the rate is a ratio and does not change.
    private static SleeveGrowthResponse InBaseCurrency(SleeveGrowthResponse growth, string baseCurrency)
    {
        if (baseCurrency == "USD")
            return growth;

        decimal? Fx(decimal? usd) => usd is { } v ? CurrencyConverter.FromUsd(v, baseCurrency) : null;
        return growth with
        {
            StartValue = Fx(growth.StartValue),
            EndValue = Fx(growth.EndValue),
            Contributed = Fx(growth.Contributed),
            NewlyLinked = Fx(growth.NewlyLinked),
            Growth = Fx(growth.Growth),
            Income = Fx(growth.Income),
            Fees = Fx(growth.Fees),
            Taxes = Fx(growth.Taxes),
            Headline = HeadlineFactory.InBaseCurrency(
                growth.Headline, baseCurrency, DateOnly.FromDateTime(DateTime.UtcNow)),
            Currency = baseCurrency,
        };
    }
}
