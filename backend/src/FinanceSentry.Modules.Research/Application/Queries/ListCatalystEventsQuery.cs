namespace FinanceSentry.Modules.Research.Application.Queries;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Research.Application.Services.Catalyst;
using FinanceSentry.Modules.Research.Domain.Repositories;
using Microsoft.Extensions.Options;

/// <summary>
/// The catalyst events the daily 8-K scan recorded (discovery funnel stage 7) since <paramref name="Since"/> (default a week before now):
/// each watched-item 8-K found on a reference-universe name and what the pipeline did about it.
/// </summary>
public sealed record ListCatalystEventsQuery(Guid UserId, DateTimeOffset? Since = null, int? Limit = null)
    : IQuery<CatalystEventDigest>;

/// <param name="Disposition"><c>Nominated</c> (entered the candidate path, tagged), <c>Covered</c> (handed to the coverage event review) or <c>SkippedRemoved</c> (the name left coverage and the filing is no newer).</param>
/// <param name="Items">The watched 8-K items the filing carries, comma separated, most wanted first.</param>
/// <param name="Tag">The nomination tag written on the candidate; null unless nominated.</param>
public sealed record CatalystEventView(
    DateTimeOffset DetectedAt,
    string Ticker,
    string Accession,
    string Form,
    string Items,
    DateOnly FilingDate,
    string Summary,
    string DocumentUrl,
    string Source,
    string Disposition,
    string? Tag,
    string Detail);

public sealed record CatalystEventDigest(
    DateTimeOffset Since,
    DateTimeOffset Until,
    int Total,
    bool Truncated,
    IReadOnlyList<CatalystEventView> Events);

public sealed class ListCatalystEventsQueryHandler(
    ICatalystRepository catalysts,
    TimeProvider clock,
    IOptions<CatalystOptions> options) : IQueryHandler<ListCatalystEventsQuery, CatalystEventDigest>
{
    private static readonly TimeSpan DefaultWindow = TimeSpan.FromDays(7);

    public async Task<CatalystEventDigest> Handle(ListCatalystEventsQuery query, CancellationToken ct)
    {
        var config = options.Value;
        var now = clock.GetUtcNow();
        var since = query.Since ?? now - DefaultWindow;
        var take = Math.Clamp(query.Limit ?? config.MaxListedEvents, 1, config.MaxListedEvents);

        var (events, total) = await catalysts.ListEventsUnscopedAsync(query.UserId, since, take, ct);
        return new CatalystEventDigest(
            since,
            now,
            total,
            total > events.Count,
            [.. events.Select(e => new CatalystEventView(
                e.DetectedAt, e.Ticker, e.Accession, e.Form, e.Items, e.FilingDate, e.Summary, e.DocumentUrl, e.Source,
                e.Disposition.ToString(), e.Tag, e.Detail))]);
    }
}
