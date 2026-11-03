namespace FinanceSentry.Modules.Wealth.Application.Services;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Core.Utils;
using FinanceSentry.Modules.Wealth.Domain;

/// <summary>One stored day the July 2026 history repair rewrites, with the figures the repair was reviewed against.</summary>
/// <param name="Date">The snapshot day (UTC).</param>
/// <param name="ExpectedTotal">The <c>TotalNetWorth</c> the row must still hold for the repair to touch it.</param>
/// <param name="ProposedTotal">The <c>TotalNetWorth</c> the repair writes.</param>
/// <param name="BankingDelta">USD added to the banking sleeve: minus the 90,000 UAH Monobank credit limit at that day's NBU rate.</param>
/// <param name="BrokerageRederived">The brokerage sleeve was $0 or a frozen quote that day and is replaced by positions x prior close.</param>
public sealed record NetWorthRepairDay(
    DateOnly Date, decimal ExpectedTotal, decimal ProposedTotal, decimal BankingDelta, bool BrokerageRederived)
{
    /// <summary>What the brokerage sleeve moves by: whatever part of the day's change the banking correction does not explain.</summary>
    public decimal BrokerageDelta => ProposedTotal - ExpectedTotal - BankingDelta;
}

/// <summary>The figures a repaired snapshot row carries, derived from the stored row and its <see cref="NetWorthRepairDay"/>.</summary>
public sealed record RepairedNetWorthSnapshot(
    decimal Banking,
    decimal Brokerage,
    decimal Total,
    decimal? Cash,
    decimal? BrokerageInvested,
    string? StaleSleeves,
    bool IsApproximate,
    FxStatus? Fx,
    decimal BankingDelta,
    decimal BrokerageDelta);

/// <summary>
/// The one-off repair of the owner's net-worth history for 2026-07-01..2026-08-30 (finance-sentry numbers report, R1).
/// Two defects are fixed, both explained in <c>docs/money-semantics.md</c> (§8, "July 2026 history repair"):
/// <list type="bullet">
/// <item>Before #531 (2026-08-31) the banking sleeve counted Monobank's 90,000 UAH credit limit as own funds, so
/// every day from 07-01 to 08-30 is overstated by that limit at the day's rate.</item>
/// <item>On 07-02, 07-03, 07-11 and 07-14..07-20 the brokerage sleeve was $0 (IBeam session lost) or a frozen quote
/// week; those days take positions x prior close instead, flagged stale and approximate with today's-rate FX status.</item>
/// </list>
/// The values are fixed here, not recomputed at run time: each row is matched on the total it held when the report was
/// written, so a row that was already repaired, or that holds anything else, is never touched.
/// </summary>
public static class JulyNetWorthHistoryRepairPlan
{
    /// <summary>The credit limit Monobank counted as own funds, in hryvnia (docs/money-semantics.md §1).</summary>
    public const decimal MonobankCreditLimitUah = 90_000m;

    public const string BackupTable = "net_worth_snapshots_backup_july_2026";
    public const string SourcesBackupTable = "net_worth_snapshot_sources_backup_july_2026";

    public static IReadOnlyList<NetWorthRepairDay> Days { get; } =
    [
        new(new DateOnly(2026, 7, 1), 17840.21m, 15830.91m, -2009.30m, false),
        new(new DateOnly(2026, 7, 2), 8442.53m, 16654.20m, -2010.33m, true),
        new(new DateOnly(2026, 7, 3), 8599.62m, 16188.76m, -2008.86m, true),
        new(new DateOnly(2026, 7, 4), 16204.58m, 14195.72m, -2008.86m, false),
        new(new DateOnly(2026, 7, 5), 16104.46m, 14095.60m, -2008.86m, false),
        new(new DateOnly(2026, 7, 6), 15785.16m, 13765.85m, -2019.31m, false),
        new(new DateOnly(2026, 7, 7), 15818.29m, 13798.71m, -2019.58m, false),
        new(new DateOnly(2026, 7, 8), 15335.54m, 13313.51m, -2022.03m, false),
        new(new DateOnly(2026, 7, 9), 15444.34m, 13420.80m, -2023.54m, false),
        new(new DateOnly(2026, 7, 10), 15739.88m, 13718.11m, -2021.77m, false),
        new(new DateOnly(2026, 7, 11), 5652.16m, 13536.39m, -2021.77m, true),
        new(new DateOnly(2026, 7, 12), 15309.88m, 13288.11m, -2021.77m, false),
        new(new DateOnly(2026, 7, 13), 15236.93m, 13214.23m, -2022.70m, false),
        new(new DateOnly(2026, 7, 14), 15127.03m, 12417.19m, -2014.57m, true),
        new(new DateOnly(2026, 7, 15), 15210.23m, 12942.76m, -2005.20m, true),
        new(new DateOnly(2026, 7, 16), 15145.44m, 12421.91m, -2011.26m, true),
        new(new DateOnly(2026, 7, 17), 15062.70m, 11692.28m, -2017.15m, true),
        new(new DateOnly(2026, 7, 18), 5028.16m, 11559.01m, -2017.15m, true),
        new(new DateOnly(2026, 7, 19), 4993.04m, 11523.89m, -2017.15m, true),
        new(new DateOnly(2026, 7, 20), 4966.80m, 11499.92m, -2014.88m, true),
        new(new DateOnly(2026, 7, 21), 13620.55m, 11607.26m, -2013.29m, false),
        new(new DateOnly(2026, 7, 22), 14407.26m, 12396.10m, -2011.16m, false),
        new(new DateOnly(2026, 7, 23), 14169.08m, 12158.85m, -2010.23m, false),
        new(new DateOnly(2026, 7, 24), 13855.61m, 11847.17m, -2008.44m, false),
        new(new DateOnly(2026, 7, 25), 13053.91m, 11045.47m, -2008.44m, false),
        new(new DateOnly(2026, 7, 26), 12900.18m, 10891.74m, -2008.44m, false),
        new(new DateOnly(2026, 7, 27), 12850.65m, 10842.11m, -2008.54m, false),
        new(new DateOnly(2026, 7, 28), 12464.25m, 10457.87m, -2006.38m, false),
        new(new DateOnly(2026, 7, 29), 12439.22m, 10436.00m, -2003.22m, false),
        new(new DateOnly(2026, 7, 30), 11875.70m, 9870.30m, -2005.40m, false),
        new(new DateOnly(2026, 7, 31), 12912.32m, 10898.52m, -2013.80m, false),
        new(new DateOnly(2026, 8, 1), 17150.83m, 15137.03m, -2013.80m, false),
        new(new DateOnly(2026, 8, 2), 17126.67m, 15112.87m, -2013.80m, false),
        new(new DateOnly(2026, 8, 3), 17072.42m, 15056.27m, -2016.15m, false),
        new(new DateOnly(2026, 8, 4), 17426.73m, 15417.25m, -2009.48m, false),
        new(new DateOnly(2026, 8, 5), 17341.80m, 15330.57m, -2011.23m, false),
        new(new DateOnly(2026, 8, 6), 13712.11m, 11698.21m, -2013.90m, false),
        new(new DateOnly(2026, 8, 7), 13469.71m, 11459.10m, -2010.61m, false),
        new(new DateOnly(2026, 8, 8), 14241.05m, 12230.44m, -2010.61m, false),
        new(new DateOnly(2026, 8, 9), 14257.56m, 12246.95m, -2010.61m, false),
        new(new DateOnly(2026, 8, 10), 14147.82m, 12137.00m, -2010.82m, false),
        new(new DateOnly(2026, 8, 11), 13963.48m, 11955.92m, -2007.56m, false),
        new(new DateOnly(2026, 8, 12), 14240.18m, 12234.21m, -2005.97m, false),
        new(new DateOnly(2026, 8, 13), 14592.74m, 12579.89m, -2012.85m, false),
        new(new DateOnly(2026, 8, 14), 14904.00m, 12890.52m, -2013.48m, false),
        new(new DateOnly(2026, 8, 15), 14766.30m, 12752.82m, -2013.48m, false),
        new(new DateOnly(2026, 8, 16), 14589.18m, 12575.70m, -2013.48m, false),
        new(new DateOnly(2026, 8, 17), 14507.25m, 12494.10m, -2013.15m, false),
        new(new DateOnly(2026, 8, 18), 14769.75m, 12756.05m, -2013.70m, false),
        new(new DateOnly(2026, 8, 19), 13950.19m, 11940.17m, -2010.02m, false),
        new(new DateOnly(2026, 8, 20), 14521.55m, 12508.15m, -2013.40m, false),
        new(new DateOnly(2026, 8, 21), 14871.41m, 12854.12m, -2017.29m, false),
        new(new DateOnly(2026, 8, 22), 15269.22m, 13251.93m, -2017.29m, false),
        new(new DateOnly(2026, 8, 23), 15380.81m, 13363.52m, -2017.29m, false),
        new(new DateOnly(2026, 8, 24), 15049.11m, 13033.84m, -2015.27m, false),
        new(new DateOnly(2026, 8, 25), 14727.54m, 12714.41m, -2013.13m, false),
        new(new DateOnly(2026, 8, 26), 14757.60m, 12742.90m, -2014.70m, false),
        new(new DateOnly(2026, 8, 27), 15268.74m, 13249.52m, -2019.22m, false),
        new(new DateOnly(2026, 8, 28), 15175.88m, 13155.43m, -2020.45m, false),
        new(new DateOnly(2026, 8, 29), 14811.34m, 12790.89m, -2020.45m, false),
        new(new DateOnly(2026, 8, 30), 14865.12m, 12844.67m, -2020.45m, false),
    ];

    /// <summary>The figures the row takes once repaired. Total is exactly the day's proposed total.</summary>
    public static RepairedNetWorthSnapshot Repair(NetWorthSnapshot row, NetWorthRepairDay day)
    {
        var banking = row.BankingTotal + day.BankingDelta;
        var brokerage = row.BrokerageTotal + day.BrokerageDelta;
        var fx = row.FxStatus;
        var stale = row.StaleSleeves;
        var approximate = row.IsApproximate;
        if (day.BrokerageRederived)
        {
            fx = (row.FxStatus ?? FxStatus.Exact).Worst(FxStatus.TodayRate);
            stale = WithStale(row.StaleSleeves, WealthSourceIds.Brokerage);
            approximate = true;
        }

        return new RepairedNetWorthSnapshot(
            banking,
            brokerage,
            row.TotalNetWorth + day.BankingDelta + day.BrokerageDelta,
            // The banking correction is cash; the brokerage move is invested (a re-derived day has no idle cash).
            row.CashTotal + day.BankingDelta,
            row.BrokerageInvested + day.BrokerageDelta,
            stale,
            approximate,
            fx,
            day.BankingDelta,
            day.BrokerageDelta);
    }

    /// <summary>Adds a sleeve to the comma-separated stale list, keeping the banking, brokerage, crypto order.</summary>
    public static string? WithStale(string? stale, string sleeve)
    {
        var names = (stale ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        if (!names.Contains(sleeve))
            names.Add(sleeve);

        return string.Join(',', names.OrderBy(SleeveOrder));
    }

    private static int SleeveOrder(string name)
    {
        for (var i = 0; i < WealthSourceIds.Sleeves.Count; i++)
            if (WealthSourceIds.Sleeves[i] == name)
                return i;

        return WealthSourceIds.Sleeves.Count;
    }
}
