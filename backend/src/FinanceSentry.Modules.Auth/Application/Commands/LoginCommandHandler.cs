using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Auth.Application.Interfaces;
using FinanceSentry.Modules.Auth.Domain.Entities;
using FinanceSentry.Modules.Auth.Domain.Exceptions;
using Microsoft.AspNetCore.Identity;

namespace FinanceSentry.Modules.Auth.Application.Commands;

public class LoginCommandHandler(
    UserManager<ApplicationUser> userManager,
    ITokenService tokenService,
    IRefreshTokenService refreshTokenService) : ICommandHandler<LoginCommand, AuthResult>
{
    public async Task<AuthResult> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByEmailAsync(request.Email);
        if (user is null)
            throw new InvalidCredentialsException();

        if (user.PasswordHash is null)
            throw new GoogleAccountOnlyException();

        if (!await userManager.CheckPasswordAsync(user, request.Password))
            throw new InvalidCredentialsException();

        var (accessToken, expiresAt) = tokenService.GenerateToken(user);

        var (rawRefreshToken, _) = await refreshTokenService.IssueAsync(user.Id, cancellationToken);

        return new AuthResult(new AuthResponse(new UserDto(user.Id, user.Email!), expiresAt), rawRefreshToken, accessToken);
    }
}
