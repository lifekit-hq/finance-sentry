namespace FinanceSentry.Modules.Research.Tests.Valuation;

using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Tests.Companion;
using FluentAssertions;
using Xunit;

public sealed class DividendYieldReaderTests
{
    private static ValuationSnapshot Snapshot(string ticker, DateTimeOffset at, decimal? yield) =>
        new() { Ticker = ticker, CapturedAt = at, Price = 100m, DividendYield = yield };

    [Fact]
    public async Task GetLatestAsync_ReadsEachTickersLatestSnapshot_AndKeepsAMissingYieldNull()
    {
        await using var db = CompanionTestContext.Create();
        var t = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
        db.ValuationSnapshots.AddRange(
            Snapshot("AAPL", t, 0.004m),
            Snapshot("AAPL", t.AddDays(2), 0.005m),
            Snapshot("AAPL", t.AddDays(1), 0.006m),
            Snapshot("TSLA", t, 0.02m),
            Snapshot("TSLA", t.AddDays(1), null),
            Snapshot("KO", t, 0.03m));
        await db.SaveChangesAsync();

        var latest = await new DividendYieldReader(db).GetLatestAsync(["aapl", " TSLA ", "NVDA"]);

        latest.Should().BeEquivalentTo(new Dictionary<string, decimal?> { ["AAPL"] = 0.005m, ["TSLA"] = null });
    }

    [Fact]
    public async Task GetLatestAsync_WithNoTickers_IsEmpty()
    {
        await using var db = CompanionTestContext.Create();

        var latest = await new DividendYieldReader(db).GetLatestAsync([]);

        latest.Should().BeEmpty();
    }
}
