namespace FinanceSentry.Modules.Auth.Infrastructure;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Auth.Domain.Entities;
using Microsoft.AspNetCore.Identity;

public class UserFireAssumptionsReader(UserManager<ApplicationUser> users) : IUserFireAssumptionsReader
{
    private readonly UserManager<ApplicationUser> _users = users;

    public async Task<FireAssumptions?> GetAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await _users.FindByIdAsync(userId.ToString());
        if (user is null) return null;
        return new FireAssumptions(user.SafeWithdrawalRate, user.RealAnnualReturn);
    }
}
