using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Auth.Application.Interfaces;

namespace FinanceSentry.Modules.Auth.Application.Commands;

public class LogoutCommandHandler(IRefreshTokenService refreshTokenService) : ICommandHandler<LogoutCommand, Unit>
{
    public async Task<Unit> Handle(LogoutCommand command, CancellationToken cancellationToken)
    {
        await refreshTokenService.RevokeTokenAsync(command.RawRefreshToken, cancellationToken);
        return Unit.Value;
    }
}
