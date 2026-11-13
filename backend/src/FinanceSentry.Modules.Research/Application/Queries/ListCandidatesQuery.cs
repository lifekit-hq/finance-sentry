namespace FinanceSentry.Modules.Research.Application.Queries;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain.Opportunity;
using FinanceSentry.Modules.Research.Domain.Repositories;

/// <summary>
/// Lists a user's opportunity candidates with their latest score (US3). Rejected and expired
/// candidates remain listed (SC-005), optionally filtered by status/source.
/// </summary>
public record ListCandidatesQuery(
    Guid UserId,
    CandidateStatus? Status = null,
    CandidateSource? Source = null) : IQuery<IReadOnlyList<CandidateListItem>>;

public sealed record CandidateListItem(
    Guid Id,
    string Ticker,
    CandidateSource Source,
    CandidateStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    Guid? PromotedThesisId,
    string? RejectedReason,
    IReadOnlyList<string> NominationReasons,
    int? StructureScore,
    int? FundamentalsScore,
    CrowdingClass? Crowding,
    DateTimeOffset? LastScoredAt,
    // Who promoted or rejected the candidate ("User" or "Ledger"); "User" while it is still Active.
    string DecisionOrigin = "User");

public sealed class ListCandidatesQueryHandler(
    ICandidateRepository candidateRepo,
    ICandidateScoreRepository scoreRepo,
    ThesisOriginReader originReader)
    : IQueryHandler<ListCandidatesQuery, IReadOnlyList<CandidateListItem>>
{
    public async Task<IReadOnlyList<CandidateListItem>> Handle(ListCandidatesQuery query, CancellationToken ct)
    {
        var candidates = await candidateRepo.ListAsync(query.UserId, query.Status, query.Source, ct);

        var origins = await originReader.ForCandidateDecisionsAsync(query.UserId, candidates.Select(c => c.Id).ToList(), ct);
        var items = new List<CandidateListItem>(candidates.Count);
        foreach (var candidate in candidates)
        {
            var latest = await scoreRepo.LatestForCandidateAsync(candidate.Id, ct);
            items.Add(new CandidateListItem(
                candidate.Id,
                candidate.Ticker,
                candidate.Source,
                candidate.Status,
                candidate.CreatedAt,
                candidate.ExpiresAt,
                candidate.PromotedThesisId,
                candidate.RejectedReason,
                candidate.NominationReasons,
                latest?.StructureScore,
                latest?.FundamentalsScore,
                latest?.CrowdingClass,
                latest?.ScoredAt,
                ThesisOriginReader.Resolve(origins, candidate.Id).ToString()));
        }

        return items;
    }
}
