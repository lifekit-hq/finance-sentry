namespace FinanceSentry.Modules.Research.Domain.Repositories;

using FinanceSentry.Modules.Research.Domain.Opportunity;

public interface ICandidateRepository
{
    /// <summary>
    /// Inserts a new Active candidate for (userId, ticker) with the given TTL, or returns the
    /// existing Active one unchanged (re-score never creates a duplicate — FR US1.4). The opportunity scan upserts
    /// with no person in scope, so the existence check opts out of the Owner query filter.
    /// </summary>
    Task<(OpportunityCandidate Candidate, bool IsNew)> UpsertActiveAsync(
        Guid userId, string ticker, CandidateSource source, TimeSpan ttl, CancellationToken ct = default);

    Task<OpportunityCandidate?> FindActiveByTickerAsync(Guid userId, string ticker, CancellationToken ct = default);

    Task<OpportunityCandidate?> GetAsync(Guid userId, Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<OpportunityCandidate>> ListAsync(
        Guid userId, CandidateStatus? status = null, CandidateSource? source = null, CancellationToken ct = default);

    /// <summary>Every user's Active candidates past their expiry, for the expiry job. Opts out of the Owner query filter.</summary>
    Task<IReadOnlyList<OpportunityCandidate>> ListExpiredUnscopedAsync(DateTimeOffset asOf, CancellationToken ct = default);

    Task UpdateAsync(OpportunityCandidate candidate, CancellationToken ct = default);
}
