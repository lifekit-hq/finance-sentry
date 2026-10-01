using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Auth.Application.Interfaces;
using FinanceSentry.Modules.Auth.Domain.Entities;
using FinanceSentry.Modules.Auth.Domain.Exceptions;
using FinanceSentry.Modules.Auth.Domain.People;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Identity;

namespace FinanceSentry.Modules.Auth.Application.Commands;

/// <summary>
/// Sets an invited account's first password from the invite token and signs it in. Only a pending invite can be
/// accepted: an account that already has a password, or was revoked, gets the same invalid-invite error as a
/// wrong or expired token.
/// </summary>
public class AcceptInviteCommandHandler(
    UserManager<ApplicationUser> userManager,
    IUserAccessService userAccess,
    ITokenService tokenService,
    IRefreshTokenService refreshTokenService) : ICommandHandler<AcceptInviteCommand, AuthResult>
{
    public async Task<AuthResult> Handle(AcceptInviteCommand request, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(request.UserId);
        if (user is null || user.PasswordHash is not null || PersonStatus.IsRevoked(user))
            throw new InvalidInviteException();

        var result = await userManager.ResetPasswordAsync(user, request.Token, request.Password);
        if (!result.Succeeded)
        {
            if (result.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.InvalidToken)))
                throw new InvalidInviteException();

            throw new ValidationException(
                result.Errors.Select(e => new ValidationFailure(nameof(request.Password), e.Description)));
        }

        var access = await userAccess.GetAsync(user);
        var (accessToken, expiresAt) = tokenService.GenerateToken(user, access.Roles);

        var (rawRefreshToken, _) = await refreshTokenService.IssueAsync(user.Id, cancellationToken);

        return new AuthResult(new AuthResponse(new UserDto(user.Id, user.Email!, access.Roles, access.Permissions), expiresAt), rawRefreshToken, accessToken);
    }
}
