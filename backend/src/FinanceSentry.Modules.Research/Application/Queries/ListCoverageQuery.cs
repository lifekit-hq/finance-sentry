namespace FinanceSentry.Modules.Research.Application.Queries;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Research.Application.Services.Coverage;
using FinanceSentry.Modules.Research.Domain.Coverage;
using FinanceSentry.Modules.Research.Domain.Repositories;
using Microsoft.Extensions.Options;

/// <summary>
/// The coverage universe ("our own index"): the capped list of companies the pipeline maintains, with why each is
/// there, plus the latest adds and removes. Held and watchlisted names are flagged; a name may carry a thesis.
/// </summary>
public sealed record ListCoverageQuery(Guid UserId) : IQuery<CoverageListView>;

public sealed record CoverageListView(
    int MaxMembers,
    int Count,
    bool OverCap,
    IReadOnlyList<CoverageMemberView> Members,
    IReadOnlyList<CoverageJournalView> RecentChanges)
{
    /// <summary>Held names in the book (owner call D2).</summary>
    public int HeldCount { get; init; }

    /// <summary>The soft cap on held names (<c>Coverage:HeldSoftCap</c>, 12); never blocks anything.</summary>
    public int HeldSoftCap { get; init; }

    /// <summary>True when <see cref="HeldCount"/> passes <see cref="HeldSoftCap"/>.</summary>
    public bool HeldOverSoftCap => HeldCount > HeldSoftCap;
}

public sealed class ListCoverageQueryHandler(
    ICoverageRepository coverage,
    CoverageSnapshotReader snapshots,
    ICoverageMemoRepository memos,
    TimeProvider clock,
    IOptions<CoverageOptions> options) : IQueryHandler<ListCoverageQuery, CoverageListView>
{
    private const int RecentChangeRows = 25;

    public async Task<CoverageListView> Handle(ListCoverageQuery query, CancellationToken ct)
    {
        var config = options.Value;
        var snapshot = await snapshots.ReadAsync(query.UserId, clock.GetUtcNow(), ct, flagsOnly: true);
        var journal = await coverage.ListJournalUnscopedAsync(query.UserId, null, Math.Max(config.MaxListed, RecentChangeRows), ct);
        var addedRule = journal
            .Where(e => e.Action == CoverageAction.Added)
            .GroupBy(e => e.Ticker, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Rule, StringComparer.OrdinalIgnoreCase);

        var memoTimes = await memos.LatestTimesUnscopedAsync(query.UserId, ct);
        var members = snapshot.Members
            .Take(config.MaxListed)
            .Select(m => CoverageExplainer.View(m, snapshot, addedRule.GetValueOrDefault(m.Ticker)) with
            {
                LatestMemoAt = memoTimes.TryGetValue(m.Ticker, out var at) ? at : null,
            })
            .ToList();

        return new CoverageListView(
            config.MaxMembers,
            snapshot.Members.Count,
            snapshot.Members.Count > config.MaxMembers,
            members,
            [.. journal.Take(RecentChangeRows).Select(CoverageJournalViews.From)])
        {
            HeldCount = snapshot.Held.Count,
            HeldSoftCap = config.HeldSoftCap,
        };
    }
}
