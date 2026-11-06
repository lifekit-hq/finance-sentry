namespace FinanceSentry.Modules.Wealth.API.Controllers;

using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Wealth.Application.Commands;
using FinanceSentry.Modules.Wealth.Application.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

/// <summary>
/// The income the person declares (#454 S4). Writes are audited by field name (the context's interceptor); the person
/// reading their own list is not audited, a read through MCP or the in-app agent is (<c>get_income_profile</c>).
/// </summary>
[ApiController]
[Authorize]
[Route("wealth/income-sources")]
public class IncomeSourcesController(
    IQueryHandler<GetIncomeSourcesQuery, IReadOnlyList<IncomeSourceDto>> list,
    ICommandHandler<CreateIncomeSourceCommand, IncomeSourceDto> create,
    ICommandHandler<UpdateIncomeSourceCommand, IncomeSourceDto> update,
    ICommandHandler<RetireIncomeSourceCommand, IncomeSourceDto> retire,
    ICommandHandler<DeleteIncomeSourceCommand, Unit> delete) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
        => Ok(await list.Handle(new GetIncomeSourcesQuery(User.RequireUserId()), ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] IncomeSourceInput request, CancellationToken ct)
    {
        var result = await create.Handle(new CreateIncomeSourceCommand(User.RequireUserId(), request), ct);
        return Created($"/api/v1/wealth/income-sources/{result.Id}", result);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] IncomeSourceInput request, CancellationToken ct)
        => Ok(await update.Handle(new UpdateIncomeSourceCommand(User.RequireUserId(), id, request), ct));

    [HttpPost("{id:guid}/retire")]
    public async Task<IActionResult> Retire(Guid id, CancellationToken ct)
        => Ok(await retire.Handle(new RetireIncomeSourceCommand(User.RequireUserId(), id), ct));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await delete.Handle(new DeleteIncomeSourceCommand(User.RequireUserId(), id), ct);
        return NoContent();
    }
}
