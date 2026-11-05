namespace FinanceSentry.Modules.PortfolioConstruction.Tests;

using FinanceSentry.Modules.PortfolioConstruction.Domain;
using FinanceSentry.Modules.PortfolioConstruction.Domain.Ports;
using FluentAssertions;
using Xunit;

public sealed class PortfolioConstructorTests
{
    private static readonly Archetype Growth = new()
    {
        Label = "Growth",
        MinStructure = 60,
        Ordering = ["structure", "fundamentals"],
        NameCount = 3,
        MaxPositionPct = 30m,
        Sleeves = new Dictionary<string, decimal> { ["Equities"] = 90m, ["Cash"] = 10m },
        MinCashPct = 5m,
    };

    private static ConstructionOutcome Build(IReadOnlyList<UniverseScorecard> universe, ConstructionPolicy? policy = null, Archetype? archetype = null) =>
        PortfolioConstructor.Construct(universe, "growth", archetype ?? Growth, policy ?? ConstructionPolicy.None);

    [Fact]
    public void ColdStart_UsesArchetypeDefaults_AndLabelsEverySource()
    {
        var outcome = Build([Cards.Scored("AAA", structure: 90), Cards.Scored("BBB", structure: 80), Cards.Scored("CCC", structure: 70)]);

        outcome.Constraints.Should().OnlyContain(c => c.Source == ConstraintSource.ArchetypeDefault);
        outcome.Entries.Values.Where(e => e.WeightPct > 0).Should().HaveCount(3);
        outcome.Entries["AAA"].WeightPct.Should().Be(30m);
        outcome.CashPct.Should().Be(10m);
        outcome.Refusal.Should().BeNull();
    }

    [Fact]
    public void Weights_AreEqual_AndSumWithCashToOneHundred()
    {
        var universe = new[] { "AAA", "BBB", "CCC" }.Select(s => Cards.Scored(s)).ToList();
        var archetype = Clone(Growth, g => g.MaxPositionPct = 50m);

        var outcome = Build(universe, archetype: archetype);

        outcome.Entries.Values.Select(e => e.WeightPct).Should().AllBeEquivalentTo(30m);
        (outcome.Entries.Values.Sum(e => e.WeightPct ?? 0m) + outcome.CashPct).Should().Be(100m);
        outcome.Sleeves.Sum(s => s.WeightPct).Should().Be(100m);
    }

    [Fact]
    public void RiskRuleCap_IsStricterThanTheArchetype_AndIsLabelled()
    {
        var universe = new[] { "AAA", "BBB", "CCC" }.Select(s => Cards.Scored(s)).ToList();

        var outcome = Build(universe, new ConstructionPolicy([], null, MaxPositionPct: 8m, MinCashPct: null));

        outcome.Entries.Values.Max(e => e.WeightPct).Should().Be(8m);
        outcome.Constraints.Single(c => c.Key == "maxPositionPct").Should().Be(
            new AppliedConstraint("maxPositionPct", "Largest single position", 8m, ConstraintSource.YourRiskRules));
        outcome.CashPct.Should().Be(76m, "the part of the equities sleeve the cap cannot hold goes to cash, never past the cap");
        outcome.Notes.Should().Contain(n => n.Contains("capped at 8%"));
    }

    [Fact]
    public void RiskRuleCap_LooserThanTheArchetype_DoesNotLoosenIt()
    {
        var universe = new[] { "AAA", "BBB", "CCC" }.Select(s => Cards.Scored(s)).ToList();

        var outcome = Build(universe, new ConstructionPolicy([], null, MaxPositionPct: 50m, MinCashPct: null));

        outcome.Constraints.Single(c => c.Key == "maxPositionPct").Source.Should().Be(ConstraintSource.ArchetypeDefault);
        outcome.Entries.Values.Max(e => e.WeightPct).Should().Be(30m);
    }

    [Fact]
    public void RiskRuleCashBuffer_LowersTheEquitiesSleeve()
    {
        var universe = new[] { "AAA", "BBB" }.Select(s => Cards.Scored(s)).ToList();

        var outcome = Build(universe, new ConstructionPolicy([], null, null, MinCashPct: 20m), Clone(Growth, g => g.MaxPositionPct = 50m));

        outcome.CashPct.Should().Be(20m);
        outcome.Constraints.Single(c => c.Key == "cashFloorPct").Source.Should().Be(ConstraintSource.YourRiskRules);
        outcome.Entries.Values.Sum(e => e.WeightPct ?? 0m).Should().Be(80m);
    }

    [Fact]
    public void IpsExclusions_AreHonoured_AndSaidSo()
    {
        var outcome = Build(
            [Cards.Scored("AAA", structure: 95), Cards.Scored("BBB", structure: 80)],
            new ConstructionPolicy(["aaa"], null, null, null));

        outcome.Entries["AAA"].WeightPct.Should().BeNull();
        outcome.Entries["AAA"].Reason.Should().Be("Excluded by your IPS.");
        outcome.Entries["BBB"].WeightPct.Should().BePositive();
    }

    [Fact]
    public void IpsExclusions_MatchingNoTicker_AreNamedAsNotApplied()
    {
        var outcome = Build(
            [Cards.Scored("AAA", structure: 95), Cards.Scored("BBB", structure: 80)],
            new ConstructionPolicy(["aaa", " Energy ", "tobacco"], null, null, null));

        outcome.Entries["AAA"].Reason.Should().Be("Excluded by your IPS.");
        outcome.Notes.Should().ContainSingle(n => n.Contains("not applied: no sector data"))
            .Which.Should().Contain("ENERGY, TOBACCO").And.NotContain("AAA");
    }

    [Fact]
    public void IpsExclusions_AllMatchingTickers_AddNoNotAppliedNote()
    {
        var outcome = Build(
            [Cards.Scored("AAA"), Cards.Scored("BBB")],
            new ConstructionPolicy(["AAA"], null, null, null));

        outcome.Notes.Should().NotContain(n => n.Contains("not applied"));
    }

    [Fact]
    public void IpsSleeves_SetTheEquitiesWeight_AndOtherSleevesStayAtTarget()
    {
        var policy = new ConstructionPolicy(
            [], [new PolicySleeve("Stocks", 50m), new PolicySleeve("Bonds", 30m), new PolicySleeve("Cash", 10m)], null, null);

        var outcome = Build(new[] { "AAA", "BBB", "CCC" }.Select(s => Cards.Scored(s)).ToList(), policy);

        outcome.Constraints.Single(c => c.Key == "equitySleevePct").Source.Should().Be(ConstraintSource.YourIps);
        outcome.Sleeves.Single(s => s.AssetClass == "Bonds").Should().BeEquivalentTo(
            new ProposedSleeve("Bonds", 30m, false, "Not built from the universe in v1: held at its target weight."));
        outcome.Sleeves.Single(s => s.AssetClass == "Equities").WeightPct.Should().Be(49.98m, "three names at 16.66%: 50% over 3, rounded down; the dust stays in cash");
        outcome.Sleeves.Sum(s => s.WeightPct).Should().Be(100m);
    }

    [Fact]
    public void IpsTargetsUnderOneHundred_PutTheShortfallInCash_AndNameIt()
    {
        var policy = new ConstructionPolicy([], [new PolicySleeve("Equities", 70m), new PolicySleeve("Cash", 10m)], null, null);

        var outcome = Build([Cards.Scored("AAA")], policy, Clone(Growth, g => g.MaxPositionPct = 100m));

        outcome.CashPct.Should().Be(30m);
        outcome.Notes.Should().Contain(n => n.Contains("IPS targets sum to 80%") && n.Contains("20 points"));
    }

    [Fact]
    public void IpsTargetsOverOneHundred_Refuse_WithAReason_AndNoWeights()
    {
        var policy = new ConstructionPolicy([], [new PolicySleeve("Equities", 80m), new PolicySleeve("Bonds", 40m)], null, null);

        var outcome = Build([Cards.Scored("AAA")], policy);

        outcome.Refusal.Should().Contain("sum to 120%");
        outcome.Entries["AAA"].WeightPct.Should().BeNull();
        outcome.Sleeves.Should().BeEmpty();
    }

    [Fact]
    public void TooFewEligibleNames_SendTheRemainderToCash_WithoutBreachingTheCap()
    {
        var outcome = Build([Cards.Scored("AAA")], archetype: Clone(Growth, g => g.MaxPositionPct = 12m));

        outcome.Entries["AAA"].WeightPct.Should().Be(12m);
        outcome.CashPct.Should().Be(88m);
        outcome.Notes.Should().Contain(n => n.Contains("Only 1 of the 3"));
    }

    [Fact]
    public void EmptyUniverse_IsExplicit_AndGivesAllCash()
    {
        var outcome = Build([]);

        outcome.Entries.Should().BeEmpty();
        outcome.CashPct.Should().Be(100m);
        outcome.Notes.Should().Contain(n => n.Contains("universe is empty"));
    }

    [Theory]
    [InlineData("Wait", 90, 90, "Normal", "WAIT")]
    [InlineData("Avoid", 90, 90, "Normal", "AVOID")]
    [InlineData("NotScored", null, null, null, "NOT SCORED")]
    public void VerdictsOutsideTheArchetype_GetNoWeight_AndTheReasonNamesTheVerdict(
        string word, int? structure, int? fundamentals, string? crowding, string label)
    {
        var outcome = Build([Cards.Scored("AAA", word, structure, fundamentals, crowding)]);

        outcome.Entries["AAA"].WeightPct.Should().BeNull();
        outcome.Entries["AAA"].Reason.Should().Contain($"The verdict is {label}");
    }

    [Fact]
    public void GateFailures_NameTheGate_AndNeverReadAMissingAxisAsAPass()
    {
        var growth = Clone(Growth, g => g.MinFundamentals = 60);
        var universe = new[]
        {
            Cards.Scored("LOWSTRUCT", structure: 55),
            Cards.Scored("NOFUND", fundamentals: null),
            Cards.Scored("WEAKFUND", fundamentals: 40),
            Cards.Scored("EXTENDED", crowding: "Extended"),
            Cards.Scored("NOCROWD", crowding: null),
            Cards.Scored("STALE", stale: true),
        };

        var outcome = Build(universe, archetype: growth);

        outcome.Entries["LOWSTRUCT"].Reason.Should().Contain("structure 55 is below the Growth gate of 60");
        outcome.Entries["NOFUND"].Reason.Should().Contain("Fundamentals missing");
        outcome.Entries["WEAKFUND"].Reason.Should().Contain("Fundamentals 40");
        outcome.Entries["EXTENDED"].Reason.Should().Contain("Extended");
        outcome.Entries["NOCROWD"].Reason.Should().Contain("could not be classified");
        outcome.Entries["STALE"].Reason.Should().Contain("stale");
        outcome.Entries.Values.Should().OnlyContain(e => e.WeightPct == null);
    }

    [Fact]
    public void FailedScorecard_IsListedWithItsReason_AndOthersStillBuild()
    {
        var outcome = Build([UniverseScorecard.Failed("BAD", "the scorecard could not be read"), Cards.Scored("GOOD")]);

        outcome.Entries["BAD"].Reason.Should().Be("Not scored: the scorecard could not be read.");
        outcome.Entries["GOOD"].WeightPct.Should().BePositive();
    }

    [Fact]
    public void Ordering_IsLexicographic_ByTheArchetypeKeys_ThenSymbol()
    {
        var universe = new[]
        {
            Cards.Scored("ZED", structure: 80, fundamentals: 90),
            Cards.Scored("ABE", structure: 80, fundamentals: 70),
            Cards.Scored("MID", structure: 95, fundamentals: 50),
            Cards.Scored("TIE", structure: 80, fundamentals: 70),
        };
        var byStructure = Build(universe, archetype: Clone(Growth, g => g.NameCount = 4));
        var byFundamentals = Build(universe, archetype: Clone(Growth, g => { g.NameCount = 4; g.Ordering = ["fundamentals", "structure"]; }));

        Ranks(byStructure).Should().Equal("MID", "ZED", "ABE", "TIE");
        Ranks(byFundamentals).Should().Equal("ZED", "ABE", "TIE", "MID");
    }

    [Fact]
    public void NamesBeyondTheCount_ArePlacedButGetNoWeight()
    {
        var universe = new[] { "AAA", "BBB", "CCC", "DDD" }.Select((s, i) => Cards.Scored(s, structure: 90 - i)).ToList();

        var outcome = Build(universe);

        outcome.Entries["DDD"].Rank.Should().Be(4);
        outcome.Entries["DDD"].WeightPct.Should().BeNull();
        outcome.Entries["DDD"].Reason.Should().Contain("ranks #4, beyond the 3 names held");
    }

    [Fact]
    public void EveryWeightedName_CarriesTheGateVerdictRankAndSizing_InItsReason()
    {
        var outcome = Build(new[] { "AAA", "BBB", "CCC" }.Select(s => Cards.Scored(s, "StrongBuy")).ToList());

        var reason = outcome.Entries["AAA"].Reason;
        reason.Should().Contain("Passed the Growth gates").And.Contain("STRONG BUY").And.Contain("ranked #1")
            .And.Contain("structure then fundamentals").And.Contain("equal weight");
    }

    [Fact]
    public void Construction_IsDeterministic_AndIndependentOfInputOrder()
    {
        var universe = new[] { "AAA", "BBB", "CCC", "DDD", "EEE" }
            .Select((s, i) => Cards.Scored(s, structure: 70 + (i % 3))).ToList();

        var first = Build(universe);
        var second = Build([.. universe.AsEnumerable().Reverse()]);

        Snapshot(first).Should().Be(Snapshot(second));
        Snapshot(first).Should().Be(Snapshot(Build(universe)));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(42)]
    public void NoName_EverExceedsTheEffectiveCap_OverRandomisedUniverses(int seed)
    {
        var random = new Random(seed);
        for (var run = 0; run < 50; run++)
        {
            var universe = Enumerable.Range(0, random.Next(0, 30))
                .Select(i => Cards.Scored(
                    $"S{i:00}",
                    new[] { "StrongBuy", "Buy", "Wait", "Avoid" }[random.Next(4)],
                    random.Next(0, 101),
                    random.Next(3) == 0 ? null : random.Next(0, 101),
                    new[] { "Early", "Normal", "Extended", null }[random.Next(4)]))
                .ToList();
            var cap = random.Next(1, 40);
            var policy = new ConstructionPolicy([], null, cap, random.Next(0, 30));

            var outcome = Build(universe, policy);

            outcome.Entries.Values.Where(e => e.WeightPct is not null).All(e => e.WeightPct <= cap).Should().BeTrue();
            (outcome.Entries.Values.Sum(e => e.WeightPct ?? 0m) + outcome.CashPct + outcome.Sleeves.Where(s => s.AssetClass != "Equities" && s.AssetClass != "Cash").Sum(s => s.WeightPct))
                .Should().Be(100m);
        }
    }

    [Fact]
    public void Outcome_CarriesNoCompositeNumber()
    {
        var banned = new[] { "Score", "Composite", "Overall", "Total", "Grade" };
        var types = new[] { typeof(ConstructionOutcome), typeof(ConstructionEntry), typeof(ProposedSleeve), typeof(AppliedConstraint) };

        types.SelectMany(t => t.GetProperties()).Select(p => p.Name)
            .Should().NotContain(n => banned.Any(b => n.Contains(b, StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void ArchetypeOrderingKeyThatIsUnknown_IsRejected()
    {
        var act = () => Build([Cards.Scored("AAA")], archetype: Clone(Growth, g => g.Ordering = ["momentum"]));

        act.Should().Throw<ArgumentException>();
    }

    private static string[] Ranks(ConstructionOutcome outcome) =>
        [.. outcome.Entries.Values.Where(e => e.Rank is not null).OrderBy(e => e.Rank).Select(e => e.Symbol)];

    private static string Snapshot(ConstructionOutcome outcome) =>
        string.Join(
            "|",
            outcome.Entries.OrderBy(e => e.Key, StringComparer.Ordinal).Select(e => $"{e.Key}:{e.Value.Rank}:{e.Value.WeightPct}:{e.Value.Reason}")
                .Concat(outcome.Sleeves.Select(s => $"{s.AssetClass}:{s.WeightPct}"))
                .Concat(outcome.Notes)
                .Append($"cash:{outcome.CashPct}"));

    private static Archetype Clone(Archetype source, Action<Archetype> change)
    {
        var copy = new Archetype
        {
            Label = source.Label,
            Description = source.Description,
            EligibleVerdicts = [.. source.EligibleVerdicts],
            MinStructure = source.MinStructure,
            MinFundamentals = source.MinFundamentals,
            ExcludeExtended = source.ExcludeExtended,
            Ordering = [.. source.Ordering],
            NameCount = source.NameCount,
            MaxPositionPct = source.MaxPositionPct,
            Sleeves = new Dictionary<string, decimal>(source.Sleeves),
            MinCashPct = source.MinCashPct,
        };
        change(copy);
        return copy;
    }
}
