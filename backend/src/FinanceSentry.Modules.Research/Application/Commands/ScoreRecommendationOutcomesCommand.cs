namespace FinanceSentry.Modules.Research.Application.Commands;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Ports;
using FinanceSentry.Modules.Research.Domain.Recommendations;
using FinanceSentry.Modules.Research.Domain.Repositories;
using Microsoft.Extensions.Logging;

/// <summary>
/// Scores decided proposals (#1069). A confirmed Add, Trim or Exit is Executed when an ingested fill on the ticker in
/// the right direction (a buy for Add, a sell for Trim/Exit) lands from a day before the decision (a broker fill is often
/// stamped at midnight, or placed before the owner confirms here) to <see cref="ExecutionWindowDays"/> after it, and Unexecuted once the window closes without one - Finance Sentry only observes the owner's own trades.
/// Confirmed and rejected proposals alike are scored against the S&amp;P 500 from the decision's journal price stamp to
/// today's quote, refreshed daily until <see cref="HorizonDays"/> have passed. Runs with no person in scope.
/// </summary>
public sealed record ScoreRecommendationOutcomesCommand(DateTimeOffset AsOf) : ICommand<int>;

public sealed class ScoreRecommendationOutcomesCommandHandler(
    IRecommendationRepository repo,
    IThesisEventRepository events,
    IPositionLedgerSource ledger,
    IMarketDataService marketData,
    ILogger<ScoreRecommendationOutcomesCommandHandler> logger)
    : ICommandHandler<ScoreRecommendationOutcomesCommand, int>
{
    public const int ExecutionWindowDays = 7;
    public const int HorizonDays = 30;
    private const int ReturnDecimals = 4;
    private const decimal Percent = 100m;

    public async Task<int> Handle(ScoreRecommendationOutcomesCommand command, CancellationToken ct)
    {
        var open = await repo.ListOutcomeOpenUnscopedAsync(ct);
        var scored = 0;
        foreach (var recommendation in open)
        {
            try
            {
                await ScoreAsync(recommendation, command.AsOf, ct);
                scored++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Recommendation outcome scoring failed for {RecommendationId}", recommendation.Id);
            }
        }

        return scored;
    }

    private async Task ScoreAsync(Recommendation recommendation, DateTimeOffset asOf, CancellationToken ct)
    {
        var decidedAt = recommendation.DecidedAt!.Value;
        if (recommendation.Execution == RecommendationExecution.Pending)
        {
            await MatchExecutionAsync(recommendation, decidedAt, asOf, ct);
        }

        var decision = (await events.ListUnscopedAsync(recommendation.UserId, recommendation.Id, ct))
            .Where(e => e.EventType is ThesisEventType.Confirmed or ThesisEventType.Rejected)
            .OrderByDescending(e => e.Timestamp)
            .FirstOrDefault();
        if (decision is { PricesPending: false, SubjectPrice: > 0m, BenchmarkPrice: > 0m })
        {
            var quotes = await marketData.GetQuotesAsync([recommendation.Ticker, decision.BenchmarkTicker], ct);
            if (quotes.TryGetValue(recommendation.Ticker, out var subject)
                && quotes.TryGetValue(decision.BenchmarkTicker, out var benchmark))
            {
                recommendation.SubjectReturnPct = Return(decision.SubjectPrice!.Value, subject.Price);
                recommendation.BenchmarkReturnPct = Return(decision.BenchmarkPrice!.Value, benchmark.Price);
            }
        }

        var executionSettled = recommendation.Execution != RecommendationExecution.Pending;
        if (executionSettled && asOf >= decidedAt.AddDays(HorizonDays))
        {
            recommendation.OutcomeScoredAt = asOf;
        }

        await repo.UpdateAsync(recommendation, ct);
    }

    private async Task MatchExecutionAsync(
        Recommendation recommendation, DateTimeOffset decidedAt, DateTimeOffset asOf, CancellationToken ct)
    {
        var windowEnd = decidedAt.AddDays(ExecutionWindowDays);
        var wantBuy = recommendation.Action == RecommendationAction.Add;
        var fills = await ledger.ReadForSymbolAsync(recommendation.UserId, recommendation.Ticker, ct);
        var match = fills.Fills
            .Where(f => wantBuy ? f.Quantity > 0m : f.Quantity < 0m)
            .Select(f => new DateTimeOffset(DateTime.SpecifyKind(f.TradedAt, DateTimeKind.Utc)))
            .Where(t => t >= decidedAt.AddDays(-1) && t <= windowEnd)
            .Order()
            .FirstOrDefault();

        if (match != default)
        {
            recommendation.Execution = RecommendationExecution.Executed;
            recommendation.ExecutedAt = match;
        }
        else if (asOf > windowEnd)
        {
            recommendation.Execution = RecommendationExecution.Unexecuted;
        }
    }

    private static decimal? Return(decimal from, decimal to)
        => from <= 0m || to <= 0m ? null : Math.Round((to / from - 1m) * Percent, ReturnDecimals);
}
