namespace FinanceSentry.Modules.Risk.Application.Services;

using FinanceSentry.Core.Domain;
using FinanceSentry.Modules.Risk.Domain;
using FinanceSentry.Modules.Risk.Domain.Ports;
using Microsoft.Extensions.Logging;

/// <summary>
/// Pure evaluation logic (SC-001): no I/O, no clock reads beyond the injected `now`. Deterministic
/// facts only — no LLM, no composite/blended score (FR-008). 039: the target allocation is read from
/// its single home (the IPS) by the caller and passed into <c>Evaluate</c> as the allocation-targets
/// argument; the service stays pure. <paramref name="logger"/> is diagnostic-only (finance-sentry#690:
/// the allocation-drift rule logs when it can't compute a sleeve's weight) and defaults to null so the
/// service stays trivially constructible in tests.
/// </summary>
public sealed class RiskEvaluationService(ILogger<RiskEvaluationService>? logger = null) : IRiskEvaluationService
{
    public ComplianceReport Evaluate(
        BookSnapshot book,
        RiskRuleSet? ruleSet,
        IReadOnlyList<AllocationDriftTarget> allocationTargets,
        IReadOnlyList<PolicyViolationAck> acks,
        DateTimeOffset? now = null)
    {
        var generatedAt = now ?? DateTimeOffset.UtcNow;

        if (ruleSet is null)
        {
            return new ComplianceReport(generatedAt, book.IsStale, book.StaleSources, [], HasRuleSet: false);
        }

        var raw = ComputeRawViolations(book, ruleSet, allocationTargets, logger);
        var acked = ApplyAcks(raw, acks);

        return new ComplianceReport(generatedAt, book.IsStale, book.StaleSources, acked, HasRuleSet: true);
    }

    public RiskVerdict EvaluateProposal(
        BookSnapshot book,
        RiskRuleSet? ruleSet,
        string ticker,
        decimal proposedUsd,
        int turnoverCountThisQuarter)
    {
        if (ruleSet is null)
        {
            return new RiskVerdict(RiskDecision.Allowed, null, null, null, null, null, "No rules on file — nothing to check.");
        }

        if (ruleSet.TurnoverBudgetPerQuarter is { } turnoverBudget && turnoverCountThisQuarter >= turnoverBudget)
        {
            return new RiskVerdict(
                RiskDecision.Refused,
                RiskRuleKeys.Turnover,
                turnoverCountThisQuarter,
                turnoverBudget,
                0m,
                0m,
                "Discretionary turnover budget for this quarter is already reached.");
        }

        // An unsynced/empty book has no denominator: weight and cash rules are not evaluable —
        // report that honestly instead of refusing every first position at "100% weight".
        if (book.TotalUsd <= 0m)
        {
            return new RiskVerdict(
                RiskDecision.Allowed, null, null, null, null, null,
                "Book is empty or not yet synced — weight/cash rules are not evaluable for this proposal.");
        }

        var existingUsd = book.Positions
            .Where(p => string.Equals(p.Symbol, ticker, StringComparison.OrdinalIgnoreCase))
            .Sum(p => p.UsdValue);
        var isNewPosition = existingUsd <= 0m;

        // A buy is cash-funded up to available cash (total unchanged); anything beyond cash is
        // external money and grows the book. One consistent model for weight AND cash checks.
        var fundedFromCash = Math.Min(proposedUsd, Math.Max(0m, book.CashUsd));
        var externalTopUp = proposedUsd - fundedFromCash;
        var projectedTotal = book.TotalUsd + externalTopUp;

        if (ruleSet.MaxPositionWeightPct is { } maxWeight && maxWeight is > 0 and <= 1)
        {
            var projectedWeight = (existingUsd + proposedUsd) / projectedTotal;
            if (projectedWeight > maxWeight)
            {
                var maxCompliantSize = MaxCompliantSize(book.TotalUsd, existingUsd, book.CashUsd, maxWeight);
                return new RiskVerdict(
                    RiskDecision.Refused,
                    RiskRuleKeys.MaxPositionWeight,
                    projectedWeight,
                    maxWeight,
                    maxCompliantSize,
                    Math.Max(0m, maxCompliantSize - proposedUsd));
            }
        }

        if (isNewPosition && ruleSet.MaxNewPositionPct is { } maxNew && maxNew is > 0 and <= 1)
        {
            var projectedWeight = proposedUsd / projectedTotal;
            if (projectedWeight > maxNew)
            {
                var maxCompliantSize = MaxCompliantSize(book.TotalUsd, 0m, book.CashUsd, maxNew);
                return new RiskVerdict(
                    RiskDecision.Refused,
                    RiskRuleKeys.MaxNewPosition,
                    projectedWeight,
                    maxNew,
                    maxCompliantSize,
                    Math.Max(0m, maxCompliantSize - proposedUsd));
            }
        }

        if (ruleSet.MinCashBufferPct is { } minCash and > 0)
        {
            var projectedCash = book.CashUsd - fundedFromCash;
            var projectedCashPct = projectedCash / projectedTotal;
            if (projectedCashPct < minCash)
            {
                var headroom = Math.Max(0m, book.CashUsd - (minCash * book.TotalUsd));
                return new RiskVerdict(
                    RiskDecision.Refused,
                    RiskRuleKeys.MinCashBuffer,
                    projectedCashPct,
                    minCash,
                    headroom,
                    headroom);
            }
        }

        var headroomUsd = ruleSet.MaxPositionWeightPct is { } cap
            ? Math.Max(0m, MaxCompliantSize(book.TotalUsd, existingUsd, book.CashUsd, cap) - proposedUsd)
            : (decimal?)null;

        return new RiskVerdict(RiskDecision.Allowed, null, null, null, null, headroomUsd);
    }

    /// <summary>
    /// Max additional USD that can go into a position without breaching `cap` weight, under the
    /// cash-funded-then-external model: cash-funded dollars leave the total unchanged
    /// (x = cap·T − existing while x ≤ cash); external dollars grow the denominator
    /// (y = (cap·T − existing − cash) / (1 − cap) beyond that).
    /// </summary>
    private static decimal MaxCompliantSize(decimal totalUsd, decimal existingUsd, decimal cashUsd, decimal cap)
    {
        if (cap >= 1m)
        {
            return decimal.MaxValue;
        }

        var cash = Math.Max(0m, cashUsd);
        var cashFundedMax = (cap * totalUsd) - existingUsd;
        if (cashFundedMax <= cash)
        {
            return Math.Max(0m, cashFundedMax);
        }

        var externalMax = ((cap * totalUsd) - existingUsd - cash) / (1m - cap);
        return Math.Max(0m, cash + Math.Max(0m, externalMax));
    }

    private static List<PolicyViolation> ComputeRawViolations(
        BookSnapshot book,
        RiskRuleSet ruleSet,
        IReadOnlyList<AllocationDriftTarget> allocationTargets,
        ILogger? logger)
    {
        var violations = new List<PolicyViolation>();

        if (ruleSet.MaxPositionWeightPct is { } maxWeight)
        {
            foreach (var p in book.Positions)
            {
                if (p.WeightPct > maxWeight)
                {
                    var limitUsd = maxWeight * book.TotalUsd;
                    violations.Add(new PolicyViolation(
                        RiskRuleKeys.MaxPositionWeight,
                        p.Symbol,
                        p.WeightPct,
                        maxWeight,
                        Math.Max(0m, p.UsdValue - limitUsd),
                        p.WeightPct - maxWeight,
                        PolicyViolationStatus.New));
                }
            }
        }

        if (ruleSet.MaxSleeveWeightPct is { } maxSleeve)
        {
            foreach (var group in book.Positions.GroupBy(p => p.Sleeve))
            {
                var sleeveWeight = group.Sum(p => p.WeightPct);
                if (sleeveWeight > maxSleeve)
                {
                    var sleeveUsd = group.Sum(p => p.UsdValue);
                    var limitUsd = maxSleeve * book.TotalUsd;
                    violations.Add(new PolicyViolation(
                        RiskRuleKeys.MaxSleeveWeight,
                        group.Key,
                        sleeveWeight,
                        maxSleeve,
                        Math.Max(0m, sleeveUsd - limitUsd),
                        sleeveWeight - maxSleeve,
                        PolicyViolationStatus.New));
                }
            }
        }

        if (ruleSet.MinCashBufferPct is { } minCash && book.TotalUsd > 0)
        {
            var cashPct = book.CashUsd / book.TotalUsd;
            if (cashPct < minCash)
            {
                var shortfallUsd = (minCash * book.TotalUsd) - book.CashUsd;
                violations.Add(new PolicyViolation(
                    RiskRuleKeys.MinCashBuffer,
                    "CASH",
                    cashPct,
                    minCash,
                    Math.Max(0m, shortfallUsd),
                    minCash - cashPct,
                    PolicyViolationStatus.New));
            }
        }

        // FR-001c: sleeve weight vs configured allocation target, breaching only past the drift
        // band. ExcessUsd is the rebalancing amount the drift implies (facts; clients attach
        // friction estimates before suggesting a trade). 039: targets come from the IPS (single
        // home), pre-translated to fraction target + symmetric drift band by the caller.
        //
        // Matched on AssetClass (the AssetClassNormalizer bucket — Equities/Bonds/Crypto/...), not
        // Sleeve (the coarse Crypto/Brokerage split MaxPositionWeight/MaxSleeveWeight use): an IPS
        // target's AssetClass essentially never equals "brokerage"/"crypto" literally, so matching
        // against Sleeve made every non-crypto target's observed weight structurally 0 regardless of
        // real holdings (finance-sentry#690).
        if (allocationTargets.Count > 0 && book.TotalUsd > 0)
        {
            var weightByAssetClass = book.Positions
                .GroupBy(p => p.AssetClass, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Sum(p => p.WeightPct), StringComparer.OrdinalIgnoreCase);

            foreach (var target in allocationTargets)
            {
                var assetClass = AssetClassNormalizer.Normalize(target.AssetClass);
                if (!weightByAssetClass.TryGetValue(assetClass, out var actual))
                {
                    // No positions landed in this asset class this run. With a fully-synced book
                    // that is a genuine 0% observation (flag it below). While the book is stale we
                    // cannot tell a real 0% apart from a sync gap that dropped the sleeve's positions
                    // entirely — emitting 0 either way would be a fabricated observation, so skip.
                    if (book.IsStale)
                    {
                        logger?.LogWarning(
                            "Allocation drift for {AssetClass}: book is stale ({StaleSources}); weight is not " +
                            "computable this run — skipping instead of reporting a 0% observation.",
                            assetClass, string.Join(", ", book.StaleSources));
                        continue;
                    }

                    actual = 0m;
                }

                var drift = actual - target.TargetPct;
                if (Math.Abs(drift) > target.DriftBandPct)
                {
                    violations.Add(new PolicyViolation(
                        RiskRuleKeys.AllocationDrift,
                        target.AssetClass,
                        actual,
                        target.TargetPct,
                        Math.Abs(drift) * book.TotalUsd,
                        Math.Abs(drift) - target.DriftBandPct,
                        PolicyViolationStatus.New));
                }
            }
        }

        return violations;
    }

    private static List<PolicyViolation> ApplyAcks(
        IReadOnlyList<PolicyViolation> raw, IReadOnlyList<PolicyViolationAck> acks)
    {
        var result = new List<PolicyViolation>(raw.Count);

        foreach (var violation in raw)
        {
            var ack = acks.SingleOrDefault(a =>
                a.RuleKey == violation.RuleKey &&
                string.Equals(a.Subject, violation.Subject, StringComparison.OrdinalIgnoreCase));

            if (ack is null)
            {
                result.Add(violation);
                continue;
            }

            // MinCashBuffer worsens as cashPct falls (lower = worse); every other rule worsens as
            // the observed value rises (higher = worse). Without this direction flip an acknowledged
            // MinCashBuffer violation that keeps deteriorating never re-opens as Worsened.
            var direction = violation.RuleKey == RiskRuleKeys.MinCashBuffer ? -1m : 1m;
            var worsenedPastStep = direction * (violation.ObservedValue - ack.ObservedAtAck) > ack.WorseningStepPct;
            result.Add(violation with
            {
                Status = worsenedPastStep ? PolicyViolationStatus.Worsened : PolicyViolationStatus.Acknowledged,
                RemediationNote = ack.RemediationNote,
                WorseningStepPct = ack.WorseningStepPct,
            });
        }

        return result;
    }
}
