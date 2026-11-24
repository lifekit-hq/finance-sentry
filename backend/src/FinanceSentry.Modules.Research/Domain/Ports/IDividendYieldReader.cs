namespace FinanceSentry.Modules.Research.Domain.Ports;

/// <summary>
/// Published read port: the dividend yield (a fraction of price, 0.03 = 3%) from each ticker's latest valuation
/// snapshot, for cross-module readers such as the Radar book-income figure. A ticker with no snapshot is absent; one
/// whose latest snapshot has no yield maps to null (unavailable, never zero).
/// </summary>
public interface IDividendYieldReader
{
    Task<IReadOnlyDictionary<string, decimal?>> GetLatestAsync(
        IReadOnlyCollection<string> tickers, CancellationToken ct = default);
}
