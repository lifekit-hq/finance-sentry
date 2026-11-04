namespace FinanceSentry.Modules.Research.Domain.Scoring;

using System.Globalization;

/// <summary>How one axis stands against the bars the verdict rules read.</summary>
public enum VerdictReasonStatus
{
    /// <summary>The axis clears the bar the rules set for it.</summary>
    Pass,

    /// <summary>The axis was read and falls short of a bar.</summary>
    Fail,

    /// <summary>The axis has nothing to read (spec 726 FR-008): a missing axis can only lower a word.</summary>
    Missing,

    /// <summary>The rules do not apply to this name at all (outside the v1 universe).</summary>
    NotApplicable,
}

/// <summary>
/// One fact behind the word, as a sentence: the axis, the value the rules read and the bar it was held
/// against. <see cref="Decisive"/> marks an axis the fired rule itself tested.
/// </summary>
public sealed record VerdictReason(string Axis, VerdictReasonStatus Status, string Text, bool Decisive);

/// <summary>
/// The reasons for a verdict word and the line saying what would turn it into BUY. <see cref="TurnsBuyWhen"/>
/// is null when the word is already BUY or better, or when the name is not scored at all.
/// </summary>
public sealed record VerdictExplanation(IReadOnlyList<VerdictReason> Reasons, string? TurnsBuyWhen);

/// <summary>
/// Explains a <see cref="Verdict"/> in words, deterministically (spec 726 US2): every number and bar comes from
/// the rule table the evaluator already returned, so the text can never disagree with the word and carries no
/// model-written copy. There is one reason per axis (three to five reads for a scored name; a name outside the
/// v1 universe gets the single reason that the rules do not apply). It adds no rule and moves no threshold.
/// </summary>
public static class VerdictReasons
{
    private const string UniverseText = "Not a single equity: valuation does not apply, so the rules do not score it.";

    public static VerdictExplanation Explain(Verdict verdict)
    {
        if (verdict.FiredRuleId == VerdictRules.OutsideUniverse)
        {
            return new VerdictExplanation(
                [new VerdictReason(nameof(VerdictAxis.Valuation), VerdictReasonStatus.NotApplicable, UniverseText, true)],
                null);
        }

        var bars = Bars.Read(verdict);
        var fired = verdict.Rules.First(r => r.Id == verdict.FiredRuleId);
        var failedBuyBar = fired.Id == VerdictRules.Otherwise;

        var reasons = new[]
        {
            StructureReason(bars, Decisive(fired, VerdictAxis.Structure, failedBuyBar && bars.StructureBelowBuy)),
            FundamentalsReason(bars, Decisive(fired, VerdictAxis.Fundamentals, failedBuyBar && bars.FundamentalsBelowBuy)),
            CrowdingReason(bars, Decisive(fired, VerdictAxis.Crowding, false)),
            ValuationReason(bars, Decisive(fired, VerdictAxis.Valuation, false)),
        };

        var turnsBuy = verdict.Word is VerdictWord.Wait or VerdictWord.Avoid ? TurnsBuyWhen(bars) : null;
        return new VerdictExplanation(reasons, turnsBuy);
    }

    private static bool Decisive(VerdictRuleOutcome fired, VerdictAxis axis, bool extra) =>
        extra || fired.Conditions.Any(c => c.Axis == axis);

    private static VerdictReason StructureReason(Bars b, bool decisive)
    {
        var (status, text) = b.Structure switch
        {
            null => (VerdictReasonStatus.Missing, "Structure: no score could be read, so no word above WAIT is possible."),
            { } s when s < b.AvoidBelow => (VerdictReasonStatus.Fail, $"Structure {s} is below the AVOID line of {b.AvoidBelow}."),
            { } s when s < b.BuyStructure => (VerdictReasonStatus.Fail, $"Structure {s} is below the BUY bar of {b.BuyStructure}."),
            { } s when s >= b.StrongStructure => (VerdictReasonStatus.Pass, $"Structure {s} clears the BUY bar of {b.BuyStructure} and the STRONG BUY bar of {b.StrongStructure}."),
            { } s => (VerdictReasonStatus.Pass, $"Structure {s} clears the BUY bar of {b.BuyStructure}."),
        };
        return new VerdictReason(nameof(VerdictAxis.Structure), status, text, decisive);
    }

    private static VerdictReason FundamentalsReason(Bars b, bool decisive)
    {
        var (status, text) = b.Fundamentals switch
        {
            null => (VerdictReasonStatus.Missing, "Fundamentals: not scored, so the word is capped at BUY."),
            { } f when f < b.BuyFundamentals => (VerdictReasonStatus.Fail, $"Fundamentals {f} are below the BUY bar of {b.BuyFundamentals}."),
            { } f when f >= b.StrongFundamentals => (VerdictReasonStatus.Pass, $"Fundamentals {f} clear the BUY bar of {b.BuyFundamentals} and the STRONG BUY bar of {b.StrongFundamentals}."),
            { } f => (VerdictReasonStatus.Pass, $"Fundamentals {f} clear the BUY bar of {b.BuyFundamentals}."),
        };
        return new VerdictReason(nameof(VerdictAxis.Fundamentals), status, text, decisive);
    }

    private static VerdictReason CrowdingReason(Bars b, bool decisive)
    {
        var (status, text) = b.Crowding switch
        {
            null => (VerdictReasonStatus.Missing, "Crowding: no extension data to classify, so STRONG BUY is out of reach."),
            nameof(Opportunity.CrowdingClass.Extended) => (VerdictReasonStatus.Fail,
                "Crowding is Extended: STRONG BUY needs it not Extended, and Extended with a P/E above its own average is AVOID."),
            var c => (VerdictReasonStatus.Pass, $"Crowding is {c}, not Extended."),
        };
        return new VerdictReason(nameof(VerdictAxis.Crowding), status, text, decisive);
    }

    private static VerdictReason ValuationReason(Bars b, bool decisive)
    {
        var (status, text) = b.Premium switch
        {
            null => (VerdictReasonStatus.Missing,
                "Valuation: no P/E premium to its own 5-year average could be read, so STRONG BUY is out of reach."),
            { } p when p > b.StrongMaxPremium => (VerdictReasonStatus.Fail,
                $"P/E premium to its 5-year average is {Signed(p)}%: STRONG BUY allows at most {Plain(b.StrongMaxPremium)}%."),
            { } p => (VerdictReasonStatus.Pass,
                $"P/E premium to its 5-year average is {Signed(p)}%, within the STRONG BUY limit of {Plain(b.StrongMaxPremium)}%."),
        };
        return new VerdictReason(nameof(VerdictAxis.Valuation), status, text, decisive);
    }

    /// <summary>
    /// What has to change for the BUY rules to fire: each BUY-side bar the name misses today, plus the
    /// Extended-and-rich guard when it is what sends the name to AVOID. Fundamentals that are missing are not a
    /// requirement: BUY is reachable on structure alone (capped).
    /// </summary>
    private static string? TurnsBuyWhen(Bars b)
    {
        var needs = new List<string>();
        if (b.Structure is null)
        {
            needs.Add($"a structure score can be read and reaches {b.BuyStructure}");
        }
        else if (b.Structure < b.BuyStructure)
        {
            needs.Add($"structure reaches {b.BuyStructure} (reads {b.Structure})");
        }

        if (b.Fundamentals is { } f && f < b.BuyFundamentals)
        {
            needs.Add($"fundamentals reach {b.BuyFundamentals} (reads {f})");
        }

        if (b.Crowding == nameof(Opportunity.CrowdingClass.Extended) && b.Premium is > 0m)
        {
            needs.Add($"crowding is no longer Extended or the P/E premium falls to 0% or below (reads {Signed(b.Premium.Value)}%)");
        }

        return needs.Count == 0 ? null : string.Join(" and ", needs);
    }

    private static string Signed(decimal value) =>
        value > 0m ? "+" + Plain(value) : Plain(value);

    private static string Plain(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>The bars and the values read, lifted out of the rule table by axis (never by position).</summary>
    private sealed record Bars(
        int? Structure,
        int? Fundamentals,
        string? Crowding,
        decimal? Premium,
        int AvoidBelow,
        int BuyStructure,
        int BuyFundamentals,
        int StrongStructure,
        int StrongFundamentals,
        decimal StrongMaxPremium)
    {
        public bool StructureBelowBuy => Structure is { } s && s < BuyStructure;

        public bool FundamentalsBelowBuy => Fundamentals is { } f && f < BuyFundamentals;

        public static Bars Read(Verdict verdict)
        {
            var avoid = Condition(verdict, VerdictRules.WeakStructure, VerdictAxis.Structure);
            var buyStructure = Condition(verdict, VerdictRules.Buy, VerdictAxis.Structure);
            var buyFundamentals = Condition(verdict, VerdictRules.Buy, VerdictAxis.Fundamentals);
            var strongStructure = Condition(verdict, VerdictRules.StrongBuy, VerdictAxis.Structure);
            var strongFundamentals = Condition(verdict, VerdictRules.StrongBuy, VerdictAxis.Fundamentals);
            var strongCrowding = Condition(verdict, VerdictRules.StrongBuy, VerdictAxis.Crowding);
            var strongValuation = Condition(verdict, VerdictRules.StrongBuy, VerdictAxis.Valuation);

            return new Bars(
                Int(buyStructure.Actual),
                Int(buyFundamentals.Actual),
                strongCrowding.Actual,
                Dec(strongValuation.Actual),
                Int(avoid.Threshold) ?? 0,
                Int(buyStructure.Threshold) ?? 0,
                Int(buyFundamentals.Threshold) ?? 0,
                Int(strongStructure.Threshold) ?? 0,
                Int(strongFundamentals.Threshold) ?? 0,
                Dec(strongValuation.Threshold) ?? 0m);
        }

        private static VerdictCondition Condition(Verdict verdict, string ruleId, VerdictAxis axis) =>
            verdict.Rules.First(r => r.Id == ruleId).Conditions.First(c => c.Axis == axis);

        private static int? Int(string? text) =>
            int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;

        private static decimal? Dec(string? text) =>
            decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? v : null;
    }
}
