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
    public void Parse_returns_null_when_result_block_absent()
    {
        const string empty = """{ "quoteSummary": { "result": [], "error": "Not Found" } }""";

        YahooValuationDataService.Parse(empty, "ZZZZ").Should().BeNull();
    }
}
