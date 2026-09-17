using FinanceSentry.Core.Cqrs;

namespace FinanceSentry.Modules.Auth.Application.Commands;

public record LogoutCommand(string RawRefreshToken) : ICommand<Unit>;
