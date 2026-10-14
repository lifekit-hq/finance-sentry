using System.Text.Json;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Auth.Domain.Entities;
using FinanceSentry.Modules.Auth.Infrastructure.Persistence;
using Hangfire;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FinanceSentry.Modules.Auth.Infrastructure.Jobs;

/// <summary>
/// Daily: erases the data of every closed account whose grace period has ended and was not reopened, then deletes the
/// account itself. Each closure is purged in its own scope, so one failure leaves the others to finish and is retried
/// on the next run. Idempotent: every eraser deletes by the person's id only, so a re-run (or a run after a crash
/// between erasing and recording) deletes nothing more; a purged closure is never picked up again. The closure record
/// itself stays as the audit trail.
/// </summary>
public sealed class AccountPurgeJob(
    IServiceScopeFactory scopes,
    TimeProvider clock,
    ILogger<AccountPurgeJob> logger)
{
    public const string JobId = "account-purge";

    [AutomaticRetry(Attempts = 0)]
    [DisableConcurrentExecution(timeoutInSeconds: 600)]
    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        List<Guid> due;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            var now = clock.GetUtcNow().UtcDateTime;
            due = await db.AccountClosures
                .Where(c => c.ReopenedAt == null && c.PurgedAt == null && c.PurgeDueAt <= now)
                .OrderBy(c => c.PurgeDueAt)
                .Select(c => c.Id)
                .ToListAsync(ct);
        }

        foreach (var closureId in due)
        {
            try
            {
                await PurgeAsync(closureId, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Purging closed account (closure {ClosureId}) failed; retried on the next run", closureId);
            }
        }
    }

    private async Task PurgeAsync(Guid closureId, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<AuthDbContext>();

        var closure = await db.AccountClosures.SingleAsync(c => c.Id == closureId, ct);
        if (!closure.IsPending)
            return;

        var erased = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var eraser in services.GetServices<IUserDataEraser>())
            erased[eraser.Module] = erased.GetValueOrDefault(eraser.Module) + await eraser.EraseAsync(closure.UserId, ct);

        // Last, after every module's rows: the user's claims, roles, logins and tokens go with it by cascade.
        var users = services.GetRequiredService<UserManager<ApplicationUser>>();
        if (await users.FindByIdAsync(closure.UserId.ToString()) is { } user)
        {
            var deleted = await users.DeleteAsync(user);
            if (!deleted.Succeeded)
                throw new InvalidOperationException(
                    $"Failed to delete the account: {string.Join(", ", deleted.Errors.Select(e => e.Description))}");
            erased["Account"] = 1;
        }

        closure.MarkPurged(clock.GetUtcNow().UtcDateTime, JsonSerializer.Serialize(erased));
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Closed account {UserId} purged: {Rows} rows erased", closure.UserId, erased.Values.Sum());
    }
}
