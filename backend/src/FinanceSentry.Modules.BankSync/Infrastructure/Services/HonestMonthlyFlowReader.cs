namespace FinanceSentry.Modules.BankSync.Infrastructure.Services;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.BankSync.Application.Queries;
using FinanceSentry.Modules.BankSync.Application.Services;

/// <summary>
/// Read-port adapter: exposes <see cref="GetMoneyFlowStatisticsQuery"/> to other modules
/// (Wealth) without them referencing BankSync directly. Collapses the per-currency and
/// synthetic counterparty rows into one USD total per month, matching the way the dashboard's
/// own monthly-savings computation aggregates <see cref="MonthlyFlow"/> rows. The day-window
/// net reads the flow breakdown the dashboard's windowed tiles are explained by.
/// </summary>
public class HonestMonthlyFlowReader(
    IQueryHandler<GetMoneyFlowStatisticsQuery, IReadOnlyList<MonthlyFlow>> handler,
    IFlowBreakdownService breakdown)
    : IHonestMonthlyFlowReader
{
    private readonly IQueryHandler<GetMoneyFlowStatisticsQuery, IReadOnlyList<MonthlyFlow>> _handler = handler;
    private readonly IFlowBreakdownService _breakdown = breakdown;

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

    public async Task<decimal> GetWindowNetAsync(Guid userId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        if (to < from)
            return 0m;

        // The breakdown classifies over whole months back from today; reach back to the window's first month.
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var months = Math.Max(1, ((today.Year - from.Year) * 12) + today.Month - from.Month);
        var range = await _breakdown.GetRangeBreakdownAsync(userId, from, to, months, ct);

        return range.Items.Where(i => i.Bucket == FlowBuckets.Income).Sum(i => i.AmountUsd)
            - range.Items.Where(i => i.Bucket == FlowBuckets.Spending).Sum(i => i.AmountUsd);
    }
}
