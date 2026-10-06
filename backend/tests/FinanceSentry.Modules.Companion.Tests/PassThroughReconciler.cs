namespace FinanceSentry.Modules.Companion.Tests;

using FinanceSentry.Modules.Companion.Application.Services;
using FinanceSentry.Modules.Companion.Domain;

/// <summary>Leaves events untouched, for tests that exercise a delivery path other than sync-failure reconciliation.</summary>
internal sealed class PassThroughReconciler : ISyncFailureReconciler
{
    public static readonly PassThroughReconciler Instance = new();

    public Task<IReadOnlyList<CompanionEvent>> ReconcileAsync(
        IReadOnlyList<CompanionEvent> events, CancellationToken ct = default)
        => Task.FromResult(events);
}
