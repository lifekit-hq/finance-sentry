namespace FinanceSentry.Modules.Research.Application.Services;

using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Repositories;

/// <summary>
/// Who made the last change to a thesis, candidate decision or watchlist entry: the origin of its latest journal event
/// of the kinds below. Nothing is stored on the entity itself; an entry with no such event (written before the audit
/// existed) reads as <see cref="ThesisEventOrigin.User"/>.
/// </summary>
public sealed class ThesisOriginReader(IThesisEventRepository events)
{
    private static readonly ThesisEventType[] ThesisWrites =
        [ThesisEventType.Created, ThesisEventType.Revised, ThesisEventType.Reverted];

    private static readonly ThesisEventType[] CandidateDecisions =
        [ThesisEventType.Promoted, ThesisEventType.Rejected];

    private static readonly ThesisEventType[] WatchlistWrites = [ThesisEventType.Created];

    public Task<IReadOnlyDictionary<Guid, ThesisEventOrigin>> ForThesesAsync(
        Guid userId, IReadOnlyCollection<Guid> ids, CancellationToken ct)
        => events.GetLatestOriginsAsync(userId, ThesisSubjectType.Thesis, ids, ThesisWrites, ct);

    public Task<IReadOnlyDictionary<Guid, ThesisEventOrigin>> ForCandidateDecisionsAsync(
        Guid userId, IReadOnlyCollection<Guid> ids, CancellationToken ct)
        => events.GetLatestOriginsAsync(userId, ThesisSubjectType.Candidate, ids, CandidateDecisions, ct);

    public Task<IReadOnlyDictionary<Guid, ThesisEventOrigin>> ForWatchlistAsync(
        Guid userId, IReadOnlyCollection<Guid> ids, CancellationToken ct)
        => events.GetLatestOriginsAsync(userId, ThesisSubjectType.Watchlist, ids, WatchlistWrites, ct);

    public static ThesisEventOrigin Resolve(IReadOnlyDictionary<Guid, ThesisEventOrigin> origins, Guid id)
        => origins.TryGetValue(id, out var origin) ? origin : ThesisEventOrigin.User;
}
