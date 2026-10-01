namespace FinanceSentry.Modules.Radar.Infrastructure.Jobs;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Radar.Application.Commands;
using FinanceSentry.Modules.Radar.Application.Services;
using FinanceSentry.Modules.Radar.Domain.MarketStructure;
using FinanceSentry.Modules.Radar.Domain.Repositories;
using Hangfire;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>Daily market-structure compute + signal emission (shortly after ingestion).</summary>
public sealed class RadarComputeJob(
    ICommandHandler<ComputeMarketStructureCommand, ComputeRunSummary> handler,
    IRadarSignalRepository signals,
    IOptions<RadarOptions> options,
    ILogger<RadarComputeJob> logger)
{
    [AutomaticRetry(Attempts = 2)]
    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        var summary = await handler.Handle(new ComputeMarketStructureCommand(), ct);
        logger.LogInformation(
            "Radar compute complete: {Computed} tickers, signals={Signals}.",
            summary.TickersComputed,
            string.Join(",", summary.SignalsByType.Select(kv => $"{kv.Key}={kv.Value}")));

        // FR-007 retention: info signals prune past the horizon; notable+ kept indefinitely.
        var cutoff = DateTimeOffset.UtcNow.AddDays(-options.Value.InfoRetentionDays);
        var pruned = await signals.PruneInfoBeforeUnscopedAsync(cutoff, ct);
        if (pruned > 0)
        {
            logger.LogInformation(
                "Radar retention: pruned {Pruned} info signals older than {Cutoff:yyyy-MM-dd}.", pruned, cutoff);
        }
    }
}
