namespace FinanceSentry.API.Commands;

using FinanceSentry.Modules.BankSync.Application.Services;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// One-shot repair of the open coverage holes: moves each hole's <c>From</c> back to the first quiet week of its gap (the
/// sentinel used to stop at the first stray purchase week). See <see cref="CoverageHoleStartRepairService"/>.
///
/// Invoked via <c>dotnet FinanceSentry.API.dll repair-coverage-hole-start [--apply] [--account &lt;guid&gt;]</c>
/// (<c>scripts/repair-coverage-hole-start.sh</c>) - not a web endpoint. Without <c>--apply</c> it is a dry run: it prints
/// every open hole, old start against new, and writes nothing (no backup table either). <c>--apply</c> backs the pending
/// rows up to <c>bank_sync.coverage_holes_backup_start</c> first, then moves their start in one transaction; running it
/// again changes nothing. <c>--account</c> limits the run to that account's open hole.
/// </summary>
public static class RepairCoverageHoleStartCommand
{
    public const string Verb = "repair-coverage-hole-start";

    public static async Task RunAsync(IServiceProvider services, string[] args)
    {
        var apply = args.Contains("--apply");
        if (apply && args.Contains("--dry-run"))
        {
            Console.Error.WriteLine($"[{Verb}] --apply and --dry-run are mutually exclusive.");
            Environment.ExitCode = 2;
            return;
        }

        Guid? account = null;
        var requested = ValueOf(args, "--account");
        if (requested is not null)
        {
            if (!Guid.TryParse(requested, out var parsed))
            {
                Console.Error.WriteLine($"[{Verb}] --account '{requested}' is not a guid.");
                Environment.ExitCode = 2;
                return;
            }

            account = parsed;
        }

        using var scope = services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<CoverageHoleStartRepairService>();
        Console.WriteLine($"[{Verb}] {(apply ? "APPLY" : "DRY RUN (nothing is written)")}");

        var report = await service.RepairAsync(account, apply);

        Console.WriteLine("hole                                  account                               status     from (old -> new)");
        foreach (var r in report.Rows)
        {
            Console.WriteLine(
                $"{r.HoleId}  {r.AccountId}  {r.Status,-9}  {r.OldFrom:yyyy-MM-dd} -> {r.NewFrom:yyyy-MM-dd}");
        }

        Console.WriteLine($"[{Verb}] {report.Rows.Count} open holes: pending={report.Pending} unchanged={report.Unchanged}");
        if (apply)
            Console.WriteLine($"[{Verb}] applied: {report.Pending} holes moved; originals in bank_sync.{CoverageHoleStartRepairService.BackupTable}.");
        else
            Console.WriteLine($"[{Verb}] dry run only. Re-run with --apply to write.");
    }

    private static string? ValueOf(string[] args, string flag)
    {
        var i = Array.IndexOf(args, flag);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
