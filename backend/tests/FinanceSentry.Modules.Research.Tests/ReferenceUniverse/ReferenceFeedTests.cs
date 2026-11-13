namespace FinanceSentry.Modules.Research.Tests.ReferenceUniverse;

using FinanceSentry.Modules.Research.Application.Services.ReferenceUniverse;
using FinanceSentry.Modules.Research.Infrastructure.Sources.ReferenceUniverse;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;
using static FinanceSentry.Modules.Research.Tests.ReferenceUniverse.ReferenceUniverseTestKit;

/// <summary>The four feeds against fixture payloads shaped like the real upstream files (no network).</summary>
public sealed class ReferenceFeedTests
{
    private static ReferenceUniverseOptions SmallMinimums() => new()
    {
        Sp500Url = "https://up/sp500.csv",
        Nasdaq100Url = "https://up/ndx.csv",
        FinanceDatabaseBaseUrl = "https://up/fd/",
        FinanceDatabaseExchanges = ["NMS", "NYQ"],
        MinSp500Members = 2,
        MinNasdaq100Members = 2,
        MinFinanceDatabaseMembers = 2,
    };

    [Fact]
    public async Task Sp500Feed_ReadsSymbolsInYahooFormAndDropsDuplicates()
    {
        var http = new StubHttpClientFactory(new()
        {
            ["https://up/sp500.csv"] = Sp500Csv(("AAPL", "Apple Inc."), ("BRK.B", "Berkshire Hathaway"), ("aapl", "Apple again")),
        });
        var feed = new Sp500ConstituentsFeed(http, Options.Create(SmallMinimums()));

        var snapshots = await feed.FetchAsync();

        var list = snapshots.Should().ContainSingle().Subject;
        list.ListKey.Should().Be("sp500");
        list.Members.Select(m => m.Ticker).Should().Equal("AAPL", "BRK-B");
        list.Members[0].Name.Should().Be("Apple Inc.");
    }

    [Fact]
    public async Task Nasdaq100Feed_ReadsTheBomPrefixedCsv()
    {
        var http = new StubHttpClientFactory(new()
        {
            ["https://up/ndx.csv"] = Nasdaq100Csv(("AAPL", "Apple Inc."), ("ABNB", "Airbnb, Inc. Class A")),
        });
        var feed = new Nasdaq100Feed(http, Options.Create(SmallMinimums()));

        var snapshots = await feed.FetchAsync();

        snapshots.Single().ListKey.Should().Be("nasdaq100");
        snapshots.Single().Members.Should().BeEquivalentTo(
            [new ReferenceMember("AAPL", "Apple Inc."), new ReferenceMember("ABNB", "Airbnb, Inc. Class A")]);
    }

    [Fact]
    public async Task FinanceDatabaseFeed_BuildsSectorAndIndustryListsFromEveryExchangeFile()
    {
        var http = new StubHttpClientFactory(new()
        {
            ["https://up/fd/NMS.csv"] = FinanceDatabaseCsv(
                ("MU", "Micron", "Information Technology", "Semiconductors", "False"),
                ("NVDA", "Nvidia", "Information Technology", "Semiconductors", "False"),
                ("OLD", "Gone Corp", "Information Technology", "Semiconductors", "True")),
            ["https://up/fd/NYQ.csv"] = FinanceDatabaseCsv(
                ("XOM", "Exxon", "Energy", "Oil & Gas Integrated", "False"),
                ("NOSECTOR", "Unclassified", "", "", "False")),
        });
        var feed = new FinanceDatabaseFeed(http, Options.Create(SmallMinimums()));

        var snapshots = await feed.FetchAsync();

        snapshots.ToDictionary(s => s.ListKey, s => s.Members.Select(m => m.Ticker).ToArray())
            .Should().BeEquivalentTo(new Dictionary<string, string[]>
            {
                ["sector:Information Technology"] = ["MU", "NVDA"],
                ["industry:Semiconductors"] = ["MU", "NVDA"],
                ["sector:Energy"] = ["XOM"],
                ["industry:Oil & Gas Integrated"] = ["XOM"],
            });
        http.Requested.Should().BeEquivalentTo("https://up/fd/NMS.csv", "https://up/fd/NYQ.csv");
    }

    [Fact]
    public async Task FinanceDatabaseFeed_FailsWhenOneExchangeFileIsMissing()
    {
        var http = new StubHttpClientFactory(new()
        {
            ["https://up/fd/NMS.csv"] = FinanceDatabaseCsv(("MU", "Micron", "Information Technology", "Semiconductors", "False")),
        });
        var feed = new FinanceDatabaseFeed(http, Options.Create(SmallMinimums()));

        var act = () => feed.FetchAsync();

        (await act.Should().ThrowAsync<ReferenceFeedException>()).WithMessage("*NYQ.csv*");
    }

    [Fact]
    public async Task Feeds_RefuseAPayloadSmallerThanARealList()
    {
        var http = new StubHttpClientFactory(new() { ["https://up/sp500.csv"] = Sp500Csv(("AAPL", "Apple")) });
        var options = SmallMinimums();
        options.MinSp500Members = 400;

        var act = () => new Sp500ConstituentsFeed(http, Options.Create(options)).FetchAsync();

        (await act.Should().ThrowAsync<ReferenceFeedException>()).WithMessage("*fewer than the 400*");
    }

    [Fact]
    public async Task Feeds_RefuseALayoutThatLostAColumn()
    {
        var http = new StubHttpClientFactory(new() { ["https://up/ndx.csv"] = "ticker,company\nAAPL,Apple\nMSFT,Microsoft\n" });

        var act = () => new Nasdaq100Feed(http, Options.Create(SmallMinimums())).FetchAsync();

        (await act.Should().ThrowAsync<ReferenceFeedException>()).WithMessage("*'symbol'*");
    }

    [Fact]
    public async Task Feeds_ReportAnUnreachableUpstreamAsAFeedFailure()
    {
        var http = new StubHttpClientFactory([]);

        var act = () => new Nasdaq100Feed(http, Options.Create(SmallMinimums())).FetchAsync();

        (await act.Should().ThrowAsync<ReferenceFeedException>()).WithMessage("*ndx.csv*");
    }

    [Fact]
    public async Task ThemeFeed_MaterialisesConfiguredThemesAsData()
    {
        var options = new ReferenceUniverseOptions
        {
            Themes = new()
            {
                ["Memory"] = ["MU", "sndk", "MU"],
                ["power"] = ["BRK.B"],
            },
        };

        var snapshots = await new ThemeListFeed(Options.Create(options)).FetchAsync();

        snapshots.Select(s => s.ListKey).Should().Equal("theme:memory", "theme:power");
        snapshots[0].Members.Select(m => m.Ticker).Should().Equal("MU", "SNDK");
        snapshots[1].Members.Single().Ticker.Should().Be("BRK-B");
    }

    [Fact]
    public async Task ThemeFeed_RefusesAThemeWithNoTickers()
    {
        var feed = new ThemeListFeed(Options.Create(new ReferenceUniverseOptions { Themes = new() { ["empty"] = [] } }));

        var act = () => feed.FetchAsync();

        (await act.Should().ThrowAsync<ReferenceFeedException>()).WithMessage("*'empty'*");
    }

    [Fact]
    public void DefaultConfiguration_DeclaresTheFiveInitialThemes()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "appsettings.reference-universe.json");
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));

        var themes = doc.RootElement.GetProperty(ReferenceUniverseOptions.SectionName).GetProperty("Themes")
            .EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetArrayLength());

        themes.Keys.Should().BeEquivalentTo("memory", "ai-infra", "power", "semiconductors", "energy");
        themes.Values.Should().OnlyContain(n => n >= 5);
    }
}
