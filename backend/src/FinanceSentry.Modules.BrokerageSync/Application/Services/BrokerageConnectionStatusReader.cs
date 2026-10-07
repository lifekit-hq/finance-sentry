using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.BrokerageSync.Domain.Repositories;

namespace FinanceSentry.Modules.BrokerageSync.Application.Services;

public sealed class BrokerageConnectionStatusReader(IInzhurCredentialRepository inzhurCredentials) : IBrokerageConnectionStatusReader
{
    public async Task<IReadOnlyDictionary<string, string>> GetStatusesAsync(Guid userId, CancellationToken ct = default)
    {
        var inzhur = await inzhurCredentials.GetByUserIdUnscopedAsync(userId, ct);
        return inzhur is null
            ? new Dictionary<string, string>()
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [InzhurHoldingsMapper.Provider] = inzhur.Status };
    }
}
