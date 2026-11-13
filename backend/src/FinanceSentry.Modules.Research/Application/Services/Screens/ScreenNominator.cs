namespace FinanceSentry.Modules.Research.Application.Services.Screens;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Research.Application.Commands;
using FinanceSentry.Modules.Research.Application.Services.Coverage;
using FinanceSentry.Modules.Research.Domain.Opportunity;
using FinanceSentry.Modules.Research.Domain.Repositories;
using FinanceSentry.Modules.Research.Domain.Screens;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>What one nomination run did.</summary>
public sealed record ScreenRunSummary(int Users, int Nominated, int SkippedCovered, int SkippedAlreadyNominated, int SkippedSuppressed, int Errors, int FailedScreens)
{
    public static ScreenRunSummary Empty { get; } = new(0, 0, 0, 0, 0, 0, 0);
}

/// <summary>
/// Runs the discovery-funnel screens (stage 5) and feeds what they find into the existing candidate path: a hit becomes a
/// <c>Scan</c> candidate scored by the same <see cref="ScoreCandidateCommand"/> the opportunity scan uses, its first
/// nomination reason the screen's tag (what the Ledger scoreboard reads to attribute a coverage add to the screen). The
/// coverage evaluator (stage 1) then decides, with its unchanged rules, whether the candidate earns a place in the user's
/// list. Nothing here adds to coverage, alerts or calls a model.
/// </summary>
/// <remarks>
/// Per user, a hit is skipped when the name is already covered, when the user already has an Active candidate carrying
/// the screen's tag (so a re-run changes nothing), or when the user's coverage removed it and the evidence is not newer
/// than the removal (<see cref="ScreenSuppression"/>). Nominations are capped per screen and per user per run, and by the
/// room left under <c>Coverage:MaxMembers</c>: with none left, the user's candidates would only wait.
/// </remarks>
public sealed class ScreenNominator(
    IEnumerable<IScreen> screens,
    ICoverageRepository coverage,
    ICandidateRepository candidates,
    IIpsRepository ips,
    ICommandHandler<ScoreCandidateCommand, ScoreCandidateResult> scorer,
    IOptions<ScreenOptions> options,
    IOptions<CoverageOptions> coverageOptions,
    ILogger<ScreenNominator> logger)
{
    public async Task<ScreenRunSummary> RunAsync(DateTimeOffset asOf, CancellationToken ct = default)
    {
        var settings = options.Value;
        if (!settings.Enabled)
        {
            return ScreenRunSummary.Empty;
        }

        var userIds = await ips.GetUserIdsWithCurrentIpsUnscopedAsync(ct);
        if (userIds.Count == 0)
        {
            logger.LogInformation("Screen nomination: no user has an IPS, so there is nobody to nominate for; no screen was run");
            return ScreenRunSummary.Empty;
        }

        var runs = new List<(IScreen Screen, ScreenRun Run)>();
        var failedScreens = 0;
        foreach (var screen in screens.Where(s => s.Enabled))
        {
            try
            {
                var run = await screen.RunAsync(asOf, ct);
                runs.Add((screen, run));
                logger.LogInformation(
                    "Screen {Tag}: examined {Examined}, hits {Hits}{Note}",
                    run.Tag, run.Examined, run.Hits.Count, run.Note is null ? string.Empty : $" ({run.Note})");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failedScreens++;
                logger.LogError(ex, "Screen {Tag} failed; the other screens still run", screen.Tag);
            }
        }

        var total = ScreenRunSummary.Empty with { Users = userIds.Count, FailedScreens = failedScreens };
        foreach (var userId in userIds)
        {
            total = Add(total, await NominateForAsync(userId, runs, asOf, ct));
        }

        logger.LogInformation(
            "Screen nomination: users {Users}, nominated {Nominated}, skipped covered {Covered}, already nominated {Already}, " +
            "suppressed after removal {Suppressed}, scoring errors {Errors}, failed screens {Failed}",
            total.Users, total.Nominated, total.SkippedCovered, total.SkippedAlreadyNominated, total.SkippedSuppressed,
            total.Errors, total.FailedScreens);

        if (failedScreens > 0)
        {
            throw new InvalidOperationException(
                $"{failedScreens} screen(s) failed; the others nominated what they found.");
        }

        return total;
    }

    private async Task<ScreenRunSummary> NominateForAsync(
        Guid userId, IReadOnlyList<(IScreen Screen, ScreenRun Run)> runs, DateTimeOffset asOf, CancellationToken ct)
    {
        var settings = options.Value;
        var members = (await coverage.ListMembersUnscopedAsync(userId, ct))
            .Select(m => m.Ticker.Trim().ToUpperInvariant())
            .ToHashSet(StringComparer.Ordinal);
        var room = coverageOptions.Value.MaxMembers - members.Count;
        if (room <= 0)
        {
            logger.LogInformation("Screen nomination: coverage is at its cap of {Cap} for user {UserId}; nothing is nominated", coverageOptions.Value.MaxMembers, userId);
            return ScreenRunSummary.Empty;
        }

        var removals = await coverage.ListLastRemovalsUnscopedAsync(userId, ct);
        var active = (await candidates.ListAllUnscopedAsync(userId, ct))
            .Where(c => c.Status == CandidateStatus.Active)
            .GroupBy(c => c.Ticker.Trim().ToUpperInvariant(), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.SelectMany(c => c.NominationReasons).ToHashSet(StringComparer.OrdinalIgnoreCase), StringComparer.Ordinal);

        var budget = Math.Min(room, settings.MaxNominationsPerUserPerRun);
        var summary = ScreenRunSummary.Empty;
        foreach (var (screen, run) in runs)
        {
            var nominatedByScreen = 0;
            foreach (var hit in run.Hits)
            {
                if (summary.Nominated >= budget || nominatedByScreen >= screen.MaxNominationsPerRun)
                {
                    break;
                }

                var ticker = hit.Ticker.Trim().ToUpperInvariant();
                if (members.Contains(ticker))
                {
                    summary = summary with { SkippedCovered = summary.SkippedCovered + 1 };
                    continue;
                }

                if (active.TryGetValue(ticker, out var reasons) && reasons.Contains(hit.Tag))
                {
                    summary = summary with { SkippedAlreadyNominated = summary.SkippedAlreadyNominated + 1 };
                    continue;
                }

                if (ScreenSuppression.Suppressed(
                        removals.TryGetValue(ticker, out var removedAt) ? removedAt : null,
                        hit.EvidenceAt, asOf, settings.RemovedCooldownDays))
                {
                    summary = summary with { SkippedSuppressed = summary.SkippedSuppressed + 1 };
                    continue;
                }

                try
                {
                    await scorer.Handle(
                        new ScoreCandidateCommand(
                            userId, ticker, $"{hit.Tag}: {hit.Detail}", CandidateSource.Scan, hit.Reasons),
                        ct);
                    summary = summary with { Nominated = summary.Nominated + 1 };
                    nominatedByScreen++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    summary = summary with { Errors = summary.Errors + 1 };
                    logger.LogError(ex, "Screen {Tag} could not score {Ticker} for user {UserId}", hit.Tag, ticker, userId);
                }
            }
        }

        return summary;
    }

    private static ScreenRunSummary Add(ScreenRunSummary total, ScreenRunSummary user)
        => total with
        {
            Nominated = total.Nominated + user.Nominated,
            SkippedCovered = total.SkippedCovered + user.SkippedCovered,
            SkippedAlreadyNominated = total.SkippedAlreadyNominated + user.SkippedAlreadyNominated,
            SkippedSuppressed = total.SkippedSuppressed + user.SkippedSuppressed,
            Errors = total.Errors + user.Errors,
        };
}
