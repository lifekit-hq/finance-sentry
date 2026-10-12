namespace FinanceSentry.Modules.Companion.Domain.Repositories;

using FinanceSentry.Modules.Companion.Domain;

/// <summary>
/// Problem reports. The request path reads and writes through the Owner filter; the forwarder runs with no person in
/// scope, so its reads are the <c>…Unscoped…</c> methods, which opt out explicitly and keep their own predicate.
/// </summary>
public interface IProblemReportRepository
{
    Task<ProblemReport> AddAsync(ProblemReport report, CancellationToken ct = default);

    /// <summary>When the user's reports created at or after <paramref name="since"/> were made, oldest first.</summary>
    Task<IReadOnlyList<DateTimeOffset>> ListCreatedAtSinceAsync(Guid userId, DateTimeOffset since, CancellationToken ct = default);

    /// <summary>Pending reports whose next attempt is due, oldest first.</summary>
    Task<IReadOnlyList<ProblemReport>> ListDueUnscopedAsync(DateTimeOffset now, int limit, CancellationToken ct = default);

    Task UpdateUnscopedAsync(ProblemReport report, CancellationToken ct = default);
}
