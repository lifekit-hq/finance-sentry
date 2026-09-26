using FinanceSentry.Core.Cqrs;

namespace FinanceSentry.Modules.Auth.Application.Commands;

public record UpdateProfileCommand(
    Guid UserId,
    string FirstName,
    string LastName,
    string BaseCurrency,
    string Theme,
    bool EmailAlerts,
    bool LowBalanceAlerts,
    decimal LowBalanceThreshold,
    bool SyncFailureAlerts,
    decimal SafeWithdrawalRate,
    decimal RealAnnualReturn) : ICommand<UserProfileDto>;

public record UpdateProfileRequest(
    string FirstName,
    string LastName,
    string BaseCurrency,
    string Theme,
    bool EmailAlerts,
    bool LowBalanceAlerts,
    decimal LowBalanceThreshold,
    bool SyncFailureAlerts,
    decimal SafeWithdrawalRate,
    decimal RealAnnualReturn);
