namespace FinanceSentry.Modules.Wealth.Application.Queries;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Core.Utils;
using FinanceSentry.Modules.Wealth.Domain.Repositories;

/// <summary>
/// A FIRE (financial independence) date is reachable, already reached, not reachable at the
/// current savings rate, or not yet computable for lack of history — each its own state so the
/// tile renders a sentence rather than a number the reader has to interpret.
/// </summary>
public enum FireProjectionStatus
{
    Projected,
    AlreadyReached,
    NotSaving,
    InsufficientHistory,
}

/// <summary>
/// Every field the tile needs to state its own arithmetic in words: the target and its two
/// assumptions, the inputs that produced it, and the outcome. Computed on read — nothing here
/// is persisted, so changing an assumption changes the next read with no job run.
/// </summary>
public record FireProjectionResponse(
    FireProjectionStatus Status,
    decimal Target,
    decimal CurrentNetWorth,
    decimal MonthlySavings,
    decimal AnnualSpend,
    decimal SafeWithdrawalRate,
    decimal RealAnnualReturn,
    DateOnly? ProjectedDate,
    decimal? MonthsToFire,
    bool HasStaleSleeves);

public record GetFireProjectionQuery(Guid UserId) : IQuery<FireProjectionResponse>;

public class GetFireProjectionQueryHandler(
    INetWorthSnapshotRepository snapshots,
    IHonestMonthlyFlowReader monthlyFlow,
    IUserFireAssumptionsReader assumptions,
    TimeProvider clock) : IQueryHandler<GetFireProjectionQuery, FireProjectionResponse>
{
    private static readonly FireAssumptions DefaultAssumptions = new(0.04m, 0.05m);

    /// <summary>
    /// Below this many COMPLETE months of flow history, a median is noise wearing a number's
    /// clothes — the same floor the net-worth projection tile uses
    /// (<c>MIN_PROJECTION_MONTHS</c> in <c>dashboard.constants.ts</c>).
    /// </summary>
    private const int MinCompleteMonths = 3;

    private const int MonthsPerYear = 12;
    private const int FlowLookbackMonths = 6;

    private readonly INetWorthSnapshotRepository _snapshots = snapshots;
    private readonly IHonestMonthlyFlowReader _monthlyFlow = monthlyFlow;
    private readonly IUserFireAssumptionsReader _assumptions = assumptions;
    private readonly TimeProvider _clock = clock;

    public async Task<FireProjectionResponse> Handle(GetFireProjectionQuery query, CancellationToken cancellationToken)
    {
        var userAssumptions = await _assumptions.GetAsync(query.UserId, cancellationToken) ?? DefaultAssumptions;

        var snapshot = await _snapshots.GetLatestByUserIdAsync(query.UserId, cancellationToken);
        var flows = await _monthlyFlow.GetMonthlyFlowAsync(query.UserId, FlowLookbackMonths, cancellationToken);

        var currentMonthKey = _clock.GetUtcNow().ToString("yyyy-MM");
        var completeMonths = flows.Where(f => f.Month != currentMonthKey).ToList();
        var hasStaleSleeves = !string.IsNullOrEmpty(snapshot?.StaleSleeves);

        if (snapshot is null || completeMonths.Count < MinCompleteMonths)
        {
            return new FireProjectionResponse(
                FireProjectionStatus.InsufficientHistory,
                Target: 0m,
                CurrentNetWorth: snapshot?.TotalNetWorth ?? 0m,
                MonthlySavings: 0m,
                AnnualSpend: 0m,
                userAssumptions.SafeWithdrawalRate,
                userAssumptions.RealAnnualReturn,
                ProjectedDate: null,
                MonthsToFire: null,
                hasStaleSleeves);
        }

        var annualSpend = MonthsPerYear * Median(completeMonths.Select(f => f.OutflowUsd));
        var monthlySavings = Median(completeMonths.Select(f => f.NetUsd));

        var calculation = FireCalculator.Calculate(
            annualSpend, userAssumptions.SafeWithdrawalRate, monthlySavings,
            snapshot.TotalNetWorth, userAssumptions.RealAnnualReturn);

        var status = calculation.Status switch
        {
            FireReachability.AlreadyReached => FireProjectionStatus.AlreadyReached,
            FireReachability.NotSaving => FireProjectionStatus.NotSaving,
            _ => FireProjectionStatus.Projected,
        };

        var projectedDate = calculation.MonthsToFire is { } months
            ? DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime).AddMonths((int)Math.Ceiling((double)months))
            : (DateOnly?)null;

        return new FireProjectionResponse(
            status,
            calculation.Target,
            snapshot.TotalNetWorth,
            monthlySavings,
            annualSpend,
            userAssumptions.SafeWithdrawalRate,
            userAssumptions.RealAnnualReturn,
            projectedDate,
            calculation.MonthsToFire,
            hasStaleSleeves);
    }

    /// <summary>Even-length samples average the two middle values, mirroring the frontend's median helper (<c>dashboard.computed.ts</c>).</summary>
    private static decimal Median(IEnumerable<decimal> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        var mid = sorted.Count / 2;
        return sorted.Count % 2 == 0
            ? (sorted[mid - 1] + sorted[mid]) / 2m
            : sorted[mid];
    }
}
