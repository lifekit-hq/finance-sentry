using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Auth.Application.Interfaces;
using FinanceSentry.Modules.Auth.Domain.Exceptions;

namespace FinanceSentry.Modules.Auth.Application.Commands;

public sealed record RevokeMcpServiceTokenCommand(string RawRefreshToken, Guid Jti) : ICommand<Unit>;

public sealed class RevokeMcpServiceTokenCommandHandler(
    IRefreshTokenService refreshTokenService,
    IMcpServiceTokenStore serviceTokenStore) : ICommandHandler<RevokeMcpServiceTokenCommand, Unit>
{
    public async Task<Unit> Handle(RevokeMcpServiceTokenCommand request, CancellationToken cancellationToken)
    {
        _ = await refreshTokenService.ValidateUnscopedAsync(request.RawRefreshToken, cancellationToken)
            ?? throw new InvalidRefreshTokenException("No session found.");

        await serviceTokenStore.RevokeAsync(request.Jti, cancellationToken);
        return Unit.Value;
    }
}
