namespace FinanceSentry.API.Commands;

using FinanceSentry.Core.Auth;
using FinanceSentry.Modules.Auth.Domain.Entities;
using FinanceSentry.Modules.Wealth.Application.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// One-shot repair of the owner's July-August 2026 net-worth history (finance-sentry numbers report, R1): removes the
/// Monobank credit limit the banking sleeve counted as own funds before #531 and re-derives the brokerage sleeve on the
/// days it was $0 or a frozen quote. See <see cref="JulyNetWorthHistoryRepairPlan"/> and
/// <see cref="NetWorthHistoryRepairService"/>.
///
/// Invoked via <c>dotnet FinanceSentry.API.dll repair-net-worth-history [--apply] [--user &lt;guid&gt;]</c>
/// (<c>scripts/repair-net-worth-history.sh</c>) - not a web endpoint. Without <c>--apply</c> it is a dry run: it prints
/// every planned row, old against new, and writes nothing (no backup table either). <c>--apply</c> backs the rows up to
/// <c>net_worth_snapshots_backup_july_2026</c> first, then rewrites them in one transaction; running it again changes
/// nothing. It touches only the Owner's rows: the one account holding the Owner role, or the account named by
/// <c>--user</c> when that account is an Owner.
/// </summary>
public static class RepairNetWorthHistoryCommand
{
    public const string Verb = "repair-net-worth-history";

    public static async Task RunAsync(IServiceProvider services, string[] args)
    {
        var apply = args.Contains("--apply");
        if (apply && args.Contains("--dry-run"))
        {
            Console.Error.WriteLine($"[{Verb}] --apply and --dry-run are mutually exclusive.");
            Environment.ExitCode = 2;
            return;
        }

        using var scope = services.CreateScope();
        var owner = await ResolveOwnerAsync(scope.ServiceProvider, ValueOf(args, "--user"));
        if (owner is null)
        {
            Environment.ExitCode = 2;
            return;
        }

        var service = scope.ServiceProvider.GetRequiredService<NetWorthHistoryRepairService>();
        Console.WriteLine($"[{Verb}] owner {owner} - {(apply ? "APPLY" : "DRY RUN (nothing is written)")}");

        NetWorthHistoryRepairReport report;
        try
        {
            report = await service.RepairAsync(owner.Value, apply);
        }
        catch (NetWorthHistoryRepairRefusedException refused)
        {
            Print(refused.Blockers);
            Console.Error.WriteLine($"[{Verb}] {refused.Message}. Nothing was changed.");
            Environment.ExitCode = 1;
            return;
        }

        Print(report.Rows);
        Console.WriteLine(
            $"[{Verb}] {report.Rows.Count} planned days: pending={report.Pending} already-repaired={report.AlreadyRepaired} " +
            $"blocked={report.Blockers.Count}");
        if (apply)
            Console.WriteLine($"[{Verb}] applied: {report.Pending} rows rewritten; originals in net_worth_snapshots_backup_july_2026 and net_worth_snapshot_sources_backup_july_2026.");
        else
            Console.WriteLine($"[{Verb}] dry run only. Re-run with --apply to write.");
    }

    private static void Print(IEnumerable<NetWorthRepairRow> rows)
    {
        Console.WriteLine("date        status           banking old -> new          brokerage old -> new        total old -> new");
        foreach (var r in rows)
        {
            Console.WriteLine(
                $"{r.Date:yyyy-MM-dd}  {r.Status,-15}  {Pair(r.OldBanking, r.NewBanking)}  {Pair(r.OldBrokerage, r.NewBrokerage)}  {Pair(r.OldTotal, r.NewTotal)}" +
                $"{(r.BrokerageRederived ? "  [brokerage re-derived]" : string.Empty)}{(r.Note is null ? string.Empty : $"  !! {r.Note}")}");
        }
    }

    private static string Pair(decimal? oldValue, decimal? newValue)
        => $"{Format(oldValue),10} -> {Format(newValue),-10}";

    private static string Format(decimal? v) => v?.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) ?? "-";

    private static string? ValueOf(string[] args, string flag)
    {
        var i = Array.IndexOf(args, flag);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    // The Owner role is held by exactly the one account that operates the deployment; with several owners the
    // caller must name which one (and it must be one of them), so the verb never guesses whose history to rewrite.
    private static async Task<Guid?> ResolveOwnerAsync(IServiceProvider sp, string? requested)
    {
        var users = sp.GetRequiredService<UserManager<ApplicationUser>>();
        var owners = (await users.GetUsersInRoleAsync(AuthRoles.Owner))
            .Select(u => Guid.TryParse(u.Id, out var id) ? id : (Guid?)null)
            .OfType<Guid>()
            .ToList();

        if (requested is not null)
        {
            if (Guid.TryParse(requested, out var wanted) && owners.Contains(wanted))
                return wanted;

            Console.Error.WriteLine($"[{Verb}] --user '{requested}' is not an account holding the {AuthRoles.Owner} role.");
            return null;
        }

        if (owners.Count == 1)
            return owners[0];

        Console.Error.WriteLine(
            $"[{Verb}] expected exactly one {AuthRoles.Owner} account, found {owners.Count}; name one with --user <guid>.");
        return null;
    }
}
