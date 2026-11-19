namespace FinanceSentry.Modules.Research.Domain.Scoring;

using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain.ThesisMonitor;

/// <summary>
/// Pure, deterministic proposal of invalidation triggers from a scorecard, offered at promotion
/// time (US3/T021). Proposes revenue/margin triggers only when the underlying fundamentals were
/// evaluable — never invents a threshold from missing data. It never proposes a price trigger: a price breach opens a
/// review and cannot break a thesis, so a price drawdown is not offered as an exit condition.
/// </summary>
public static class TriggerPrefill
{
    private const int DrawdownRoundingDecimals = 4;

    // Spec 019 FR-011: fundamentals triggers require 2 consecutive quarters — one soft print
    // must never kill a thesis.
    private const int FundamentalsConsecutiveQuarters = 2;
    private const string RevenueYoyRationale = "Thesis assumes continued revenue growth; break if YoY growth turns negative.";
    private const string GrossMarginRationale = "Break if gross margin compresses below the latest observed level, minus a buffer.";

    public static IReadOnlyList<ProposedTrigger> Propose(
        FundamentalsScoreDetail fundamentals, OpportunityOptions options)
    {
        var triggers = new List<ProposedTrigger>();

        if (fundamentals.RevenueYoy is { } revenueYoy && revenueYoy > 0)
        {
            triggers.Add(new ProposedTrigger(
                ThesisMetric.RevenueYoy,
                "lessThan",
                0m,
                ProxyTicker: null,
                ConsecutivePeriods: FundamentalsConsecutiveQuarters,
                RevenueYoyRationale));
        }

        if (fundamentals.GrossMarginLatest is { } marginLatest)
        {
            var threshold = Math.Round(marginLatest - options.GrossMarginPrefillBuffer, DrawdownRoundingDecimals);
            triggers.Add(new ProposedTrigger(
                ThesisMetric.GrossMargin,
                "lessThan",
                threshold,
                ProxyTicker: null,
                ConsecutivePeriods: FundamentalsConsecutiveQuarters,
                GrossMarginRationale));
        }

        return triggers;
    }
}

/// <summary>The subset of a fundamentals score the trigger prefill needs — decoupled from the full scorecard.</summary>
public sealed record FundamentalsScoreDetail(decimal? RevenueYoy, decimal? GrossMarginLatest);
