namespace FinanceSentry.Modules.Research.Application.Queries;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain.Repositories;
using FinanceSentry.Modules.Research.Domain.Revisions;

/// <summary>The thesis-revision queue: pending proposals oldest first, and those closed in the last 30 days.</summary>
public sealed record GetThesisRevisionsQuery(Guid UserId) : IQuery<ThesisRevisionsResult>;

public sealed class GetThesisRevisionsQueryHandler(IThesisRevisionRepository repo, IThesisEvidenceLinkResolver links)
    : IQueryHandler<GetThesisRevisionsQuery, ThesisRevisionsResult>
{
    public const int DecidedWindowDays = 30;

    public async Task<ThesisRevisionsResult> Handle(GetThesisRevisionsQuery query, CancellationToken ct)
    {
        var pending = await repo.ListPendingAsync(query.UserId, ct);
        var decided = await repo.ListClosedSinceAsync(query.UserId, DateTimeOffset.UtcNow.AddDays(-DecidedWindowDays), ct);
        var resolved = await links.ResolveAsync([.. pending, .. decided], ct);
        return new ThesisRevisionsResult(
            pending.Select(r => WithLinks(r, resolved)).ToList(),
            decided.Select(r => WithLinks(r, resolved)).ToList());
    }

    private static ThesisRevisionDto WithLinks(
        ThesisRevision revision, IReadOnlyDictionary<Guid, IReadOnlyList<ThesisEvidenceLinkDto>> resolved)
        => ThesisRevisionDto.From(revision) with { EvidenceLinks = resolved.GetValueOrDefault(revision.Id) };
}
