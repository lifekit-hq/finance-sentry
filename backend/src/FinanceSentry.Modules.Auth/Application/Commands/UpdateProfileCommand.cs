using FinanceSentry.Core.Cqrs;

namespace FinanceSentry.Modules.Auth.Application.Commands;

public record UpdateProfileCommand(
    Guid UserId,
    string FirstName,
    string LastName,
    string BaseCurrency,
    bool EmailAlerts,
    bool LowBalanceAlerts,
    decimal LowBalanceThreshold,
    bool SyncFailureAlerts,
    bool WatchlistAnalystAlerts,
    decimal SafeWithdrawalRate,
    decimal RealAnnualReturn,
    bool? HideEmptyBalances = null,
    bool? HideSmallBalances = null,
    decimal? SmallBalanceThreshold = null) : ICommand<UserProfileDto>;

public record UpdateProfileRequest(
    string FirstName,
    string LastName,
    string BaseCurrency,
    bool EmailAlerts,
    bool LowBalanceAlerts,
    decimal LowBalanceThreshold,
    bool SyncFailureAlerts,
    bool WatchlistAnalystAlerts,
    decimal SafeWithdrawalRate,
    decimal RealAnnualReturn,
    bool? HideEmptyBalances = null,
    bool? HideSmallBalances = null,
    decimal? SmallBalanceThreshold = null);
