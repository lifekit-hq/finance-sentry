namespace FinanceSentry.Modules.Wealth.Application.Services;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Core.Measures;
using FinanceSentry.Modules.Wealth.Domain;
using FinanceSentry.Modules.Wealth.Domain.Repositories;

/// <summary>
/// What every projection starts from (M10, C8): the person's assumption set, the latest net-worth snapshot,
/// the complete months of honest flow in the trailing <see cref="Projection.LookbackMonths"/>, and whether any
/// of the snapshot's sources is stale. <paramref name="SavingsPath"/> is null when there is no snapshot or
/// fewer than <see cref="Projection.MinCompleteMonths"/> complete months, and then nothing is projected.
/// </summary>
public sealed record ProjectionInputs(
    ProjectionAssumptions Assumptions,
    NetWorthSnapshot? Snapshot,
    IReadOnlyList<HonestMonthlyFlow> CompleteMonths,
    SavingsPath? SavingsPath,
    bool HasStaleSleeves);

/// <summary>
/// The one reader of a projection's inputs, so the twelve-month projection and the financial-independence
/// projection cannot drift onto different assumptions, savings paths or start values (gap G7).
/// </summary>
public class ProjectionInputsReader(
    INetWorthSnapshotRepository snapshots,
    IHonestMonthlyFlowReader monthlyFlow,
    IUserFireAssumptionsReader assumptions,
    TimeProvider clock)
{
    private readonly INetWorthSnapshotRepository _snapshots = snapshots;
    private readonly IHonestMonthlyFlowReader _monthlyFlow = monthlyFlow;
    private readonly IUserFireAssumptionsReader _assumptions = assumptions;
    private readonly TimeProvider _clock = clock;

    public async Task<ProjectionInputs> ReadAsync(Guid userId, CancellationToken ct)
    {
        var stored = await _assumptions.GetAsync(userId, ct);
        var assumptionSet = stored is null
            ? ProjectionAssumptions.Default
            : new ProjectionAssumptions(stored.RealAnnualReturn, stored.SafeWithdrawalRate);

        // Unscoped read, keyed on the caller's own id: the monthly brief job runs with no principal, so the
        // owner-filtered read would find no snapshot there. Requests pass the id of their own principal.
        var snapshot = await _snapshots.GetLatestByUserIdUnscopedAsync(userId, ct);
        var flows = await _monthlyFlow.GetMonthlyFlowAsync(userId, Projection.LookbackMonths, ct);

        var currentMonthKey = _clock.GetUtcNow().ToString("yyyy-MM");
        var completeMonths = flows.Where(f => f.Month != currentMonthKey).ToList();
        // Stale when any of the snapshot's sources failed to read or was carried forward (per-source reasons).
        var hasStaleSleeves = snapshot is not null && SnapshotSources
            .Of(snapshot, await _snapshots.GetSourcesUnscopedAsync(userId, [snapshot.Id], ct))
            .StaleSources.Count > 0;

        var savingsPath = snapshot is null
            ? null
            : Projection.SavingsPathOf([.. completeMonths.Select(f => f.NetUsd)]);

        return new ProjectionInputs(assumptionSet, snapshot, completeMonths, savingsPath, hasStaleSleeves);
    }
}
