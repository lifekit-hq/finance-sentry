namespace FinanceSentry.Modules.Wealth.Infrastructure.Jobs;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

public sealed class NetWorthSnapshotCatchUpHostedService(
    IServiceScopeFactory scopeFactory,
    ILogger<NetWorthSnapshotCatchUpHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartupDelay, stoppingToken);

            using var scope = scopeFactory.CreateScope();
            var catchUpService = scope.ServiceProvider.GetRequiredService<NetWorthSnapshotBackfillService>();
            await catchUpService.BackfillAsync(stoppingToken);

            // After the catch-up so rows it adds are covered too. Idempotent: fills only null split columns.
            var splitBackfill = scope.ServiceProvider.GetRequiredService<NetWorthSplitBackfillService>();
            var filled = await splitBackfill.BackfillAsync(stoppingToken);
            logger.LogInformation("Net worth cash/invested split backfill filled {Rows} snapshot rows.", filled);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Net worth snapshot catch-up failed.");
        }
    }
}
