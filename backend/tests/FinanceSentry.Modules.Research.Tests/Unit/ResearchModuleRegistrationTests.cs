namespace FinanceSentry.Modules.Research.Tests.Unit;

using FinanceSentry.Modules.Research;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Application.Services.Etf;
using FinanceSentry.Modules.Research.Infrastructure.Sources;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
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

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("https://github.com/lifekit-hq/finance-sentry")]
    public void Production_refuses_to_start_without_an_email_contact(string? contact)
    {
        var result = new SecEdgarOptionsValidator(new FakeEnvironment(Environments.Production))
            .Validate(null, new SecEdgarOptions { Contact = contact });

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("SEC_EDGAR_CONTACT");
    }

    [Fact]
    public void Production_starts_with_an_email_contact()
    {
        var result = new SecEdgarOptionsValidator(new FakeEnvironment(Environments.Production))
            .Validate(null, new SecEdgarOptions { Contact = "ops@example.org" });

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void A_non_production_host_starts_without_a_contact()
    {
        var result = new SecEdgarOptionsValidator(new FakeEnvironment(Environments.Development))
            .Validate(null, new SecEdgarOptions());

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void A_production_host_without_a_contact_fails_when_the_options_are_first_read()
    {
        var services = BuildServices([]);
        services.AddSingleton<IHostEnvironment>(new FakeEnvironment(Environments.Production));
        using var provider = services.BuildServiceProvider();

        var read = () => provider.GetRequiredService<IOptions<SecEdgarOptions>>().Value;

        read.Should().Throw<OptionsValidationException>().WithMessage("*SEC_EDGAR_CONTACT*");
    }

    [Fact]
    public void A_production_host_with_a_contact_reads_the_options()
    {
        var services = BuildServices(new Dictionary<string, string?> { ["SecEdgar:Contact"] = "ops@example.org" });
        services.AddSingleton<IHostEnvironment>(new FakeEnvironment(Environments.Production));
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IOptions<SecEdgarOptions>>().Value.Contact.Should().Be("ops@example.org");
    }

    private sealed class FakeEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "test";
        public string ContentRootPath { get; set; } = "/";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
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
