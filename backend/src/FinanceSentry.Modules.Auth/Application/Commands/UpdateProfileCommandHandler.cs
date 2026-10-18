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
        user.EmailAlerts = command.EmailAlerts;
        user.LowBalanceAlerts = command.LowBalanceAlerts;
        user.LowBalanceThreshold = command.LowBalanceThreshold;
        user.SyncFailureAlerts = command.SyncFailureAlerts;
        user.WatchlistAnalystAlerts = command.WatchlistAnalystAlerts;
        user.SafeWithdrawalRate = command.SafeWithdrawalRate;
        user.RealAnnualReturn = command.RealAnnualReturn;
        // Omitted (null) leaves the stored choice alone, so a client that predates these settings cannot reset them.
        user.HideEmptyBalances = command.HideEmptyBalances ?? user.HideEmptyBalances;
        user.HideSmallBalances = command.HideSmallBalances ?? user.HideSmallBalances;
        user.SmallBalanceThreshold = command.SmallBalanceThreshold ?? user.SmallBalanceThreshold;

        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            throw new AccountUpdateConflictException();
        }

        return GetProfileQueryHandler.ToDto(user);
    }
}
