namespace FinanceSentry.API.Commands;

using FinanceSentry.Modules.BankSync.Application.Services;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// One-shot repair for #1229: restores the pending rows the old pending reconciler retired although their settled
/// copy had not arrived, then settles those the provider now reports as booked. Run it once, after the date-aware
/// reconciler is deployed. Idempotent: a second run finds nothing to restore. See <see cref="IRetiredPendingRepairService"/>.
///
/// Invoked via <c>dotnet FinanceSentry.API.dll repair-retired-pending [--dry-run]</c> (<c>scripts/repair-retired-pending.sh</c>)
/// — not a web endpoint. <c>--dry-run</c> only counts what it would restore and reads no provider.
/// </summary>
public static class RepairRetiredPendingCommand
{
    public const string Verb = "repair-retired-pending";

    public static async Task RunAsync(IServiceProvider services, string[] args)
    {
        var dryRun = args.Contains("--dry-run");

        using var scope = services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IRetiredPendingRepairService>();

        Console.WriteLine($"[{Verb}] running (dryRun={dryRun})...");
        var r = await service.RepairAsync(dryRun);
        Console.WriteLine(
            $"[{Verb}] before: eligible={r.EligibleBefore} | reactivated={r.Reactivated} settled={r.Settled} " +
            $"accounts-unread={r.AccountsUnread} | after: eligible={r.EligibleAfter}");
        Console.WriteLine($"[{Verb}] done.");
    }
}
