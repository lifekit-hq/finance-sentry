namespace FinanceSentry.Modules.CryptoSync.Infrastructure.Connections;

using FinanceSentry.Core.Connections;
using FinanceSentry.Modules.CryptoSync.Domain;
using FinanceSentry.Modules.CryptoSync.Domain.Repositories;

/// <summary>
/// The crypto module's stored health for the read surfaces (<see cref="IConnectionHealthReader"/>): the person's active
/// exchange credential per venue.
/// </summary>
public sealed class CryptoConnectionHealthReader(IExchangeCredentialRepository credentials) : IConnectionHealthReader
{
    private static readonly string[] Providers = [CryptoExchangeProvider.Binance, CryptoExchangeProvider.RevolutX];

    public async Task<IReadOnlyList<ConnectionHealthEntry>> GetForUserAsync(Guid userId, CancellationToken ct = default)
    {
        var entries = new List<ConnectionHealthEntry>();
        foreach (var provider in Providers)
        {
            if (await credentials.GetUnscopedAsync(userId, provider, ct) is { IsActive: true } credential)
                entries.Add(new ConnectionHealthEntry(provider, nameof(ExchangeCredential), credential.Id, credential.Health));
        }

        return entries;
    }
}
