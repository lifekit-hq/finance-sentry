using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Auth.Application.Interfaces;
using FinanceSentry.Modules.Auth.Domain.Entities;
using FinanceSentry.Modules.Auth.Domain.Exceptions;
using FinanceSentry.Modules.Auth.Domain.People;
using FinanceSentry.Modules.Auth.Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace FinanceSentry.Modules.Auth.Application.Commands;

public class LoginCommandHandler(
    UserManager<ApplicationUser> userManager,
    IUserAccessService userAccess,
    SignInManager<ApplicationUser> signInManager,
    ITokenService tokenService,
    IRefreshTokenService refreshTokenService,
    IOptions<AuthSignInOptions> signInOptions) : ICommandHandler<LoginCommand, AuthResult>
{
    public async Task<AuthResult> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        if (!signInOptions.Value.PasswordLogin.Enabled)
            throw new SignInMethodDisabledException();

        var user = await userManager.FindByEmailAsync(request.Email);
        if (user is null)
            throw new InvalidCredentialsException();

        // No password: a Google-only account, or an invite that has not been accepted yet.
        if (user.PasswordHash is null)
            throw (await userManager.GetLoginsAsync(user)).Count > 0
                ? new GoogleAccountOnlyException()
                : new InvalidCredentialsException();

        // Counts failures towards Identity lockout. A throttled account says so, otherwise the user retries blind;
        // a revoked person stays on the generic error so revocation is not disclosed at the login form.
        var signIn = await signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
        if (signIn.IsLockedOut && !PersonStatus.IsRevoked(user))
            throw new AccountLockedException((user.LockoutEnd ?? DateTimeOffset.UtcNow) - DateTimeOffset.UtcNow);
        if (!signIn.Succeeded)
            throw new InvalidCredentialsException();

        var access = await userAccess.GetAsync(user);
        var (accessToken, expiresAt) = tokenService.GenerateToken(user, access.Roles);

        var (rawRefreshToken, _) = await refreshTokenService.IssueAsync(user.Id, cancellationToken);

        return new AuthResult(new AuthResponse(new UserDto(user.Id, user.Email!, access.Roles, access.Permissions), expiresAt), rawRefreshToken, accessToken);
    }
}
