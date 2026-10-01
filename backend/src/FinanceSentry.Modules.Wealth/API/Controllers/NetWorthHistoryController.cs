namespace FinanceSentry.Modules.Wealth.API.Controllers;

using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Wealth.Application.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Authorize]
[Route("net-worth")]
public class NetWorthHistoryController(IQueryHandler<GetNetWorthHistoryQuery, NetWorthHistoryResponse> handler) : ControllerBase
{
    private readonly IQueryHandler<GetNetWorthHistoryQuery, NetWorthHistoryResponse> _handler
        = handler ?? throw new ArgumentNullException(nameof(handler));

    [HttpGet("history")]
    public async Task<IActionResult> GetHistory(
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null,
        CancellationToken ct = default)
    {
        var result = await _handler.Handle(new GetNetWorthHistoryQuery(User.RequireUserId(), from, to), ct);
        return Ok(result);
    }
}
