namespace FinanceSentry.Modules.PortfolioConstruction.Domain;

using System.Globalization;
using FinanceSentry.Core.Domain;
using FinanceSentry.Modules.PortfolioConstruction.Domain.Ports;

/// <summary>
/// The rules-based constructor (spec 726 OQ-5, OQ-6): gate the scored universe by the archetype, order what passes
/// by the archetype's axis keys (lexicographic, never a weighted sum), take the top N, equal-weight them, cap each
/// at the effective position cap and send the remainder to cash. Pure and deterministic: the same universe,
/// archetype and policy always give the same outcome (FR-013). Every weight comes with the one-line reason that set
/// it (FR-016), and every applied limit is labelled with its source (FR-014). It creates nothing: no trade, order,
/// candidate, thesis or IPS (FR-018).
/// </summary>
public static class PortfolioConstructor
{
    private const decimal Hundred = 100m;
    private const decimal Tolerance = 0.005m;
    private const int WeightDecimals = 2;

    public const string StructureKey = "structure";
    public const string FundamentalsKey = "fundamentals";

    public static ConstructionOutcome Construct(
        IReadOnlyList<UniverseScorecard> universe,
        string archetypeKey,
        Archetype archetype,
        ConstructionPolicy policy)
    {
        var excluded = policy.Exclusions
            .Select(e => e.Trim().ToUpperInvariant())
            .Where(e => e.Length > 0)
            .ToHashSet();

        var sleeveSource = policy.Sleeves is { Count: > 0 } ? ConstraintSource.YourIps : ConstraintSource.ArchetypeDefault;
        var targets = SleeveTargets(policy.Sleeves is { Count: > 0 }
            ? policy.Sleeves
            : archetype.Sleeves.Select(s => new PolicySleeve(s.Key, s.Value)).ToList());
        var sleeveTotal = targets.Sum(t => t.Value);

        var (cap, capSource) = Stricter(archetype.MaxPositionPct, policy.MaxPositionPct, min: true);
        var (cashFloor, cashSource) = Stricter(archetype.MinCashPct, policy.MinCashPct, min: false);
        var equityTarget = targets.GetValueOrDefault(AssetClassNormalizer.Equities);

        var constraints = new List<AppliedConstraint>
        {
            new("equitySleevePct", "Equities sleeve", equityTarget, sleeveSource),
            new("maxPositionPct", "Largest single position", cap, capSource),
            new("cashFloorPct", "Cash floor", cashFloor, cashSource),
            new("nameCount", "Most names held", archetype.NameCount, ConstraintSource.ArchetypeDefault),
        };

        if (sleeveTotal > Hundred + Tolerance)
        {
            var refusal = $"Your IPS targets sum to {Format(sleeveTotal)}%, over 100%: the policy cannot be built as written. "
                + "Nothing is proposed until the targets sum to 100% or less.";
            var refused = universe.ToDictionary(
                u => u.Symbol,
                u => new ConstructionEntry(u.Symbol, null, null, refusal),
                StringComparer.OrdinalIgnoreCase);
            return new ConstructionOutcome(refused, [], 0m, constraints, [], refusal);
        }

        var notes = new List<string>();
        var reasons = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var eligible = new List<UniverseScorecard>();
        foreach (var card in universe)
        {
            var why = Ineligible(card, archetype, excluded);
            if (why is null)
            {
                eligible.Add(card);
            }
            else
            {
                reasons[card.Symbol] = why;
            }
        }

        // OQ-10 (b): IPS points nobody allocated sit in cash and are named, never spread onto other sleeves.
        var unallocated = Math.Max(0m, Hundred - sleeveTotal);
        var cashTarget = targets.GetValueOrDefault(AssetClassNormalizer.Cash) + unallocated;
        if (unallocated > Tolerance)
        {
            notes.Add($"Cash: IPS targets sum to {Format(sleeveTotal)}%, so {Format(unallocated)} points are held as cash.");
        }

        var equityBudget = equityTarget;
        if (cashTarget < cashFloor)
        {
            var deficit = Math.Min(equityBudget, cashFloor - cashTarget);
            equityBudget -= deficit;
            cashTarget += deficit;
            notes.Add($"The cash floor is {Format(cashFloor)}% ({cashSource}), so the equities sleeve is {Format(equityBudget)}% instead of {Format(equityTarget)}%.");
        }

        var ordered = Order(eligible, archetype.Ordering);
        var take = Math.Min(archetype.NameCount, ordered.Count);
        var entries = new Dictionary<string, ConstructionEntry>(StringComparer.OrdinalIgnoreCase);
        decimal weightEach = 0m;

        if (take > 0 && equityBudget > 0m)
        {
            var perName = equityBudget / take;
            weightEach = decimal.Round(Math.Min(perName, cap), WeightDecimals, MidpointRounding.ToZero);
            if (perName > cap)
            {
                notes.Add($"Each name is capped at {Format(cap)}% ({capSource}), so the part of the equities sleeve the cap cannot hold goes to cash.");
            }
        }

        if (ordered.Count < archetype.NameCount && equityBudget > 0m)
        {
            notes.Add($"Only {ordered.Count} of the {archetype.NameCount} names {archetype.Label} would hold pass its gates.");
        }

        var rank = 0;
        foreach (var card in ordered)
        {
            rank++;
            if (rank <= take && weightEach > 0m)
            {
                entries[card.Symbol] = new ConstructionEntry(card.Symbol, rank, weightEach, WeightReason(card, archetype, rank, take, equityBudget, weightEach, cap, capSource));
            }
            else
            {
                var beyond = rank > take
                    ? $"Passed the {archetype.Label} gates but ranks #{rank}, beyond the {take} names held."
                    : "Passed the gates, but the equities sleeve is empty.";
                entries[card.Symbol] = new ConstructionEntry(card.Symbol, rank, null, beyond);
            }
        }

        foreach (var (symbol, why) in reasons)
        {
            entries[symbol] = new ConstructionEntry(symbol, null, null, why);
        }

        var equityActual = weightEach * (weightEach > 0m ? take : 0);
        var others = targets
            .Where(t => t.Key != AssetClassNormalizer.Equities && t.Key != AssetClassNormalizer.Cash)
            .OrderBy(t => t.Key, StringComparer.Ordinal)
            .ToList();
        var cash = Hundred - equityActual - others.Sum(o => o.Value);

        var sleeves = new List<ProposedSleeve> { new(AssetClassNormalizer.Equities, equityActual, true, null) };
        sleeves.AddRange(others.Select(o => new ProposedSleeve(o.Key, o.Value, false, "Not built from the universe in v1: held at its target weight.")));
        sleeves.Add(new ProposedSleeve(AssetClassNormalizer.Cash, cash, false, null));

        if (universe.Count == 0)
        {
            notes.Add("The universe is empty, so nothing can be constructed.");
        }

        return new ConstructionOutcome(entries, sleeves, cash, constraints, notes, null);
    }

    /// <summary>The reason a name gets no weight, or null when it passes every gate (the first failing gate wins).</summary>
    private static string? Ineligible(UniverseScorecard card, Archetype archetype, HashSet<string> excluded)
    {
        if (excluded.Contains(card.Symbol.Trim().ToUpperInvariant()))
        {
            return "Excluded by your IPS.";
        }

        if (!card.Scored)
        {
            return $"Not scored: {card.FailureReason}.";
        }

        var word = card.VerdictWord ?? "NotScored";
        if (!archetype.EligibleVerdicts.Contains(word, StringComparer.OrdinalIgnoreCase))
        {
            return $"The verdict is {VerdictLabel(word)}, which {archetype.Label} does not build from "
                + $"({string.Join(", ", archetype.EligibleVerdicts.Select(VerdictLabel))}).";
        }

        if (card.ValuationStale)
        {
            return "The valuation quote is stale, so the name is not built until it refreshes.";
        }

        if (archetype.MinStructure is { } minStructure && !(card.StructureAdjusted >= minStructure))
        {
            return $"Adjusted structure {Show(card.StructureAdjusted)} is below the {archetype.Label} gate of {minStructure}.";
        }

        if (archetype.MinFundamentals is { } minFundamentals && !(card.Fundamentals >= minFundamentals))
        {
            return $"Fundamentals {Show(card.Fundamentals)} are below the {archetype.Label} gate of {minFundamentals}.";
        }

        if (archetype.ExcludeExtended && !(card.Crowding is { } crowding && !string.Equals(crowding, "Extended", StringComparison.OrdinalIgnoreCase)))
        {
            return card.Crowding is null
                ? "Crowding could not be classified, so the not-Extended gate is not met."
                : "Crowding is Extended, which the archetype excludes.";
        }

        return null;
    }

    private static List<UniverseScorecard> Order(IEnumerable<UniverseScorecard> eligible, IReadOnlyList<string> ordering)
    {
        IOrderedEnumerable<UniverseScorecard>? sorted = null;
        foreach (var key in ordering)
        {
            Func<UniverseScorecard, int> selector = key.ToLowerInvariant() switch
            {
                StructureKey => c => c.StructureAdjusted ?? int.MinValue,
                FundamentalsKey => c => c.Fundamentals ?? int.MinValue,
                _ => throw new ArgumentException($"Unknown ordering key '{key}'.", nameof(ordering)),
            };
            sorted = sorted is null ? eligible.OrderByDescending(selector) : sorted.ThenByDescending(selector);
        }

        sorted = sorted is null
            ? eligible.OrderBy(c => c.Symbol, StringComparer.Ordinal)
            : sorted.ThenBy(c => c.Symbol, StringComparer.Ordinal);
        return [.. sorted];
    }

    private static string WeightReason(
        UniverseScorecard card, Archetype archetype, int rank, int count, decimal equityBudget, decimal weight, decimal cap, string capSource)
    {
        var ordering = string.Join(" then ", archetype.Ordering.Select(k => k.ToLowerInvariant()));
        var sizing = equityBudget / count > cap
            ? $"equal weight, capped at {Format(cap)}% ({capSource})"
            : $"equal weight: {Format(equityBudget)}% equities sleeve over {count} names";
        return $"Passed the {archetype.Label} gates with {VerdictLabel(card.VerdictWord)}; "
            + $"ranked #{rank} by {ordering}; {Format(weight)}% ({sizing}).";
    }

    /// <summary>Groups the targets by canonical asset class, so "Stocks" and "Equities" are one sleeve.</summary>
    private static Dictionary<string, decimal> SleeveTargets(IEnumerable<PolicySleeve> sleeves)
    {
        var result = new Dictionary<string, decimal>(StringComparer.Ordinal);
        foreach (var sleeve in sleeves)
        {
            var key = AssetClassNormalizer.Normalize(sleeve.AssetClass);
            result[key] = result.GetValueOrDefault(key) + sleeve.TargetPct;
        }

        return result;
    }

    /// <summary>The stricter of the archetype default and the user's own limit, with where it came from (FR-014).</summary>
    private static (decimal Value, string Source) Stricter(decimal archetypeValue, decimal? userValue, bool min)
    {
        if (userValue is not { } user)
        {
            return (archetypeValue, ConstraintSource.ArchetypeDefault);
        }

        var userWins = min ? user < archetypeValue : user > archetypeValue;
        return userWins ? (user, ConstraintSource.YourRiskRules) : (archetypeValue, ConstraintSource.ArchetypeDefault);
    }

    /// <summary>The published label of a verdict word, as the Research module spells it for people.</summary>
    public static string VerdictLabel(string? word) => word switch
    {
        "StrongBuy" => "STRONG BUY",
        "Buy" => "BUY",
        "Wait" => "WAIT",
        "Avoid" => "AVOID",
        _ => "NOT SCORED",
    };

    private static string Show(int? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "missing";

    private static string Format(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
