namespace FinanceSentry.Modules.Research.Tests.Companion;

using FinanceSentry.Modules.Research.Infrastructure.Sources;
using FluentAssertions;
using Xunit;

/// <summary>
/// External-contract test (constitution-mandated) for Yahoo's
/// <c>quoteSummary?modules=price,summaryDetail,defaultKeyStatistics,financialData,assetProfile</c>
/// shape (feature 030, T030). Asserts the parser reads each valuation field from the documented path,
/// tolerates missing fields (null, never zero — FR-006), and flags non-equity quote types.
/// </summary>
public sealed class YahooValuationModulesContractTests
{
    private const string SampleJson = """
    {
      "quoteSummary": {
        "result": [
          {
            "price": {
              "quoteType": "EQUITY",
              "longName": "McDonald's Corporation",
              "shortName": "McDonald's Corporation",
              "regularMarketPrice": { "raw": 265.1, "fmt": "265.10" }
            },
            "summaryDetail": {
              "trailingPE": { "raw": 24.1, "fmt": "24.10" },
              "forwardPE": { "raw": 21.6, "fmt": "21.60" },
              "dividendYield": { "raw": 0.0295, "fmt": "2.95%" }
            },
            "defaultKeyStatistics": {
              "enterpriseValue": { "raw": 249000000000, "fmt": "249B" },
              "forwardPE": { "raw": 21.7, "fmt": "21.70" }
            },
            "financialData": {
              "ebitda": { "raw": 15000000000, "fmt": "15B" },
              "targetMeanPrice": { "raw": 336.0, "fmt": "336.00" }
            },
            "assetProfile": {
              "sector": "Consumer Cyclical",
              "industry": "Restaurants"
            }
          }
        ],
        "error": null
      }
    }
    """;

    [Fact]
    public void Parse_reads_all_valuation_fields_from_documented_paths()
    {
        var metrics = YahooValuationDataService.Parse(SampleJson, "mcd");

        metrics.Should().NotBeNull();
        metrics!.Ticker.Should().Be("MCD");
        metrics.NotApplicable.Should().BeFalse();
        metrics.Price.Should().Be(265.1m);
        metrics.TrailingPe.Should().Be(24.1m);
        metrics.ForwardPe.Should().Be(21.6m);
        metrics.DividendYield.Should().Be(0.0295m);
        metrics.ConsensusTarget.Should().Be(336.0m);
        metrics.EvToEbitda.Should().Be(16.6m, "249B enterprise value / 15B EBITDA rounds to 16.60");
        metrics.Sector.Should().Be("Consumer Cyclical");
        metrics.Industry.Should().Be("Restaurants");
        metrics.Name.Should().Be("McDonald's Corporation");
    }

    [Fact]
    public void Parse_leaves_missing_metrics_null_never_zero()
    {
        const string sparse = """
        {
          "quoteSummary": {
            "result": [
              { "price": { "quoteType": "EQUITY" }, "summaryDetail": { "trailingPE": { "raw": 30.0 } } }
            ],
            "error": null
          }
        }
        """;

        var metrics = YahooValuationDataService.Parse(sparse, "AAPL");

        metrics.Should().NotBeNull();
        metrics!.TrailingPe.Should().Be(30.0m);
        metrics.ForwardPe.Should().BeNull();
        metrics.EvToEbitda.Should().BeNull("EV/EBITDA must not be fabricated when either input is missing");
        metrics.DividendYield.Should().BeNull();
        metrics.ConsensusTarget.Should().BeNull();
    }

    [Theory]
    [InlineData("""{ "summaryDetail": { "beta": { "raw": 0.43 } } }""", 0.43)]
    [InlineData("""{ "defaultKeyStatistics": { "beta": { "raw": 1.25 } } }""", 1.25)]
    [InlineData("""{ "summaryDetail": { "beta": { "raw": 0.43 } }, "defaultKeyStatistics": { "beta": { "raw": 1.25 } } }""", 0.43)]
    public void Parse_reads_beta_from_summary_detail_then_key_statistics(string modules, double expected)
    {
        var json = $$"""{ "quoteSummary": { "result": [ { "price": { "quoteType": "EQUITY" }, {{modules.Trim()[1..^1]}} } ], "error": null } }""";

        YahooValuationDataService.Parse(json, "MCD")!.Beta.Should().Be((decimal)expected);
    }

    [Fact]
    public void Parse_leaves_beta_null_when_yahoo_has_none()
    {
        const string noBeta = """
        { "quoteSummary": { "result": [ { "price": { "quoteType": "EQUITY" }, "summaryDetail": { "trailingPE": { "raw": 30.0 } } } ], "error": null } }
        """;

        YahooValuationDataService.Parse(noBeta, "MCD")!.Beta.Should().BeNull("a missing beta must not default to 1.0");
    }

    [Fact]
    public void Parse_flags_non_equity_quote_types_as_not_applicable()
    {
        const string crypto = """
        {
          "quoteSummary": {
            "result": [
              { "price": { "quoteType": "CRYPTOCURRENCY", "regularMarketPrice": { "raw": 150.0 } } }
            ],
            "error": null
          }
        }
        """;

        var metrics = YahooValuationDataService.Parse(crypto, "SOL-USD");

        metrics.Should().NotBeNull();
        metrics!.NotApplicable.Should().BeTrue();
        metrics.TrailingPe.Should().BeNull();
    }

    [Fact]
    public void Parse_pence_quote_keeps_the_quoted_unit_and_label()
    {
        const string london = """
        {
          "quoteSummary": {
            "result": [
              {
                "price": {
                  "quoteType": "EQUITY",
                  "currency": "GBp",
                  "regularMarketPrice": { "raw": 2650.0 }
                },
                "financialData": {
                  "targetMeanPrice": { "raw": 3000.0 },
                  "targetLowPrice": { "raw": 2400.0 },
                  "targetHighPrice": { "raw": 3600.0 }
                }
              }
            ],
            "error": null
          }
        }
        """;

        var metrics = YahooValuationDataService.Parse(london, "SHEL.L");

        metrics.Should().NotBeNull();
        metrics!.Currency.Should().Be("GBp");
        metrics.Price.Should().Be(2650m);
        metrics.ConsensusTarget.Should().Be(3000m);
        metrics.TargetLow.Should().Be(2400m);
        metrics.TargetHigh.Should().Be(3600m);
    }

    private static string QuoteAt(long? regularMarketTime) => $$"""
        { "quoteSummary": { "result": [ { "price": { "quoteType": "EQUITY", "marketState": "CLOSED"{{(regularMarketTime is { } t ? $", \"regularMarketTime\": {t}" : "")}} } } ], "error": null } }
        """;

    [Theory]
    [InlineData("2026-11-23T21:00:00Z", "2026-11-24T03:00:00Z", false)] // closed overnight, quote from the last session
    [InlineData("2026-11-27T21:00:00Z", "2026-11-28T12:00:00Z", false)] // Saturday, quote from Friday's session
    [InlineData("2026-11-27T21:00:00Z", "2026-11-30T09:00:00Z", false)] // Monday before the open, still Friday's session
    [InlineData("2026-11-18T21:00:00Z", "2026-11-24T15:00:00Z", true)] // days behind the last session
    public void Parse_derives_IsStale_from_the_age_of_the_price_not_the_closed_market_state(string quoteAt, string now, bool expected)
    {
        var quote = DateTimeOffset.Parse(quoteAt, System.Globalization.CultureInfo.InvariantCulture).ToUnixTimeSeconds();

        var metrics = YahooValuationDataService.Parse(
            QuoteAt(quote), "GRAB", DateTimeOffset.Parse(now, System.Globalization.CultureInfo.InvariantCulture));

        metrics!.IsStale.Should().Be(expected);
    }

    [Fact]
    public void Parse_treats_a_price_with_no_time_as_stale()
    {
        YahooValuationDataService.Parse(QuoteAt(null), "GRAB", DateTimeOffset.Parse("2026-11-24T03:00:00Z", System.Globalization.CultureInfo.InvariantCulture))!
            .IsStale.Should().BeTrue();
    }

    [Fact]
    public void Parse_returns_null_when_result_block_absent()
    {
        const string empty = """{ "quoteSummary": { "result": [], "error": "Not Found" } }""";

        YahooValuationDataService.Parse(empty, "ZZZZ").Should().BeNull();
    }

    [Fact]
    public void Parse_reads_comps_inputs_from_a_recorded_live_payload()
    {
        var json = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory, "Unit", "Fixtures", "Yahoo", "quotesummary-valuation-MCD-2026-11-14.json"));

        var comps = YahooValuationDataService.Parse(json, "MCD")!.Comps;

        comps.Should().NotBeNull();
        comps!.EnterpriseValue.Should().Be(245302280192m);
        comps.Ebitda.Should().Be(15075000320m);
        comps.TotalRevenue.Should().Be(27782000640m);
        comps.RevenueGrowth.Should().Be(0.011m);
        comps.EbitdaMargin.Should().Be(0.54262m);
        comps.TrailingEps.Should().Be(12.64m);
        comps.BookValuePerShare.Should().Be(-0.486m);
        comps.TotalDebt.Should().Be(54542999552m);
        comps.TotalCash.Should().Be(1192999936m);
        comps.SharesOutstanding.Should().Be(705784744m);
        comps.FinancialCurrency.Should().Be("USD");
        comps.PriceToBook.Should().BeNull("Yahoo returns an empty object for a negative book value, never a zero");
    }

    [Fact]
    public void Parse_reads_price_to_book_and_leaves_missing_comps_inputs_null()
    {
        const string sparse = """
        {
          "quoteSummary": {
            "result": [
              {
                "price": { "quoteType": "EQUITY" },
                "defaultKeyStatistics": { "priceToBook": { "raw": 1.4 } }
              }
            ],
            "error": null
          }
        }
        """;

        var comps = YahooValuationDataService.Parse(sparse, "BANK")!.Comps!;

        comps.PriceToBook.Should().Be(1.4m);
        comps.EnterpriseValue.Should().BeNull();
        comps.Ebitda.Should().BeNull();
        comps.TotalRevenue.Should().BeNull();
        comps.RevenueGrowth.Should().BeNull();
        comps.EbitdaMargin.Should().BeNull();
        comps.TotalDebt.Should().BeNull("a missing debt figure is unknown, not zero");
        comps.TotalCash.Should().BeNull();
        comps.SharesOutstanding.Should().BeNull();
        comps.FinancialCurrency.Should().BeNull();
    }
}
