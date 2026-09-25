namespace FinanceSentry.Infrastructure.Fx;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

public static class ExchangeRateServiceCollectionExtensions
{
    private const string DefaultBaseUrl = "https://open.er-api.com/";
    private const string DefaultNbuBaseUrl = "https://bank.gov.ua/";
    private const string DefaultFrankfurterBaseUrl = "https://api.frankfurter.dev/";
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
        var baseUrl = config["ExchangeRates:BaseUrl"];
        if (string.IsNullOrWhiteSpace(baseUrl))
            baseUrl = DefaultBaseUrl;

        services.AddHttpClient<IExchangeRateProvider, OpenErApiExchangeRateProvider>(client =>
        {
            client.BaseAddress = new Uri(baseUrl);
            client.Timeout = TimeSpan.FromSeconds(RequestTimeoutSeconds);
        });

        services.AddTransient<ExchangeRateRefreshJob>();

        AddHistoricalRates(services, config);

        return services;
    }

    private static void AddHistoricalRates(IServiceCollection services, IConfiguration config)
    {
        var nbuBaseUrl = config["ExchangeRates:NbuBaseUrl"];
        if (string.IsNullOrWhiteSpace(nbuBaseUrl))
            nbuBaseUrl = DefaultNbuBaseUrl;

        var frankfurterBaseUrl = config["ExchangeRates:FrankfurterBaseUrl"];
        if (string.IsNullOrWhiteSpace(frankfurterBaseUrl))
            frankfurterBaseUrl = DefaultFrankfurterBaseUrl;

        // Historical windows can span years, so these get a longer timeout than the
        // single-shot latest-rates call.
        services.AddHttpClient<NbuHistoricalExchangeRateProvider>(client =>
        {
            client.BaseAddress = new Uri(nbuBaseUrl);
            client.Timeout = TimeSpan.FromSeconds(HistoryRequestTimeoutSeconds);
        });

        services.AddHttpClient<FrankfurterHistoricalExchangeRateProvider>(client =>
        {
            client.BaseAddress = new Uri(frankfurterBaseUrl);
            client.Timeout = TimeSpan.FromSeconds(HistoryRequestTimeoutSeconds);
        });

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
