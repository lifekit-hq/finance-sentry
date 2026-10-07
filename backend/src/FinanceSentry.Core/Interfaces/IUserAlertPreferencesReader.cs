namespace FinanceSentry.Core.Interfaces;

public interface IUserAlertPreferencesReader
{
    Task<UserAlertPreferences?> GetAsync(Guid userId, CancellationToken ct = default);
}

public sealed record UserAlertPreferences(
    bool LowBalanceAlerts,
    decimal LowBalanceThreshold,
    bool SyncFailureAlerts,
    bool WatchlistAnalystAlerts = false);
