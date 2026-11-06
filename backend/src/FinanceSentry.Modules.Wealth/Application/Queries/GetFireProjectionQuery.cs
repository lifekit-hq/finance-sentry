namespace FinanceSentry.Modules.Wealth.Application.Queries;

using System.Text.Json.Serialization;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Measures;
using FinanceSentry.Modules.Wealth.Application.Services;

/// <summary>
/// A FIRE (financial independence) date is reachable, already reached, not reachable at the
/// current savings rate, or not yet computable for lack of history — each its own state so the
/// tile renders a sentence rather than a number the reader has to interpret. Serialized by name
/// so the wire contract is the state, not an ordinal.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<FireProjectionStatus>))]
public enum FireProjectionStatus
{
    Projected,
    AlreadyReached,
    NotSaving,
    InsufficientHistory,

    /// <summary>A scenario's savings path never reaches the target inside the simulated 100 years. Never a baseline status.</summary>
    NotWithinHorizon,
}

/// <summary>
/// The financial-independence date on the savings path with the person's career events applied, next to the baseline and never
/// replacing it (#454 S5). <paramref name="Outcome"/> is how it resolved; the months and dates are null unless it is
/// <see cref="FireProjectionStatus.Projected"/> (and zero months when already reached). The whole block is
/// <see cref="ScenarioStatus.Unreadable"/> with no outcome when the events could not be read on this host.
/// </summary>
public record FireScenario(
    ScenarioStatus Status,
    int EventCount,
    FireProjectionStatus? Outcome,
    decimal? MonthsToFire,
    DateOnly? ProjectedDate,
    ProjectionRange? MonthsToFireRange,
    DateOnly? EarliestDate,
    DateOnly? LatestDate,
    string Label = ScenarioText.Label,
    string Basis = ScenarioText.Basis);

/// <summary>
/// Every field the tile needs to state its own arithmetic in words: the target and its two
/// assumptions, the inputs that produced it, and the outcome. Computed on read — nothing here
/// is persisted, so changing an assumption changes the next read with no job run. The handler
/// computes in USD; the API controller re-expresses every amount in the profile base currency and
/// sets <paramref name="BaseCurrency"/> to the code the figures are actually in.
/// <para>
/// The projection is a range (M10, C8): <paramref name="RealAnnualReturn"/>, <paramref name="ProjectedDate"/>
/// and <paramref name="MonthsToFire"/> are the central case, <paramref name="RealAnnualReturnRange"/> the
/// shared low / central / high return assumption, and <paramref name="MonthsToFireRange"/>,
/// <paramref name="EarliestDate"/> and <paramref name="LatestDate"/> the outcome at the high and low return.
/// <paramref name="CompleteMonths"/> is how many complete months the median spend and savings are taken over.
/// </para>
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
    bool HasStaleSleeves,
    string BaseCurrency = "USD",
    ProjectionRange RealAnnualReturnRange = default,
    ProjectionRange? MonthsToFireRange = null,
    DateOnly? EarliestDate = null,
    DateOnly? LatestDate = null,
    int CompleteMonths = 0,
    FireScenario? Scenario = null);

public record GetFireProjectionQuery(Guid UserId) : IQuery<FireProjectionResponse>;

public class GetFireProjectionQueryHandler(
    ProjectionInputsReader inputs,
    ScenarioReader scenarios,
    TimeProvider clock) : IQueryHandler<GetFireProjectionQuery, FireProjectionResponse>
{
    private const int MonthsPerYear = 12;

    private readonly ProjectionInputsReader _inputs = inputs;
    private readonly ScenarioReader _scenarios = scenarios;
    private readonly TimeProvider _clock = clock;

    public async Task<FireProjectionResponse> Handle(GetFireProjectionQuery query, CancellationToken cancellationToken)
    {
        var inputs = await _inputs.ReadAsync(query.UserId, cancellationToken);
        var assumptions = inputs.Assumptions;

        if (inputs.Snapshot is null || inputs.SavingsPath is null)
        {
            return new FireProjectionResponse(
                FireProjectionStatus.InsufficientHistory,
                Target: 0m,
                CurrentNetWorth: inputs.Snapshot?.TotalNetWorth ?? 0m,
                MonthlySavings: 0m,
                AnnualSpend: 0m,
                assumptions.SafeWithdrawalRate,
                assumptions.RealAnnualReturn,
                ProjectedDate: null,
                MonthsToFire: null,
                inputs.HasStaleSleeves,
                "USD",
                assumptions.RealAnnualReturns,
                CompleteMonths: inputs.CompleteMonths.Count);
        }

        var annualSpend = MonthsPerYear * Projection.Median(inputs.CompleteMonths.Select(f => f.OutflowUsd));
        var monthlySavings = inputs.SavingsPath.MonthlySavingsUsd;

        var calculation = Projection.FinancialIndependence(
            annualSpend, inputs.Snapshot.TotalNetWorth, monthlySavings, assumptions);

        var status = calculation.Status switch
        {
            FireReachability.AlreadyReached => FireProjectionStatus.AlreadyReached,
            FireReachability.NotSaving => FireProjectionStatus.NotSaving,
            _ => FireProjectionStatus.Projected,
        };

        var months = calculation.MonthsToTarget;
        var scenario = await ScenarioOf(
            query.UserId, annualSpend, inputs.Snapshot.TotalNetWorth, monthlySavings, assumptions, cancellationToken);
        return new FireProjectionResponse(
            status,
            calculation.TargetUsd,
            inputs.Snapshot.TotalNetWorth,
            monthlySavings,
            annualSpend,
            assumptions.SafeWithdrawalRate,
            assumptions.RealAnnualReturn,
            DateAfter(months?.Central),
            months?.Central,
            inputs.HasStaleSleeves,
            "USD",
            assumptions.RealAnnualReturns,
            months,
            DateAfter(months?.Low),
            DateAfter(months?.High),
            inputs.SavingsPath.CompleteMonths,
            scenario);
    }

    private async Task<FireScenario?> ScenarioOf(
        Guid userId, decimal annualSpend, decimal current, decimal monthlySavings, ProjectionAssumptions assumptions, CancellationToken ct)
    {
        var scenario = await _scenarios.ReadAsync(userId, ct);
        if (scenario.Status == ScenarioStatus.None)
            return null;
        if (!scenario.IsApplied)
            return new FireScenario(scenario.Status, scenario.Events.Count, null, null, null, null, null, null);

        var result = Projection.FinancialIndependence(
            annualSpend, current, monthlySavings, assumptions, scenario.Deltas(Projection.MaxScenarioMonths));
        var outcome = result.Status switch
        {
            FireReachability.AlreadyReached => FireProjectionStatus.AlreadyReached,
            FireReachability.NotSaving => FireProjectionStatus.NotSaving,
            FireReachability.NotWithinHorizon => FireProjectionStatus.NotWithinHorizon,
            _ => FireProjectionStatus.Projected,
        };
        var range = result.MonthsToTarget;
        return new FireScenario(
            scenario.Status, scenario.Events.Count, outcome, range?.Central, DateAfter(range?.Central), range,
            DateAfter(range?.Low), DateAfter(range?.High));
    }

    /// <summary>Today plus the months rounded up; null when there are no months.</summary>
    private DateOnly? DateAfter(decimal? months) => months is { } value
        ? DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime).AddMonths((int)Math.Ceiling((double)value))
        : null;
}
