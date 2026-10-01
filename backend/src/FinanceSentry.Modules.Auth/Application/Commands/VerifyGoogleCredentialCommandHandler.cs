namespace FinanceSentry.Modules.Auth.Application.Commands;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Auth.Application.Interfaces;
using FinanceSentry.Modules.Auth.Domain.Entities;
using FinanceSentry.Modules.Auth.Domain.Exceptions;
using Microsoft.AspNetCore.Identity;

/// <summary>
/// Google sign-in for existing accounts only. The Google account resolves through Identity's external-login
/// table; on first use it links to the account with the same (Google-verified) email, which includes a pending
/// invite. An email with no account is refused: accounts are created by invite, never by sign-in.
/// </summary>
public class VerifyGoogleCredentialCommandHandler(
    UserManager<ApplicationUser> userManager,
    IUserAccessService userAccess,
    ITokenService tokenService,
    IRefreshTokenService refreshTokenService,
    IGoogleCredentialVerifier verifier) : ICommandHandler<VerifyGoogleCredentialCommand, AuthResult>
{
    public const string LoginProvider = "Google";

    public async Task<AuthResult> Handle(VerifyGoogleCredentialCommand request, CancellationToken cancellationToken)
    {
        var googleUser = await verifier.VerifyAsync(request.Credential);

        var user = await userManager.FindByLoginAsync(LoginProvider, googleUser.GoogleId);
        var isLinked = user is not null;

        user ??= await userManager.FindByEmailAsync(googleUser.Email)
            ?? throw new AccountNotInvitedException();

        if (await userManager.IsLockedOutAsync(user))
            throw new InvalidCredentialsException();

        if (!isLinked)
        {
            var linked = await userManager.AddLoginAsync(user, new UserLoginInfo(LoginProvider, googleUser.GoogleId, LoginProvider));
            if (!linked.Succeeded)
                throw new InvalidOperationException(
                    "Failed to link the Google account: " + string.Join(", ", linked.Errors.Select(e => e.Description)));
        }

        var access = await userAccess.GetAsync(user);
        var (accessToken, expiresAt) = tokenService.GenerateToken(user, access.Roles);

        var (rawRefreshToken, _) = await refreshTokenService.IssueAsync(user.Id, cancellationToken);

        return new AuthResult(new AuthResponse(new UserDto(user.Id, user.Email!, access.Roles, access.Permissions), expiresAt), rawRefreshToken, accessToken);
    }
}
