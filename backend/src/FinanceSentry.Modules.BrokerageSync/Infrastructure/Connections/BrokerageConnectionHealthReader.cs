using FinanceSentry.Core.Connections;
using FinanceSentry.Modules.BrokerageSync.Domain;
using FinanceSentry.Modules.BrokerageSync.Domain.Repositories;
using FinanceSentry.Modules.BrokerageSync.Infrastructure.IBKR.Flex;
using FinanceSentry.Modules.BrokerageSync.Infrastructure.IBKR.OAuth;

namespace FinanceSentry.Modules.BrokerageSync.Infrastructure.Connections;

/// <summary>
/// The brokerage module's stored health for the read surfaces (<see cref="IConnectionHealthReader"/>): the person's active
/// IBKR and IBKR Flex credentials. Inzhur is outside the policy and is not listed.
/// </summary>
public sealed class BrokerageConnectionHealthReader(
    IIBKRCredentialRepository ibkrCredentials,
    IIBKRFlexCredentialRepository flexCredentials) : IConnectionHealthReader
{
    public async Task<IReadOnlyList<ConnectionHealthEntry>> GetForUserAsync(Guid userId, CancellationToken ct = default)
    {
        var entries = new List<ConnectionHealthEntry>();
        if (await ibkrCredentials.GetByUserIdUnscopedAsync(userId, ct) is { IsActive: true } ibkr)
        {
            entries.Add(new ConnectionHealthEntry(
                IbkrFailureClassifier.ProviderName, nameof(IBKRCredential), ibkr.Id, ibkr.Health));
        }

        if (await flexCredentials.GetByUserIdUnscopedAsync(userId, ct) is { IsActive: true } flex)
        {
            entries.Add(new ConnectionHealthEntry(
                IbkrFlexFailureClassifier.ProviderName, nameof(IBKRFlexCredential), flex.Id, flex.Health));
        }

        return entries;
    }
}
