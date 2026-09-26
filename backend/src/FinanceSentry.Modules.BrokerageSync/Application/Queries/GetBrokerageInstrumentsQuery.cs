using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.BrokerageSync.Domain;
using FinanceSentry.Modules.BrokerageSync.Domain.Repositories;

namespace FinanceSentry.Modules.BrokerageSync.Application.Queries;

public sealed record GetBrokerageInstrumentsQuery(Guid UserId) : IQuery<BrokerageInstrumentsResponse>;

public sealed record BrokerageInstrumentDto(
    Guid Id,
    string Provider,
    long Conid,
    string? Isin,
    string Symbol,
    string InstrumentType,
    InstrumentClassification? Classification);

public sealed record BrokerageInstrumentsResponse(IReadOnlyList<BrokerageInstrumentDto> Items);

public sealed class GetBrokerageInstrumentsQueryHandler(IBrokerageInstrumentRepository instrumentRepository)
    : IQueryHandler<GetBrokerageInstrumentsQuery, BrokerageInstrumentsResponse>
{
    public async Task<BrokerageInstrumentsResponse> Handle(GetBrokerageInstrumentsQuery request, CancellationToken ct)
    {
        var instruments = await instrumentRepository.GetByUserIdAsync(request.UserId, ct);

        var items = instruments
            .Select(i => new BrokerageInstrumentDto(
                i.Id,
                i.Provider,
                i.Conid,
                i.Isin,
                i.Symbol,
                i.InstrumentType,
                i.Classification))
            .ToList();

        return new BrokerageInstrumentsResponse(items);
    }
}
