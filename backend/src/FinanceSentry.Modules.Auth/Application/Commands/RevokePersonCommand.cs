using FinanceSentry.Core.Cqrs;

namespace FinanceSentry.Modules.Auth.Application.Commands;

public record RevokePersonCommand(string UserId, string RequestedByUserId) : ICommand<Unit>;
