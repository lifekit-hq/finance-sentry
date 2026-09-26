using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Auth.Domain.Entities;
using FinanceSentry.Modules.Auth.Domain.Exceptions;
using Microsoft.AspNetCore.Identity;

namespace FinanceSentry.Modules.Auth.Application.Commands;

public class GetProfileQueryHandler(UserManager<ApplicationUser> userManager)
    : IQueryHandler<GetProfileQuery, UserProfileDto>
{
    public async Task<UserProfileDto> Handle(GetProfileQuery query, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(query.UserId.ToString())
            ?? throw new UserNotFoundException();

        return ToDto(user);
    }

    internal static UserProfileDto ToDto(ApplicationUser user) =>
        new(
            user.FirstName ?? string.Empty,
            user.LastName ?? string.Empty,
            user.Email ?? string.Empty,
            user.BaseCurrency,
            user.Theme,
            user.EmailAlerts,
            user.LowBalanceAlerts,
            user.LowBalanceThreshold,
            user.SyncFailureAlerts,
            user.TwoFactorEnabled,
            user.SafeWithdrawalRate,
            user.RealAnnualReturn);
}
