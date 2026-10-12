namespace FinanceSentry.Modules.Research.Domain;

using FinanceSentry.Core.Auth;
using FinanceSentry.Modules.Research.Domain.Opportunity;

/// <summary>
/// A nominated conviction candidate under active tracking (019/US1). Exactly one <c>Active</c>
/// candidate exists per (UserId, Ticker) — re-scoring appends a <see cref="CandidateScore"/> rather
/// than creating a duplicate (FR US1.4). No composite/aggregate score column here or anywhere in
/// the model (FR-007) — sub-scores stay separate.
/// </summary>
public class OpportunityCandidate : IUserOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string Ticker { get; set; } = string.Empty;
    public CandidateSource Source { get; set; } = CandidateSource.User;
    public CandidateStatus Status { get; set; } = CandidateStatus.Active;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; set; }
    public Guid? PromotedThesisId { get; set; }
    public string? RejectedReason { get; set; }
    public List<string> NominationReasons { get; set; } = [];
}
