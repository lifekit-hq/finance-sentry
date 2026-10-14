using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Auth.Domain.Entities;
using FinanceSentry.Modules.Auth.Domain.Exceptions;
using FinanceSentry.Modules.Auth.Domain.People;
using Microsoft.AspNetCore.Identity;

namespace FinanceSentry.Modules.Auth.Application.Commands;

public class GetAccountClosureQueryHandler(UserManager<ApplicationUser> userManager)
    : IQueryHandler<GetAccountClosureQuery, AccountClosureOptionsDto>
{
    public async Task<AccountClosureOptionsDto> Handle(GetAccountClosureQuery query, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(query.UserId.ToString())
            ?? throw new UserNotFoundException();

        var graceDays = (int)AccountClosure.GracePeriod.TotalDays;
        return await LastOwner.IsAsync(userManager, user)
            ? new AccountClosureOptionsDto(false, new LastOwnerCannotCloseException().Message, graceDays)
            : new AccountClosureOptionsDto(true, null, graceDays);
    }
}
