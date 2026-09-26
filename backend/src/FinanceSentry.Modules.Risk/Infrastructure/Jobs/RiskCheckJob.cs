namespace FinanceSentry.Modules.Risk.Infrastructure.Jobs;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Risk.Application.Services;
using FinanceSentry.Modules.Risk.Domain;
using FinanceSentry.Modules.Risk.Domain.Ports;
using FinanceSentry.Modules.Risk.Domain.Repositories;
using Hangfire;
using Microsoft.Extensions.Options;

/// <summary>
/// Daily (after sync) check of the live book against the active RiskRuleSet (FR-002). Writes a
/// HoldingSnapshot row per position, raises Alerts + radar_signals for violations, and flags
/// adds-to-broken-thesis (FR-006). Deterministic only — no LLM, no composite score (FR-008).
/// </summary>
public sealed class RiskCheckJob(
    IBankingTotalsReader bankingTotals,
    IBookSnapshotReader bookReader,
    IRiskRuleSetRepository ruleSetRepo,
    IPolicyViolationAckRepository ackRepo,
    IHoldingSnapshotRepository snapshotRepo,
    IRiskEvaluationService evaluationService,
    IAllocationPolicySource allocationPolicySource,
    ITurnoverTracker turnoverTracker,
    IAddToBrokenThesisDetector brokenThesisDetector,
    IBrokenThesisReader brokenThesisReader,
    IAlertGeneratorService alertGenerator,
    IRadarSignalWriter signalWriter,
    IOptions<RiskOptions> options)
{
    private readonly int _rollingQuarterDays = options.Value.RollingQuarterDays;

    [AutomaticRetry(Attempts = 2)]
    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        var userIds = await bankingTotals.GetActiveUserIdsAsync(ct);
        foreach (var userId in userIds)
        {
            await CheckForUserAsync(userId, ct);
        }
    }

    public async Task CheckForUserAsync(Guid userId, CancellationToken ct = default)
    {
        var book = await bookReader.ReadAsync(userId, ct);
        var now = DateTimeOffset.UtcNow;

        await snapshotRepo.AddRangeAsync(
            book.Positions
                .Select(p => new HoldingSnapshot
                {
                    UserId = userId,
                    Symbol = p.Symbol,
                    Sleeve = p.Sleeve,
                    Quantity = p.Quantity,
                    UsdValue = p.UsdValue,
                    CapturedAt = now,
                })
                .ToList(),
            ct);

        var ruleSet = await ruleSetRepo.GetCurrentAsync(userId, ct);
        var acks = await ackRepo.ListActiveAsync(userId, ct);
        var allocationTargets = await allocationPolicySource.GetAllocationTargetsAsync(userId, ct);
        var report = evaluationService.Evaluate(book, ruleSet, allocationTargets, acks, now);

        await EmitComplianceSignalsAsync(userId, report, ct);
        await ResolveClearedViolationsAsync(userId, book, report, ct);
        await CheckTurnoverBudgetAsync(userId, ruleSet, now, ct);
        await DetectAddToBrokenThesisAsync(userId, now, ct);
    }

    /// <summary>
    /// FR-002/FR-011-style hygiene: a violation that has cleared in fresh data resolves its active
    /// Alert. Resolving a subject with no active alert is a no-op, so this simply sweeps every
    /// currently-compliant subject. Never runs on stale book data — a violation must not
    /// "auto-clear" because a sync died (spec edge case).
    /// </summary>
    private async Task ResolveClearedViolationsAsync(
        Guid userId, BookSnapshot book, ComplianceReport report, CancellationToken ct)
    {
        if (!report.HasRuleSet || report.IsStale)
        {
            return;
        }

        var violated = report.Violations
            .Select(v => (v.RuleKey, Subject: v.Subject.ToUpperInvariant()))
            .ToHashSet();

        foreach (var position in book.Positions)
        {
            if (!violated.Contains((RiskRuleKeys.MaxPositionWeight, position.Symbol.ToUpperInvariant())))
            {
                await alertGenerator.ResolvePolicyViolationAlertAsync(
                    userId, RiskRuleKeys.MaxPositionWeight, position.Symbol, ct);
            }
        }

        foreach (var sleeve in book.Positions.Select(p => p.Sleeve).Distinct())
        {
            if (!violated.Contains((RiskRuleKeys.MaxSleeveWeight, sleeve.ToUpperInvariant())))
            {
                await alertGenerator.ResolvePolicyViolationAlertAsync(
                    userId, RiskRuleKeys.MaxSleeveWeight, sleeve, ct);
            }
        }

        if (!violated.Contains((RiskRuleKeys.MinCashBuffer, "CASH")))
        {
            await alertGenerator.ResolvePolicyViolationAlertAsync(
                userId, RiskRuleKeys.MinCashBuffer, "CASH", ct);
        }
    }

    /// <summary>
    /// FR-001b: the scheduled check flags when the rolling-quarter discretionary trade count has
    /// reached the configured budget — one notable signal + Alert per user per calendar quarter.
    /// </summary>
    private async Task CheckTurnoverBudgetAsync(
        Guid userId, RiskRuleSet? ruleSet, DateTimeOffset now, CancellationToken ct)
    {
        if (ruleSet?.TurnoverBudgetPerQuarter is not { } budget)
        {
            return;
        }

        var history = await snapshotRepo.ListSinceAsync(userId, now - TimeSpan.FromDays(_rollingQuarterDays), ct);
        var count = turnoverTracker.CountDiscretionaryTradesInRollingQuarter(history, now);
        if (count < budget)
        {
            return;
        }

        var quarter = $"{now.Year}-Q{(now.Month - 1) / 3 + 1}";
        var appended = await signalWriter.AppendSignalAsync(new RadarSignalRequest(
            Scanner: "risk_rules",
            SignalType: "turnover_budget_reached",
            Severity: SignalSeverity.Notable,
            SubjectType: "User",
            Subject: userId.ToString(),
            UserId: userId,
            DedupKey: $"risk_rules:turnover_budget_reached:{userId}:{quarter}",
            Payload: new { count, budget, quarter },
            OneTime: true), ct);

        if (appended)
        {
            await alertGenerator.GeneratePolicyViolationAlertAsync(
                userId, RiskRuleKeys.Turnover, quarter, count, budget, isOverride: false, ct);
        }
    }

    private async Task EmitComplianceSignalsAsync(Guid userId, ComplianceReport report, CancellationToken ct)
    {
        if (!report.HasRuleSet)
        {
            await signalWriter.AppendSignalAsync(new RadarSignalRequest(
                Scanner: "risk_rules",
                SignalType: "no_rules_configured",
                Severity: SignalSeverity.Info,
                SubjectType: "User",
                Subject: userId.ToString(),
                UserId: userId,
                // Spec edge case: a ONE-TIME setup nudge, not a daily nag.
                DedupKey: $"risk_rules:no_rules:{userId}",
                Payload: new { },
                OneTime: true), ct);
            return;
        }

        var alertWorthy = report.Violations
            .Where(v => v.Reportable)
            .ToList();

        foreach (var violation in alertWorthy)
        {
            await alertGenerator.GeneratePolicyViolationAlertAsync(
                userId, violation.RuleKey, violation.Subject, violation.ObservedValue, violation.LimitValue,
                isOverride: false, ct);

            await signalWriter.AppendSignalAsync(new RadarSignalRequest(
                Scanner: "risk_rules",
                SignalType: "policy_violation",
                Severity: SignalSeverity.Alerted,
                SubjectType: "Ticker",
                Subject: violation.Subject,
                UserId: userId,
                DedupKey: $"risk_rules:policy_violation:{userId}:{violation.RuleKey}:{violation.Subject}:{report.GeneratedAt:yyyy-MM-dd}",
                Payload: new
                {
                    violation.RuleKey,
                    violation.ObservedValue,
                    violation.LimitValue,
                    violation.ExcessUsd,
                    violation.Status,
                }), ct);
        }

        if (report.Violations.Count == 0)
        {
            await signalWriter.AppendSignalAsync(new RadarSignalRequest(
                Scanner: "risk_rules",
                SignalType: "compliant",
                Severity: SignalSeverity.Info,
                SubjectType: "User",
                Subject: userId.ToString(),
                UserId: userId,
                DedupKey: $"risk_rules:compliant:{userId}:{report.GeneratedAt:yyyy-MM-dd}",
                Payload: new { }), ct);
        }
    }

    private async Task DetectAddToBrokenThesisAsync(Guid userId, DateTimeOffset now, CancellationToken ct)
    {
        var brokenTheses = await brokenThesisReader.ListBrokenAsync(userId, ct);
        if (brokenTheses.Count == 0)
        {
            return;
        }

        var history = await snapshotRepo.ListSinceAsync(userId, now - TimeSpan.FromDays(_rollingQuarterDays), ct);
        var flags = brokenThesisDetector.Detect(history, brokenTheses);

        foreach (var flag in flags)
        {
            // One flag per historical add event: the signal's OneTime dedup (keyed on the add
            // date) decides freshness, and the Alert fires only for a fresh flag — a 90-day-old
            // add must not re-alert every day for 90 days.
            var appended = await signalWriter.AppendSignalAsync(new RadarSignalRequest(
                Scanner: "risk_rules",
                SignalType: "add_to_broken_thesis",
                Severity: SignalSeverity.Notable,
                SubjectType: "Ticker",
                Subject: flag.Ticker,
                UserId: userId,
                DedupKey: $"risk_rules:add_to_broken_thesis:{userId}:{flag.Ticker}:{flag.IncreasedAt:yyyy-MM-dd}",
                Payload: new
                {
                    flag.FromQuantity,
                    flag.ToQuantity,
                    flag.IncreasedAt,
                },
                OneTime: true), ct);

            if (appended)
            {
                await alertGenerator.GeneratePolicyViolationAlertAsync(
                    userId, RiskRuleKeys.AddToBrokenThesis, flag.Ticker, flag.ToQuantity, flag.FromQuantity,
                    isOverride: false, ct);
            }
        }
    }
}
