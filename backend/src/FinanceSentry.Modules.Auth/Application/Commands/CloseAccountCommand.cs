using FinanceSentry.Core.Cqrs;

namespace FinanceSentry.Modules.Auth.Application.Commands;

/// <summary>The signed-in person closes their own account; <paramref name="ConfirmEmail"/> is what they typed to confirm.</summary>
public record CloseAccountCommand(Guid UserId, string ConfirmEmail) : ICommand<AccountClosureDto>;

public record CloseAccountRequest(string ConfirmEmail);

/// <summary>When the closed account's data is erased unless an Owner reopens it first.</summary>
public record AccountClosureDto(DateTime ClosedAt, DateTime PurgeDueAt);
