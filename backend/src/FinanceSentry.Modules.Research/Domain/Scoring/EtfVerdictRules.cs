namespace FinanceSentry.Modules.Research.Domain.Scoring;

using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain.Opportunity;

/// <summary>
/// What the ETF table reads: the fund's own adjusted structure and crowding, and the share (percent) of the
/// top-10 weight whose holdings read BUY or better, null when the holdings could not be read.
/// </summary>
public sealed record EtfVerdictInputs(
    int? StructureScore,
    CrowdingClass? Crowding,
    decimal? BuyWeightSharePct);

/// <summary>
/// The small published ETF table (issue #1324): a fund is read by looking through to its top 10 holdings, each
/// scored by the single-name rules, and the fund gets its word from this ordered list, first match fires, like
/// <see cref="VerdictRules"/>. AVOID on the fund's own structure below the equity AVOID line; BUY needs the fund's
/// structure at the equity BUY bar, crowding not Extended, and more than half of the top-10 weight reading BUY or
/// better. There is no STRONG BUY rule: an ETF word is capped at BUY in v1. A fund whose holdings could not be read
/// can still be AVOID (its own structure), but cannot reach BUY: a missing look-through can only lower the word.
/// </summary>
public static class EtfVerdictRules
{
    public const string NoData = "etf_no_data";
    public const string WeakStructure = "etf_weak_structure";
    public const string StructureMissing = "etf_structure_missing";
    public const string Buy = "etf_buy";
    public const string Otherwise = "etf_otherwise";

    /// <summary>True when <paramref name="verdict"/> came from this table rather than the single-name one.</summary>
    public static bool Reads(Verdict verdict) => verdict.Rules.Any(r => r.Id == NoData);

    public static Verdict Evaluate(EtfVerdictInputs inputs, VerdictOptions options)
    {
        var rules = new List<VerdictRuleOutcome>
        {
            VerdictRules.Rule(NoData, VerdictWord.NotScored,
                "Neither the fund's own structure nor its top holdings could be read.",
                VerdictRules.Missing(VerdictAxis.Structure, inputs.StructureScore),
                VerdictRules.Missing(VerdictAxis.LookThrough, inputs.BuyWeightSharePct)),
            VerdictRules.Rule(WeakStructure, VerdictWord.Avoid,
                $"The fund's adjusted structure is below {options.AvoidStructureBelow}.",
                VerdictRules.Compare(VerdictAxis.Structure, VerdictOperator.LessThan, inputs.StructureScore, options.AvoidStructureBelow)),
            VerdictRules.Rule(StructureMissing, VerdictWord.Wait,
                "No structure score for the fund to read.",
                VerdictRules.Missing(VerdictAxis.Structure, inputs.StructureScore)),
            VerdictRules.Rule(Buy, VerdictWord.Buy,
                $"The fund's adjusted structure is at least {options.BuyStructureAtLeast}, its crowding is not Extended, "
                + $"and more than {VerdictRules.Format(options.EtfBuyWeightSharePctAbove)}% of its top-10 weight reads BUY or better.",
                VerdictRules.Compare(VerdictAxis.Structure, VerdictOperator.AtLeast, inputs.StructureScore, options.BuyStructureAtLeast),
                VerdictRules.CrowdingIs(VerdictOperator.IsNot, inputs.Crowding),
                VerdictRules.Compare(VerdictAxis.LookThrough, VerdictOperator.GreaterThan, inputs.BuyWeightSharePct, options.EtfBuyWeightSharePctAbove)),
            VerdictRules.Rule(Otherwise, VerdictWord.Wait, OtherwiseDescription(inputs, options)),
        };

        var fired = rules.First(r => r.Matched);
        return new Verdict(fired.Word, options.EtfRulesVersion, fired.Id, rules);
    }

    /// <summary>The catch-all's reason: the BUY bars the read missed, with the value it read.</summary>
    private static string OtherwiseDescription(EtfVerdictInputs inputs, VerdictOptions options)
    {
        var missed = new List<string>();
        if (inputs.StructureScore is { } structure && structure < options.BuyStructureAtLeast)
        {
            missed.Add($"the fund's adjusted structure {structure} is below the BUY bar of {options.BuyStructureAtLeast}");
        }

        if (inputs.Crowding == CrowdingClass.Extended)
        {
            missed.Add("the fund's crowding is Extended");
        }

        if (inputs.Crowding is null)
        {
            missed.Add("the fund's crowding could not be read");
        }

        if (inputs.BuyWeightSharePct is null)
        {
            missed.Add("its top holdings could not be read, so the look-through was not applied");
        }
        else if (inputs.BuyWeightSharePct <= options.EtfBuyWeightSharePctAbove)
        {
            missed.Add(
                $"only {VerdictRules.Format(inputs.BuyWeightSharePct.Value)}% of its top-10 weight reads BUY or better "
                + $"(more than {VerdictRules.Format(options.EtfBuyWeightSharePctAbove)}% is needed)");
        }

        return missed.Count == 0
            ? "No earlier rule matched."
            : $"No earlier rule matched: {string.Join(" and ", missed)}.";
    }
}
