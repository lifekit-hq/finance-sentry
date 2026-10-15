namespace FinanceSentry.Infrastructure.Connections;

using FinanceSentry.Core.Connections;
using FinanceSentry.Core.Interfaces;
using Hangfire;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

public static class ConnectionHealthServiceCollectionExtensions
{
    /// <summary>
    /// Registers the connection-health shadow recorder with its thresholds bound from the
    /// <c>ConnectionHealth</c> section. Each sync module calls it; only the first call registers.
    /// </summary>
    public static IServiceCollection AddConnectionHealthShadow(this IServiceCollection services, IConfiguration config)
    {
        if (services.Any(d => d.ServiceType == typeof(IConnectionHealthShadow)))
            return services;

        services.Configure<ConnectionHealthOptions>(config.GetSection(ConnectionHealthOptions.SectionName));
        services.AddSingleton<IPostConfigureOptions<ConnectionHealthSourceOptions>>(
            new BindSourceOptionsFromConfiguration(config.GetSection(ConnectionHealthOptions.SectionName)
                .GetSection(ConnectionHealthSourceOptions.SectionName)));
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IConnectionHealthShadow, ConnectionHealthShadowRecorder>();
        return services;
    }

    /// <summary>
    /// Registers a sync module's probes (<typeparamref name="TProbe"/>) with the shared
    /// <see cref="ConnectionHealthProbeJob"/>, which the first call registers together with its recurring schedule.
    /// </summary>
    public static IServiceCollection AddConnectionHealthProbe<TProbe>(this IServiceCollection services)
        where TProbe : class, IConnectionHealthProbe
    {
        services.AddScoped<IConnectionHealthProbe, TProbe>();
        services.TryAddScoped<ConnectionHealthProbeRecorder>();
        if (services.Any(d => d.ServiceType == typeof(ConnectionHealthProbeJob)))
            return services;

        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<ConnectionHealthProbeJob>();
        services.AddSingleton<IJobRegistrar, ProbeJobRegistrar>();
        return services;
    }

    private sealed class ProbeJobRegistrar : IJobRegistrar
    {
        public void RegisterJobs(IServiceProvider sp) =>
            sp.GetRequiredService<IRecurringJobManager>().AddOrUpdate<ConnectionHealthProbeJob>(
                ConnectionHealthProbeJob.JobId, job => job.ExecuteAsync(CancellationToken.None), ConnectionHealthProbeJob.Cron);
    }

    /// <summary>
    /// A source's values are the named options keyed by its provider name. The code defaults a module registers
    /// with <c>Configure&lt;ConnectionHealthSourceOptions&gt;(provider, ...)</c> run first, then this binds
    /// <c>ConnectionHealth:Sources:&lt;provider&gt;</c> over them, so configuration always wins whatever the
    /// registration order.
    /// </summary>
    private sealed class BindSourceOptionsFromConfiguration(IConfigurationSection sources)
        : IPostConfigureOptions<ConnectionHealthSourceOptions>
    {
        public void PostConfigure(string? name, ConnectionHealthSourceOptions options)
        {
            if (string.IsNullOrEmpty(name))
                return;

            var section = sources.GetSection(name);
            // The binder appends configured array entries to the existing ones; a configured list replaces the
            // code default instead, as a scalar key does.
            if (section.GetSection(nameof(ConnectionHealthSourceOptions.EarlyRechecks)).GetChildren().Any())
                options.EarlyRechecks = [];
            section.Bind(options);
        }
    }
}
