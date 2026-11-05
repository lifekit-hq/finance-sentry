namespace FinanceSentry.Modules.Research.Tests.Unit.Dossier;

using System.Text.Json;
using FinanceSentry.Modules.Research.Infrastructure.Sources;
using FluentAssertions;
using Xunit;

public class YahooPriceBarParseTests
{
    // 2026-10-12 13:30 UTC, the New York open (UTC-4 in October).
    private const long Open = 1791811800;
    private const long Day = 86_400;
    private const int NewYorkOffset = -14_400;

    [Fact]
    public void Parse_ReadsAllFiveSeriesOldestFirst_OnTheRealInstant()
    {
        var series = YahooPriceBarService.Parse(Chart(
            "usd", NewYorkOffset, [Open + Day, Open],
            open: ["11", "10"], high: ["12", "11"], low: ["10.5", "9.5"], close: ["11.5", "10.5"], volume: ["2000", "1000"],
            zone: "America/New_York"), "1d");

        series.Currency.Should().Be("USD");
        series.TimeZone.Should().Be("America/New_York");
        series.Bars.Select(b => b.Close).Should().Equal(10.5m, 11.5m);
        series.Bars[0].Should().Be(new FinanceSentry.Modules.Research.Application.Services.PriceBar(
            Open, NewYorkOffset, 10m, 11m, 9.5m, 10.5m, 1000));
        series.Bars[0].Date.Should().Be(new DateOnly(2026, 10, 12));
    }

    [Fact]
    public void Parse_AsiaOpenBeforeMidnightUtc_KeepsTheExchangeDate()
    {
        // 2026-10-13 09:00 in Tokyo is 00:00 UTC; 2026-10-13 10:00 in Sydney is 23:00 UTC the day before.
        const long sydneyOpen = 1791846000;   // 2026-10-12 23:00 UTC
        var series = YahooPriceBarService.Parse(Chart("aud", 39_600, [sydneyOpen], ["1"], ["1"], ["1"], ["1"], ["1"], zone: "Australia/Sydney"), "5m");

        series.Bars.Single().Date.Should().Be(new DateOnly(2026, 10, 13));
    }

    [Fact]
    public void Parse_BarsAcrossADstChange_EachReadTheExchangeClockOfItsOwnDay()
    {
        // US DST ended 2026-11-01. Yahoo reports only today's offset (UTC-5); a 09:30 New York open is
        // 13:30 UTC (EDT) on Fri 2026-10-30 and 14:30 UTC (EST) on Mon 2026-11-02.
        var beforeChange = new DateTimeOffset(2026, 10, 30, 13, 30, 0, TimeSpan.Zero).ToUnixTimeSeconds();
        var afterChange = new DateTimeOffset(2026, 11, 2, 14, 30, 0, TimeSpan.Zero).ToUnixTimeSeconds();

        var series = YahooPriceBarService.Parse(Chart(
            "usd", -18_000, [beforeChange, afterChange], ["1", "1"], ["1", "1"], ["1", "1"], ["1", "1"], ["1", "1"],
            zone: "America/New_York"), "5m");

        series.Bars.Select(b => DateTimeOffset.FromUnixTimeSeconds(b.Time + b.Offset).UtcDateTime)
            .Should().Equal(new DateTime(2026, 10, 30, 9, 30, 0), new DateTime(2026, 11, 2, 9, 30, 0));
    }

    [Fact]
    public void Parse_UnknownZone_FallsBackToTheReportedOffset()
    {
        var series = YahooPriceBarService.Parse(Chart(
            "usd", NewYorkOffset, [Open], ["1"], ["1"], ["1"], ["1"], ["1"], zone: "Nowhere/Atlantis"), "5m");

        series.Bars.Single().Should().Match<FinanceSentry.Modules.Research.Application.Services.PriceBar>(b => b.Time == Open && b.Offset == NewYorkOffset);
    }

    [Fact]
    public void Parse_RowMissingAnyPrice_IsDropped_NotFilled()
    {
        var series = YahooPriceBarService.Parse(Chart(
            "usd", 0, [Open, Open + Day, Open + (2 * Day)],
            open: ["10", "null", "12"], high: ["11", "11", "13"], low: ["9", "9", "11"], close: ["10.5", "10", "null"],
            volume: ["100", "100", "100"]), "1d");

        series.Bars.Should().ContainSingle().Which.Close.Should().Be(10.5m);
    }

    [Fact]
    public void Parse_NullVolume_IsZero_AndHighBelowLowIsDropped()
    {
        var series = YahooPriceBarService.Parse(Chart(
            "usd", 0, [Open, Open + Day], ["10", "10"], ["11", "8"], ["9", "9"], ["10", "10"], ["null", "5"]), "1d");

        series.Bars.Should().ContainSingle().Which.Volume.Should().Be(0);
    }

    [Fact]
    public void Parse_IntradayRowRepeatingAnInstant_KeepsOneBar()
    {
        var series = YahooPriceBarService.Parse(Chart(
            "usd", 0, [Open, Open], ["10", "10"], ["11", "12"], ["9", "9"], ["10", "11"], ["1", "2"]), "5m");

        series.Bars.Should().ContainSingle().Which.High.Should().Be(12m);
    }

    [Theory]
    [InlineData("1d")]
    [InlineData("1w")]
    [InlineData("1mo")]
    public void Parse_LiveRowOnAnAlreadyListedDate_KeepsOneBarPerDate(string interval)
    {
        // The period's own row and the live row Yahoo appends for the same date carry different timestamps.
        var series = YahooPriceBarService.Parse(Chart(
            "usd", NewYorkOffset, [Open, Open + 3_600], ["10", "10"], ["11", "12"], ["9", "9"], ["10", "11"], ["1", "2"],
            zone: "America/New_York"), interval);

        series.Bars.Should().ContainSingle().Which.Close.Should().Be(11m);
    }

    [Fact]
    public void Parse_FallBackRepeatedHour_KeepsBothBarsInRealOrder()
    {
        // Europe/London left BST on 2026-10-25: 00:30 UTC reads 01:30 BST, 01:30 UTC reads 01:30 GMT.
        var bst = new DateTimeOffset(2026, 10, 25, 0, 30, 0, TimeSpan.Zero).ToUnixTimeSeconds();
        var gmt = new DateTimeOffset(2026, 10, 25, 1, 30, 0, TimeSpan.Zero).ToUnixTimeSeconds();

        var series = YahooPriceBarService.Parse(Chart(
            "gbp", 0, [gmt, bst], ["2", "1"], ["2", "1"], ["2", "1"], ["2", "1"], ["1", "1"],
            zone: "Europe/London"), "5m");

        series.Bars.Select(b => b.Close).Should().Equal(1m, 2m);
        series.Bars.Select(b => b.Time).Should().BeInAscendingOrder().And.OnlyHaveUniqueItems().And.Equal(bst, gmt);
        series.Bars.Select(b => b.Time + b.Offset).Should().Equal(gmt, gmt);
    }

    [Fact]
    public void Parse_MinorUnitQuote_IsReturnedInTheMajorCurrency()
    {
        var series = YahooPriceBarService.Parse(Chart("GBp", 0, [Open], ["1250"], ["1300"], ["1200"], ["1262.5"], ["10"]), "1d");

        series.Currency.Should().Be("GBP");
        series.Bars.Single().High.Should().Be(13m);
        series.Bars.Single().Close.Should().Be(12.625m);
    }

    [Fact]
    public void Parse_NoResult_IsEmpty()
    {
        using var doc = JsonDocument.Parse("""{"chart":{"result":[]}}""");

        YahooPriceBarService.Parse(doc.RootElement, "1d").Bars.Should().BeEmpty();
    }

    private static JsonElement Chart(
        string currency, int gmtOffset, long[] timestamps,
        string[] open, string[] high, string[] low, string[] close, string[] volume,
        string? zone = null)
    {
        string Join(string[] values) => string.Join(",", values);
        var json =
            "{\"chart\":{\"result\":[{" +
            $"\"meta\":{{\"currency\":\"{currency}\",\"gmtoffset\":{gmtOffset}" + (zone is null ? string.Empty : $",\"exchangeTimezoneName\":\"{zone}\"") + "}," +
            $"\"timestamp\":[{string.Join(",", timestamps)}]," +
            "\"indicators\":{\"quote\":[{" +
            $"\"open\":[{Join(open)}],\"high\":[{Join(high)}],\"low\":[{Join(low)}]," +
            $"\"close\":[{Join(close)}],\"volume\":[{Join(volume)}]" +
            "}]}}]}}";
        return JsonDocument.Parse(json).RootElement.Clone();
    }
}
