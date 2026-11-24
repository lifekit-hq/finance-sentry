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
public sealed record VerdictExplanation(IReadOnlyList<VerdictReason> Reasons, string? TurnsBuyWhen)
{
    /// <summary>
    /// A short note set only when the weak-structure rule alone produced an AVOID: the word comes from price
    /// structure and fundamentals and valuation did not enter it. Null for every other verdict.
    /// </summary>
    public string? Note { get; init; }
}

/// <summary>
/// Explains a <see cref="Verdict"/> in words, deterministically (spec 726 US2): every number and bar comes from
/// the rule table the evaluator already returned, so the text can never disagree with the word and carries no
/// model-written copy. There is one reason per axis (up to five reads for a scored name; a name outside the
/// v1 universe gets the single reason that the rules do not apply). It adds no rule and moves no threshold.
/// </summary>
public static class VerdictReasons
{
    /// <summary>The note on an AVOID that only the weak-structure rule produced (issue #1529).</summary>
    public const string TrendOnlyNote =
        "Trend-only: this AVOID comes from price structure alone. Fundamentals and valuation did not enter it.";

    private const string UniverseText = "Not a single equity: valuation does not apply, so the rules do not score it.";

    public static VerdictExplanation Explain(Verdict verdict)
    {
        if (EtfVerdictRules.Reads(verdict))
        {
            return ExplainEtf(verdict);
        }

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
            GrowthReason(bars, Decisive(fired, VerdictAxis.Growth, false)),
        };

        var turnsBuy = verdict.Word is VerdictWord.Wait or VerdictWord.Avoid ? TurnsBuyWhen(bars) : null;
        return new VerdictExplanation(reasons, turnsBuy) { Note = TrendOnly(verdict) ? TrendOnlyNote : null };
    }

    /// <summary>
    /// True when the weak-structure rule fired and no other AVOID rule also matched, so no other axis had a say in
    /// the word. The rule table and its thresholds are read, never changed.
    /// </summary>
    private static bool TrendOnly(Verdict verdict) =>
        verdict.FiredRuleId == VerdictRules.WeakStructure
        && verdict.Rules.Where(r => r.Word == VerdictWord.Avoid && r.Id != VerdictRules.WeakStructure).All(r => !r.Matched);

    /// <summary>
    /// The reasons for an ETF word (issue #1324): the fund's own structure and crowding, and the look-through, one
    /// sentence each, every number and bar lifted from the ETF rule table.
    /// </summary>
    private static VerdictExplanation ExplainEtf(Verdict verdict)
    {
        var fired = verdict.Rules.First(r => r.Id == verdict.FiredRuleId);
        var avoidBelow = Int(EtfCondition(verdict, EtfVerdictRules.WeakStructure, VerdictAxis.Structure).Threshold) ?? 0;
        var buyStructure = EtfCondition(verdict, EtfVerdictRules.Buy, VerdictAxis.Structure);
        var buyBar = Int(buyStructure.Threshold) ?? 0;
        var structure = Int(buyStructure.Actual);
        var crowding = EtfCondition(verdict, EtfVerdictRules.Buy, VerdictAxis.Crowding).Actual;
        var lookThrough = EtfCondition(verdict, EtfVerdictRules.Buy, VerdictAxis.LookThrough);
        var share = Dec(lookThrough.Actual);
        var shareBar = Dec(lookThrough.Threshold) ?? 0m;
        var failedBuyBar = fired.Id == EtfVerdictRules.Otherwise;

        var (structureStatus, structureText) = structure switch
        {
            null => (VerdictReasonStatus.Missing, "Fund structure: no score could be read, so no word above WAIT is possible."),
            { } s when s < avoidBelow => (VerdictReasonStatus.Fail, $"Fund structure {s} is below the AVOID line of {avoidBelow}."),
            { } s when s < buyBar => (VerdictReasonStatus.Fail, $"Fund structure {s} is below the BUY bar of {buyBar}."),
            { } s => (VerdictReasonStatus.Pass, $"Fund structure {s} clears the BUY bar of {buyBar}."),
        };
        var (crowdingStatus, crowdingText) = crowding switch
        {
            null => (VerdictReasonStatus.Missing, "Fund crowding: no extension data to classify, so BUY is out of reach."),
            nameof(Opportunity.CrowdingClass.Extended) => (VerdictReasonStatus.Fail, "Fund crowding is Extended: BUY needs it not Extended."),
            var c => (VerdictReasonStatus.Pass, $"Fund crowding is {c}, not Extended."),
        };
        var (lookStatus, lookText) = share switch
        {
            null => (VerdictReasonStatus.Missing,
                "Look-through: the fund's top holdings could not be read, so BUY is out of reach."),
            { } p when p <= shareBar => (VerdictReasonStatus.Fail,
                $"{Plain(p)}% of the top-10 weight reads BUY or better; BUY needs more than {Plain(shareBar)}%."),
            { } p => (VerdictReasonStatus.Pass,
                $"{Plain(p)}% of the top-10 weight reads BUY or better, above the {Plain(shareBar)}% BUY needs."),
        };

        var reasons = new[]
        {
            new VerdictReason(nameof(VerdictAxis.Structure), structureStatus, structureText,
                Decisive(fired, VerdictAxis.Structure, failedBuyBar && structureStatus == VerdictReasonStatus.Fail)),
            new VerdictReason(nameof(VerdictAxis.Crowding), crowdingStatus, crowdingText,
                Decisive(fired, VerdictAxis.Crowding, failedBuyBar && crowdingStatus != VerdictReasonStatus.Pass)),
            new VerdictReason(nameof(VerdictAxis.LookThrough), lookStatus, lookText,
                Decisive(fired, VerdictAxis.LookThrough, failedBuyBar && lookStatus != VerdictReasonStatus.Pass)),
        };

        var needs = new List<string>();
        if (structure is null)
        {
            needs.Add($"a structure score for the fund can be read and reaches {buyBar}");
        }
        else if (structure < buyBar)
        {
            needs.Add($"the fund's structure reaches {buyBar} (reads {structure})");
        }

        if (crowding == nameof(Opportunity.CrowdingClass.Extended))
        {
            needs.Add("the fund's crowding is no longer Extended");
        }

        if (share is null)
        {
            needs.Add($"the fund's top holdings can be read and more than {Plain(shareBar)}% of their weight reads BUY or better");
        }
        else if (share <= shareBar)
        {
            needs.Add($"more than {Plain(shareBar)}% of the top-10 weight reads BUY or better (reads {Plain(share.Value)}%)");
        }

        var turnsBuy = verdict.Word is VerdictWord.Wait or VerdictWord.Avoid && needs.Count > 0
            ? string.Join(" and ", needs)
            : null;
        return new VerdictExplanation(reasons, turnsBuy);
    }

    private static VerdictCondition EtfCondition(Verdict verdict, string ruleId, VerdictAxis axis) =>
        verdict.Rules.First(r => r.Id == ruleId).Conditions.First(c => c.Axis == axis);

    private static int? Int(string? text) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;

    private static decimal? Dec(string? text) =>
        decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? v : null;

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
        var yardstick = b.Lens == PriceLenses.Peers ? "forward P/E premium to the peer median" : "P/E premium to its 5-year average";
        var (status, text) = b.Premium switch
        {
            null => (VerdictReasonStatus.Missing,
                "Valuation: no P/E premium to its own 5-year average and no forward P/E against peers could be read, so STRONG BUY is out of reach."),
            { } p when p > b.StrongMaxPremium => (VerdictReasonStatus.Fail,
                $"{Capitalise(yardstick)} is {Signed(p)}%: STRONG BUY allows at most {Plain(b.StrongMaxPremium)}%."),
            { } p => (VerdictReasonStatus.Pass,
                $"{Capitalise(yardstick)} is {Signed(p)}%, within the STRONG BUY limit of {Plain(b.StrongMaxPremium)}%."),
        };
        return new VerdictReason(nameof(VerdictAxis.Valuation), status, text, decisive);
    }

    private static VerdictReason GrowthReason(Bars b, bool decisive)
    {
        var (status, text) = b.RevenueYoy switch
        {
            null => (VerdictReasonStatus.Missing,
                "Growth: no revenue growth year over year could be read, so STRONG BUY is out of reach."),
            { } g when g < b.StrongMinRevenueYoy => (VerdictReasonStatus.Fail,
                $"Revenue growth is {Signed(g)}% year over year: STRONG BUY needs at least {Plain(b.StrongMinRevenueYoy)}%."),
            { } g => (VerdictReasonStatus.Pass,
                $"Revenue growth is {Signed(g)}% year over year, clearing the STRONG BUY gate of {Plain(b.StrongMinRevenueYoy)}%."),
        };
        return new VerdictReason(nameof(VerdictAxis.Growth), status, text, decisive);
    }

    private static string Capitalise(string text) => char.ToUpperInvariant(text[0]) + text[1..];

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
        decimal? RevenueYoy,
        string? Lens,
        int AvoidBelow,
        int BuyStructure,
        int BuyFundamentals,
        int StrongStructure,
        int StrongFundamentals,
        decimal StrongMaxPremium,
        decimal StrongMinRevenueYoy)
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
            var strongGrowth = Condition(verdict, VerdictRules.StrongBuy, VerdictAxis.Growth);

            return new Bars(
                Int(buyStructure.Actual),
                Int(buyFundamentals.Actual),
                strongCrowding.Actual,
                Dec(strongValuation.Actual),
                Dec(strongGrowth.Actual),
                verdict.PriceLens,
                Int(avoid.Threshold) ?? 0,
                Int(buyStructure.Threshold) ?? 0,
                Int(buyFundamentals.Threshold) ?? 0,
                Int(strongStructure.Threshold) ?? 0,
                Int(strongFundamentals.Threshold) ?? 0,
                Dec(strongValuation.Threshold) ?? 0m,
                Dec(strongGrowth.Threshold) ?? 0m);
        }

        private static VerdictCondition Condition(Verdict verdict, string ruleId, VerdictAxis axis) =>
            verdict.Rules.First(r => r.Id == ruleId).Conditions.First(c => c.Axis == axis);

        private static int? Int(string? text) =>
            int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;

        private static decimal? Dec(string? text) =>
            decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? v : null;
    }
}
