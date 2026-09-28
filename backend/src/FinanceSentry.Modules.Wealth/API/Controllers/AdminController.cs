namespace FinanceSentry.Modules.Wealth.API.Controllers;

using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Wealth.Application.Commands;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;

[ApiController]
[Route("wealth/admin")]
public class AdminController(
    ICommandHandler<BackfillNetWorthHistoryCommand, BackfillNetWorthHistoryResult> backfillHandler,
    IHostEnvironment env) : ControllerBase
{
    [HttpPost("backfill-net-worth-history")]
    public async Task<IActionResult> BackfillNetWorthHistory(CancellationToken ct)
    {
        if (!env.IsDevelopment())
        {
            return NotFound();
        }

        var result = await backfillHandler.Handle(new BackfillNetWorthHistoryCommand(User.RequireUserId()), ct);
        return Ok(result);
    }
}
