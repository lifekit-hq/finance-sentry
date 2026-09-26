using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Auth.Domain.Entities;
using FinanceSentry.Modules.Auth.Domain.Exceptions;
using Microsoft.AspNetCore.Identity;

namespace FinanceSentry.Modules.Auth.Application.Commands;

public class UpdateProfileCommandHandler(UserManager<ApplicationUser> userManager)
    : ICommandHandler<UpdateProfileCommand, UserProfileDto>
{
    public async Task<UserProfileDto> Handle(UpdateProfileCommand command, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(command.UserId.ToString())
            ?? throw new UserNotFoundException();

        user.FirstName = command.FirstName;
        user.LastName = command.LastName;
        user.BaseCurrency = command.BaseCurrency;
        user.Theme = command.Theme;
        user.EmailAlerts = command.EmailAlerts;
        user.LowBalanceAlerts = command.LowBalanceAlerts;
        user.LowBalanceThreshold = command.LowBalanceThreshold;
        user.SyncFailureAlerts = command.SyncFailureAlerts;
        user.SafeWithdrawalRate = command.SafeWithdrawalRate;
        user.RealAnnualReturn = command.RealAnnualReturn;

        await userManager.UpdateAsync(user);

        return GetProfileQueryHandler.ToDto(user);
    }
}
