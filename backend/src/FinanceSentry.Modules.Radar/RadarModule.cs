namespace FinanceSentry.Modules.Radar;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Radar.Application.Services;
using FinanceSentry.Modules.Radar.Domain.Ports;
using FinanceSentry.Modules.Radar.Domain.Repositories;
using FinanceSentry.Modules.Radar.Infrastructure.Jobs;
using FinanceSentry.Modules.Radar.Infrastructure.MarketData;
using FinanceSentry.Modules.Radar.Infrastructure.Persistence;
using FinanceSentry.Modules.Radar.Infrastructure.Persistence.Repositories;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using FinanceSentry.Infrastructure.Persistence;

public static class RadarModule
{
    internal sealed class ModuleRegistrar : IModuleRegistrar
    {
        public void Register(IServiceCollection services, IConfiguration config)
            => services.AddRadarModule(config);
    }

    private sealed class JobRegistrar : IJobRegistrar
    {
        public void RegisterJobs(IServiceProvider sp)
        {
            var mgr = sp.GetRequiredService<IRecurringJobManager>();
            var options = sp.GetRequiredService<IOptions<RadarOptions>>().Value;
            var regimeOptions = sp.GetRequiredService<IOptions<RegimeOptions>>().Value;

            mgr.AddOrUpdate<RegimeComputeJob>(
                "regime-compute",
                job => job.ExecuteAsync(CancellationToken.None),
                Cron.Daily(regimeOptions.ComputeHourUtc));

            mgr.AddOrUpdate<RadarIngestionJob>(
                "radar-ingestion",
                job => job.ExecuteAsync(CancellationToken.None),
                Cron.Daily(options.IngestionHourUtc));

            mgr.AddOrUpdate<RadarComputeJob>(
                "radar-compute",
                job => job.ExecuteAsync(CancellationToken.None),
                Cron.Daily(options.ComputeHourUtc));

            mgr.AddOrUpdate<RadarFreshnessWatchdogJob>(
                "radar-freshness-watchdog",
                job => job.ExecuteAsync(CancellationToken.None),
                Cron.Daily(options.ComputeHourUtc));

            // 412: weekly book-vs-SPY TWR brief; Monday 08:00 UTC.
            mgr.AddOrUpdate<BookPerformanceBriefJob>(
                "book-performance-brief",
                job => job.ExecuteAsync(CancellationToken.None),
                "0 8 * * 1");

            // 413: daily portfolio-state scanner; 02:00 UTC after banking/brokerage sync.
            mgr.AddOrUpdate<PortfolioScannerJob>(
                "portfolio-scanner",
                job => job.ExecuteAsync(CancellationToken.None),
                Cron.Daily(2));
        }
    }

    public static IServiceCollection AddRadarModule(
        this IServiceCollection services, IConfiguration config)
    {
        services.AddUpdatedAtStamping();
        services.AddDbContext<RadarDbContext>(
            (sp, o) => o.UseNpgsql(
                config.GetConnectionString("Default")!,
                b => b.MigrationsHistoryTable("__ef_migrations_history_radar", "public")).UseUpdatedAtStamping(sp));

        services.Configure<RadarOptions>(config.GetSection(RadarOptions.SectionName));
        services.Configure<RegimeOptions>(config.GetSection(RegimeOptions.SectionName));

        services.AddScoped<IDailyBarRepository, DailyBarRepository>();
        services.AddScoped<IRadarSignalRepository, RadarSignalRepository>();
        services.AddScoped<IRadarUniverseRepository, RadarUniverseRepository>();
        services.AddScoped<IRegimeReadingRepository, RegimeReadingRepository>();

        services.AddScoped<IRadarUniverseService, RadarUniverseService>();
        services.AddScoped<IStructureQueryService, StructureQueryService>();
        services.AddScoped<IRadarSignalWriter, RadarSignalWriter>();
        services.AddScoped<IRadarSignalReader, RadarSignalReader>();
        // #673: published read port - the Integration adapter reaches Radar signals only through it.
        services.AddScoped<ISubjectSignalReader, SubjectSignalReader>();
        services.AddScoped<IMarketStructureReader, MarketStructureReader>();

        services.AddScoped<IBookPerformanceService, BookPerformanceService>();
        services.AddScoped<BookPerformanceBriefJob>();
        services.AddScoped<RadarIngestionJob>();
        services.AddScoped<RadarComputeJob>();
        services.AddScoped<RadarFreshnessWatchdogJob>();
        services.AddScoped<PortfolioScannerJob>();

        services.AddHttpClient(YahooMarketHistorySource.HttpClientName, client =>
        {
            client.BaseAddress = new Uri("https://query1.finance.yahoo.com");
            client.Timeout = TimeSpan.FromSeconds(10);
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (compatible; FinanceSentry/1.0; +https://finance-sentry.local)");
        });
        services.AddScoped<IMarketHistorySource, YahooMarketHistorySource>();

        // Feature 021 — market regime: keyless-silent FRED yield-curve source + read-only port.
        var regimeOptions = config.GetSection(RegimeOptions.SectionName).Get<RegimeOptions>() ?? new RegimeOptions();
        services.AddHttpClient(FredYieldCurveSource.HttpClientName, client =>
        {
            client.BaseAddress = new Uri(regimeOptions.Fred.BaseUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(10);
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (compatible; FinanceSentry/1.0; +https://finance-sentry.local)");
        });
        services.AddScoped<IYieldCurveSource, FredYieldCurveSource>();
        services.AddScoped<IMarketRegimeSource, MarketRegimeSource>();
        services.AddScoped<RegimeComputeJob>();

        services.AddSingleton<IJobRegistrar, JobRegistrar>();

        return services;
    }
}
