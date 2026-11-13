namespace FinanceSentry.Modules.Research.Infrastructure.Jobs;

using System.Security.Cryptography;
using System.Text;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Repositories;
using Microsoft.Extensions.Logging;

/// <summary>
/// Daily Hangfire job: when a dated catalyst on a thesis (<see cref="InvestmentThesis.Catalysts"/>) passed inside the last
/// <see cref="LookbackDays"/> days, raises the thesis-review notice (<see cref="ReviewDueRaiser"/>, source
/// <c>catalyst</c>, reference = catalyst date and a short digest of its text, so two catalysts on one day stay distinct).
/// Nothing else reads those dates: they were only ever shown on the Events calendar. Digest lane. A catalyst dated today has
/// not passed yet; one older than the window is not revived (a thesis saved with old dates must not flood the digest), and
/// the generator's once-only identity makes the overlap between runs a no-op.
/// </summary>
public sealed class CatalystPassedJob(
    IBankingTotalsReader banking,
    IThesisRepository theses,
    ReviewDueRaiser reviewDue,
    ILogger<CatalystPassedJob> logger)
{
    /// <summary>The Hangfire recurring-job id this detector is scheduled under.</summary>
    public const string RecurringJobId = "catalyst-passed";

    /// <summary>How far back a passed catalyst date still counts.</summary>
    public const int LookbackDays = 7;

    private const int TextDigestBytes = 4;

    public Task ExecuteAsync(CancellationToken ct = default) => ExecuteAsync(DateTime.UtcNow, ct);

    /// <summary>Overload taking the reference instant explicitly, so tests aren't at the mercy of the day they run on.</summary>
    public async Task ExecuteAsync(DateTime nowUtc, CancellationToken ct = default)
    {
        var userIds = await banking.GetActiveUserIdsAsync(ct);
        if (userIds.Count == 0)
        {
            logger.LogDebug("CatalystPassed: no active users found, skipping.");
            return;
        }

        var today = DateOnly.FromDateTime(nowUtc);
        foreach (var userId in userIds)
        {
            try
            {
                await ProcessUserAsync(userId, today, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "CatalystPassed: error processing user {UserId}", userId);
            }
        }
    }

    private async Task ProcessUserAsync(Guid userId, DateOnly today, CancellationToken ct)
    {
        var earliest = today.AddDays(-LookbackDays);
        var thesisList = await theses.ListUnscopedAsync(userId, ct);

        foreach (var thesis in thesisList)
        {
            foreach (var catalyst in thesis.Catalysts.Where(c => c.Date >= earliest && c.Date < today))
            {
                // Raised against the thesis alone: a catalyst belongs to it, not to its proxies' other subjects.
                var own = new ReviewSubjects([thesis], []);
                await reviewDue.RaiseAsync(
                    userId, own, thesis.Ticker, ReviewDueSource.Catalyst,
                    $"{catalyst.Date:yyyy-MM-dd}:{TextDigest(catalyst.Event)}",
                    [$"catalyst:{catalyst.Date:yyyy-MM-dd}"],
                    $"catalyst passed on {catalyst.Date:yyyy-MM-dd}: {catalyst.Event}", ct);
            }
        }
    }

    private static string TextDigest(string text)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.Trim().ToUpperInvariant())).AsSpan(0, TextDigestBytes));
}
