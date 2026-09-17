using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Auth.Application.Interfaces;
using FinanceSentry.Modules.Auth.Domain.Entities;
using FinanceSentry.Modules.Auth.Domain.Exceptions;
using Microsoft.AspNetCore.Identity;

namespace FinanceSentry.Modules.Auth.Application.Commands;

public class RefreshCommandHandler(
    IRefreshTokenService refreshTokenService,
    ITokenService tokenService,
    UserManager<ApplicationUser> userManager) : ICommandHandler<RefreshCommand, AuthResult>
{
    public async Task<AuthResult> Handle(RefreshCommand request, CancellationToken cancellationToken)
    {
        var existing = await refreshTokenService.ValidateAsync(request.RawRefreshToken, cancellationToken) ?? throw new InvalidRefreshTokenException();

        var user = await userManager.FindByIdAsync(existing.UserId) ?? throw new InvalidRefreshTokenException();
        var (newRaw, _) = await refreshTokenService.RotateAsync(existing, cancellationToken);

        var (accessToken, expiresAt) = tokenService.GenerateToken(user);

        return new AuthResult(new AuthResponse(new UserDto(user.Id, user.Email!), expiresAt), newRaw, accessToken);
    }
}
