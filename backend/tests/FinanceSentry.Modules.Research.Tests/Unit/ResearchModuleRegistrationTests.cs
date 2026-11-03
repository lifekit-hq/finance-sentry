namespace FinanceSentry.Modules.Research.Tests.Unit;

using FinanceSentry.Modules.Research;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Infrastructure.Sources;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
    public void Sec_edgar_user_agent_defaults_to_the_project_url_without_an_email_address()
    {
        var userAgent = SecEdgarUserAgent([]);

        userAgent.Should().Be("FinanceSentry/1.0 (contact: https://github.com/lifekit-hq/finance-sentry)");
        userAgent.Should().NotContain("@");
    }

    [Fact]
    public void Sec_edgar_user_agent_carries_the_configured_contact()
    {
        var userAgent = SecEdgarUserAgent(new Dictionary<string, string?>
        {
            ["SecEdgar:Contact"] = "ops@example.org",
        });

        userAgent.Should().Be("FinanceSentry/1.0 (contact: ops@example.org)");
    }

    [Fact]
    public void Sec_edgar_user_agent_falls_back_to_the_default_when_the_contact_is_blank()
    {
        var userAgent = SecEdgarUserAgent(new Dictionary<string, string?>
        {
            ["SecEdgar:Contact"] = "",
        });

        userAgent.Should().Be("FinanceSentry/1.0 (contact: https://github.com/lifekit-hq/finance-sentry)");
    }

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
