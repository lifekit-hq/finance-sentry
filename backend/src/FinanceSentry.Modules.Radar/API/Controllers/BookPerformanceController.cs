namespace FinanceSentry.Modules.Radar.API.Controllers;

using FinanceSentry.Core.Auth;
using FinanceSentry.Modules.Radar.Application.Services;
using FinanceSentry.Modules.Radar.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

/// <summary>
/// Book vs SPY over the four standard lookback windows, the figures the MCP tool <c>get_book_performance</c>
/// returns. The investments tab reads it for the growth-of-10,000 chart (presentation pattern P2).
/// </summary>
[ApiController]
[Authorize]
[Route("radar/book-performance")]
public class BookPerformanceController(IBookPerformanceService performance) : ControllerBase
{
    private static readonly IReadOnlyList<BookPerformancePeriod> AllPeriods = Enum.GetValues<BookPerformancePeriod>();

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
        => Ok(await performance.GetAsync(User.RequireUserId(), AllPeriods, ct));
}
