namespace FinanceSentry.Modules.Research.Application.Services.Catalyst;

using System.Globalization;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Research.Application.Commands;
using FinanceSentry.Modules.Research.Application.Services.ReferenceUniverse;
using FinanceSentry.Modules.Research.Domain.Catalyst;
using FinanceSentry.Modules.Research.Domain.Opportunity;
using FinanceSentry.Modules.Research.Domain.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>What one catalyst scan did.</summary>
public sealed record CatalystRunSummary(
    bool Ran,
    string? Source,
    string? FallbackReason,
    int Universe,
    int Filings,
    int Users,
    int Nominated,
    int Covered,
    int SkippedRemoved,
    int Deferred,
    int Errors)
{
    public static CatalystRunSummary Disabled { get; } = new(false, null, null, 0, 0, 0, 0, 0, 0, 0, 0);

    /// <summary>A run that found nothing to do.</summary>
    public static CatalystRunSummary Empty { get; } = new(true, null, null, 0, 0, 0, 0, 0, 0, 0, 0);
}

/// <summary>
/// Catalyst detection beyond the ticker feed (discovery funnel stage 7): once a day, find the 8-Ks that carry a watched item
/// (1.01 agreements, 2.02 results, 5.02 officers, 8.01 other events) on any name of the reference universe, so a name outside
/// coverage can come to the pipeline's notice on a filing. For each user, once ever per filing:
/// <list type="bullet">
/// <item>a name already in coverage goes to the stage 4 event review (<see cref="ReviewDueRaiser.NoteCoverageOnly"/> then
/// <see cref="ReviewDueRaiser.CompleteAsync"/>: the same event row, re-score and remove rules the hourly filing watch uses);</item>
/// <item>a name that left coverage is skipped unless the filing is newer than its removal (<see cref="CatalystRules.IsNewerThanRemoval"/>);</item>
/// <item>any other name is nominated into the existing candidate path (<see cref="ScoreCandidateCommand"/> with the Scan source and a
/// <see cref="CatalystRules.Tag"/> nomination reason), at most <see cref="CatalystOptions.MaxNominationsPerRun"/> a run. It enters
/// coverage only if the stage 1 rules (<see cref="CoverageEvaluator"/>) then agree: this class adds no member and no rule.</item>
/// </list>
/// Every handled filing is recorded as a <see cref="CatalystEvent"/>. No model is called anywhere.
/// </summary>
public sealed class CatalystDetector(
    IReferenceUniverseReader reference,
    CatalystFilingScanner scanner,
    ICatalystRepository catalysts,
    ICoverageRepository coverage,
    IIpsRepository ips,
    ICommandHandler<ScoreCandidateCommand, ScoreCandidateResult> scorer,
    ReviewDueRaiser reviewDue,
    IOptions<CatalystOptions> options,
    TimeProvider clock,
    ILogger<CatalystDetector> logger)
{
    private readonly CatalystOptions _options = options.Value;

    public async Task<CatalystRunSummary> RunAsync(CancellationToken ct = default)
    {
        if (!_options.Enabled)
        {
            logger.LogInformation("Catalyst scan is off (Catalyst:Enabled=false)");
            return CatalystRunSummary.Disabled;
        }

        var now = clock.GetUtcNow();
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var from = today.AddDays(-Math.Max(0, _options.LookbackDays));

        var universe = await ReadUniverseAsync(ct);
        if (universe.Count == 0)
        {
            logger.LogWarning(
                "Catalyst scan skipped: the reference universe has no members in the configured list kinds ({Kinds}); the weekly refresh fills it",
                string.Join(", ", _options.ListKinds));
            return new CatalystRunSummary(true, null, null, 0, 0, 0, 0, 0, 0, 0, 0);
        }

        var scan = await scanner.ScanAsync(universe, from, today, now, ct);

        var users = new HashSet<Guid>(await ips.GetUserIdsWithCurrentIpsUnscopedAsync(ct));
        users.UnionWith(await coverage.ListUserIdsUnscopedAsync(ct));

        var total = new CatalystRunSummary(
            true, scan.Source, scan.FallbackReason, universe.Count, scan.Filings.Count, users.Count, 0, 0, 0, 0, 0);
        var failedUsers = 0;
        foreach (var userId in users)
        {
            try
            {
                var one = await HandleUserAsync(userId, scan.Filings, from, now, ct);
                total = total with
                {
                    Nominated = total.Nominated + one.Nominated,
                    Covered = total.Covered + one.Covered,
                    SkippedRemoved = total.SkippedRemoved + one.SkippedRemoved,
                    Deferred = total.Deferred + one.Deferred,
                    Errors = total.Errors + one.Errors,
                };
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failedUsers++;
                logger.LogError(ex, "Catalyst scan failed for user {UserId}", userId);
            }
        }

        var pruned = await catalysts.PruneEventsUnscopedAsync(now.AddDays(-_options.EventRetentionDays), ct);
        logger.LogInformation(
            "Catalyst scan: source {Source}{Fallback}, universe {Universe}, watched-item 8-Ks {Filings}, users {Users}, nominated {Nominated}, " +
            "covered {Covered}, skipped as removed {Skipped}, deferred by the cap {Deferred}, errors {Errors}, pruned {Pruned}, failed users {Failed}",
            total.Source, total.FallbackReason is null ? string.Empty : " (fallback: " + total.FallbackReason + ")",
            total.Universe, total.Filings, total.Users, total.Nominated, total.Covered, total.SkippedRemoved, total.Deferred,
            total.Errors, pruned, failedUsers);

        if (failedUsers > 0)
        {
            throw new InvalidOperationException(
                $"Catalyst scan failed for {failedUsers} of {users.Count} user(s); the others were saved.");
        }

        return total;
    }

    private async Task<IReadOnlySet<string>> ReadUniverseAsync(CancellationToken ct)
    {
        var kinds = _options.ListKinds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var tickers = new HashSet<string>(StringComparer.Ordinal);
        foreach (var list in (await reference.GetListsAsync(ct)).Where(l => kinds.Contains(l.Kind)))
        {
            foreach (var member in await reference.GetMembersAsync(list.ListKey, ct) ?? [])
            {
                tickers.Add(member.Ticker);
            }
        }

        return tickers;
    }

    private async Task<CatalystRunSummary> HandleUserAsync(
        Guid userId, IReadOnlyList<CatalystFiling> filings, DateOnly from, DateTimeOffset now, CancellationToken ct)
    {
        var handled = await catalysts.ListHandledUnscopedAsync(userId, from, ct);
        var fresh = filings.Where(f => !handled.Contains(f.Ticker + "|" + f.Accession)).ToList();
        if (fresh.Count == 0)
        {
            return CatalystRunSummary.Empty;
        }

        var members = (await coverage.ListMembersUnscopedAsync(userId, ct))
            .Select(m => m.Ticker.ToUpperInvariant())
            .ToHashSet(StringComparer.Ordinal);
        var removals = await coverage.ListLastRemovalsUnscopedAsync(userId, ct);

        var subjects = await reviewDue.LoadSubjectsAsync(userId, ct);
        subjects.TrackCoverage(members);

        var rows = new List<CatalystEvent>();
        var covered = new List<CatalystEvent>();
        var nominatedTickers = new HashSet<string>(StringComparer.Ordinal);
        int nominated = 0, skipped = 0, deferred = 0, errors = 0;

        foreach (var filing in CatalystRules.Rank(
                     fresh, _options.Items, f => CatalystRules.WatchedItems(f.Items, _options.Items), f => f.FilingDate, f => f.Ticker))
        {
            var items = CatalystRules.WatchedItems(filing.Items, _options.Items);
            var summary = CatalystRules.Summary(items, filing.FilingDate);

            if (members.Contains(filing.Ticker))
            {
                reviewDue.NoteCoverageOnly(subjects, filing.Ticker, ReviewDueSource.Filing, filing.Accession, summary);
                covered.Add(Row(userId, filing, items, summary, now, CatalystDisposition.Covered, null,
                    "Already covered: handed to the coverage event review."));
                continue;
            }

            if (removals.TryGetValue(filing.Ticker, out var removedAt) && !CatalystRules.IsNewerThanRemoval(filing.FilingDate, removedAt))
            {
                skipped++;
                rows.Add(Row(userId, filing, items, summary, now, CatalystDisposition.SkippedRemoved, null,
                    string.Create(CultureInfo.InvariantCulture,
                        $"Left coverage {removedAt:yyyy-MM-dd}; a filing dated {filing.FilingDate:yyyy-MM-dd} is no newer evidence.")));
                continue;
            }

            var tag = CatalystRules.Tag(items[0]);
            if (nominatedTickers.Contains(filing.Ticker))
            {
                rows.Add(Row(userId, filing, items, summary, now, CatalystDisposition.Nominated, tag,
                    "The name was nominated by another filing in this run."));
                continue;
            }

            if (nominatedTickers.Count >= _options.MaxNominationsPerRun)
            {
                deferred++;
                continue;
            }

            try
            {
                await scorer.Handle(
                    new ScoreCandidateCommand(
                        userId, filing.Ticker, DecisionNote: null, Source: CandidateSource.Scan,
                        NominationReasons: [.. items.Select(CatalystRules.Tag)]),
                    ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Not recorded: the filing is reconsidered on the next run inside the lookback window.
                errors++;
                logger.LogError(ex, "Catalyst nomination failed to score {Ticker} for user {UserId}", filing.Ticker, userId);
                continue;
            }

            nominatedTickers.Add(filing.Ticker);
            nominated++;
            rows.Add(Row(userId, filing, items, summary, now, CatalystDisposition.Nominated, tag,
                "Nominated into the candidate path; the coverage rules decide the add."));
        }

        if (deferred > 0)
        {
            logger.LogWarning(
                "Catalyst scan: the cap of {Cap} nominations left {Deferred} filing(s) for the next run, user {UserId}",
                _options.MaxNominationsPerRun, deferred, userId);
        }

        if (covered.Count > 0)
        {
            await reviewDue.CompleteAsync(userId, subjects, ct);
        }

        await catalysts.AddEventsUnscopedAsync(userId, [.. rows, .. covered], ct);
        return new CatalystRunSummary(true, null, null, 0, 0, 0, nominated, covered.Count, skipped, deferred, errors);
    }

    private static CatalystEvent Row(
        Guid userId, CatalystFiling filing, IReadOnlyList<string> items, string summary, DateTimeOffset now,
        CatalystDisposition disposition, string? tag, string detail)
        => new()
        {
            UserId = userId,
            Ticker = filing.Ticker,
            Accession = filing.Accession,
            Form = filing.Form,
            Items = string.Join(',', items),
            FilingDate = filing.FilingDate,
            Summary = summary,
            DocumentUrl = filing.DocumentUrl,
            Source = filing.Source,
            DetectedAt = now,
            Disposition = disposition,
            Tag = tag,
            Detail = detail,
        };
}
