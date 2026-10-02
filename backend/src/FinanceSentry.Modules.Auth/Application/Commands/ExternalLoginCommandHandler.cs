namespace FinanceSentry.Modules.Auth.Application.Commands;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Auth.Application.Interfaces;
using FinanceSentry.Modules.Auth.Domain.Entities;
using FinanceSentry.Modules.Auth.Domain.Exceptions;
using Microsoft.AspNetCore.Identity;

/// <summary>
/// External sign-in for existing accounts only. The provider account resolves through Identity's external-login
/// table; on first use it links to the account with the same (provider-verified) email, which includes a pending
/// invite. An email with no account is refused: accounts are created by invite, never by sign-in.
/// </summary>
public class ExternalLoginCommandHandler(
    UserManager<ApplicationUser> userManager,
    IUserAccessService userAccess,
    ITokenService tokenService,
    IRefreshTokenService refreshTokenService) : ICommandHandler<ExternalLoginCommand, AuthResult>
{
    public async Task<AuthResult> Handle(ExternalLoginCommand request, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByLoginAsync(request.Provider, request.Subject);
        var isLinked = user is not null;

        if (user is null)
        {
            // An unverified email must never relink an existing account.
            if (!request.EmailVerified)
                throw new AccountNotInvitedException();

            user = await userManager.FindByEmailAsync(request.Email)
                ?? throw new AccountNotInvitedException();
        }

        if (await userManager.IsLockedOutAsync(user))
            throw new InvalidCredentialsException();

        if (!isLinked)
        {
            var linked = await userManager.AddLoginAsync(user, new UserLoginInfo(request.Provider, request.Subject, request.Provider));
            if (!linked.Succeeded)
                throw new InvalidOperationException(
                    $"Failed to link the {request.Provider} account: " + string.Join(", ", linked.Errors.Select(e => e.Description)));
        }

        var access = await userAccess.GetAsync(user);
        var (accessToken, expiresAt) = tokenService.GenerateToken(user, access.Roles);

        var (rawRefreshToken, _) = await refreshTokenService.IssueAsync(user.Id, cancellationToken);

        return new AuthResult(new AuthResponse(new UserDto(user.Id, user.Email!, access.Roles, access.Permissions), expiresAt), rawRefreshToken, accessToken);
    }
}
