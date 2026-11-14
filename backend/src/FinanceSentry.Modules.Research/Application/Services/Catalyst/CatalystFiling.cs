namespace FinanceSentry.Modules.Research.Application.Services.Catalyst;

/// <summary>An 8-K found by one of the feeds, before the universe, the item filter and the user's coverage are applied.</summary>
/// <param name="Items">Every item code the filing carries (<c>2.02</c>, <c>9.01</c>); the scan keeps the watched ones.</param>
/// <param name="Source">Which feed found it, <see cref="Domain.Catalyst.CatalystRules.SourceFullText"/> or <see cref="Domain.Catalyst.CatalystRules.SourceSubmissions"/>.</param>
public sealed record CatalystFiling(
    string Ticker,
    string Accession,
    string Form,
    IReadOnlyList<string> Items,
    DateOnly FilingDate,
    string DocumentUrl,
    string Source);

/// <summary>A feed failed in a way the scan should fall back on: the endpoint refused, timed out, or answered in a shape it does not recognise.</summary>
public sealed class CatalystFeedException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>EDGAR full-text search for 8-K filings (the primary feed). Throws <see cref="CatalystFeedException"/> on any failure.</summary>
public interface IEdgarFullTextSearch
{
    /// <summary>
    /// The 8-K filings dated <paramref name="from"/> to <paramref name="to"/> (inclusive) that mention <paramref name="item"/>, as
    /// many as the options' page bound allows. The hits' own item lists are what the scan trusts; the phrase only narrows the query.
    /// </summary>
    Task<IReadOnlyList<CatalystFiling>> SearchAsync(string item, DateOnly from, DateOnly to, CancellationToken ct = default);
}
