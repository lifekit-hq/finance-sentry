namespace FinanceSentry.Modules.Companion.Application.Services;

using FinanceSentry.Modules.Companion.Domain;

/// <summary>
/// The Finance-Sentry-owned policy for what counts as a proactively-notifiable event and how it maps
/// to the current mode (feature 031). Pure — no I/O — so it is unit-testable in isolation.
/// </summary>
public interface IMaterialityPolicy
{
    /// <summary>Maps an alert type to a companion event kind, or null if it is not surfaced.</summary>
    CompanionEventKind? ClassifyAlert(string alertType);

    /// <summary>The disposition a freshly-captured event gets under the given mode.</summary>
    EventDisposition DispositionForMode(NotificationMode mode);

    /// <summary>
    /// Kind-aware disposition. Behaves like <see cref="DispositionForMode"/> except: operational failures
    /// carry elevated criticality — they are never suppressed purely by a quiet mode (US4); and sync
    /// failures are held for the digest unless the failing source has gone without a successful sync
    /// for longer than <see cref="SyncFailureEscalationAge"/>.
    /// </summary>
    /// <param name="sourceStaleness">
    /// Time since the event's source last synced successfully, or null when unknown (no account
    /// reference, or never synced). Only consulted for <see cref="CompanionEventKind.SyncFailure"/>.
    /// </param>
    EventDisposition DispositionFor(NotificationMode mode, CompanionEventKind kind, TimeSpan? sourceStaleness = null);

    /// <summary>How long a source may stay without a successful sync before a SyncFailure escalates past the digest.</summary>
    TimeSpan SyncFailureEscalationAge { get; }

    string AlertDedupKey(Guid alertId);

    /// <summary>The alert id an <see cref="AlertDedupKey"/> was built from; null for any other key.</summary>
    Guid? AlertIdFromDedupKey(string dedupKey);

    string AnalystDedupKey(Guid userId, Guid analystActionId);
}
