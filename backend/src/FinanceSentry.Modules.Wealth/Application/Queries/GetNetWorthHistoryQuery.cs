namespace FinanceSentry.Modules.Wealth.Application.Queries;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Wealth.Domain.Repositories;

public record NetWorthSnapshotDto(
    DateOnly SnapshotDate,
    decimal BankingTotal,
    decimal BrokerageTotal,
    decimal CryptoTotal,
    decimal TotalNetWorth,
    string Currency,
    string? StaleSleeves,
    bool IsApproximate,
    decimal? CashTotal = null,
    decimal? BrokerageInvested = null,
    decimal? CryptoInvested = null);

public record NetWorthHistoryResponse(
    IReadOnlyList<NetWorthSnapshotDto> Snapshots,
    bool HasHistory);

public record GetNetWorthHistoryQuery(Guid UserId, DateOnly? From, DateOnly? To) : IQuery<NetWorthHistoryResponse>;

public class GetNetWorthHistoryQueryHandler(INetWorthSnapshotRepository repository)
    : IQueryHandler<GetNetWorthHistoryQuery, NetWorthHistoryResponse>
{
    private readonly INetWorthSnapshotRepository _repository = repository ?? throw new ArgumentNullException(nameof(repository));

    public async Task<NetWorthHistoryResponse> Handle(GetNetWorthHistoryQuery query, CancellationToken cancellationToken)
    {
        var snapshots = await _repository.GetByUserIdAsync(query.UserId, query.From, query.To, cancellationToken);

        var dtos = snapshots
            .Select(s => new NetWorthSnapshotDto(
                s.SnapshotDate, s.BankingTotal, s.BrokerageTotal,
                s.CryptoTotal, s.TotalNetWorth, s.Currency, s.StaleSleeves, s.IsApproximate,
                s.CashTotal, s.BrokerageInvested, s.CryptoInvested))
            .ToList();

        return new NetWorthHistoryResponse(dtos, dtos.Count > 0);
    }
}
