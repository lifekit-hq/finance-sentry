using FinanceSentry.Core.Domain;
using FinanceSentry.Modules.Risk.Application.Services;
using FinanceSentry.Modules.Risk.Domain;
using FinanceSentry.Modules.Risk.Domain.Ports;
using FluentAssertions;
using Xunit;

namespace FinanceSentry.Modules.Risk.Tests;

public sealed class RiskEvaluationServiceTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private readonly RiskEvaluationService _service = new();

    [Fact]
    public void Evaluate_NoRuleSet_ReturnsNoRulesOnFile_NoInferredViolations()
    {
        var book = new BookSnapshot(15000m, 0m, [new BookPosition("DRAM", RiskSleeve.Brokerage, 100m, 6900m, 0.46m)], false, [], 0m);

        var report = _service.Evaluate(book, null, [], []);

        report.HasRuleSet.Should().BeFalse();
        report.Violations.Should().BeEmpty();
    }

    [Fact]
    public void Evaluate_SeededDram46PercentVs25PercentCap_ProducesExactlyOneViolation()
    {
        // The real seeded case: ~$15k book, one position (DRAM) at ~46%, cap at 25%.
        var book = new BookSnapshot(15000m, 1000m, [new BookPosition("DRAM", RiskSleeve.Brokerage, 100m, 6900m, 0.46m)], false, [], 0m);
        var ruleSet = new RiskRuleSet { UserId = UserId, MaxPositionWeightPct = 0.25m };

        var report = _service.Evaluate(book, ruleSet, [], []);

        report.Violations.Should().ContainSingle();
        var violation = report.Violations.Single();
        violation.RuleKey.Should().Be(RiskRuleKeys.MaxPositionWeight);
        violation.Subject.Should().Be("DRAM");
        violation.ObservedValue.Should().Be(0.46m);
        violation.LimitValue.Should().Be(0.25m);
        violation.ExcessUsd.Should().BeGreaterThan(0m);
        violation.Status.Should().Be(PolicyViolationStatus.New);
    }

    [Fact]
    public void Evaluate_CompliantBook_ReturnsEmptyViolations()
    {
        var book = new BookSnapshot(10000m, 3000m, [new BookPosition("AAPL", RiskSleeve.Brokerage, 10m, 2000m, 0.2m)], false, [], 0m);
        var ruleSet = new RiskRuleSet { UserId = UserId, MaxPositionWeightPct = 0.25m };

        var report = _service.Evaluate(book, ruleSet, [], []);

        report.Violations.Should().BeEmpty();
        report.HasRuleSet.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_MaxSleeveWeightBreach_IsFlagged()
    {
        var book = new BookSnapshot(10000m,
            0m,
            [
                new BookPosition("NVDA", RiskSleeve.Brokerage, 1m, 4000m, 0.4m),
                new BookPosition("AAPL", RiskSleeve.Brokerage, 1m, 4000m, 0.4m),
            ],
            false, [], 0m);
        var ruleSet = new RiskRuleSet { UserId = UserId, MaxSleeveWeightPct = 0.5m };

        var report = _service.Evaluate(book, ruleSet, [], []);

        report.Violations.Should().ContainSingle(v => v.RuleKey == RiskRuleKeys.MaxSleeveWeight && v.Subject == RiskSleeve.Brokerage);
    }

    [Fact]
    public void Evaluate_MinCashBufferBreach_IsFlagged()
    {
        var book = new BookSnapshot(10000m, 200m, [new BookPosition("AAPL", RiskSleeve.Brokerage, 1m, 9800m, 0.98m)], false, [], 0m);
        var ruleSet = new RiskRuleSet { UserId = UserId, MinCashBufferPct = 0.1m };

        var report = _service.Evaluate(book, ruleSet, [], []);

        report.Violations.Should().ContainSingle(v => v.RuleKey == RiskRuleKeys.MinCashBuffer && v.Subject == "CASH");
    }

    // #689: the agent-facing risk check must carry acknowledgement state on each violation so a
    // caller can decide whether to report without re-deriving anything from prior turns.

    [Fact]
    public void Evaluate_UnacknowledgedViolation_IsReportable_AndNotAcknowledged()
    {
        var book = new BookSnapshot(15000m, 1000m, [new BookPosition("DRAM", RiskSleeve.Brokerage, 100m, 6900m, 0.46m)], false, [], 0m);
        var ruleSet = new RiskRuleSet { UserId = UserId, MaxPositionWeightPct = 0.25m };

        var report = _service.Evaluate(book, ruleSet, [], []);

        var violation = report.Violations.Should().ContainSingle().Subject;
        violation.Reportable.Should().BeTrue();
        violation.IsAcknowledged.Should().BeFalse();
        violation.HasWorsenedPastStep.Should().BeFalse();
        violation.WorseningStepPct.Should().BeNull();
    }

    [Fact]
    public void Evaluate_AcknowledgedViolation_NotWorsened_IsNotReportable_ButStillVisibleInPolicyView()
    {
        var book = new BookSnapshot(15000m, 1000m, [new BookPosition("DRAM", RiskSleeve.Brokerage, 100m, 6900m, 0.46m)], false, [], 0m);
        var ruleSet = new RiskRuleSet { UserId = UserId, MaxPositionWeightPct = 0.25m };
        var ack = new PolicyViolationAck
        {
            UserId = UserId,
            RuleKey = RiskRuleKeys.MaxPositionWeight,
            Subject = "DRAM",
            RemediationNote = "trim DRAM on strength to <=30% by Q4",
            ObservedAtAck = 0.46m,
            WorseningStepPct = 0.05m,
        };

        var report = _service.Evaluate(book, ruleSet, [], [ack]);

        // Still in the policy view — acknowledging never hides a breach.
        var violation = report.Violations.Should().ContainSingle().Subject;
        violation.Status.Should().Be(PolicyViolationStatus.Acknowledged);
        violation.IsAcknowledged.Should().BeTrue();
        violation.WorseningStepPct.Should().Be(0.05m);
        violation.HasWorsenedPastStep.Should().BeFalse();

        // But it must not be re-reported.
        violation.Reportable.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_AcknowledgedViolation_WorsenedPastStep_IsReportableAgain()
    {
        var book = new BookSnapshot(15000m, 1000m, [new BookPosition("DRAM", RiskSleeve.Brokerage, 100m, 8000m, 0.55m)], false, [], 0m);
        var ruleSet = new RiskRuleSet { UserId = UserId, MaxPositionWeightPct = 0.25m };
        var ack = new PolicyViolationAck
        {
            UserId = UserId,
            RuleKey = RiskRuleKeys.MaxPositionWeight,
            Subject = "DRAM",
            RemediationNote = "trim DRAM on strength to <=30% by Q4",
            ObservedAtAck = 0.46m,
            WorseningStepPct = 0.05m,
        };

        var report = _service.Evaluate(book, ruleSet, [], [ack]);

        var violation = report.Violations.Should().ContainSingle().Subject;
        violation.Status.Should().Be(PolicyViolationStatus.Worsened);
        violation.IsAcknowledged.Should().BeTrue();
        violation.HasWorsenedPastStep.Should().BeTrue();
        violation.Reportable.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_AcknowledgedViolation_ReportsAcknowledged_NotNew()
    {
        var book = new BookSnapshot(15000m, 1000m, [new BookPosition("DRAM", RiskSleeve.Brokerage, 100m, 6900m, 0.46m)], false, [], 0m);
        var ruleSet = new RiskRuleSet { UserId = UserId, MaxPositionWeightPct = 0.25m };
        var ack = new PolicyViolationAck
        {
            UserId = UserId,
            RuleKey = RiskRuleKeys.MaxPositionWeight,
            Subject = "DRAM",
            RemediationNote = "trim DRAM on strength to <=30% by Q4",
            ObservedAtAck = 0.46m,
            WorseningStepPct = 0.05m,
        };

        var report = _service.Evaluate(book, ruleSet, [], [ack]);

        var violation = report.Violations.Should().ContainSingle().Subject;
        violation.Status.Should().Be(PolicyViolationStatus.Acknowledged);
        violation.RemediationNote.Should().Be(ack.RemediationNote);
    }

    [Fact]
    public void Evaluate_AcknowledgedViolation_WorsensPastStep_ReopensAsWorsened()
    {
        var book = new BookSnapshot(15000m, 1000m, [new BookPosition("DRAM", RiskSleeve.Brokerage, 100m, 8000m, 0.55m)], false, [], 0m);
        var ruleSet = new RiskRuleSet { UserId = UserId, MaxPositionWeightPct = 0.25m };
        var ack = new PolicyViolationAck
        {
            UserId = UserId,
            RuleKey = RiskRuleKeys.MaxPositionWeight,
            Subject = "DRAM",
            RemediationNote = "trim DRAM on strength to <=30% by Q4",
            ObservedAtAck = 0.46m,
            WorseningStepPct = 0.05m,
        };

        var report = _service.Evaluate(book, ruleSet, [], [ack]);

        report.Violations.Should().ContainSingle().Which.Status.Should().Be(PolicyViolationStatus.Worsened);
    }

    // MinCashBuffer worsening-direction regression tests (issue #417).
    // The old check used `observed - observedAtAck > step` which is correct for MAX rules
    // (higher = worse) but inverted for MinCashBuffer (lower = worse): a deteriorating cash
    // position always produced a negative delta that could never exceed a positive step, so the
    // violation stayed Acknowledged forever instead of re-opening as Worsened.

    [Fact]
    public void Evaluate_MinCashBuffer_Acknowledged_WorsensSignificantlyBelow_ReopensAsWorsened()
    {
        // cashPct drops from 3 % (acked) to 1 % — 2 pp deterioration exceeds the 1 pp step.
        var book = new BookSnapshot(10000m, 100m, [new BookPosition("AAPL", RiskSleeve.Brokerage, 1m, 9900m, 0.99m)], false, [], 0m);
        var ruleSet = new RiskRuleSet { UserId = UserId, MinCashBufferPct = 0.05m };
        var ack = new PolicyViolationAck
        {
            UserId = UserId,
            RuleKey = RiskRuleKeys.MinCashBuffer,
            Subject = "CASH",
            RemediationNote = "liquidate a small position to rebuild cash",
            ObservedAtAck = 0.03m,
            WorseningStepPct = 0.01m,
        };

        var report = _service.Evaluate(book, ruleSet, [], [ack]);

        report.Violations.Should().ContainSingle().Which.Status.Should().Be(PolicyViolationStatus.Worsened);
    }

    [Fact]
    public void Evaluate_MinCashBuffer_Acknowledged_SameLevel_RemainsAcknowledged()
    {
        // cashPct unchanged at 3 % — no worsening, must stay Acknowledged.
        var book = new BookSnapshot(10000m, 300m, [new BookPosition("AAPL", RiskSleeve.Brokerage, 1m, 9700m, 0.97m)], false, [], 0m);
        var ruleSet = new RiskRuleSet { UserId = UserId, MinCashBufferPct = 0.05m };
        var ack = new PolicyViolationAck
        {
            UserId = UserId,
            RuleKey = RiskRuleKeys.MinCashBuffer,
            Subject = "CASH",
            ObservedAtAck = 0.03m,
            WorseningStepPct = 0.01m,
        };

        var report = _service.Evaluate(book, ruleSet, [], [ack]);

        report.Violations.Should().ContainSingle().Which.Status.Should().Be(PolicyViolationStatus.Acknowledged);
    }

    [Fact]
    public void Evaluate_MinCashBuffer_Acknowledged_Improves_RemainsAcknowledged()
    {
        // cashPct rises from 2 % (acked) to 4 % — still violating min=5 % but improving.
        // Regression guard: the pre-fix inverted check treated rising cashPct as worsening
        // and would have emitted Worsened here (0.04 - 0.02 = 0.02 > 0.01 step).
        var book = new BookSnapshot(10000m, 400m, [new BookPosition("AAPL", RiskSleeve.Brokerage, 1m, 9600m, 0.96m)], false, [], 0m);
        var ruleSet = new RiskRuleSet { UserId = UserId, MinCashBufferPct = 0.05m };
        var ack = new PolicyViolationAck
        {
            UserId = UserId,
            RuleKey = RiskRuleKeys.MinCashBuffer,
            Subject = "CASH",
            ObservedAtAck = 0.02m,
            WorseningStepPct = 0.01m,
        };

        var report = _service.Evaluate(book, ruleSet, [], [ack]);

        report.Violations.Should().ContainSingle().Which.Status.Should().Be(PolicyViolationStatus.Acknowledged);
    }

    [Fact]
    public void Evaluate_StaleBook_FlagsReportStale_ButDoesNotAutoClearViolations()
    {
        var book = new BookSnapshot(15000m, 1000m, [new BookPosition("DRAM", RiskSleeve.Brokerage, 100m, 6900m, 0.46m)], true, ["brokerage"], 0m);
        var ruleSet = new RiskRuleSet { UserId = UserId, MaxPositionWeightPct = 0.25m };

        var report = _service.Evaluate(book, ruleSet, [], []);

        report.IsStale.Should().BeTrue();
        report.Violations.Should().ContainSingle();
    }

    // 039 (US2/SC-002): allocation-drift verdicts are now produced from the target allocation passed
    // in from its single home (the IPS), not a copy on the rule set. The comparison and emitted
    // violation fields are unchanged — a sleeve past its drift band flags exactly as before.
    [Fact]
    public void Evaluate_SleeveDriftPastBand_FlagsAllocationDrift_FromInjectedTargets()
    {
        var book = new BookSnapshot(
            10000m, 0m,
            [new BookPosition("DRAM", RiskSleeve.Brokerage, 100m, 4600m, 0.46m)],
            false, [], 0m);
        var ruleSet = new RiskRuleSet { UserId = UserId, MaxPositionWeightPct = 0.50m };
        AllocationDriftTarget[] targets = [new(RiskSleeve.Brokerage, 0.30m, 0.05m)];

        var report = _service.Evaluate(book, ruleSet, targets, []);

        var drift = report.Violations.Should()
            .ContainSingle(v => v.RuleKey == RiskRuleKeys.AllocationDrift).Subject;
        drift.Subject.Should().Be(RiskSleeve.Brokerage);
        drift.ObservedValue.Should().Be(0.46m);
        drift.LimitValue.Should().Be(0.30m);
        drift.ExcessUsd.Should().BeApproximately(0.16m * 10000m, 0.01m);
    }

    [Fact]
    public void Evaluate_SleeveWithinBand_NoAllocationDrift_FromInjectedTargets()
    {
        var book = new BookSnapshot(
            10000m, 0m,
            [new BookPosition("DRAM", RiskSleeve.Brokerage, 100m, 3200m, 0.32m)],
            false, [], 0m);
        var ruleSet = new RiskRuleSet { UserId = UserId, MaxPositionWeightPct = 0.50m };
        AllocationDriftTarget[] targets = [new(RiskSleeve.Brokerage, 0.30m, 0.05m)];

        var report = _service.Evaluate(book, ruleSet, targets, []);

        report.Violations.Should().NotContain(v => v.RuleKey == RiskRuleKeys.AllocationDrift);
    }

    [Fact]
    public void Evaluate_NoInjectedTargets_EmitsNoAllocationDrift()
    {
        var book = new BookSnapshot(
            10000m, 0m,
            [new BookPosition("DRAM", RiskSleeve.Brokerage, 100m, 4600m, 0.46m)],
            false, [], 0m);
        var ruleSet = new RiskRuleSet { UserId = UserId, MaxPositionWeightPct = 0.50m };

        var report = _service.Evaluate(book, ruleSet, [], []);

        report.Violations.Should().NotContain(v => v.RuleKey == RiskRuleKeys.AllocationDrift);
    }

    // finance-sentry#690: a target's AssetClass (the IPS taxonomy — Equities/Bonds/Crypto/...) is a
    // different axis from the coarse RiskSleeve grouping (brokerage/crypto) the concentration rules
    // use. Matching drift targets against Sleeve — as this rule used to — meant a real, non-empty
    // "Equities" sleeve could never match the literal string "brokerage", so its observed weight was
    // silently reported as 0 regardless of what was actually held.
    [Fact]
    public void Evaluate_NonCryptoSleeveHoldsRealPosition_ReportsItsActualWeight_NotZero()
    {
        var book = new BookSnapshot(
            10000m, 0m,
            [new BookPosition("DRAM", RiskSleeve.Brokerage, 100m, 8000m, 0.80m, AssetClassNormalizer.Equities)],
            false, [], 0m);
        var ruleSet = new RiskRuleSet { UserId = UserId, MaxPositionWeightPct = 0.90m };
        AllocationDriftTarget[] targets = [new(AssetClassNormalizer.Equities, 0.30m, 0.05m)];

        var report = _service.Evaluate(book, ruleSet, targets, []);

        var drift = report.Violations.Should()
            .ContainSingle(v => v.RuleKey == RiskRuleKeys.AllocationDrift).Subject;
        drift.ObservedValue.Should().Be(0.80m, "the Equities sleeve is 80% of the book, not empty");
    }

    [Fact]
    public void Evaluate_TargetAssetClassAbsentFromBook_BookStale_SkipsRatherThanReportingZero()
    {
        var book = new BookSnapshot(
            10000m, 0m,
            [new BookPosition("BTC", RiskSleeve.Crypto, 1m, 10000m, 1.00m, AssetClassNormalizer.Crypto)],
            true, ["ibkr"], 0m);
        var ruleSet = new RiskRuleSet { UserId = UserId, MaxPositionWeightPct = 1.00m };
        AllocationDriftTarget[] targets = [new(AssetClassNormalizer.Equities, 0.30m, 0.05m)];

        var report = _service.Evaluate(book, ruleSet, targets, []);

        report.Violations.Should().NotContain(v => v.RuleKey == RiskRuleKeys.AllocationDrift,
            "Equities isn't in this run's positions while a source is stale — that's a data gap, not a computed 0%");
    }

    [Fact]
    public void Evaluate_TargetAssetClassAbsentFromBook_BookFresh_FlagsGenuineZeroWeight()
    {
        var book = new BookSnapshot(
            10000m, 0m,
            [new BookPosition("BTC", RiskSleeve.Crypto, 1m, 10000m, 1.00m, AssetClassNormalizer.Crypto)],
            false, [], 0m);
        var ruleSet = new RiskRuleSet { UserId = UserId, MaxPositionWeightPct = 1.00m };
        AllocationDriftTarget[] targets = [new(AssetClassNormalizer.Equities, 0.30m, 0.05m)];

        var report = _service.Evaluate(book, ruleSet, targets, []);

        var drift = report.Violations.Should()
            .ContainSingle(v => v.RuleKey == RiskRuleKeys.AllocationDrift).Subject;
        drift.ObservedValue.Should().Be(0m, "a fully-synced book with no Equities positions really is 0%");
    }

    [Fact]
    public void EvaluateProposal_NoRuleSet_ReturnsAllowed()
    {
        var book = new BookSnapshot(10000m, 5000m, [], false, [], 0m);

        var verdict = _service.EvaluateProposal(book, null, "NVDA", 1000m, 0);

        verdict.Decision.Should().Be(RiskDecision.Allowed);
    }

    [Fact]
    public void EvaluateProposal_WithinLimits_ReturnsAllowedWithHeadroom()
    {
        var book = new BookSnapshot(10000m, 5000m, [], false, [], 0m);
        var ruleSet = new RiskRuleSet { UserId = UserId, MaxPositionWeightPct = 0.25m };

        var verdict = _service.EvaluateProposal(book, ruleSet, "NVDA", 500m, 0);

        verdict.Decision.Should().Be(RiskDecision.Allowed);
        verdict.HeadroomUsd.Should().NotBeNull();
    }

    [Fact]
    public void EvaluateProposal_BreachesMaxPositionWeight_ReturnsRefusedWithMaxCompliantSize()
    {
        var book = new BookSnapshot(10000m, 5000m, [], false, [], 0m);
        var ruleSet = new RiskRuleSet { UserId = UserId, MaxPositionWeightPct = 0.25m };

        var verdict = _service.EvaluateProposal(book, ruleSet, "NVDA", 5000m, 0);

        verdict.Decision.Should().Be(RiskDecision.Refused);
        verdict.RuleKey.Should().Be(RiskRuleKeys.MaxPositionWeight);
        verdict.MaxCompliantSizeUsd.Should().NotBeNull();
        verdict.MaxCompliantSizeUsd!.Value.Should().BeLessThan(5000m);
    }

    [Fact]
    public void EvaluateProposal_BreachesMinCashBuffer_ReturnsRefused()
    {
        var book = new BookSnapshot(10000m, 1500m, [], false, [], 0m);
        var ruleSet = new RiskRuleSet { UserId = UserId, MinCashBufferPct = 0.1m };

        var verdict = _service.EvaluateProposal(book, ruleSet, "NVDA", 1000m, 0);

        verdict.Decision.Should().Be(RiskDecision.Refused);
        verdict.RuleKey.Should().Be(RiskRuleKeys.MinCashBuffer);
    }

    [Fact]
    public void EvaluateProposal_Paper_BreachesMinCashBuffer_StillAllowed()
    {
        var book = new BookSnapshot(10000m, 1500m, [], false, [], 0m);
        var ruleSet = new RiskRuleSet { UserId = UserId, MinCashBufferPct = 0.1m };

        var verdict = _service.EvaluateProposal(book, ruleSet, "NVDA", 1000m, 0, isPaper: true);

        verdict.Decision.Should().Be(RiskDecision.Allowed, "paper/tracking promotions never draw down real cash (finance-sentry#704)");
    }

    [Fact]
    public void EvaluateProposal_Paper_BreachesMaxPositionWeight_StillRefused()
    {
        var book = new BookSnapshot(10000m, 1500m, [], false, [], 0m);
        var ruleSet = new RiskRuleSet { UserId = UserId, MaxPositionWeightPct = 0.25m, MinCashBufferPct = 0.5m };

        var verdict = _service.EvaluateProposal(book, ruleSet, "NVDA", 5000m, 0, isPaper: true);

        verdict.Decision.Should().Be(RiskDecision.Refused, "concentration rules still apply to paper promotions");
        verdict.RuleKey.Should().Be(RiskRuleKeys.MaxPositionWeight);
    }

    [Fact]
    public void EvaluateProposal_BreachesMaxNewPosition_ReturnsRefused()
    {
        var book = new BookSnapshot(10000m, 5000m, [], false, [], 0m);
        var ruleSet = new RiskRuleSet { UserId = UserId, MaxNewPositionPct = 0.1m };

        var verdict = _service.EvaluateProposal(book, ruleSet, "NVDA", 5000m, 0);

        verdict.Decision.Should().Be(RiskDecision.Refused);
        verdict.RuleKey.Should().Be(RiskRuleKeys.MaxNewPosition);
    }

    [Fact]
    public void EvaluateProposal_TurnoverBudgetAtCap_ReturnsRefusedTurnover()
    {
        var book = new BookSnapshot(10000m, 5000m, [], false, [], 0m);
        var ruleSet = new RiskRuleSet { UserId = UserId, TurnoverBudgetPerQuarter = 5 };

        var verdict = _service.EvaluateProposal(book, ruleSet, "NVDA", 100m, turnoverCountThisQuarter: 5);

        verdict.Decision.Should().Be(RiskDecision.Refused);
        verdict.RuleKey.Should().Be(RiskRuleKeys.Turnover);
    }
}
