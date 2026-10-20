namespace FinanceSentry.Infrastructure.Fx;

using FinanceSentry.Infrastructure.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

public static class ExchangeRateServiceCollectionExtensions
{
    private const int RequestTimeoutSeconds = 15;
    private const int HistoryRequestTimeoutSeconds = 30;

    /// <summary>
    /// Registers the live FX rate provider + refresh job, plus the historical-rate feeds
    /// used to price past payments. Base URLs are overridable via
    /// <c>ExchangeRates:BaseUrl</c>, <c>ExchangeRates:NbuBaseUrl</c> and
    /// <c>ExchangeRates:FrankfurterBaseUrl</c>.
    /// </summary>
    public static IServiceCollection AddExchangeRates(
        this IServiceCollection services, IConfiguration config)
    {
        services.AddOptions<ExchangeRatesOptions>()
            .Bind(config.GetSection(ExchangeRatesOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        var latestRatesTimeout = TimeSpan.FromSeconds(RequestTimeoutSeconds);
        services.AddHttpClient<IExchangeRateProvider, OpenErApiExchangeRateProvider>((sp, client) =>
        {
            client.BaseAddress = new Uri(sp.GetRequiredService<IOptions<ExchangeRatesOptions>>().Value.BaseUrl);
            client.Timeout = latestRatesTimeout;
        })
            .AddProviderResilience(latestRatesTimeout);

        services.AddTransient<ExchangeRateRefreshJob>();

        AddHistoricalRates(services);

        return services;
    }

    private static void AddHistoricalRates(IServiceCollection services)
    {
        // Historical windows can span years, so these get a longer timeout than the
        // single-shot latest-rates call.
        var historyTimeout = TimeSpan.FromSeconds(HistoryRequestTimeoutSeconds);
        services.AddHttpClient<NbuHistoricalExchangeRateProvider>((sp, client) =>
        {
            client.BaseAddress = new Uri(sp.GetRequiredService<IOptions<ExchangeRatesOptions>>().Value.NbuBaseUrl);
            client.Timeout = historyTimeout;
        })
            .AddProviderResilience(historyTimeout);

        services.AddHttpClient<FrankfurterHistoricalExchangeRateProvider>((sp, client) =>
        {
            client.BaseAddress = new Uri(sp.GetRequiredService<IOptions<ExchangeRatesOptions>>().Value.FrankfurterBaseUrl);
            client.Timeout = historyTimeout;
        })
            .AddProviderResilience(historyTimeout);

        // Registered as the interface so the caching service receives both, and routes by
        // currency: NBU is the only free feed carrying UAH, ECB covers the rest.
        services.AddTransient<IHistoricalExchangeRateProvider>(sp =>
            sp.GetRequiredService<NbuHistoricalExchangeRateProvider>());
        services.AddTransient<IHistoricalExchangeRateProvider>(sp =>
            sp.GetRequiredService<FrankfurterHistoricalExchangeRateProvider>());

        // IMemoryCache is the singleton here, so cached series outlive any one request.
        // The service itself stays scoped — it depends on typed HttpClients, which must not
        // be captured by a singleton (stale DNS / socket reuse).
        services.AddMemoryCache();
        services.AddScoped<IHistoricalExchangeRateService, CachingHistoricalExchangeRateService>();
        services.AddScoped<IEurReportingConverter, EurReportingConverter>();
    }
}
