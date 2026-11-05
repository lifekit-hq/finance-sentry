using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Auth.Application.Commands;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinanceSentry.Modules.Auth.API.Controllers;

/// <summary>
/// The signed-in person's audit trail: who wrote to, or read through MCP or the in-app agent, their declared profile
/// data. Read-only and scoped to the caller - there is no way to ask for another person's events and no export of them.
/// </summary>
[ApiController]
[Authorize]
[Route("me/audit-events")]
public class AuditEventsController(IQueryHandler<ListAuditEventsQuery, AuditEventPageDto> listHandler) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] DateTimeOffset? before, [FromQuery] Guid? beforeId,
        [FromQuery] int take = ListAuditEventsQuery.DefaultTake, CancellationToken ct = default)
        => Ok(await listHandler.Handle(new ListAuditEventsQuery(User.RequireUserId(), before?.UtcDateTime, beforeId, take), ct));
}
