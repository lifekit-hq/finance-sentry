using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Auth.Application.Commands;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinanceSentry.Modules.Auth.API.Controllers;

/// <summary>The signed-in person's own account. There is no user id in the route: every action is on the caller.</summary>
[ApiController]
[Authorize]
[Route("account")]
public class AccountController(
    IQueryHandler<GetAccountClosureQuery, AccountClosureOptionsDto> closureOptionsHandler,
    ICommandHandler<CloseAccountCommand, AccountClosureDto> closeAccountHandler) : ControllerBase
{
    [HttpGet("closure")]
    public async Task<IActionResult> ClosureOptions(CancellationToken ct)
        => Ok(await closureOptionsHandler.Handle(new GetAccountClosureQuery(User.RequireUserId()), ct));

    [HttpPost("close")]
    public async Task<IActionResult> Close([FromBody] CloseAccountRequest request, CancellationToken ct)
        => Ok(await closeAccountHandler.Handle(new CloseAccountCommand(User.RequireUserId(), request.ConfirmEmail), ct));
}
