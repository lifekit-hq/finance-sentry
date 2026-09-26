using FinanceSentry.Core.Cqrs;

namespace FinanceSentry.Modules.Auth.Application.Commands;

public record GetProfileQuery(Guid UserId) : IQuery<UserProfileDto>;

public record UserProfileDto(
    string FirstName,
    string LastName,
    string Email,
    string BaseCurrency,
    string Theme,
    bool EmailAlerts,
    bool LowBalanceAlerts,
    decimal LowBalanceThreshold,
    bool SyncFailureAlerts,
    bool TwoFactor,
    decimal SafeWithdrawalRate,
    decimal RealAnnualReturn);
