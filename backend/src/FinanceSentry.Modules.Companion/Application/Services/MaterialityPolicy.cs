namespace FinanceSentry.Modules.Companion.Application.Services;

using FinanceSentry.Core.Connections;
using FinanceSentry.Modules.Companion.Domain;

/// <summary>
/// Default materiality policy (feature 031). An alert is already "worth surfacing" (the alerts module
/// decided so) — we map its type to a companion kind and skip types we don't surface. Disposition
/// follows the mode: quiet suppresses, digest holds, scan/realtime queue as pending. Sync failures are
/// demoted below the mode: they ride the digest unless the source is genuinely stale. For a provider the
/// connection-health policy drives, the class the alert was raised with decides instead (slice S5): a confirmed
/// credential loss (ActionRequired) is relayed at once in every mode, Quiet included; a Failing outage keeps the
/// sync-failure rule; a Dormant notice is one digest line.
/// </summary>
public sealed class MaterialityPolicy : IMaterialityPolicy
{
    // A transient sync error re-alerts every 12h per account and was 313 of 344 alerts over 90 days.
    // Only a source that has not synced successfully for a full day is worth a proactive wake.
    public TimeSpan SyncFailureEscalationAge { get; } = TimeSpan.FromHours(24);

    // Alert type strings are the cross-module contract value carried on each alert (Alerts module).
    public CompanionEventKind? ClassifyAlert(string alertType) => alertType switch
    {
        "PolicyViolation" => CompanionEventKind.RiskViolation,
        "SyncFailure" => CompanionEventKind.SyncFailure,
        "UnusualSpend" => CompanionEventKind.UnusualSpend,
        "Opportunity" => CompanionEventKind.Opportunity,
        "ThesisBroken" => CompanionEventKind.ThesisBreak,
        "MarketStructure" => CompanionEventKind.MarketStructure,
        "LowBalance" => CompanionEventKind.LowBalance,
        "CashShortfall" => CompanionEventKind.CashShortfall,
        "ConsentExpiring" => CompanionEventKind.ConsentExpiring,
        "JobFailure" => CompanionEventKind.OperationalFailure,
        "PriceHike" => CompanionEventKind.PriceHike,
        "DuplicateCharge" => CompanionEventKind.DuplicateCharge,
        "CategorySpike" => CompanionEventKind.CategorySpike,
        "FxSpread" => CompanionEventKind.FxSpread,
        "PerformanceBrief" => CompanionEventKind.PerformanceBrief,
        "RebalanceProposal" => CompanionEventKind.RebalanceProposal,
        "CashSweepProposal" => CompanionEventKind.CashSweepProposal,
        "EarningsAhead" => CompanionEventKind.EarningsAhead,
        "FilingLanded" => CompanionEventKind.FilingLanded,
        "NewsCluster" => CompanionEventKind.NewsCluster,
        "BudgetBreach" => CompanionEventKind.BudgetBreach,
        "FamilyStatement" => CompanionEventKind.FamilyStatement,
        "FireBrief" => CompanionEventKind.FireBrief,
        "PolicyReview" => CompanionEventKind.PolicyReview,
        "PolicyReviewMissed" => CompanionEventKind.PolicyReviewMissed,
        "RelativeUnderperformance" => CompanionEventKind.RelativeUnderperformance,
        "MacroEventAhead" => CompanionEventKind.MacroEventAhead,
        "MacroEventReleased" => CompanionEventKind.MacroEventReleased,
        "ConnectionDormant" => CompanionEventKind.ConnectionDormant,
        "AccountRetired" => CompanionEventKind.AccountRetired,
        "CoverageGap" => CompanionEventKind.CoverageGap,
        _ => null,
    };

    public EventDisposition DispositionForMode(NotificationMode mode) => mode switch
    {
        NotificationMode.Quiet => EventDisposition.SuppressedByMode,
        NotificationMode.Digest => EventDisposition.HeldForDigest,
        NotificationMode.Scan => EventDisposition.Pending,
        NotificationMode.Realtime => EventDisposition.Pending,
        _ => EventDisposition.Pending,
    };

    public EventDisposition DispositionFor(
        NotificationMode mode, CompanionEventKind kind, TimeSpan? sourceStaleness = null, string? healthState = null)
    {
        // Only the person can fix a lost credential, so the reconnect ask is never held or silenced (decided 2026-10-06:
        // a confirmed credential loss goes out proactively, and Quiet still surfaces it).
        if (kind == CompanionEventKind.SyncFailure && healthState == nameof(ConnectionHealthState.ActionRequired))
            return EventDisposition.Pending;

        // A Dormant connection is told once, in the digest; Quiet keeps it silent like any other digest line.
        // A retired account is told once, in the digest, the same way.
        if (kind is CompanionEventKind.ConnectionDormant or CompanionEventKind.AccountRetired)
            return mode == NotificationMode.Quiet ? EventDisposition.SuppressedByMode : EventDisposition.HeldForDigest;

        // Operational failures must reach the operator even under a quiet mode — surface instead of
        // suppressing. Digest still batches them; the other modes already queue as pending.
        if (kind == CompanionEventKind.OperationalFailure && mode == NotificationMode.Quiet)
            return EventDisposition.Pending;

        // Sync failures are digest material unless the source is stale past the escalation age.
        // Unknown staleness counts as not stale — the digest (and the agent's pulse) still sees it.
        // Quiet keeps suppressing: a held event under quiet would surface what the mode silenced.
        if (kind == CompanionEventKind.SyncFailure && mode != NotificationMode.Quiet && !IsStale(sourceStaleness))
            return EventDisposition.HeldForDigest;

        return DispositionForMode(mode);
    }

    private bool IsStale(TimeSpan? sourceStaleness)
        => sourceStaleness is { } age && age > SyncFailureEscalationAge;

    private const string AlertKeyPrefix = "alert:";

    public string AlertDedupKey(Guid alertId) => $"{AlertKeyPrefix}{alertId}";

    public Guid? AlertIdFromDedupKey(string dedupKey)
        => dedupKey.StartsWith(AlertKeyPrefix, StringComparison.Ordinal)
           && Guid.TryParse(dedupKey.AsSpan(AlertKeyPrefix.Length), out var alertId)
            ? alertId
            : null;

    public string AnalystDedupKey(Guid userId, Guid analystActionId) => $"analyst:{userId}:{analystActionId}";
}
