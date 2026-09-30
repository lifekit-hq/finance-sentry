namespace FinanceSentry.Modules.Research.API.Controllers;

using FinanceSentry.Core.Auth;
using FinanceSentry.Modules.Research.Infrastructure.Jobs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;

[ApiController]
[Authorize(Policy = AuthPolicies.RequireOwner)]
[Route("research/admin")]
public class AdminController(
    NewsIngestionJob newsJob,
    MacroCalendarSeedJob seedJob,
    IHostEnvironment env) : ControllerBase
{
    [HttpPost("seed-macro")]
    public async Task<IActionResult> SeedMacro(CancellationToken ct)
    {
        if (!env.IsDevelopment())
        {
            return NotFound();
        }

        await seedJob.ExecuteAsync(ct);
        return NoContent();
    }

    [HttpPost("ingest-news-tickers")]
    public async Task<IActionResult> IngestNewsTickers(CancellationToken ct)
    {
        if (!env.IsDevelopment())
        {
            return NotFound();
        }

        await newsJob.IngestTickersAsync(ct);
        return NoContent();
    }

    [HttpPost("ingest-news-fed")]
    public async Task<IActionResult> IngestNewsFed(CancellationToken ct)
    {
        if (!env.IsDevelopment())
        {
            return NotFound();
        }

        await newsJob.IngestFedAsync(ct);
        return NoContent();
    }
}
