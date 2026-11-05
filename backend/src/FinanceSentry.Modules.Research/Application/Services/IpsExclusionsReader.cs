namespace FinanceSentry.Modules.Research.Application.Services;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Queries;
using FinanceSentry.Modules.Research.Domain.Ports;

/// <summary><see cref="IIpsExclusionsReader"/> impl over Research's own <see cref="GetIpsQuery"/>.</summary>
public sealed class IpsExclusionsReader(IQueryHandler<GetIpsQuery, IpsDto?> getIps) : IIpsExclusionsReader
{
    public async Task<IReadOnlyList<string>> GetCurrentAsync(Guid userId, CancellationToken ct = default)
        => (await getIps.Handle(new GetIpsQuery(userId), ct))?.Exclusions ?? [];
}
