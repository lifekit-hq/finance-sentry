namespace FinanceSentry.Modules.Research.Application.Services.ReferenceUniverse;

using FinanceSentry.Modules.Research.Domain.ReferenceUniverse;
using FinanceSentry.Modules.Research.Domain.Repositories;
using Microsoft.Extensions.Options;

public sealed record ReferenceListInfo(string ListKey, string Kind, string Source, int MemberCount);

public sealed record ReferenceMemberInfo(string Ticker, string? Name);

/// <summary>
/// Where a dataset stands: its last success, its last failure and the reason, and whether it is overdue.
/// <c>Stale</c> is true when the last success is older than the configured window, or there never was one.
/// </summary>
public sealed record ReferenceSourceStatus(
    string Source,
    DateTimeOffset? LastSuccessAt,
    int? LastMemberCount,
    DateTimeOffset? LastFailureAt,
    string? LastError,
    int ConsecutiveFailures,
    bool Stale);

/// <summary>Reads the reference universe: members by list, lists for a ticker, and each dataset's freshness.</summary>
public interface IReferenceUniverseReader
{
    Task<IReadOnlyList<ReferenceListInfo>> GetListsAsync(CancellationToken ct = default);

    /// <summary>Members of a list; the key is matched case-insensitively. Null when no such list exists.</summary>
    Task<IReadOnlyList<ReferenceMemberInfo>?> GetMembersAsync(string listKey, CancellationToken ct = default);

    /// <summary>The lists a ticker belongs to (empty when none); <c>BRK.B</c> and <c>brk-b</c> read the same.</summary>
    Task<IReadOnlyList<string>> GetListsForTickerAsync(string ticker, CancellationToken ct = default);

    Task<IReadOnlyList<ReferenceSourceStatus>> GetStatusAsync(CancellationToken ct = default);
}

public sealed class ReferenceUniverseReader(
    IReferenceUniverseRepository repository,
    IOptions<ReferenceUniverseOptions> options,
    TimeProvider time) : IReferenceUniverseReader
{
    private const int RunsConsidered = 200;

    public async Task<IReadOnlyList<ReferenceListInfo>> GetListsAsync(CancellationToken ct = default)
        => (await repository.ListListsAsync(ct))
            .Select(l => new ReferenceListInfo(l.ListKey, ReferenceListKeys.KindOf(l.ListKey), l.Source, l.MemberCount))
            .ToList();

    public async Task<IReadOnlyList<ReferenceMemberInfo>?> GetMembersAsync(string listKey, CancellationToken ct = default)
    {
        var lists = await repository.ListListsAsync(ct);
        var match = lists.FirstOrDefault(l => string.Equals(l.ListKey, listKey.Trim(), StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            return null;
        }

        return (await repository.ListMembersAsync(match.ListKey, ct))
            .Select(m => new ReferenceMemberInfo(m.Ticker, m.Name))
            .ToList();
    }

    public async Task<IReadOnlyList<string>> GetListsForTickerAsync(string ticker, CancellationToken ct = default)
    {
        var normalised = ReferenceListKeys.NormalizeTicker(ticker);
        return normalised is null ? [] : await repository.ListKeysForTickerAsync(normalised, ct);
    }

    public async Task<IReadOnlyList<ReferenceSourceStatus>> GetStatusAsync(CancellationToken ct = default)
    {
        var runs = await repository.ListRecentRunsAsync(RunsConsidered, ct);
        var now = time.GetUtcNow();
        var staleAfter = TimeSpan.FromDays(options.Value.StaleAfterDays);

        var statuses = new List<ReferenceSourceStatus>();
        foreach (var source in ReferenceSources.All)
        {
            var mine = runs.Where(r => r.Source == source).OrderByDescending(r => r.FinishedAt).ToList();
            var lastSuccess = mine.FirstOrDefault(r => r.Succeeded);
            var lastFailure = mine.FirstOrDefault(r => !r.Succeeded);
            var failuresSince = mine.TakeWhile(r => !r.Succeeded).Count();

            statuses.Add(new ReferenceSourceStatus(
                source,
                lastSuccess?.FinishedAt,
                lastSuccess?.MemberCount,
                lastFailure?.FinishedAt,
                lastFailure?.Error,
                failuresSince,
                lastSuccess is null || now - lastSuccess.FinishedAt > staleAfter));
        }

        return statuses;
    }
}
