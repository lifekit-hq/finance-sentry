using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Auth.Application.Commands;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinanceSentry.Modules.Auth.API.Controllers;

[ApiController]
[Authorize]
[Route("profile")]
public class ProfileController(
    IQueryHandler<GetProfileQuery, UserProfileDto> getProfileHandler,
    ICommandHandler<UpdateProfileCommand, UserProfileDto> updateProfileHandler) : ControllerBase
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
            request.EmailAlerts,
            request.LowBalanceAlerts,
            request.LowBalanceThreshold,
            request.SyncFailureAlerts,
            request.WatchlistAnalystAlerts,
            request.SafeWithdrawalRate,
            request.RealAnnualReturn,
            request.HideEmptyBalances,
            request.HideSmallBalances,
            request.SmallBalanceThreshold);
        var profile = await updateProfileHandler.Handle(command, ct);
        return Ok(profile);
    }
}
