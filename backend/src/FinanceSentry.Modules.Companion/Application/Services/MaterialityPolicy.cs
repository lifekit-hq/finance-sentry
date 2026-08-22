namespace FinanceSentry.Modules.Companion.Application.Services;

using FinanceSentry.Modules.Companion.Domain;

/// <summary>
/// Default materiality policy (feature 031). An alert is already "worth surfacing" (the alerts module
/// decided so) — we map its type to a companion kind and skip types we don't surface. Disposition
/// follows the mode: quiet suppresses, digest holds, scan/realtime queue as pending.
/// </summary>
public sealed class MaterialityPolicy : IMaterialityPolicy
{
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

    public EventDisposition DispositionFor(NotificationMode mode, CompanionEventKind kind)
    {
        // Operational failures must reach the operator even under a quiet mode — surface instead of
        // suppressing. Digest still batches them; the other modes already queue as pending.
        if (kind == CompanionEventKind.OperationalFailure && mode == NotificationMode.Quiet)
            return EventDisposition.Pending;

        return DispositionForMode(mode);
    }

    public string AlertDedupKey(Guid alertId) => $"alert:{alertId}";

    public string AnalystDedupKey(Guid userId, Guid analystActionId) => $"analyst:{userId}:{analystActionId}";
}
