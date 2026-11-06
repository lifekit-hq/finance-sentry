namespace FinanceSentry.Modules.Wealth.Application.Queries;

using System.Text.Json.Serialization;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Core.Measures;
using FinanceSentry.Modules.Wealth.Application.Services;
using Microsoft.Extensions.Logging;

/// <summary>
/// How much of a section of the financial profile is there (#454 S7). <see cref="Complete"/>: everything the section is meant
/// to say is present. <see cref="Partial"/>: some of it is, and <see cref="ProfileSection{T}.Reasons"/> says what is not.
/// <see cref="Missing"/>: nothing usable, so the reader says so instead of guessing. Serialized by name.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<DataQuality>))]
public enum DataQuality
{
    Complete,
    Partial,
    Missing,
}

/// <summary>Typed reasons a profile section is partial or missing (money-semantics §20).</summary>
public static class FinancialProfileReasons
{
    /// <summary>The section's read failed; it is reported missing rather than failing the whole profile.</summary>
    public const string SectionUnavailable = "SECTION_UNAVAILABLE";

    public const string NoGoals = "NO_GOALS";
    public const string GoalNeedsInput = "GOAL_NEEDS_INPUT";
    public const string NoSnapshot = "NO_SNAPSHOT";
    public const string StaleSources = "STALE_SOURCES";
    public const string NoDeclaredIncome = "NO_DECLARED_INCOME";
    public const string DeclaredUnreadable = "DECLARED_UNREADABLE";
    public const string NotComparable = "NOT_COMPARABLE";
    public const string NoObservedIncome = "NO_OBSERVED_INCOME";
    public const string NoCareerEvents = "NO_CAREER_EVENTS";
    public const string CareerUnreadable = "CAREER_UNREADABLE";
    public const string NoPositions = "NO_POSITIONS";
    public const string PositionsWithheld = "POSITIONS_WITHHELD";
    public const string NoIps = "NO_IPS";
    public const string RiskCapacityNotSet = "RISK_CAPACITY_NOT_SET";
    public const string DrawdownNotSet = "DRAWDOWN_TOLERANCE_NOT_SET";
    public const string RiskRemeasurementDue = "RISK_REMEASUREMENT_DUE";
}

/// <summary>
/// One section of the financial profile: how complete it is, why it is not, and its summary. <paramref name="Data"/> is
/// null when the section could not be read at all or has nothing on file to summarise (no goals, no IPS); a section that
/// is <see cref="DataQuality.Missing"/> for lack of enough history still carries the summary it was read as, so the reason
/// can be shown beside it. A section never carries a made-up value.
/// </summary>
public record ProfileSection<T>(DataQuality DataQuality, IReadOnlyList<string> Reasons, T? Data)
    where T : class;

/// <summary>The cashflow numbers the profile carries (#454 S1), unconverted USD.</summary>
public record CashflowSummary(
    CashflowProfileStatus Status,
    int CompleteMonths,
    string? FromMonth,
    string? ToMonth,
    decimal? IncomeMonthlyMedian,
    decimal? BurnMonthlyMedian,
    decimal? SavingsRate,
    decimal? RunwayMonths,
    string? RunwayReason,
    bool HasStaleSleeves,
    string? Reason);

/// <summary>One goal as the profile shows it (#454 S2): the IPS wording and where it stands, with no ranges.</summary>
public record GoalSummary(
    string Label,
    decimal? TargetAmount,
    DateOnly? TargetDate,
    int? MonthsLeft,
    GoalTrack OnTrack,
    string? Reason);

/// <summary>Goal progress at a glance (#454 S2): each goal's standing, the nearest dated one, plan against measured savings.</summary>
public record GoalsSummary(
    decimal? CurrentNetWorth,
    decimal? MonthlySavings,
    IReadOnlyList<GoalSummary> Goals,
    int? ShortestGoalMonths,
    decimal? PlannedMonthlyContribution,
    decimal? ContributionDifference,
    bool ScenarioApplied);

/// <summary>Declared income next to observed income (#454 S4), without any source's label or amount.</summary>
public record IncomeSummary(
    bool DeclaredReadable,
    int ActiveSources,
    decimal? DeclaredMonthlyUsd,
    string? DeclaredBasis,
    decimal? ObservedMonthlyUsd,
    IncomeVariance Variance);

/// <summary>The career timeline at a glance (#454 S5): how many events there are and how many shape the scenario.</summary>
public record CareerSummary(bool Readable, int Events, int CountedEvents);

/// <summary>
/// The capacity indicator with the recorded value beside it (#454 S7). <paramref name="RecordedCapacity"/> is what the IPS
/// holds today (null until set); <paramref name="DiffersFromRecorded"/> is true when a suggestion exists and is not that
/// number. The indicator never writes the IPS: the owner records the number (<c>record_risk_remeasurement</c>).
/// </summary>
public record CapacitySuggestion(
    CapacityStatus Status,
    int? SuggestedBand,
    int? RecordedCapacity,
    bool DiffersFromRecorded,
    IReadOnlyList<CapacityFactor> Factors,
    IReadOnlyList<string> Missing,
    string? Reason,
    string Label = CapacityIndicator.Label);

/// <summary>
/// The agent-readable map of the person's money situation (#454 S7): the cashflow, goal, income, career, disposal-tax and
/// policy-risk summaries in one object, each with its own <see cref="DataQuality"/>, so the reader states what is missing
/// instead of guessing. <paramref name="MissingSections"/> names the sections that are <see cref="DataQuality.Missing"/>.
/// Computed on read; nothing is stored. Every figure is context for the person's own planning, never a trading signal.
/// Amounts are USD except the disposal-tax estimate, which is EUR.
/// </summary>
public record FinancialProfileResponse(
    DateTimeOffset AsOf,
    ProfileSection<CashflowSummary> Cashflow,
    ProfileSection<GoalsSummary> Goals,
    ProfileSection<IncomeSummary> Income,
    ProfileSection<CareerSummary> Career,
    ProfileSection<DisposalTaxSummary> DisposalTax,
    ProfileSection<IpsRiskProfile> Risk,
    CapacitySuggestion CapacityIndicator,
    IReadOnlyList<string> MissingSections,
    string Disclosure = FinancialProfileText.Disclosure);

/// <summary>The sentence every financial-profile response and the MCP tool description carry.</summary>
public static class FinancialProfileText
{
    public const string Disclosure =
        "Context for your own planning, never a trading signal or advice. It is a summary of what is on file as of the time "
        + "shown: a section marked Partial or Missing is incomplete, so say what is missing instead of filling it in.";
}

public record GetFinancialProfileQuery(Guid UserId) : IQuery<FinancialProfileResponse>;

/// <summary>
/// Composes the profile from the readers the single-purpose tools already use, so a figure here is the figure there. A
/// section whose read throws is reported <see cref="DataQuality.Missing"/> with <see cref="FinancialProfileReasons.SectionUnavailable"/>
/// and the rest of the profile still returns.
/// </summary>
public class GetFinancialProfileQueryHandler(
    IQueryHandler<GetCashflowProfileQuery, CashflowProfileResponse> cashflow,
    IQueryHandler<GetGoalsProgressQuery, GoalsProgressResponse> goals,
    IQueryHandler<GetIncomeProfileQuery, IncomeProfileResponse> income,
    IQueryHandler<GetCareerTimelineQuery, CareerTimelineResponse> career,
    ProjectionInputsReader inputs,
    IDisposalTaxSummaryReader disposalTax,
    IRiskProfileReader risk,
    TimeProvider clock,
    ILogger<GetFinancialProfileQueryHandler> logger)
    : IQueryHandler<GetFinancialProfileQuery, FinancialProfileResponse>
{
    public async Task<FinancialProfileResponse> Handle(GetFinancialProfileQuery query, CancellationToken cancellationToken)
    {
        var userId = query.UserId;
        var asOf = clock.GetUtcNow();

        var cashflowRead = await ReadAsync("cashflow", () => cashflow.Handle(new GetCashflowProfileQuery(userId), cancellationToken));
        var goalsRead = await ReadAsync("goals", () => goals.Handle(new GetGoalsProgressQuery(userId), cancellationToken));
        var incomeRead = await ReadAsync("income", () => income.Handle(new GetIncomeProfileQuery(userId), cancellationToken));
        var careerRead = await ReadAsync("career", () => career.Handle(new GetCareerTimelineQuery(userId), cancellationToken));
        var taxRead = await ReadAsync("disposal tax", () => disposalTax.ReadAsync(userId, cancellationToken));
        var riskRead = await ReadAsync("risk", () => risk.GetCurrentAsync(userId, cancellationToken));
        var spreadRead = await ReadAsync("income spread", async () => IncomeSpreadOf(await inputs.ReadAsync(userId, cancellationToken)));

        var cashflowSection = Section(cashflowRead, CashflowOf);
        var goalsSection = Section(goalsRead, GoalsOf);
        var incomeSection = Section(incomeRead, IncomeOf);
        var careerSection = Section(careerRead, CareerOf);
        var taxSection = Section(taxRead, DisposalTaxOf);
        var riskSection = RiskSection(riskRead);

        var capacity = CapacityOf(cashflowRead.Value, goalsSection.Data, incomeRead.Value, spreadRead.Value, riskRead.Value);

        var sections = new (string Name, DataQuality Quality)[]
        {
            ("cashflow", cashflowSection.DataQuality),
            ("goals", goalsSection.DataQuality),
            ("income", incomeSection.DataQuality),
            ("career", careerSection.DataQuality),
            ("disposalTax", taxSection.DataQuality),
            ("risk", riskSection.DataQuality),
        };

        return new FinancialProfileResponse(
            asOf,
            cashflowSection,
            goalsSection,
            incomeSection,
            careerSection,
            taxSection,
            riskSection,
            capacity,
            [.. sections.Where(s => s.Quality == DataQuality.Missing).Select(s => s.Name)]);
    }

    private readonly record struct Read<T>(T? Value, bool Failed)
        where T : class;

    private async Task<Read<T>> ReadAsync<T>(string section, Func<Task<T?>> read)
        where T : class
    {
        try
        {
            return new Read<T>(await read(), false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Financial profile section {Section} unavailable; reporting it missing.", section);
            return new Read<T>(null, true);
        }
    }

    private static ProfileSection<TOut> Section<TIn, TOut>(Read<TIn> read, Func<TIn, ProfileSection<TOut>> map)
        where TIn : class
        where TOut : class
        => read.Failed || read.Value is null ? Unavailable<TOut>() : map(read.Value);

    private static ProfileSection<T> Unavailable<T>()
        where T : class
        => new(DataQuality.Missing, [FinancialProfileReasons.SectionUnavailable], null);

    private static ProfileSection<CashflowSummary> CashflowOf(CashflowProfileResponse c)
    {
        var summary = new CashflowSummary(
            c.Status, c.CompleteMonths, c.FromMonth, c.ToMonth, c.IncomeMonthlyMedian, c.BurnMonthlyMedian, c.SavingsRate,
            c.RunwayMonths, c.RunwayReason, c.HasStaleSleeves, c.Reason);

        if (c.Status == CashflowProfileStatus.InsufficientHistory)
            return new(DataQuality.Missing, [c.Reason ?? CashflowProfileReasons.InsufficientMonths], summary);

        var reasons = new List<string>();
        if (c.RunwayReason is { } runwayReason)
            reasons.Add(runwayReason);
        if (c.HasStaleSleeves)
            reasons.Add(FinancialProfileReasons.StaleSources);

        return new(reasons.Count == 0 ? DataQuality.Complete : DataQuality.Partial, reasons, summary);
    }

    private static ProfileSection<GoalsSummary> GoalsOf(GoalsProgressResponse g)
    {
        if (g.Status == GoalsProgressStatus.NoGoals || g.Goals.Count == 0)
            return new(DataQuality.Missing, [FinancialProfileReasons.NoGoals], null);

        var summary = new GoalsSummary(
            g.CurrentNetWorth,
            g.MonthlySavings,
            [.. g.Goals.Select(x => new GoalSummary(x.Label, x.TargetAmount, x.TargetDate, x.MonthsLeft, x.OnTrack, x.Reason))],
            g.Goals.Where(x => x.MonthsLeft is > 0).Select(x => x.MonthsLeft).Min(),
            g.ContributionPlan?.PlannedMonthly,
            g.ContributionPlan?.Difference,
            g.Scenario?.Status == ScenarioStatus.Applied);

        var reasons = new List<string>();
        if (g.Goals.Any(x => x.OnTrack is GoalTrack.NoTargetAmount or GoalTrack.NoTargetDate or GoalTrack.InsufficientHistory))
            reasons.Add(FinancialProfileReasons.GoalNeedsInput);
        if (g.CurrentNetWorth is null)
            reasons.Add(FinancialProfileReasons.NoSnapshot);
        if (g.HasStaleSleeves)
            reasons.Add(FinancialProfileReasons.StaleSources);

        return new(reasons.Count == 0 ? DataQuality.Complete : DataQuality.Partial, reasons, summary);
    }

    private static ProfileSection<IncomeSummary> IncomeOf(IncomeProfileResponse i)
    {
        var summary = new IncomeSummary(
            i.DeclaredReadable, i.ActiveSources, i.DeclaredMonthlyUsd, i.DeclaredBasis, i.ObservedMonthlyUsd, i.Variance);

        return i.Variance switch
        {
            IncomeVariance.NoDeclared => new(DataQuality.Missing, [FinancialProfileReasons.NoDeclaredIncome], summary),
            IncomeVariance.DeclaredUnreadable => new(DataQuality.Partial, [FinancialProfileReasons.DeclaredUnreadable], summary),
            IncomeVariance.NotComparable => new(DataQuality.Partial, [FinancialProfileReasons.NotComparable], summary),
            IncomeVariance.NoObserved => new(DataQuality.Partial, [FinancialProfileReasons.NoObservedIncome], summary),
            _ => new(DataQuality.Complete, [], summary),
        };
    }

    private static ProfileSection<CareerSummary> CareerOf(CareerTimelineResponse c)
    {
        var summary = new CareerSummary(c.Readable, c.Events.Count, c.CountedEvents);
        if (c.Events.Count == 0)
            return new(DataQuality.Missing, [FinancialProfileReasons.NoCareerEvents], summary);

        return c.Readable
            ? new(DataQuality.Complete, [], summary)
            : new(DataQuality.Partial, [FinancialProfileReasons.CareerUnreadable], summary);
    }

    private static ProfileSection<DisposalTaxSummary> DisposalTaxOf(DisposalTaxSummary t)
    {
        if (t.PositionCount == 0)
            return new(DataQuality.Missing, [FinancialProfileReasons.NoPositions], t);

        return t.WithheldCount == 0
            ? new(DataQuality.Complete, [], t)
            : new(t.ShownCount == 0 ? DataQuality.Missing : DataQuality.Partial, [FinancialProfileReasons.PositionsWithheld], t);
    }

    private static ProfileSection<IpsRiskProfile> RiskSection(Read<IpsRiskProfile> read)
    {
        if (read.Failed)
            return Unavailable<IpsRiskProfile>();
        if (read.Value is not { } profile)
            return new(DataQuality.Missing, [FinancialProfileReasons.NoIps], null);

        var reasons = new List<string>();
        if (profile.RiskCapacity is null)
            reasons.Add(FinancialProfileReasons.RiskCapacityNotSet);
        if (profile.MaxDrawdownTolerancePct is null)
            reasons.Add(FinancialProfileReasons.DrawdownNotSet);
        if (profile.RemeasurementDue)
            reasons.Add(FinancialProfileReasons.RiskRemeasurementDue);

        return new(reasons.Count == 0 ? DataQuality.Complete : DataQuality.Partial, reasons, profile);
    }

    /// <summary>The range of the observed months' income over its median; null with fewer than three months or no positive median.</summary>
    private static IncomeSpreadReading? IncomeSpreadOf(ProjectionInputs read)
    {
        var incomes = read.CompleteMonths.Select(m => m.NetUsd + m.OutflowUsd).ToList();
        if (incomes.Count < Projection.MinCompleteMonths)
            return null;

        var median = Projection.Median(incomes);
        return median > 0m ? new IncomeSpreadReading((incomes.Max() - incomes.Min()) / median) : null;
    }

    private sealed record IncomeSpreadReading(decimal Spread);

    private static CapacitySuggestion CapacityOf(
        CashflowProfileResponse? cashflow,
        GoalsSummary? goals,
        IncomeProfileResponse? income,
        IncomeSpreadReading? spread,
        IpsRiskProfile? ips)
    {
        // No cash is a runway of zero months; a runway that is merely unknown stays unknown.
        decimal? runway = cashflow?.Status == CashflowProfileStatus.NoCash ? 0m : cashflow?.RunwayMonths;
        var shown = cashflow is { Status: not CashflowProfileStatus.InsufficientHistory };

        var result = CapacityIndicator.Suggest(new CapacityInputs(
            runway,
            shown ? cashflow!.BurnMonthlyMedian : null,
            shown ? cashflow!.IncomeMonthlyMedian : null,
            spread?.Spread,
            income?.Variance == IncomeVariance.ObservedLower,
            goals?.ShortestGoalMonths));

        var recorded = ips?.RiskCapacity;
        return new CapacitySuggestion(
            result.Status,
            result.SuggestedBand,
            recorded,
            result.SuggestedBand is { } band && recorded != band,
            result.Factors,
            result.Missing,
            result.Reason);
    }
}
