namespace FinanceSentry.Modules.Radar.Domain.Ports;

/// <summary>What the stored data says about a holding's next payouts: its dividend yield and its next ex-dividend day.</summary>
public sealed record IncomeOutlook(decimal? DividendYield, DateOnly? NextExDividend);

/// <summary>
/// Reads the forward-looking income inputs for held tickers: the latest stored dividend yield (a fraction of price)
/// and the next ex-dividend date within the horizon. Both are already collected; a ticker with neither is absent.
/// </summary>
public interface IIncomeOutlookSource
{
    Task<IReadOnlyDictionary<string, IncomeOutlook>> GetAsync(
        IReadOnlyCollection<string> tickers, DateOnly from, DateOnly to, CancellationToken ct = default);
}
