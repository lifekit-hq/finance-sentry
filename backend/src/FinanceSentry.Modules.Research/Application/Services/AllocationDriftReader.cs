namespace FinanceSentry.Modules.Research.Application.Services;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Queries;
using FinanceSentry.Modules.Research.Domain.Ports;
using FinanceSentry.Modules.Research.Domain.Repositories;

/// <summary>
/// <see cref="IAllocationDriftReader"/> impl over Research's own <see cref="GetAllocationDriftQuery"/>
/// and the internal IPS repository.
/// </summary>
public sealed class AllocationDriftReader(
    IQueryHandler<GetAllocationDriftQuery, AllocationDriftDto> driftQuery,
    IIpsRepository ipsRepo) : IAllocationDriftReader
{
    public async Task<AllocationDriftReading> GetAsync(Guid userId, CancellationToken ct = default)
    {
        var drift = await driftQuery.Handle(new GetAllocationDriftQuery(userId), ct);
        var sleeves = drift.HasIps
            ? drift.Sleeves
                .Select(s => new SleeveDriftReading(s.AssetClass, s.TargetPct, s.ActualPct, s.DriftPct, s.Status))
                .ToList()
            : (IReadOnlyList<SleeveDriftReading>)[];
        return new AllocationDriftReading(drift.HasIps, sleeves);
    }

    public Task<IReadOnlyList<Guid>> ListUserIdsWithCurrentIpsAsync(CancellationToken ct = default)
        => ipsRepo.GetUserIdsWithCurrentIpsUnscopedAsync(ct);
}
