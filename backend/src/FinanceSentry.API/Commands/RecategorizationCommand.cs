namespace FinanceSentry.API.Commands;

using FinanceSentry.Core.Auth;
using FinanceSentry.Modules.BankSync.Application.Services;
using FinanceSentry.Modules.BankSync.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// One-shot maintenance command: re-categorizes existing transactions after the category
/// taxonomy overhaul by re-fetching the raw MCC/PFC from the providers (the raw signal was
/// not stored on rows ingested before the overhaul, so it cannot be recovered from the DB).
///
/// Invoked via <c>dotnet FinanceSentry.API.dll recategorize [userId]</c> — not a web endpoint.
/// Omit the userId to process every user that has bank accounts.
///
/// <c>--from CATEGORY --to CATEGORY [--dry-run]</c> narrows the pass to one idempotent category move
/// (rows in CATEGORY the ladder now resolves to the target; no provider re-fetch; nothing else changes).
/// </summary>
public static class RecategorizationCommand
{
    public const string Verb = "recategorize";

    public static async Task RunAsync(IServiceProvider services, string[] args)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<BankSyncDbContext>();

        List<Guid> userIds;
        var moveScope = ParseScope(args);
        if (args.Length > 1 && Guid.TryParse(args[1], out var userId))
            userIds = [userId];
        else
            // Offline command: no person in scope, so it opts out of the Owner filter to enumerate every user.
            userIds = await db.BankAccounts.IgnoreQueryFilters([OwnerQueryFilter.Name, BankSyncDbContext.AccountActiveFilterName]).Select(a => a.UserId).Distinct().ToListAsync();

        // The service is intentionally not DI-registered — this is a one-off, so build it inline.
        var service = ActivatorUtilities.CreateInstance<TransactionRecategorizationService>(sp);

        Console.WriteLine($"[recategorize] processing {userIds.Count} user(s)...");
        foreach (var id in userIds)
        {
            var r = await service.RecategorizeUserAsync(id, scope: moveScope);
            Console.WriteLine(
                $"[recategorize] {id}: examined={r.Examined} re-resolved={r.ReResolved} " +
                $"re-fetched={r.ReFetchedUpdated} still-uncategorized={r.StillUncategorized}");
        }
        Console.WriteLine("[recategorize] done.");
    }

    private static RecategorizationScope? ParseScope(string[] args)
    {
        var from = ArgValue(args, "--from");
        var to = ArgValue(args, "--to");
        var dryRun = args.Contains("--dry-run");
        if (from is null && to is null)
            return dryRun
                ? throw new ArgumentException("--dry-run requires --from and --to.")
                : null;
        if (from is null || to is null)
            throw new ArgumentException("--from and --to must be given together.");
        return new RecategorizationScope(from, to, dryRun);
    }

    private static string? ArgValue(string[] args, string flag)
    {
        var i = Array.IndexOf(args, flag);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
