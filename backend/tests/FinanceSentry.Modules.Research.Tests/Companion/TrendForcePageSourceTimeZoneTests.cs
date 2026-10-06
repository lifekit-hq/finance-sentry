namespace FinanceSentry.Modules.Research.Tests.Companion;

using FinanceSentry.Modules.Research.Infrastructure.Sources;
using FluentAssertions;
using Xunit;

/// <summary>
/// Pins the TrendForce date parse to the same instant whatever the host zone: an offset-less date is UTC,
/// a dated-with-offset value keeps its instant. Swaps <c>TZ</c> process-wide, so it runs alone.
/// </summary>
[CollectionDefinition(nameof(TrendForcePageSourceTimeZoneTests), DisableParallelization = true)]
public sealed class TrendForcePageSourceTimeZoneCollection;

[Collection(nameof(TrendForcePageSourceTimeZoneTests))]
public sealed class TrendForcePageSourceTimeZoneTests
{
    private const string PageUrl = "https://www.trendforce.com/presscenter/news";

    private static string Page(string dateMarkup) => $$"""
    <html><body><div class="press-list"><article>
      <a href="/presscenter/news/20260721-12345.html"><h3 class="title">DRAM prices rise</h3></a>
      {{dateMarkup}}
    </article></div></body></html>
    """;

    [Theory]
    [InlineData("Europe/London")]
    [InlineData("America/Los_Angeles")]
    [InlineData("Asia/Tokyo")]
    public async Task Dates_resolve_to_the_same_instant_in_any_host_zone(string zone)
    {
        var original = Environment.GetEnvironmentVariable("TZ");
        try
        {
            Environment.SetEnvironmentVariable("TZ", zone);
            TimeZoneInfo.ClearCachedData();
            TimeZoneInfo.Local.Id.Should().Be(zone, "the test must really run under a non-UTC zone");

            var noOffset = await TrendForcePageSource.ParseAsync(Page("""<time datetime="2026-07-21T10:30:00">x</time>"""), PageUrl);
            var withOffset = await TrendForcePageSource.ParseAsync(Page("""<time datetime="2026-07-21T10:30:00+02:00">x</time>"""), PageUrl);
            var textual = await TrendForcePageSource.ParseAsync(Page("""<h4 class="color-green">21 July 2026</h4>"""), PageUrl);

            noOffset[0].PublishedAt.Should().Be(new DateTimeOffset(2026, 7, 21, 10, 30, 0, TimeSpan.Zero));
            noOffset[0].PublishedAt.Offset.Should().Be(TimeSpan.Zero);
            withOffset[0].PublishedAt.Should().Be(new DateTimeOffset(2026, 7, 21, 8, 30, 0, TimeSpan.Zero));
            textual[0].PublishedAt.Should().Be(new DateTimeOffset(2026, 7, 21, 0, 0, 0, TimeSpan.Zero));
            textual[0].PublishedAt.Offset.Should().Be(TimeSpan.Zero);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TZ", original);
            TimeZoneInfo.ClearCachedData();
        }
    }
}
