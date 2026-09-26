namespace FinanceSentry.Modules.BankSync.Infrastructure.Services;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.BankSync.Application.Queries;
using FinanceSentry.Modules.BankSync.Application.Services;

/// <summary>
/// Read-port adapter: exposes <see cref="GetMoneyFlowStatisticsQuery"/> to other modules
/// (Wealth) without them referencing BankSync directly. Collapses the per-currency and
/// synthetic counterparty rows into one USD total per month, matching the way the dashboard's
/// own monthly-savings computation aggregates <see cref="MonthlyFlow"/> rows.
/// </summary>
public class HonestMonthlyFlowReader(
    IQueryHandler<GetMoneyFlowStatisticsQuery, IReadOnlyList<MonthlyFlow>> handler)
    : IHonestMonthlyFlowReader
{
    private readonly IQueryHandler<GetMoneyFlowStatisticsQuery, IReadOnlyList<MonthlyFlow>> _handler = handler;

    public async Task<IReadOnlyList<HonestMonthlyFlow>> GetMonthlyFlowAsync(
        Guid userId, int months, CancellationToken ct = default)
    {
        var flows = await _handler.Handle(new GetMoneyFlowStatisticsQuery(userId, months), ct);

        return flows
            .GroupBy(f => f.Month)
            .Select(g => new HonestMonthlyFlow(g.Key, g.Sum(f => f.OutflowUsd), g.Sum(f => f.NetUsd)))
            .OrderBy(f => f.Month)
            .ToList();
    }
}
