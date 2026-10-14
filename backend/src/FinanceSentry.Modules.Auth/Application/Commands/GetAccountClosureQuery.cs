using FinanceSentry.Core.Cqrs;

namespace FinanceSentry.Modules.Auth.Application.Commands;

/// <summary>Whether the signed-in person may close their own account, for the Settings danger zone.</summary>
public record GetAccountClosureQuery(Guid UserId) : IQuery<AccountClosureOptionsDto>;

/// <summary>
/// <see cref="CanClose"/> is false for the last Owner (<see cref="Reason"/> says why); <see cref="GraceDays"/> is how
/// long a closed account's data is kept, reopenable, before it is erased.
/// </summary>
public record AccountClosureOptionsDto(bool CanClose, string? Reason, int GraceDays);
