using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Auth.Domain.Entities;
using FinanceSentry.Modules.Auth.Domain.Exceptions;
using FinanceSentry.Modules.Auth.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FinanceSentry.Modules.Auth.Application.Commands;

/// <summary>
/// Reopens a closed account during its grace period: the lockout is lifted and the closure recorded as reopened, so
/// the purge leaves the data alone. The sign-in link and the provider keys were removed at closing, so the person signs
/// in again the way an invited person does and reconnects their providers.
/// </summary>
public class ReopenAccountCommandHandler(
    UserManager<ApplicationUser> userManager,
    AuthDbContext db,
    TimeProvider clock) : ICommandHandler<ReopenAccountCommand, Unit>
{
    public async Task<Unit> Handle(ReopenAccountCommand request, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        if (!Guid.TryParse(request.UserId, out var userId))
            throw new UserNotFoundException();

        var closure = await db.AccountClosures
            .FirstOrDefaultAsync(c => c.UserId == userId && c.ReopenedAt == null && c.PurgedAt == null, cancellationToken);
        if (closure is null || closure.PurgeDueAt <= now)
            throw new AccountNotClosedException();

        var user = await userManager.FindByIdAsync(request.UserId)
            ?? throw new UserNotFoundException();

        // A closed account an Owner then revoked has lost its roles: reopening must not quietly undo the revoke.
        if ((await userManager.GetRolesAsync(user)).Count == 0)
            throw new AccountNotClosedException();

        closure.Reopen(request.RequestedByUserId, now);

        // Saved together with the closure by the user store's save of the shared context.
        Ensure(await userManager.SetLockoutEndDateAsync(user, null), "lift the lockout");
        Ensure(await userManager.ResetAccessFailedCountAsync(user), "reset failed sign-ins");
        Ensure(await userManager.UpdateSecurityStampAsync(user), "rotate the security stamp");

        return Unit.Value;
    }

    private static void Ensure(IdentityResult result, string action)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException(
                $"Failed to {action}: {string.Join(", ", result.Errors.Select(e => e.Description))}");
    }
}
