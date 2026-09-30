namespace FinanceSentry.Modules.Research.Application.Queries;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Repositories;

/// <summary>
/// Diffs two versions of the policy statement (#700). With no versions given it compares the
/// current version against the one before it; null when the statement has no earlier version or a
/// requested version does not exist.
/// </summary>
public record GetIpsDiffQuery(Guid UserId, int? FromVersion = null, int? ToVersion = null) : IQuery<IpsDiffDto?>;

public class GetIpsDiffQueryHandler(IIpsRepository repo) : IQueryHandler<GetIpsDiffQuery, IpsDiffDto?>
{
    public async Task<IpsDiffDto?> Handle(GetIpsDiffQuery query, CancellationToken ct)
    {
        var versions = await repo.ListVersionsAsync(query.UserId, ct);

        var to = query.ToVersion is { } toVersion
            ? versions.FirstOrDefault(v => v.Version == toVersion)
            : versions.FirstOrDefault(v => v.IsCurrent) ?? versions.MaxBy(v => v.Version);
        if (to is null)
            return null;

        var from = query.FromVersion is { } fromVersion
            ? versions.FirstOrDefault(v => v.Version == fromVersion)
            : versions.Where(v => v.Version < to.Version).MaxBy(v => v.Version);
        if (from is null)
            return null;

        return ToDto(from, to);
    }

    public static IpsDiffDto ToDto(InvestmentPolicyStatement from, InvestmentPolicyStatement to)
        => new(from.Version, from.CreatedAt, to.Version, to.CreatedAt, IpsDiff.Compare(from, to));
}
