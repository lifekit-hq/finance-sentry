namespace FinanceSentry.Modules.Research.Application.Queries;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain.Tax;

/// <summary>
/// The Irish disposal-tax context for one held symbol (#454 S6): the regime that would apply to a sale today and a
/// rough euro amount, or the reasons it is withheld. Null when the user holds no position in the symbol.
/// </summary>
public sealed record GetDisposalTaxContextQuery(Guid UserId, string Symbol) : IQuery<DisposalTaxContext?>;

public sealed class GetDisposalTaxContextQueryHandler(
    IBookFiguresService bookFigures,
    PositionReturnsReader returnsReader,
    DisposalTaxContextReader disposalTax)
    : IQueryHandler<GetDisposalTaxContextQuery, DisposalTaxContext?>
{
    public async Task<DisposalTaxContext?> Handle(GetDisposalTaxContextQuery request, CancellationToken ct)
    {
        var symbol = request.Symbol.Trim().ToUpperInvariant();
        var book = await bookFigures.ReadAsync(request.UserId, ct);
        var position = book.Positions.FirstOrDefault(p => p.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase));
        if (position is null)
        {
            return null;
        }

        var reading = await returnsReader.ReadAsync(request.UserId, position, ct);
        return await disposalTax.ReadAsync(request.UserId, position, reading, ct);
    }
}
