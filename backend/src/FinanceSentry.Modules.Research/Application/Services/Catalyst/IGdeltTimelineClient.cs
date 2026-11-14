namespace FinanceSentry.Modules.Research.Application.Services.Catalyst;

/// <summary>One day of GDELT article volume for a phrase.</summary>
/// <param name="Articles">Articles that day matching the phrase.</param>
/// <param name="TotalArticles">All articles GDELT monitored that day; null when the response carried no normaliser.</param>
public sealed record GdeltDay(DateOnly Day, long Articles, long? TotalArticles);

/// <summary>GDELT DOC 2.0 article-volume timeline for a phrase. Throws <see cref="CatalystFeedException"/> on any failure.</summary>
public interface IGdeltTimelineClient
{
    Task<IReadOnlyList<GdeltDay>> GetDailyVolumeAsync(string phrase, int days, CancellationToken ct = default);
}
