using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Auth.Application.Commands;
using Microsoft.AspNetCore.Mvc;

namespace FinanceSentry.Modules.Auth.API.Controllers;

[ApiController]
[Route("profile")]
public class ProfileController(
    IQueryHandler<GetProfileQuery, UserProfileDto> getProfileHandler,
    ICommandHandler<UpdateProfileCommand, UserProfileDto> updateProfileHandler,
    ICommandHandler<ChangePasswordCommand, Unit> changePasswordHandler) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetProfile(CancellationToken ct)
    {
        var profile = await getProfileHandler.Handle(new GetProfileQuery(User.RequireUserId()), ct);
        return Ok(profile);
    }

    [HttpPut]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileRequest request, CancellationToken ct)
    {
        var command = new UpdateProfileCommand(
            User.RequireUserId(),
            request.FirstName,
            request.LastName,
            request.BaseCurrency,
            request.Theme,
            request.EmailAlerts,
            request.LowBalanceAlerts,
            request.LowBalanceThreshold,
            request.SyncFailureAlerts,
            request.SafeWithdrawalRate,
            request.RealAnnualReturn);
        var profile = await updateProfileHandler.Handle(command, ct);
        return Ok(profile);
    }

    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request, CancellationToken ct)
    {
        await changePasswordHandler.Handle(
            new ChangePasswordCommand(User.RequireUserId(), request.CurrentPassword, request.NewPassword), ct);
        return NoContent();
    }
}
