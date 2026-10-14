using System.Text.Json;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Auth.Application.Interfaces;
using FinanceSentry.Modules.Auth.Domain.Entities;
using FinanceSentry.Modules.Auth.Domain.Exceptions;
using FinanceSentry.Modules.Auth.Domain.People;
using FinanceSentry.Modules.Auth.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FinanceSentry.Modules.Auth.Application.Commands;

/// <summary>
/// Closes the caller's own account. At once: the account is locked out for good, unlinked from the sign-in provider,
/// its security stamp rotated and every refresh and MCP service token revoked (the API loads the account per request,
/// so a token already issued stops working on its next request), and every module deletes the provider keys it holds
/// for the person and stops their scheduled work. The rest of the data stays until the purge job erases it after
/// <see cref="AccountClosure.GracePeriod"/>, unless an Owner reopens the account first.
/// <para>
/// The command carries no target: it acts on the authenticated caller only. The last Owner is refused, so the
/// instance always keeps someone who can manage it. Idempotent: closing an account that already has a pending closure
/// returns that closure.
/// </para>
/// </summary>
public class CloseAccountCommandHandler(
    UserManager<ApplicationUser> userManager,
    AuthDbContext db,
    IRefreshTokenService refreshTokenService,
    IMcpServiceTokenStore serviceTokens,
    IEnumerable<IUserCredentialRevoker> credentialRevokers,
    TimeProvider clock,
    ILogger<CloseAccountCommandHandler> logger) : ICommandHandler<CloseAccountCommand, AccountClosureDto>
{
    public async Task<AccountClosureDto> Handle(CloseAccountCommand request, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(request.UserId.ToString())
            ?? throw new UserNotFoundException();

        if (string.IsNullOrWhiteSpace(request.ConfirmEmail)
            || user.NormalizedEmail is null
            || userManager.NormalizeEmail(request.ConfirmEmail.Trim()) != user.NormalizedEmail)
        {
            throw new ClosureConfirmationMismatchException();
        }

        var pending = await db.AccountClosures
            .FirstOrDefaultAsync(c => c.UserId == request.UserId && c.ReopenedAt == null && c.PurgedAt == null, cancellationToken);
        if (pending is not null)
            return ToDto(pending);

        if (await LastOwner.IsAsync(userManager, user))
            throw new LastOwnerCannotCloseException();

        // The closure record and the lockout are committed last and in one save, so a failure in an earlier step leaves
        // the account signed in and able to retry, and a pending closure always means the account is fully closed.
        var closure = new AccountClosure(request.UserId, clock.GetUtcNow().UtcDateTime);

        await refreshTokenService.RevokeAsync(user.Id, cancellationToken);
        await serviceTokens.RevokeAllAsync(user.Id, cancellationToken);

        closure.RecordCredentialRevocation(JsonSerializer.Serialize(await RevokeCredentialsAsync(request.UserId, cancellationToken)));

        foreach (var login in await userManager.GetLoginsAsync(user))
            Ensure(await userManager.RemoveLoginAsync(user, login.LoginProvider, login.ProviderKey), "unlink the sign-in");

        // The user store saves the shared context, so this one update persists the lockout, the new security stamp and
        // the closure record together.
        db.AccountClosures.Add(closure);
        user.LockoutEnabled = true;
        user.LockoutEnd = PersonStatus.RevokedLockoutEnd;
        Ensure(await userManager.UpdateSecurityStampAsync(user), "lock the account");

        logger.LogInformation(
            "Account {UserId} closed; data purge due {PurgeDueAt:O}", request.UserId, closure.PurgeDueAt);

        return ToDto(closure);
    }

    // A revoker reports a failed provider revoke in its outcome and throws only for a local failure, which aborts the
    // closure before it commits so the person stays signed in and can retry.
    private async Task<Dictionary<string, CredentialRevocation>> RevokeCredentialsAsync(Guid userId, CancellationToken ct)
    {
        var outcomes = new Dictionary<string, CredentialRevocation>(StringComparer.Ordinal);
        foreach (var revoker in credentialRevokers)
            outcomes[revoker.Module] = await revoker.RevokeAsync(userId, ct);

        return outcomes;
    }

    private static AccountClosureDto ToDto(AccountClosure closure) => new(closure.ClosedAt, closure.PurgeDueAt);

    private static void Ensure(IdentityResult result, string action)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException(
                $"Failed to {action}: {string.Join(", ", result.Errors.Select(e => e.Description))}");
    }
}
