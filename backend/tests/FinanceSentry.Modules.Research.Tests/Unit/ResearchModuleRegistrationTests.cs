namespace FinanceSentry.Modules.Research.Tests.Unit;

using FinanceSentry.Modules.Research;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Application.Services.Etf;
using FinanceSentry.Modules.Research.Infrastructure.Sources;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using FinanceSentry.Core.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

/// <summary>
/// DI-registration behavior of the analyst-source flags (feature 037): MarketBeat demotion is a
/// config flip (FR-004), and the retired Yahoo analyst scraper must not resurface as a source.
/// </summary>
public sealed class ResearchModuleRegistrationTests
{
    [Fact]
    public void Marketbeat_enabled_by_default_registers_exactly_one_actions_source()
    {
        var services = BuildServices([]);

        var sources = services.Where(d => d.ServiceType == typeof(IAnalystActionsSource)).ToList();

        sources.Should().HaveCount(1, "MarketBeat is the sole per-action source since 037 retired the Yahoo scraper");
    }

    [Fact]
    public void Marketbeat_disabled_registers_no_actions_sources()
    {
        var services = BuildServices(new Dictionary<string, string?>
        {
            ["AnalystSources:Marketbeat:Enabled"] = "false",
        });

        services.Should().NotContain(d => d.ServiceType == typeof(IAnalystActionsSource));
        services.Should().NotContain(d => d.ServiceType == typeof(MarketBeatAnalystActionsSource));
    }

    [Fact]
    public void Recommendation_trends_service_is_always_registered()
    {
        var services = BuildServices([]);

        services.Should().Contain(d =>
            d.ServiceType == typeof(IRecommendationTrendsService) &&
            d.ImplementationType == typeof(FinnhubRecommendationTrendsService),
            "the trends service no-ops via IsConfigured, so the DI graph stays stable without a key");
    }

    [Fact]
    public void Fund_holdings_source_resolves_with_the_issuer_files_bound_from_configuration()
    {
        var services = BuildServices(new Dictionary<string, string?>
        {
            ["FundHoldings:IssuerFiles:VWCE"] = "https://issuer.example/vwce.csv",
        });
        services.AddLogging();
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IFundHoldingsSource>().Should().BeOfType<FundHoldingsSource>();
        provider.GetRequiredService<IOptions<FundHoldingsOptions>>().Value.IssuerFiles["vwce"]
            .Should().Be("https://issuer.example/vwce.csv");
    }

    [Fact]
    public void The_scorecard_reader_is_given_the_etf_look_through()
    {
        var services = BuildServices([]);

        services.Should().Contain(d => d.ServiceType == typeof(EtfLookThroughReader));
        services.Should().Contain(d => d.ServiceType == typeof(ScorecardValuationReader));
        services.Should().Contain(d => d.ServiceType == typeof(ScorecardReader));
    }

    [Fact]
    public void Sec_edgar_user_agent_carries_the_configured_email_contact()
    {
        var userAgent = SecEdgarUserAgent(new Dictionary<string, string?>
        {
            ["SecEdgar:Contact"] = "ops@example.org",
        });

        userAgent.Should().Be("FinanceSentry/1.0 (contact: ops@example.org)");
        SecEdgarOptions.IsEmailContact(ContactIn(userAgent)).Should().BeTrue(
            "the SEC edge answers 403 to a User-Agent without an email address (#1443)");
    }

    [Fact]
    public void Sec_edgar_user_agent_has_no_project_url_fallback_when_the_contact_is_unset()
    {
        var userAgent = SecEdgarUserAgent([]);

        userAgent.Should().Be("FinanceSentry/1.0");
        userAgent.Should().NotContain("github.com");
    }

    [Theory]
    [InlineData("ops@example.org")]
    [InlineData("Ops Team <ops@example.org>")]
    [InlineData("  ops@example.org  ")]
    public void An_email_contact_is_accepted(string contact)
        => SecEdgarOptions.IsEmailContact(contact).Should().BeTrue();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("https://github.com/lifekit-hq/finance-sentry")]
    [InlineData("not-an-email@")]
    public void A_contact_without_an_email_is_rejected(string? contact)
        => SecEdgarOptions.IsEmailContact(contact).Should().BeFalse();

    [Fact]
    public async Task The_guard_forwards_a_request_when_the_contact_is_an_email()
    {
        var inner = new RecordingHandler();
        using var client = GuardedClient("ops@example.org", inner);

        using var response = await client.GetAsync("https://www.sec.gov/files/company_tickers.json");

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        inner.Requests.Should().ContainSingle();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("https://github.com/lifekit-hq/finance-sentry")]
    public async Task The_guard_sends_nothing_without_an_email_contact(string? contact)
    {
        var inner = new RecordingHandler();
        using var client = GuardedClient(contact, inner);

        var send = () => client.GetAsync("https://www.sec.gov/files/company_tickers.json");

        (await send.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*SEC_EDGAR_CONTACT*email*");
        inner.Requests.Should().BeEmpty("a request the SEC edge will answer with 403 must not be sent at all");
    }

    [Fact]
    public async Task The_registered_sec_edgar_client_refuses_to_send_without_a_contact()
    {
        using var provider = BuildServices([]).BuildServiceProvider();
        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(SecEdgarService.HttpClientName);

        var send = () => client.GetAsync("https://www.sec.gov/files/company_tickers.json");

        (await send.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*SEC_EDGAR_CONTACT*");
    }

    [Fact]
    public async Task A_ticker_map_over_a_guarded_client_without_a_contact_reports_unavailable_naming_the_setting()
    {
        var inner = new RecordingHandler();
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(SecEdgarService.HttpClientName)).Returns(() => GuardedClient(null, inner));
        var map = new EdgarTickerMap(
            factory.Object,
            new ManualTimeProvider(new DateTimeOffset(2026, 11, 16, 8, 0, 0, TimeSpan.Zero)),
            NullLogger.Instance,
            new EdgarTickerMapOptions { RetryDelays = [] });

        var read = await map.GetAsync(SecEdgarService.HttpClientName, CancellationToken.None);

        read.Map.Should().BeNull();
        inner.Requests.Should().BeEmpty();
        var health = map.Read();
        health.State.Should().Be(DataSourceHealthState.Unavailable);
        health.Detail.Should().Contain("SEC_EDGAR_CONTACT");
    }

    private static HttpClient GuardedClient(string? contact, HttpMessageHandler inner)
        => new(new SecContactGuardHandler(Options.Create(new SecEdgarOptions { Contact = contact })) { InnerHandler = inner });

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
        }
    }

    private static string? ContactIn(string userAgent)
        => userAgent.Contains("contact: ") ? userAgent[(userAgent.IndexOf("contact: ", StringComparison.Ordinal) + 9)..].TrimEnd(')') : null;

    private static string SecEdgarUserAgent(Dictionary<string, string?> settings)
    {
        using var provider = BuildServices(settings).BuildServiceProvider();
        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(SecEdgarService.HttpClientName);
        return client.DefaultRequestHeaders.UserAgent.ToString();
    }

    private static ServiceCollection BuildServices(Dictionary<string, string?> settings)
    {
        settings["ConnectionStrings:Default"] = "Host=localhost;Database=test;Username=t;Password=t";
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var services = new ServiceCollection();
        services.AddResearchModule(config);
        return services;
    }
}
