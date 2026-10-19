namespace FinanceSentry.Modules.Wealth.API.Controllers;

using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Wealth.Application.Commands;
using FinanceSentry.Modules.Wealth.Application.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

/// <summary>Values the person enters by hand for wealth no provider reads (framework §5.5).</summary>
[ApiController]
[Authorize]
[Route("wealth/manual-holdings")]
public class ManualHoldingsController(
    IQueryHandler<GetManualHoldingsQuery, IReadOnlyList<ManualHoldingDto>> list,
    ICommandHandler<CreateManualHoldingCommand, ManualHoldingDto> create,
    ICommandHandler<UpdateManualHoldingCommand, ManualHoldingDto> update,
    ICommandHandler<DeleteManualHoldingCommand, Unit> delete) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
        => Ok(await list.Handle(new GetManualHoldingsQuery(User.RequireUserId()), ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] ManualHoldingInput request, CancellationToken ct)
    {
        var result = await create.Handle(new CreateManualHoldingCommand(User.RequireUserId(), request), ct);
        return Created($"/api/v1/wealth/manual-holdings/{result.Id}", result);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] ManualHoldingInput request, CancellationToken ct)
        => Ok(await update.Handle(new UpdateManualHoldingCommand(User.RequireUserId(), id, request), ct));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await delete.Handle(new DeleteManualHoldingCommand(User.RequireUserId(), id), ct);
        return NoContent();
    }
}
