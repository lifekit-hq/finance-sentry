namespace FinanceSentry.Infrastructure.Connections;

using FinanceSentry.Core.Connections;
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
            if (!string.IsNullOrEmpty(name))
                sources.GetSection(name).Bind(options);
        }
    }
}
