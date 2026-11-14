namespace FinanceSentry.Modules.Research.Domain.Scoring;

using System.Globalization;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain.Opportunity;

/// <summary>
/// The verdict decision list (spec 726 OQ-4): an ordered list of rules, each a conjunction of axis
/// conditions, where the first rule that matches fires. Pure and deterministic: the same inputs and the
/// same <see cref="VerdictOptions"/> always give the same word (FR-005). Every rule is evaluated and
/// returned so the drill-down shows the whole table, not only the winner (FR-006). There are no points
/// and no weights: a word is never derived from a sum of axes (019 FR-007 as amended).
/// Rules v2 keeps the five words and the table's shape: STRONG BUY also needs revenue growth, the price lens
/// falls back to the forward P/E against peers when the own-history premium is missing, and every WAIT names its
/// reason (<see cref="WaitReasons"/>).
/// </summary>
/// <remarks>
/// Null axes are explicit (FR-008): <see cref="VerdictOperator.IsMissing"/> tests for them, and a
/// comparison against a missing axis is never met, so a missing axis can only lower a word. A missing
/// fundamentals axis caps a name at BUY; a missing crowding or valuation axis keeps it from STRONG BUY.
/// </remarks>
public static class VerdictRules
{
    public const string OutsideUniverse = "outside_universe";
    public const string NoAxisData = "no_axis_data";
    public const string WeakStructure = "weak_structure";
    public const string ExtendedAndRich = "extended_and_rich";
    public const string NotEnoughData = "not_enough_data";
    public const string StrongBuy = "strong_buy";
    public const string Buy = "buy";
    public const string BuyFundamentalsMissing = "buy_fundamentals_missing";
    public const string StrongBusinessWeakTrend = "strong_business_weak_trend";
    public const string TrendWithoutBusiness = "trend_without_business";
    public const string WeakTrendBusinessUnread = "weak_trend_business_unread";
    public const string Otherwise = "otherwise";

    /// <summary>Threshold token of the outside-universe condition.</summary>
    public const string NotApplicable = "not_applicable";

    /// <summary>Actual token when the valuation axis applies.</summary>
    public const string Applicable = "applicable";

    public static Verdict Evaluate(VerdictInputs inputs, VerdictOptions options)
    {
        var rules = new List<VerdictRuleOutcome>
        {
            Rule(OutsideUniverse, VerdictWord.NotScored,
                "Outside the v1 universe: valuation does not apply, so this is not a single equity.",
                ValuationApplies(inputs)),
            Rule(NoAxisData, VerdictWord.NotScored,
                "No axis has data to read.",
                Missing(VerdictAxis.Structure, inputs.StructureScore),
                Missing(VerdictAxis.Fundamentals, inputs.FundamentalsScore),
                Missing(VerdictAxis.Crowding, inputs.Crowding),
                Missing(VerdictAxis.Valuation, inputs.ValuationPremiumPct)),
            Rule(WeakStructure, VerdictWord.Avoid,
                $"Adjusted structure below {options.AvoidStructureBelow}.",
                Compare(VerdictAxis.Structure, VerdictOperator.LessThan, inputs.StructureScore, options.AvoidStructureBelow)),
            Rule(ExtendedAndRich, VerdictWord.Avoid,
                "Crowding is Extended and the price lens reads above its yardstick: the trailing P/E above its own "
                + "five-year average or, without that history, the forward P/E above the peer median.",
                CrowdingIs(VerdictOperator.Is, inputs.Crowding),
                Compare(VerdictAxis.Valuation, VerdictOperator.GreaterThan, inputs.ValuationPremiumPct, 0m)),
            Rule(NotEnoughData, VerdictWord.Wait,
                "No structure score to read.",
                WaitReasons.NotEnoughData,
                Missing(VerdictAxis.Structure, inputs.StructureScore)),
            Rule(StrongBuy, VerdictWord.StrongBuy,
                $"Adjusted structure at least {options.StrongBuyStructureAtLeast}, fundamentals at least "
                + $"{options.StrongBuyFundamentalsAtLeast}, crowding not Extended, the price lens at most "
                + $"{Format(options.StrongBuyMaxValuationPremiumPct)}% above its yardstick (the trailing P/E over its own "
                + "five-year average, else the forward P/E over the peer median), and revenue growing at least "
                + $"{Format(options.StrongBuyMinRevenueYoyPct)}% year over year.",
                Compare(VerdictAxis.Structure, VerdictOperator.AtLeast, inputs.StructureScore, options.StrongBuyStructureAtLeast),
                Compare(VerdictAxis.Fundamentals, VerdictOperator.AtLeast, inputs.FundamentalsScore, options.StrongBuyFundamentalsAtLeast),
                CrowdingIs(VerdictOperator.IsNot, inputs.Crowding),
                Compare(VerdictAxis.Valuation, VerdictOperator.AtMost, inputs.ValuationPremiumPct, options.StrongBuyMaxValuationPremiumPct),
                Compare(VerdictAxis.Growth, VerdictOperator.AtLeast, inputs.RevenueYoyPct, options.StrongBuyMinRevenueYoyPct)),
            Rule(Buy, VerdictWord.Buy,
                $"Adjusted structure at least {options.BuyStructureAtLeast} and fundamentals at least {options.BuyFundamentalsAtLeast}.",
                Compare(VerdictAxis.Structure, VerdictOperator.AtLeast, inputs.StructureScore, options.BuyStructureAtLeast),
                Compare(VerdictAxis.Fundamentals, VerdictOperator.AtLeast, inputs.FundamentalsScore, options.BuyFundamentalsAtLeast)),
            Rule(BuyFundamentalsMissing, VerdictWord.Buy,
                $"Adjusted structure at least {options.BuyStructureAtLeast} with no fundamentals score: capped at BUY.",
                Compare(VerdictAxis.Structure, VerdictOperator.AtLeast, inputs.StructureScore, options.BuyStructureAtLeast),
                Missing(VerdictAxis.Fundamentals, inputs.FundamentalsScore)),
            Rule(StrongBusinessWeakTrend, VerdictWord.Wait,
                $"Fundamentals at least {options.WaitStrongBusinessFundamentalsAtLeast} but adjusted structure below the BUY bar of "
                + $"{options.BuyStructureAtLeast}: a strong business whose chart is not there yet.",
                WaitReasons.StrongBusinessWeakTrend,
                Compare(VerdictAxis.Fundamentals, VerdictOperator.AtLeast, inputs.FundamentalsScore, options.WaitStrongBusinessFundamentalsAtLeast),
                Compare(VerdictAxis.Structure, VerdictOperator.LessThan, inputs.StructureScore, options.BuyStructureAtLeast)),
            Rule(TrendWithoutBusiness, VerdictWord.Wait,
                $"Adjusted structure at least {options.BuyStructureAtLeast} but fundamentals below the BUY bar of "
                + $"{options.BuyFundamentalsAtLeast}: a chart without the business behind it.",
                WaitReasons.TrendWithoutBusiness,
                Compare(VerdictAxis.Structure, VerdictOperator.AtLeast, inputs.StructureScore, options.BuyStructureAtLeast),
                Compare(VerdictAxis.Fundamentals, VerdictOperator.LessThan, inputs.FundamentalsScore, options.BuyFundamentalsAtLeast)),
            Rule(WeakTrendBusinessUnread, VerdictWord.Wait,
                $"Adjusted structure below the BUY bar of {options.BuyStructureAtLeast} and no fundamentals score to read.",
                WaitReasons.NotEnoughData,
                Compare(VerdictAxis.Structure, VerdictOperator.LessThan, inputs.StructureScore, options.BuyStructureAtLeast),
                Missing(VerdictAxis.Fundamentals, inputs.FundamentalsScore)),
            Rule(Otherwise, VerdictWord.Wait, OtherwiseDescription(inputs, options), WaitReasons.ShortOfBuyBar),
        };

        var fired = rules.First(r => r.Matched);
        return new Verdict(fired.Word, options.RulesVersion, fired.Id, rules, fired.WaitReason, inputs.PriceLens);
    }

    /// <summary>
    /// The catch-all's reason: names the BUY bars the read missed, with the value it read, so a WAIT that no
    /// earlier rule claimed still says why. The word is unchanged.
    /// </summary>
    private static string OtherwiseDescription(VerdictInputs inputs, VerdictOptions options)
    {
        var missed = new List<string>();
        if (inputs.StructureScore is { } structure && structure < options.BuyStructureAtLeast)
        {
            missed.Add($"adjusted structure {structure} is below the BUY bar of {options.BuyStructureAtLeast}");
        }

        if (inputs.FundamentalsScore is { } fundamentals && fundamentals < options.BuyFundamentalsAtLeast)
        {
            missed.Add($"fundamentals {fundamentals} are below the BUY bar of {options.BuyFundamentalsAtLeast}");
        }

        return missed.Count == 0
            ? "No earlier rule matched."
            : $"No earlier rule matched: {string.Join(" and ", missed)}.";
    }

    private static VerdictRuleOutcome Rule(string id, VerdictWord word, string description, params VerdictCondition[] conditions) =>
        new(id, word, description, conditions, conditions.All(c => c.Met));

    private static VerdictRuleOutcome Rule(
        string id, VerdictWord word, string description, string waitReason, params VerdictCondition[] conditions) =>
        new(id, word, description, conditions, conditions.All(c => c.Met), waitReason);

    private static VerdictCondition ValuationApplies(VerdictInputs inputs) =>
        new(VerdictAxis.Valuation, VerdictOperator.Is, NotApplicable,
            inputs.ValuationNotApplicable ? NotApplicable : Applicable,
            inputs.ValuationNotApplicable);

    private static VerdictCondition Missing<T>(VerdictAxis axis, T? value)
        where T : struct =>
        new(axis, VerdictOperator.IsMissing, null, Format(value), value is null);

    private static VerdictCondition CrowdingIs(VerdictOperator op, CrowdingClass? crowding)
    {
        var met = crowding is { } actual
            && (op == VerdictOperator.Is ? actual == CrowdingClass.Extended : actual != CrowdingClass.Extended);
        return new VerdictCondition(VerdictAxis.Crowding, op, nameof(CrowdingClass.Extended), crowding?.ToString(), met);
    }

    private static VerdictCondition Compare(VerdictAxis axis, VerdictOperator op, decimal? actual, decimal threshold)
    {
        var met = actual is { } value && op switch
        {
            VerdictOperator.LessThan => value < threshold,
            VerdictOperator.GreaterThan => value > threshold,
            VerdictOperator.AtLeast => value >= threshold,
            VerdictOperator.AtMost => value <= threshold,
            _ => throw new ArgumentOutOfRangeException(nameof(op), op, "Not a comparison operator."),
        };
        return new VerdictCondition(axis, op, Format(threshold), Format(actual), met);
    }

    private static string? Format<T>(T? value)
        where T : struct =>
        value is IFormattable formattable ? formattable.ToString(null, CultureInfo.InvariantCulture) : value?.ToString();

    private static string Format(decimal value) => value.ToString(CultureInfo.InvariantCulture);
}
