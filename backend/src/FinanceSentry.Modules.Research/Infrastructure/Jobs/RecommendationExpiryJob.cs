namespace FinanceSentry.Modules.Research.Infrastructure.Jobs;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Research.Application.Commands;
using Hangfire;
using Microsoft.Extensions.Logging;

/// <summary>
/// Daily expiry of pending proposals past their expiry (#1069). Expired rows stay and are journalled.
/// </summary>
public sealed class RecommendationExpiryJob(
    ICommandHandler<ExpireRecommendationsCommand, int> handler,
    ILogger<RecommendationExpiryJob> logger)
{
    [AutomaticRetry(Attempts = 2)]
    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        var count = await handler.Handle(new ExpireRecommendationsCommand(DateTimeOffset.UtcNow), ct);
        logger.LogInformation("Recommendation expiry: {Count} proposals expired", count);
    }
}
