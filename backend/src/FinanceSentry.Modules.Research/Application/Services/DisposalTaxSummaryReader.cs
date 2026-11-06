namespace FinanceSentry.Modules.Research.Application.Services;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Research.Domain.Tax;

/// <summary>
/// <see cref="IDisposalTaxSummaryReader"/> impl (#454 S7): runs the same per-position disposal-tax context the dossier
/// and <c>get_disposal_tax_context</c> show over every held position and counts the result. A position the calculator
/// withholds for its provider alone is counted without reading its fills, so a crypto venue's holdings cost nothing here.
/// </summary>
public sealed class DisposalTaxSummaryReader(
    IBookFiguresService bookFigures,
    PositionReturnsReader returnsReader,
    DisposalTaxContextReader disposalTax) : IDisposalTaxSummaryReader
{
    public async Task<DisposalTaxSummary> ReadAsync(Guid userId, CancellationToken ct = default)
    {
        var book = await bookFigures.ReadAsync(userId, ct);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var contexts = new List<DisposalTaxContext>();
        foreach (var position in book.Positions)
        {
            ct.ThrowIfCancellationRequested();
            contexts.Add(await ContextOfAsync(userId, position, today, ct));
        }

        var shown = contexts.Where(c => c.Status == DisposalTaxCalculator.Shown).ToList();
        var reasons = contexts
            .Where(c => c.Status == DisposalTaxCalculator.Withheld)
            .SelectMany(c => c.WithheldReasons)
            .GroupBy(r => r, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        return new DisposalTaxSummary(
            contexts.Count,
            shown.Count,
            contexts.Count - shown.Count,
            shown.Count == 0 ? null : shown.Sum(c => c.EstimatedTaxEur ?? 0m),
            shown.Select(c => c.TaxYear).FirstOrDefault(y => y is not null),
            reasons);
    }

    private async Task<DisposalTaxContext> ContextOfAsync(
        Guid userId, BookFigurePosition position, DateOnly today, CancellationToken ct)
    {
        // The provider gate needs no fills: ask the calculator with an empty input before paying for the ledger.
        var probe = DisposalTaxCalculator.Compute(
            new DisposalTaxInput(
                position.Symbol, position.Provider, position.Quantity, position.UsdValue, string.Empty, null, null, []),
            today,
            (_, _) => 1m,
            _ => 1m);
        if (probe.Status == DisposalTaxCalculator.Withheld
            && probe.WithheldReasons.Contains(DisposalTaxCalculator.ProviderNotSupported))
        {
            return probe;
        }

        var reading = await returnsReader.ReadAsync(userId, position, ct);
        return await disposalTax.ReadAsync(userId, position, reading, ct);
    }
}
