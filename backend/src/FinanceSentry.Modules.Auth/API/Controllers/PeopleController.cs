using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Auth.Application.Commands;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinanceSentry.Modules.Auth.API.Controllers;

/// <summary>
/// Invite-only onboarding: list the people with access, invite someone, revoke someone, reopen a closed account.
/// </summary>
[ApiController]
[Authorize(Policy = AuthPolicies.RequireUsersManage)]
[Route("people")]
public class PeopleController(
    IQueryHandler<ListPeopleQuery, IReadOnlyList<PersonDto>> listPeopleHandler,
    ICommandHandler<CreateInviteCommand, InviteDto> createInviteHandler,
    ICommandHandler<RevokePersonCommand, Unit> revokePersonHandler,
    ICommandHandler<ReopenAccountCommand, Unit> reopenAccountHandler) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
        => Ok(await listPeopleHandler.Handle(new ListPeopleQuery(), ct));

    [HttpPost("invites")]
    public async Task<IActionResult> CreateInvite([FromBody] CreateInviteRequest request, CancellationToken ct)
        => Ok(await createInviteHandler.Handle(new CreateInviteCommand(request.Email), ct));

    [HttpPost("{userId}/revoke")]
    public async Task<IActionResult> Revoke(string userId, CancellationToken ct)
    {
        await revokePersonHandler.Handle(new RevokePersonCommand(userId, User.RequireUserId().ToString()), ct);
        return NoContent();
    }

    [HttpPost("{userId}/reopen")]
    public async Task<IActionResult> Reopen(string userId, CancellationToken ct)
    {
        await reopenAccountHandler.Handle(new ReopenAccountCommand(userId, User.RequireUserId()), ct);
        return NoContent();
    }
}
