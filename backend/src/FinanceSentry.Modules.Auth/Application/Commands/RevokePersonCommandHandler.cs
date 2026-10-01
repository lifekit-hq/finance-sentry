using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Auth.Application.Interfaces;
using FinanceSentry.Modules.Auth.Domain.Entities;
using FinanceSentry.Modules.Auth.Domain.Exceptions;
using FinanceSentry.Modules.Auth.Domain.People;
using Microsoft.AspNetCore.Identity;

namespace FinanceSentry.Modules.Auth.Application.Commands;

/// <summary>
/// Revokes a person's access: a permanent lockout, every role and per-person permission grant removed, the
/// security stamp rotated (voiding an outstanding invite link), and every refresh and MCP service token revoked.
/// The API loads the account per request, so a token already issued stops working on its next request.
/// Idempotent. The caller's own account and Owner accounts cannot be revoked.
/// </summary>
public class RevokePersonCommandHandler(
    UserManager<ApplicationUser> userManager,
    IRefreshTokenService refreshTokenService,
    IMcpServiceTokenStore serviceTokens) : ICommandHandler<RevokePersonCommand, Unit>
{
    public async Task<Unit> Handle(RevokePersonCommand request, CancellationToken cancellationToken)
    {
        if (request.UserId == request.RequestedByUserId)
            throw new CannotRevokeSelfException();

        var user = await userManager.FindByIdAsync(request.UserId)
            ?? throw new UserNotFoundException();

        if (await userManager.IsInRoleAsync(user, AuthRoles.Owner))
            throw new CannotRevokeOwnerException();

        Ensure(await userManager.SetLockoutEnabledAsync(user, true), "enable lockout");
        Ensure(await userManager.SetLockoutEndDateAsync(user, PersonStatus.RevokedLockoutEnd), "lock the account");

        var roles = await userManager.GetRolesAsync(user);
        if (roles.Count > 0)
            Ensure(await userManager.RemoveFromRolesAsync(user, roles), "remove roles");

        var grants = (await userManager.GetClaimsAsync(user)).Where(c => c.Type == Permissions.ClaimType).ToList();
        if (grants.Count > 0)
            Ensure(await userManager.RemoveClaimsAsync(user, grants), "remove permission grants");

        Ensure(await userManager.UpdateSecurityStampAsync(user), "rotate the security stamp");

        await refreshTokenService.RevokeAsync(user.Id, cancellationToken);
        await serviceTokens.RevokeAllAsync(user.Id, cancellationToken);

        return Unit.Value;
    }

    private static void Ensure(IdentityResult result, string action)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException(
                $"Failed to {action}: {string.Join(", ", result.Errors.Select(e => e.Description))}");
    }
}
