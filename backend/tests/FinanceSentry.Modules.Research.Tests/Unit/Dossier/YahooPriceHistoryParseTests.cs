namespace FinanceSentry.Modules.Research.Tests.Unit.Dossier;

using System.Text.Json;
using FinanceSentry.Modules.Research.Infrastructure.Sources;
using FluentAssertions;
using Xunit;

public class YahooPriceHistoryParseTests
{
    private const long Monday = 1791806400;   // 2026-10-12 12:00 UTC
    private const long Day = 86_400;

    [Fact]
    public void Parse_ReadsTheCurrencyAndClosesOldestFirst()
    {
        var history = YahooPriceHistoryService.Parse(Chart("usd", [Monday + Day, Monday], ["101.5", "100"]));

        history.Currency.Should().Be("USD");
        history.Closes.Select(c => c.Close).Should().Equal(100m, 101.5m);
        history.Closes[0].Date.Should().Be(new DateOnly(2026, 10, 12));
    }

    [Fact]
    public void Parse_SkipsNullClosesAndKeepsOneBarPerDate()
    {
        // A weekly series carries the live day as a second bar in its last period.
        var history = YahooPriceHistoryService.Parse(
            Chart("EUR", [Monday, Monday + Day, Monday + Day + 60], ["10", "null", "12"]));

        history.Closes.Select(c => c.Close).Should().Equal(10m, 12m);
    }

    [Fact]
    public void Parse_NoResult_IsEmpty()
    {
        using var doc = JsonDocument.Parse("""{"chart":{"result":[]}}""");

        YahooPriceHistoryService.Parse(doc.RootElement).Closes.Should().BeEmpty();
    }

    [Fact]
    public void Parse_NoCurrency_IsNull()
    {
        using var doc = JsonDocument.Parse(
            $$$"""{"chart":{"result":[{"timestamp":[{{{Monday}}}],"indicators":{"quote":[{"close":[5]}]}}]}}""");

        YahooPriceHistoryService.Parse(doc.RootElement).Currency.Should().BeNull();
    }

    private static JsonElement Chart(string currency, long[] timestamps, string[] closes)
    {
        var json = $$$"""
            {"chart":{"result":[{"meta":{"currency":"{{{currency}}}"},
            "timestamp":[{{{string.Join(",", timestamps)}}}],
            "indicators":{"quote":[{"close":[{{{string.Join(",", closes)}}}]}]}}]}}
            """;
        return JsonDocument.Parse(json).RootElement.Clone();
    }
}
