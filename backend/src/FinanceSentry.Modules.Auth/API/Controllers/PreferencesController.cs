using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Auth.Application.Commands;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinanceSentry.Modules.Auth.API.Controllers;

/// <summary>
/// The signed-in person's account preferences in the lifekit app-shell shape (<c>GET</c> and <c>PATCH</c> of
/// one resource). Today it carries the appearance; the profile and alert fields stay on <c>/profile</c>.
/// </summary>
[ApiController]
[Authorize]
[Route("me/preferences")]
public class PreferencesController(
    IQueryHandler<GetPreferencesQuery, PreferencesDto> getPreferencesHandler,
    ICommandHandler<UpdatePreferencesCommand, PreferencesDto> updatePreferencesHandler) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetPreferences(CancellationToken ct)
    {
        var preferences = await getPreferencesHandler.Handle(new GetPreferencesQuery(User.RequireUserId()), ct);
        return Ok(preferences);
    }

    [HttpPatch]
    public async Task<IActionResult> UpdatePreferences([FromBody] UpdatePreferencesRequest request, CancellationToken ct)
    {
        var preferences = await updatePreferencesHandler.Handle(
            new UpdatePreferencesCommand(User.RequireUserId(), request.Appearance), ct);
        return Ok(preferences);
    }
}
