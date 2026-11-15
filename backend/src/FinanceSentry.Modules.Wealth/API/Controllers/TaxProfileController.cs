namespace FinanceSentry.Modules.Wealth.API.Controllers;

using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Wealth.Application.Commands;
using FinanceSentry.Modules.Wealth.Application.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

/// <summary>
/// The tax situation the person states (#454): one profile per person, empty until they set it. Writes are audited by field
/// name (the context's interceptor); the person reading their own profile is not audited, a read through MCP or the in-app
/// agent is (<c>get_financial_profile</c>). It is the person's own context, never tax advice.
/// </summary>
[ApiController]
[Authorize]
[Route("wealth/tax-profile")]
public class TaxProfileController(
    IQueryHandler<GetTaxProfileQuery, TaxProfileDto> get,
    ICommandHandler<UpsertTaxProfileCommand, TaxProfileDto> upsert,
    ICommandHandler<DeleteTaxProfileCommand, Unit> delete) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
        => Ok(await get.Handle(new GetTaxProfileQuery(User.RequireUserId()), ct));

    /// <summary>Restates the whole profile: an omitted field is cleared.</summary>
    [HttpPut]
    public async Task<IActionResult> Put([FromBody] TaxProfileInput request, CancellationToken ct)
        => Ok(await upsert.Handle(new UpsertTaxProfileCommand(User.RequireUserId(), request), ct));

    [HttpDelete]
    public async Task<IActionResult> Delete(CancellationToken ct)
    {
        await delete.Handle(new DeleteTaxProfileCommand(User.RequireUserId()), ct);
        return NoContent();
    }
}
