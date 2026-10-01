using FinanceSentry.Core.Cqrs;

namespace FinanceSentry.Modules.Auth.Application.Commands;

public record AcceptInviteRequest(string UserId, string Token, string Password);

public record AcceptInviteCommand(string UserId, string Token, string Password) : ICommand<AuthResult>;
