namespace FinanceSentry.Modules.Wealth.Application.Queries;

using System.Text.Json.Serialization;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Core.Measures;
using FinanceSentry.Modules.Wealth.Application.Services;

/// <summary>
/// Whether there are goals to track: <see cref="NoGoals"/> when the person has no current IPS or it lists no goal,
/// and the card renders its empty state. Serialized by name.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<GoalsProgressStatus>))]
public enum GoalsProgressStatus
{
    Shown,
    NoGoals,
}

/// <summary>
/// Where one goal stands (#454 S2). <see cref="OnTrack"/>: the current savings path, compounded at the central return,
/// reaches the target by its date. <see cref="Behind"/>: it does not (or the date has passed short of the target).
/// <see cref="NoTargetDate"/> / <see cref="NoTargetAmount"/>: the IPS goal lacks the one input a month count or a
/// required saving needs, so nothing is claimed. <see cref="InsufficientHistory"/>: no snapshot, or under three
/// complete months of flow, so the savings path (and so the verdict) is withheld. Serialized by name.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<GoalTrack>))]
public enum GoalTrack
{
    OnTrack,
    Behind,
    NoTargetDate,
    NoTargetAmount,
    InsufficientHistory,
}

/// <summary>Typed reasons a goal's figure or verdict is withheld (money-semantics §16).</summary>
public static class GoalsProgressReasons
{
    /// <summary>There is no net-worth snapshot to measure the goal against.</summary>
    public const string NoSnapshot = "NO_SNAPSHOT";

    /// <summary>Fewer than <see cref="Projection.MinCompleteMonths"/> complete months of flow: no savings path.</summary>
    public const string InsufficientMonths = "INSUFFICIENT_MONTHS";

    /// <summary>The target date has passed (or is this month) with the target not yet reached.</summary>
    public const string TargetDatePassed = "TARGET_DATE_PASSED";

    /// <summary>The contribution plan's cadence is not one the monthly equivalent can be worked out for.</summary>
    public const string UnrecognizedCadence = "UNRECOGNIZED_CADENCE";

    /// <summary>The target date is further out than <c>GoalHorizonMonths</c>: no required saving or projection is worked out.</summary>
    public const string TargetDateTooFar = "TARGET_DATE_TOO_FAR";
}

/// <summary>
/// What the person's career events do to one goal (#454 S5), shown beside the baseline figures and never replacing them:
/// the same range arithmetic with the events applied. <paramref name="RequiredMonthlySaving"/> is the level saving still needed
/// once the events are counted (zero when they get there on their own). Present only when a scenario was applied and the goal
/// has months left.
/// </summary>
public record GoalScenario(
    GoalTrack OnTrack,
    ProjectionRange RequiredMonthlySaving,
    ProjectionRange ProjectedAtTargetDate);

/// <summary>
/// One IPS goal against the current net worth. <paramref name="MonthsLeft"/> counts whole months to the target
/// date (at least 1 while the date is ahead) and is 0 once it has passed. <paramref name="RequiredMonthlySaving"/> is the level saving that reaches the
/// target by then at the low / central / high return (zero when the balance gets there on its own; Low is the saving at the highest return, High at the lowest);
/// <paramref name="ProjectedAtTargetDate"/> is where the current savings path lands by then, as the same range.
/// <paramref name="AlreadyReached"/> is true when net worth already meets the target. Amounts are USD from the
/// handler; the controller re-expresses them.
/// </summary>
public record GoalProgress(
    string Label,
    decimal? TargetAmount,
    DateOnly? TargetDate,
    string Priority,
    int? MonthsLeft,
    ProjectionRange? RequiredMonthlySaving,
    ProjectionRange? ProjectedAtTargetDate,
    bool AlreadyReached,
    GoalTrack OnTrack,
    string? Reason = null,
    GoalScenario? Scenario = null);

/// <summary>
/// The IPS contribution plan beside the measured savings path (#454 S2). <paramref name="PlannedAmount"/> is what the
/// IPS says per <paramref name="Cadence"/>; <paramref name="PlannedMonthly"/> its monthly equivalent (null for a
/// cadence that is not recognised). <paramref name="ActualMonthlySavings"/> is the median complete-month net, the
/// savings path; <paramref name="Difference"/> is actual minus planned (positive: saving more than planned).
/// </summary>
public record ContributionPlanComparison(
    decimal PlannedAmount,
    string Cadence,
    decimal? PlannedMonthly,
    decimal? ActualMonthlySavings,
    decimal? Difference,
    string? Reason = null);

/// <summary>
/// Goal progress (#454 S2): each IPS goal tracked against total net worth plus the savings path, on the shared
/// assumption set and its low / central / high return range. Context for the person's own planning, never a
/// trading signal. Computed on read from what the IPS and the projections already hold; nothing is stored. The handler
/// computes in USD; the API controller re-expresses the amounts in the profile base currency.
/// <paramref name="CurrentNetWorth"/> is null with no snapshot; <paramref name="MonthlySavings"/> is null without a
/// savings path. <paramref name="Basis"/> states what the goals are measured against; <paramref name="Disclosure"/>
/// is the projection disclosure every projection carries, plus the context-only line.
/// </summary>
public record GoalsProgressResponse(
    GoalsProgressStatus Status,
    decimal? CurrentNetWorth,
    decimal? MonthlySavings,
    int CompleteMonths,
    IReadOnlyList<GoalProgress> Goals,
    ContributionPlanComparison? ContributionPlan,
    ProjectionRange RealAnnualReturnRange,
    bool HasStaleSleeves,
    string BaseCurrency = "USD",
    string Basis = GoalsProgressText.Basis,
    string Disclosure = GoalsProgressText.Disclosure,
    GoalsScenarioNote? Scenario = null);

/// <summary>
/// Whether the goals carry a scenario (#454 S5): <see cref="ScenarioStatus.Applied"/> puts a <see cref="GoalScenario"/> on each
/// goal with months left, <see cref="ScenarioStatus.Unreadable"/> says events count but this host cannot read them. Absent when
/// no event counts.
/// </summary>
public record GoalsScenarioNote(
    ScenarioStatus Status,
    int EventCount,
    string Label = ScenarioText.Label,
    string Basis = ScenarioText.Basis);

/// <summary>The sentences every goals response and the MCP tool description carry.</summary>
public static class GoalsProgressText
{
    public const string Basis =
        "Each goal is measured against your whole net worth plus your savings, not against money set aside for it, "
        + "so goals are not additive.";

    public const string Disclosure =
        "Hypothetical, not a guarantee. Context for your own planning, never a trading signal or advice.";
}

public record GetGoalsProgressQuery(Guid UserId) : IQuery<GoalsProgressResponse>;

/// <summary>
/// Reads through <see cref="ProjectionInputsReader"/> and <see cref="Projection"/>, the reader and arithmetic the
/// twelve-month and financial-independence projections use, so a goal is measured on the same assumption set, savings
/// path and latest snapshot - there is no second return assumption.
/// </summary>
public class GetGoalsProgressQueryHandler(
    ProjectionInputsReader inputs, ScenarioReader scenarios, IGoalsReader goals, TimeProvider clock)
    : IQueryHandler<GetGoalsProgressQuery, GoalsProgressResponse>
{
    private const decimal WeeksPerYear = 52m;
    private const decimal MonthsPerYear = 12m;
    private const int PlanDecimals = 2;
    private const int GoalHorizonMonths = 1200;

    private readonly ProjectionInputsReader _inputs = inputs;
    private readonly ScenarioReader _scenarios = scenarios;
    private readonly IGoalsReader _goals = goals;
    private readonly TimeProvider _clock = clock;

    public async Task<GoalsProgressResponse> Handle(GetGoalsProgressQuery query, CancellationToken cancellationToken)
    {
        var plan = await _goals.GetCurrentAsync(query.UserId, cancellationToken);
        var inputs = await _inputs.ReadAsync(query.UserId, cancellationToken);
        var returns = inputs.Assumptions.RealAnnualReturns;
        var currentNetWorth = inputs.Snapshot?.TotalNetWorth;
        var savings = inputs.SavingsPath;

        if (plan is null || plan.Goals.Count == 0)
        {
            return new GoalsProgressResponse(
                GoalsProgressStatus.NoGoals,
                currentNetWorth,
                savings?.MonthlySavingsUsd,
                inputs.CompleteMonths.Count,
                [],
                ContributionPlan: null,
                returns,
                inputs.HasStaleSleeves);
        }

        var today = DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime);
        var scenario = await _scenarios.ReadAsync(query.UserId, cancellationToken);
        var progress = plan.Goals.Select(g => Track(g, inputs, scenario, today)).ToList();

        return new GoalsProgressResponse(
            GoalsProgressStatus.Shown,
            currentNetWorth,
            savings?.MonthlySavingsUsd,
            inputs.CompleteMonths.Count,
            progress,
            plan.ContributionPlan is { } contribution ? Compare(contribution, savings) : null,
            returns,
            inputs.HasStaleSleeves,
            Scenario: scenario.Status == ScenarioStatus.None
                ? null
                : new GoalsScenarioNote(scenario.Status, scenario.Events.Count));
    }

    private static GoalProgress Track(GoalTarget goal, ProjectionInputs inputs, ScenarioInputs scenario, DateOnly today)
    {
        GoalProgress Result(
            GoalTrack track,
            string? reason = null,
            int? monthsLeft = null,
            ProjectionRange? required = null,
            ProjectionRange? projected = null,
            bool reached = false,
            GoalScenario? withEvents = null) => new(
            goal.Label, goal.TargetAmountUsd, goal.TargetDate, goal.Priority, monthsLeft, required, projected, reached, track, reason,
            withEvents);

        if (goal.TargetAmountUsd is not > 0m)
            return Result(GoalTrack.NoTargetAmount);
        var target = goal.TargetAmountUsd.Value;

        var reached = inputs.Snapshot is { } latest && latest.TotalNetWorth >= target;

        if (goal.TargetDate is not { } date)
            return Result(GoalTrack.NoTargetDate, reached: reached);

        if (inputs.Snapshot is null)
            return Result(GoalTrack.InsufficientHistory, GoalsProgressReasons.NoSnapshot);
        var current = inputs.Snapshot.TotalNetWorth;

        // Whole months to the date, at least one while it is still ahead: a goal due next week is not a goal that is due now.
        var monthsLeft = date <= today ? 0 : Math.Max(1, WholeMonthsUntil(today, date));
        if (monthsLeft == 0)
        {
            // The date is here: the net worth either made it or did not, no projection to run.
            return reached
                ? Result(GoalTrack.OnTrack, monthsLeft: 0, required: new ProjectionRange(0m, 0m, 0m), reached: true)
                : Result(GoalTrack.Behind, GoalsProgressReasons.TargetDatePassed, monthsLeft: 0);
        }

        if (monthsLeft > GoalHorizonMonths)
            return Result(GoalTrack.NoTargetDate, GoalsProgressReasons.TargetDateTooFar, monthsLeft, reached: reached);

        var required = Projection.RequiredMonthlySaving(monthsLeft, target, current, inputs.Assumptions);
        if (inputs.SavingsPath is not { } path)
        {
            return Result(
                GoalTrack.InsufficientHistory, GoalsProgressReasons.InsufficientMonths,
                monthsLeft, required, reached: reached);
        }

        var projected = Projection.ValueAfter(monthsLeft, current, path.MonthlySavingsUsd, inputs.Assumptions);
        return Result(
            projected.Central >= target ? GoalTrack.OnTrack : GoalTrack.Behind,
            monthsLeft: monthsLeft, required: required, projected: projected, reached: reached,
            withEvents: scenario.IsApplied ? Scenario(scenario, monthsLeft, target, current, path, inputs.Assumptions) : null);
    }

    // The same arithmetic with the person's events applied; the baseline figures beside it are untouched.
    private static GoalScenario Scenario(
        ScenarioInputs scenario, int monthsLeft, decimal target, decimal current, SavingsPath path, ProjectionAssumptions assumptions)
    {
        var deltas = scenario.Deltas(monthsLeft);
        var projected = Projection.ValueAfter(monthsLeft, current, path.MonthlySavingsUsd, assumptions, deltas);
        return new GoalScenario(
            projected.Central >= target ? GoalTrack.OnTrack : GoalTrack.Behind,
            Projection.RequiredMonthlySaving(monthsLeft, target, current, assumptions, deltas),
            projected);
    }

    /// <summary>Whole calendar months from <paramref name="from"/> to <paramref name="to"/>.</summary>
    private static int WholeMonthsUntil(DateOnly from, DateOnly to)
    {
        var months = (to.Year - from.Year) * (int)MonthsPerYear + to.Month - from.Month;
        return to.Day < from.Day ? months - 1 : months;
    }

    private static ContributionPlanComparison Compare(GoalContributionPlan plan, SavingsPath? savings)
    {
        var planned = MonthlyEquivalent(plan.AmountUsd, plan.Cadence);
        var actual = savings?.MonthlySavingsUsd;
        var reason = planned is null
            ? GoalsProgressReasons.UnrecognizedCadence
            : savings is null ? GoalsProgressReasons.InsufficientMonths : null;

        return new ContributionPlanComparison(
            plan.AmountUsd,
            plan.Cadence,
            planned,
            actual,
            planned is { } p && actual is { } a ? a - p : null,
            reason);
    }

    /// <summary>The IPS cadence is free text; the common spellings convert, anything else is left unconverted.</summary>
    private static decimal? MonthlyEquivalent(decimal amount, string cadence) => cadence.Trim().ToLowerInvariant() switch
    {
        "weekly" => Math.Round(amount * WeeksPerYear / MonthsPerYear, PlanDecimals),
        "biweekly" or "fortnightly" => Math.Round(amount * (WeeksPerYear / 2m) / MonthsPerYear, PlanDecimals),
        "monthly" => amount,
        "quarterly" => Math.Round(amount / 3m, PlanDecimals),
        "semiannual" or "semi-annual" or "semiannually" => Math.Round(amount / 6m, PlanDecimals),
        "annual" or "annually" or "yearly" => Math.Round(amount / MonthsPerYear, PlanDecimals),
        _ => null,
    };
}
