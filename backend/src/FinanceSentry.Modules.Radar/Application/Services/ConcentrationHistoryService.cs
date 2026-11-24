using FinanceSentry.Modules.Radar.Domain;
using FinanceSentry.Modules.Radar.Domain.Ports;

namespace FinanceSentry.Modules.Radar.Application.Services;

/// <summary>The risk picture's concentration-and-allocation history, read from the captures and snapshots already stored.</summary>
public sealed class ConcentrationHistoryService(IConcentrationHistorySource source) : IConcentrationHistoryService
{
    private const int HistoryYears = 2;

    public async Task<ConcentrationHistory> GetAsync(Guid userId, CancellationToken ct = default)
    {
        var from = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-HistoryYears);
        return ConcentrationHistoryCalculator.Build(await source.ReadAsync(userId, from, ct));
    }
}
