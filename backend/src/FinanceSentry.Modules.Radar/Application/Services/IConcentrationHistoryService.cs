using FinanceSentry.Modules.Radar.Domain;

namespace FinanceSentry.Modules.Radar.Application.Services;

public interface IConcentrationHistoryService
{
    Task<ConcentrationHistory> GetAsync(Guid userId, CancellationToken ct = default);
}
