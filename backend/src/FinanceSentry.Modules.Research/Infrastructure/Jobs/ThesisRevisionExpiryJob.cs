namespace FinanceSentry.Modules.Research.Infrastructure.Jobs;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Research.Application.Commands;
using Hangfire;
using Microsoft.Extensions.Logging;

/// <summary>Daily expiry of pending thesis revisions past their expiry. Expired rows stay.</summary>
public sealed class ThesisRevisionExpiryJob(
    ICommandHandler<ExpireThesisRevisionsCommand, int> handler,
    ILogger<ThesisRevisionExpiryJob> logger)
{
    [AutomaticRetry(Attempts = 2)]
    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        var count = await handler.Handle(new ExpireThesisRevisionsCommand(DateTimeOffset.UtcNow), ct);
        logger.LogInformation("Thesis revision expiry: {Count} proposals expired", count);
    }
}
