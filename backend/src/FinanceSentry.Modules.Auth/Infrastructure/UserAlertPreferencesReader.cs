namespace FinanceSentry.Modules.Auth.Infrastructure;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Auth.Domain.Entities;
using Microsoft.AspNetCore.Identity;

public class UserAlertPreferencesReader(UserManager<ApplicationUser> users) : IUserAlertPreferencesReader
{
    private readonly UserManager<ApplicationUser> _users = users;

    public async Task<UserAlertPreferences?> GetAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await _users.FindByIdAsync(userId.ToString());
        if (user is null) return null;
        return new UserAlertPreferences(
            user.LowBalanceAlerts,
            user.LowBalanceThreshold,
            user.SyncFailureAlerts,
            user.WatchlistAnalystAlerts);
    }
}
