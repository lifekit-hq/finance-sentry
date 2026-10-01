namespace FinanceSentry.Modules.Research.Application.Queries;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Domain;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Domain.Repositories;
using Microsoft.Extensions.Logging;

public record GetAllocationDriftQuery(Guid UserId) : IQuery<AllocationDriftDto>;

public class GetAllocationDriftQueryHandler(
    IIpsRepository ipsRepo,
    IBookFiguresService bookFigures,
    ILogger<GetAllocationDriftQueryHandler> logger)
    : IQueryHandler<GetAllocationDriftQuery, AllocationDriftDto>
{
    private const decimal MaterialUnplannedPct = 1m;

    private const string StatusWithin = "Within";
    private const string StatusOverBand = "OverBand";
    private const string StatusUnderBand = "UnderBand";
    private const string StatusUnplanned = "Unplanned";

    public async Task<AllocationDriftDto> Handle(GetAllocationDriftQuery query, CancellationToken ct)
    {
        var ips = await ipsRepo.GetCurrentUnscopedAsync(query.UserId, ct);
        var book = await bookFigures.ReadAsync(query.UserId, ct);

        var byClass = new Dictionary<string, decimal>(StringComparer.Ordinal);
        void Add(string canonical, decimal usd)
        {
            if (usd <= 0) return;
            byClass[canonical] = byClass.GetValueOrDefault(canonical) + usd;
        }

        foreach (var p in book.Positions)
            Add(p.AssetClass, p.UsdValue);

        if (book.CashUsd > 0)
            Add(AssetClassNormalizer.Cash, book.CashUsd);

        if (book.IsStale)
            logger.LogWarning("Book figures are stale for user {UserId}; stale sources: {Sources}.", query.UserId, string.Join(", ", book.StaleSources));

        var total = book.TotalValueUsd;

        if (ips is null)
        {
            var currentOnly = byClass
                .Select(kv => new AllocationSleeveDrift(
                    kv.Key, 0m, 0m, 0m,
                    Percent(kv.Value, total), kv.Value, Percent(kv.Value, total), StatusUnplanned))
                .OrderByDescending(s => s.ActualValueUsd)
                .ToList();
            return new AllocationDriftDto(false, total, book.CashUsd, book.InvestedValueUsd, false, currentOnly, "n/a", book.IsStale);
        }

        var rule = ips.RebalancingRule;
        var sleeves = new List<AllocationSleeveDrift>();
        var covered = new HashSet<string>(StringComparer.Ordinal);
        var needsRebalance = false;

        foreach (var target in ips.AllocationTargets)
        {
            var canonical = AssetClassNormalizer.Normalize(target.AssetClass);
            covered.Add(canonical);

            var actualUsd = byClass.GetValueOrDefault(canonical);
            var actualPct = Percent(actualUsd, total);

            var band = Math.Max(rule.AbsoluteBandPct, target.TargetPct * rule.RelativeBandPct / 100m);
            var min = target.MinPct > 0 ? target.MinPct : Math.Max(0m, target.TargetPct - band);
            var max = target.MaxPct > 0 ? target.MaxPct : target.TargetPct + band;

            var status = actualPct < min ? StatusUnderBand : actualPct > max ? StatusOverBand : StatusWithin;
            if (status != StatusWithin) needsRebalance = true;

            sleeves.Add(new AllocationSleeveDrift(
                canonical, target.TargetPct, Math.Round(min, 2), Math.Round(max, 2),
                actualPct, actualUsd, Math.Round(actualPct - target.TargetPct, 2), status));
        }

        foreach (var kv in byClass.Where(kv => !covered.Contains(kv.Key)))
        {
            var actualPct = Percent(kv.Value, total);
            if (actualPct >= MaterialUnplannedPct) needsRebalance = true;
            sleeves.Add(new AllocationSleeveDrift(
                kv.Key, 0m, 0m, 0m, actualPct, kv.Value, actualPct, StatusUnplanned));
        }

        return new AllocationDriftDto(
            true, total, book.CashUsd, book.InvestedValueUsd, needsRebalance,
            sleeves.OrderByDescending(s => s.ActualValueUsd).ToList(),
            ips.ReviewCadence,
            book.IsStale);
    }

    private static decimal Percent(decimal value, decimal total)
        => total > 0 ? Math.Round(value / total * 100m, 2) : 0m;
}
