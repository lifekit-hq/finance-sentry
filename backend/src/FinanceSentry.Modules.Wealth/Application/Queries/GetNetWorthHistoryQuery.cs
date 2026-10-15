namespace FinanceSentry.Modules.Wealth.Application.Queries;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Headlines;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Core.Utils;
using FinanceSentry.Modules.Wealth.Domain;
using FinanceSentry.Modules.Wealth.Domain.Repositories;

public record NetWorthSnapshotDto(
    DateOnly SnapshotDate,
    decimal BankingTotal,
    decimal BrokerageTotal,
    decimal CryptoTotal,
    decimal TotalNetWorth,
    string Currency,
    string? StaleSleeves,
    bool IsApproximate,
    decimal? CashTotal = null,
    decimal? BrokerageInvested = null,
    decimal? CryptoInvested = null,
    IReadOnlyList<string>? FxReasons = null,
    IReadOnlyList<NetWorthSourceDto>? Sources = null,
    decimal? OtherInvested = null);

/// <summary>
/// One source's part of a history row (framework §5.6): <c>Total</c> is the figure the row counted for it (a
/// carried-forward sleeve's carried figure; null only for a source outside the three sleeves whose read failed),
/// <c>Health</c> COMPLETE / ESTIMATED / DEGRADED / UNAVAILABLE and <c>Reasons</c> its typed codes
/// (<c>STALE_SOURCE</c>, <c>FX_TODAY_RATE</c>, <c>FX_UNKNOWN_1TO1</c>). A row taken before per-source rows were
/// stored lists the three sleeves from its columns, with <c>STALE_SOURCE</c> on the ones it carried forward.
/// </summary>
public record NetWorthSourceDto(
    string SourceId,
    string Kind,
    decimal? Total,
    HeadlineQuality Health,
    IReadOnlyList<string> Reasons);

/// <summary>
/// An account or connection that entered or left the book inside the returned window: <c>Kind</c> is
/// <c>linked</c> or <c>removed</c>, <c>At</c> the date it happened (UTC).
/// </summary>
public record NetWorthScopeChangeDto(string Source, string Kind, DateOnly At);

/// <summary>
/// <see cref="ChangeHeadline"/> is the change in total net worth from the first to the last returned snapshot as a
/// typed <see cref="Headline"/> (M1 value change, USD until the controller applies the base currency): no percent
/// across a scope change or from a zero start, and the stale, reconstructed and FX flags of both end rows.
/// </summary>
public record NetWorthHistoryResponse(
    IReadOnlyList<NetWorthSnapshotDto> Snapshots,
    bool HasHistory,
    IReadOnlyList<NetWorthScopeChangeDto>? ScopeChanges = null,
    Headline? ChangeHeadline = null);

public record GetNetWorthHistoryQuery(Guid UserId, DateOnly? From, DateOnly? To) : IQuery<NetWorthHistoryResponse>;

public class GetNetWorthHistoryQueryHandler(
    INetWorthSnapshotRepository repository,
    IEnumerable<IAccountScopeChangeReader> scopeChangeReaders)
    : IQueryHandler<GetNetWorthHistoryQuery, NetWorthHistoryResponse>
{
    private readonly INetWorthSnapshotRepository _repository = repository ?? throw new ArgumentNullException(nameof(repository));

    public async Task<NetWorthHistoryResponse> Handle(GetNetWorthHistoryQuery query, CancellationToken cancellationToken)
    {
        var snapshots = await _repository.GetByUserIdAsync(query.UserId, query.From, query.To, cancellationToken);
        var withSources = SnapshotSources.OfEach(
            snapshots,
            await _repository.GetSourcesAsync(query.UserId, [.. snapshots.Select(s => s.Id)], cancellationToken));

        var dtos = withSources
            .Select(x => new NetWorthSnapshotDto(
                x.Snapshot.SnapshotDate, x.Snapshot.BankingTotal, x.Snapshot.BrokerageTotal,
                x.Snapshot.CryptoTotal, x.Snapshot.TotalNetWorth, x.Snapshot.Currency, x.Sources.StaleSleeves,
                x.Snapshot.IsApproximate, x.Snapshot.CashTotal, x.Snapshot.BrokerageInvested, x.Snapshot.CryptoInvested,
                // A measured row carries the status its snapshot recorded; a reconstructed row (none recorded)
                // is valued at today's rate rather than the rate of its own day.
                (x.Snapshot.FxStatus ?? (x.Snapshot.IsApproximate ? FxStatus.TodayRate : FxStatus.Exact)).ToReasons(),
                [.. x.Sources.Sources.Select(ToDto)],
                x.Snapshot.CashTotal is null ? null : x.Sources.OtherInvested))
            .ToList();

        // A change across the window includes accounts that joined or left it; the dashboard reads this to
        // state the amount without a percentage. Removal is approximate: the scope readers report it, not the per-source rows.
        var scopeChanges = new List<NetWorthScopeChangeDto>();
        if (dtos.Count > 0)
        {
            var windowStart = dtos.Min(d => d.SnapshotDate);
            var since = windowStart.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            foreach (var reader in scopeChangeReaders)
            {
                var changes = await reader.GetChangesSinceAsync(query.UserId, since, cancellationToken);
                scopeChanges.AddRange(changes.Select(c =>
                    new NetWorthScopeChangeDto(c.Source, c.Kind, DateOnly.FromDateTime(c.AtUtc))));
            }
        }

        return new NetWorthHistoryResponse(
            dtos, dtos.Count > 0, scopeChanges, ChangeHeadline(withSources, scopeChanges.Count > 0));
    }

    private static NetWorthSourceDto ToDto(SnapshotSourceFigure source)
        => new(source.SourceId, source.Kind.ToString(), source.Total, Quality(source.Health), source.Reasons);

    // A source's health is the framework's quality vocabulary (§3.6), named the same way a headline's is.
    private static HeadlineQuality Quality(SourceHealth health) => health switch
    {
        SourceHealth.Estimated => HeadlineQuality.Estimated,
        SourceHealth.Degraded => HeadlineQuality.Degraded,
        SourceHealth.Unavailable => HeadlineQuality.Unavailable,
        _ => HeadlineQuality.Complete,
    };

    // The repository returns the window in date order: the change runs from its first row to its last.
    private static Headline? ChangeHeadline(
        IReadOnlyList<(NetWorthSnapshot Snapshot, SnapshotSources Sources)> snapshots, bool scopeChanged)
    {
        if (snapshots.Count == 0)
            return null;

        var (first, firstSources) = snapshots[0];
        var (last, lastSources) = snapshots[^1];
        if (snapshots.Count == 1)
        {
            return HeadlineFactory.Change(
                null, null, HeadlineScope.Umbrella, first.SnapshotDate, first.SnapshotDate, false, HeadlineSignals.None);
        }

        var signals = firstSources.Signals(first).Merge(lastSources.Signals(last));
        return HeadlineFactory.Change(
            first.TotalNetWorth, last.TotalNetWorth, HeadlineScope.Umbrella,
            first.SnapshotDate, last.SnapshotDate, scopeChanged, signals);
    }
}
