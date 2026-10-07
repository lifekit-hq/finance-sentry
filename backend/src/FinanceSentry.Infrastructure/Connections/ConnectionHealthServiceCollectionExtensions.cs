namespace FinanceSentry.Infrastructure.Connections;

using FinanceSentry.Core.Connections;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IConnectionHealthShadow, ConnectionHealthShadowRecorder>();
        return services;
    }
}
