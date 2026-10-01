using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Auth.Application.Interfaces;
using FinanceSentry.Modules.Auth.Domain.Entities;
using FinanceSentry.Modules.Auth.Domain.Exceptions;
using Microsoft.AspNetCore.Identity;

namespace FinanceSentry.Modules.Auth.Application.Commands;

public class LoginCommandHandler(
    UserManager<ApplicationUser> userManager,
    IUserAccessService userAccess,
    SignInManager<ApplicationUser> signInManager,
    ITokenService tokenService,
    IRefreshTokenService refreshTokenService) : ICommandHandler<LoginCommand, AuthResult>
{
    public async Task<AuthResult> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByEmailAsync(request.Email);
        if (user is null)
            throw new InvalidCredentialsException();

        // No password: a Google-only account, or an invite that has not been accepted yet.
        if (user.PasswordHash is null)
            throw (await userManager.GetLoginsAsync(user)).Count > 0
                ? new GoogleAccountOnlyException()
                : new InvalidCredentialsException();

        // Counts failures towards Identity lockout; a locked-out account gets the same generic error.
        var signIn = await signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
        if (!signIn.Succeeded)
            throw new InvalidCredentialsException();

        var access = await userAccess.GetAsync(user);
        var (accessToken, expiresAt) = tokenService.GenerateToken(user, access.Roles);

        var (rawRefreshToken, _) = await refreshTokenService.IssueAsync(user.Id, cancellationToken);

        return new AuthResult(new AuthResponse(new UserDto(user.Id, user.Email!, access.Roles, access.Permissions), expiresAt), rawRefreshToken, accessToken);
    }
}
