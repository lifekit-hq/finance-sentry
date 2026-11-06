using System.Text.Json.Serialization;

namespace FinanceSentry.Core.Measures;

/// <summary>
/// Whether the indicator could suggest a band. <see cref="Insufficient"/>: the one input every suggestion rests on, the
/// runway, is missing, so no band is offered rather than a guessed one. Serialized by name.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<CapacityStatus>))]
public enum CapacityStatus
{
    Suggested,
    Insufficient,
}

/// <summary>
/// What the capacity indicator reads (#454 S7). Every field is nullable: an input that is not known is left out of the
/// suggestion and named in the result, never filled with a stand-in.
/// <para>
/// <paramref name="RunwayMonths"/> is cash over typical monthly spending (zero when there is no cash).
/// <paramref name="BurnMonthlyUsd"/> and <paramref name="IncomeMonthlyUsd"/> are the typical complete month's spending and
/// income. <paramref name="IncomeSpread"/> is how far the observed months' income ranges, as (largest - smallest) over the
/// median. <paramref name="ObservedBelowDeclared"/> is true when the income that landed is lower than the person says they
/// expect. <paramref name="ShortestGoalMonths"/> is the whole months to the nearest dated goal.
/// </para>
/// </summary>
public sealed record CapacityInputs(
    decimal? RunwayMonths,
    decimal? BurnMonthlyUsd,
    decimal? IncomeMonthlyUsd,
    decimal? IncomeSpread,
    bool ObservedBelowDeclared,
    int? ShortestGoalMonths);

/// <summary>
/// One input's effect on the suggestion. <paramref name="Code"/> names the rule, <paramref name="Value"/> is the figure it
/// read, <paramref name="CapBand"/> the highest band that input allows (5 = it does not hold the band down), and
/// <paramref name="Note"/> a short plain-words reading.
/// </summary>
public sealed record CapacityFactor(string Code, decimal? Value, int CapBand, string Note);

/// <summary>
/// A SUGGESTED risk-capacity band (1-5) for the owner's quarterly re-measurement. The owner still records the number;
/// the indicator never writes the investment policy statement. <paramref name="Factors"/> lists every input that was read
/// and what it allowed, <paramref name="Missing"/> the inputs that were not available.
/// </summary>
public sealed record CapacityIndicatorResult(
    CapacityStatus Status,
    int? SuggestedBand,
    IReadOnlyList<CapacityFactor> Factors,
    IReadOnlyList<string> Missing,
    string? Reason = null);

/// <summary>
/// The capacity indicator (#454 S7): a pure function of how long cash lasts, what is spent against what comes in, how
/// steady the income is and how soon a dated goal falls due. It is a rubric, not a measure: each input caps the band the
/// answer can reach, and the suggestion is the lowest cap, so one weak input holds the suggestion down however strong the
/// others are. Capacity is what the person can afford to lose, as opposed to tolerance, which is what they would sit
/// through; this reads only the former and never infers a tolerance. Context for the owner's own re-measurement, never a
/// trading signal; it produces no buy, sell or hold call. The thresholds are named below and written out in
/// money-semantics §20.
/// </summary>
public static class CapacityIndicator
{
    public const int MinBand = 1;
    public const int MaxBand = 5;

    /// <summary>Runway thresholds in months: under the first the cap is band 1, under the second band 2, and so on.</summary>
    public static readonly decimal[] RunwayMonthsBelow = [3m, 6m, 12m, 24m];

    /// <summary>Income range over the median: above the first the cap is band 4, above the second band 3.</summary>
    public const decimal ModerateIncomeSpread = 0.5m;

    public const decimal HighIncomeSpread = 1.0m;

    /// <summary>Spending at or above this share of income caps the band at 2: the person is not saving from what comes in.</summary>
    public const decimal SpendingToIncomeCeiling = 1.0m;

    /// <summary>Months to the nearest dated goal: under the first the cap is band 2, under the second band 3, under the third band 4.</summary>
    public static readonly int[] GoalMonthsBelow = [12, 36, 60];

    public const string Runway = "RUNWAY";
    public const string SpendingToIncome = "SPENDING_TO_INCOME";
    public const string IncomeSpreadCode = "INCOME_SPREAD";
    public const string IncomeBelowDeclared = "INCOME_BELOW_DECLARED";
    public const string GoalHorizon = "GOAL_HORIZON";

    public const string RunwayUnknown = "RUNWAY_UNKNOWN";
    public const string IncomeUnknown = "INCOME_UNKNOWN";
    public const string NoDatedGoal = "NO_DATED_GOAL";

    public const string Label =
        "A suggestion for your own re-measurement, not a measurement: you record the number. It reads how long cash lasts, "
        + "what you spend against what comes in, how steady income has been and how soon a dated goal falls due; it does not "
        + "read your risk tolerance, which only you can say.";

    public static CapacityIndicatorResult Suggest(CapacityInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        var missing = new List<string>();
        if (inputs.RunwayMonths is null)
            missing.Add(RunwayUnknown);
        if (inputs.IncomeSpread is null && inputs.BurnMonthlyUsd is null)
            missing.Add(IncomeUnknown);
        if (inputs.ShortestGoalMonths is null)
            missing.Add(NoDatedGoal);

        // The runway is the one input every suggestion stands on; without it there is nothing to cap.
        if (inputs.RunwayMonths is not { } runway)
            return new CapacityIndicatorResult(CapacityStatus.Insufficient, null, [], missing, RunwayUnknown);

        var factors = new List<CapacityFactor> { RunwayFactor(runway) };

        if (inputs.BurnMonthlyUsd is { } burn && inputs.IncomeMonthlyUsd is { } income && income > 0m)
        {
            var ratio = Math.Round(burn / income, 2);
            factors.Add(ratio >= SpendingToIncomeCeiling
                ? new CapacityFactor(SpendingToIncome, ratio, 2, "Spending is at or above income in the typical month.")
                : new CapacityFactor(SpendingToIncome, ratio, MaxBand, "Spending is below income in the typical month."));
        }

        if (inputs.IncomeSpread is { } spread)
            factors.Add(SpreadFactor(spread));

        if (inputs.ObservedBelowDeclared)
            factors.Add(new CapacityFactor(IncomeBelowDeclared, null, 4, "Income that landed is lower than the income declared."));

        if (inputs.ShortestGoalMonths is { } goalMonths)
            factors.Add(GoalFactor(goalMonths));

        return new CapacityIndicatorResult(CapacityStatus.Suggested, factors.Min(f => f.CapBand), factors, missing);
    }

    private static CapacityFactor RunwayFactor(decimal runway)
    {
        var band = MinBand;
        foreach (var threshold in RunwayMonthsBelow)
        {
            if (runway < threshold)
                break;
            band++;
        }

        return new CapacityFactor(Runway, runway, band, band == MaxBand
            ? "Cash covers two years or more of typical spending."
            : "Cash covers this many months of typical spending.");
    }

    private static CapacityFactor SpreadFactor(decimal spread)
    {
        var rounded = Math.Round(spread, 2);
        if (spread > HighIncomeSpread)
            return new CapacityFactor(IncomeSpreadCode, rounded, 3, "Income has varied by more than its typical month.");
        if (spread > ModerateIncomeSpread)
            return new CapacityFactor(IncomeSpreadCode, rounded, 4, "Income has varied by more than half its typical month.");

        return new CapacityFactor(IncomeSpreadCode, rounded, MaxBand, "Income has been steady across the complete months.");
    }

    private static CapacityFactor GoalFactor(int months)
    {
        var band = 2;
        foreach (var threshold in GoalMonthsBelow)
        {
            if (months < threshold)
                break;
            band++;
        }

        return new CapacityFactor(GoalHorizon, months, Math.Min(band, MaxBand), band >= MaxBand
            ? "The nearest dated goal is five years or more away."
            : "A dated goal falls due within this many months.");
    }
}
