namespace FinanceSentry.Modules.Radar.API.Controllers;

using FinanceSentry.Core.Auth;
using FinanceSentry.Modules.Radar.Application.Services;
using FinanceSentry.Modules.Radar.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

/// <summary>
/// Book vs SPY over the four standard lookback windows, the figures the MCP tool <c>get_book_performance</c>
/// returns; its <c>income</c> sub-route serves the income-received tile. The investments tab reads it for the growth-of-10,000 chart (presentation pattern P2).
/// </summary>
[ApiController]
[Authorize]
[Route("radar/book-performance")]
public class BookPerformanceController(IBookPerformanceService performance, IBookIncomeService income) : ControllerBase
{
    private static readonly IReadOnlyList<BookPerformancePeriod> AllPeriods = Enum.GetValues<BookPerformancePeriod>();

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
        => Ok(await performance.GetAsync(User.RequireUserId(), AllPeriods, ct));

    /// <summary>The book's income received, the same figure <c>get_book_performance</c> carries as <c>income</c>; read apart so the returns tab does not wait on the dividend calendar.</summary>
    [HttpGet("income")]
    public async Task<IActionResult> GetIncome(CancellationToken ct)
        => Ok(await income.GetAsync(User.RequireUserId(), ct));
}

/// <summary>
/// The risk picture shown beside the book's performance: volatility, maximum drawdown and beta (M8), the largest
/// positions' weights against the stored position flag, the crypto share and the drawdown tolerance.
/// </summary>
[ApiController]
[Authorize]
[Route("radar/risk-picture")]
public class RiskPictureController(IRiskPictureService riskPicture, IConcentrationHistoryService history) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
        => Ok(await riskPicture.GetAsync(User.RequireUserId(), ct));

    /// <summary>Top-three share, crypto share and cash share of the book over the captured days, with the IPS targets they are read against.</summary>
    [HttpGet("history")]
    public async Task<IActionResult> GetHistory(CancellationToken ct)
        => Ok(await history.GetAsync(User.RequireUserId(), ct));
}
