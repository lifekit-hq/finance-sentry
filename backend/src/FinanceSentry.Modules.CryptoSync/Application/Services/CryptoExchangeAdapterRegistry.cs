using FinanceSentry.Modules.CryptoSync.Domain.Exceptions;
using FinanceSentry.Modules.CryptoSync.Domain.Interfaces;

namespace FinanceSentry.Modules.CryptoSync.Application.Services;

/// <summary>Resolves the <see cref="ICryptoExchangeAdapter"/> for a provider slug.</summary>
public sealed class CryptoExchangeAdapterRegistry(IEnumerable<ICryptoExchangeAdapter> adapters)
{
    private readonly IReadOnlyDictionary<string, ICryptoExchangeAdapter> _byProvider =
        adapters.ToDictionary(a => a.ExchangeName, StringComparer.Ordinal);

    public ICryptoExchangeAdapter Get(string provider) =>
        _byProvider.TryGetValue(provider, out var adapter)
            ? adapter
            : throw new UnknownExchangeProviderException(provider);

    public bool IsSupported(string provider) => _byProvider.ContainsKey(provider);
}
