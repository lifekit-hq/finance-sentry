namespace FinanceSentry.Core.Interfaces;

/// <summary>
/// Core read of the tickers a user carries an investment thesis on, for the Radar universe — lets
/// Radar keep a thesis symbol's bars fresh without depending on the Research module's internal
/// repository.
/// </summary>
public interface IThesisTickerReader
{
    Task<IReadOnlyList<string>> ListTickersAsync(Guid userId, CancellationToken ct = default);
}
