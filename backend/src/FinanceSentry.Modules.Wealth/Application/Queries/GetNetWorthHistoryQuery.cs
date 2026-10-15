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
    IReadOnlyList<string>? FxReasons = null);

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

        var dtos = snapshots
            .Select(s => new NetWorthSnapshotDto(
                s.SnapshotDate, s.BankingTotal, s.BrokerageTotal,
                s.CryptoTotal, s.TotalNetWorth, s.Currency, s.StaleSleeves, s.IsApproximate,
                s.CashTotal, s.BrokerageInvested, s.CryptoInvested,
                // A measured row carries the status its snapshot recorded; a reconstructed row (none recorded)
                // is valued at today's rate rather than the rate of its own day.
                (s.FxStatus ?? (s.IsApproximate ? FxStatus.TodayRate : FxStatus.Exact)).ToReasons()))
            .ToList();

        // A change across the window includes accounts that joined or left it; the dashboard reads this to
        // state the amount without a percentage. Removal is approximate until per-source rows exist (R8).
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

        return new NetWorthHistoryResponse(dtos, dtos.Count > 0, scopeChanges, ChangeHeadline(snapshots, scopeChanges.Count > 0));
    }

    // The repository returns the window in date order: the change runs from its first row to its last.
    private static Headline? ChangeHeadline(IReadOnlyList<NetWorthSnapshot> snapshots, bool scopeChanged)
    {
        if (snapshots.Count == 0)
            return null;

        var first = snapshots[0];
        var last = snapshots[^1];
        var signals = HeadlineSignals.FromSnapshot(first.StaleSleeves, first.IsApproximate, first.FxStatus)
            .Merge(HeadlineSignals.FromSnapshot(last.StaleSleeves, last.IsApproximate, last.FxStatus));
        return HeadlineFactory.Change(
            first.TotalNetWorth, last.TotalNetWorth, HeadlineScope.Umbrella,
            first.SnapshotDate, last.SnapshotDate, scopeChanged, signals);
    }
}
