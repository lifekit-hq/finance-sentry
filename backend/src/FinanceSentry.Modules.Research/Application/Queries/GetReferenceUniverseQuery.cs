namespace FinanceSentry.Modules.Research.Application.Queries;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Research.Application.Services.ReferenceUniverse;

/// <summary>
/// The reference universe read (stage A of the discovery funnel). With neither argument: the catalogue of lists
/// and each dataset's freshness. With <see cref="List"/>: that list's members. With <see cref="Ticker"/>: the
/// lists that ticker belongs to. <see cref="Limit"/> caps a member listing.
/// </summary>
public record GetReferenceUniverseQuery(string? List, string? Ticker, int? Limit) : IQuery<ReferenceUniverseView>;

public sealed record ReferenceUniverseView(
    IReadOnlyList<ReferenceListInfo>? Lists,
    string? List,
    int? MemberCount,
    IReadOnlyList<ReferenceMemberInfo>? Members,
    string? Ticker,
    IReadOnlyList<string>? ListsForTicker,
    IReadOnlyList<ReferenceSourceStatus> Sources,
    string? Note);

public class GetReferenceUniverseQueryHandler(IReferenceUniverseReader reader)
    : IQueryHandler<GetReferenceUniverseQuery, ReferenceUniverseView>
{
    public const int DefaultLimit = 200;
    public const int MaxLimit = 1000;

    public async Task<ReferenceUniverseView> Handle(GetReferenceUniverseQuery query, CancellationToken ct)
    {
        var sources = await reader.GetStatusAsync(ct);
        var note = sources.Any(s => s.Stale)
            ? "One or more datasets are stale or never refreshed (see sources[].lastError); lists show their last good refresh."
            : null;

        if (!string.IsNullOrWhiteSpace(query.Ticker))
        {
            var lists = await reader.GetListsForTickerAsync(query.Ticker, ct);
            return new ReferenceUniverseView(null, null, null, null, query.Ticker.Trim().ToUpperInvariant(), lists, sources, note);
        }

        if (!string.IsNullOrWhiteSpace(query.List))
        {
            var members = await reader.GetMembersAsync(query.List, ct);
            if (members is null)
            {
                return new ReferenceUniverseView(null, query.List.Trim(), 0, [], null, null, sources,
                    $"No list '{query.List.Trim()}'. Call without arguments for the catalogue.");
            }

            var limit = Math.Clamp(query.Limit ?? DefaultLimit, 1, MaxLimit);
            return new ReferenceUniverseView(null, query.List.Trim(), members.Count, members.Take(limit).ToList(),
                null, null, sources, note);
        }

        return new ReferenceUniverseView(await reader.GetListsAsync(ct), null, null, null, null, null, sources, note);
    }
}
