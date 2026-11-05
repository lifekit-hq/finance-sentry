namespace FinanceSentry.Modules.PortfolioConstruction;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.PortfolioConstruction.Application.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

/// <summary>
/// Portfolio Construction (spec 726): turns a scored universe plus an archetype into a ranked screen with suggested
/// weights. It stores nothing and reaches Research and Risk only through the consumer-side ports in
/// <c>Domain/Ports</c>, whose adapters live in FinanceSentry.Integration.
/// </summary>
public static class PortfolioConstructionModule
{
    internal sealed class ModuleRegistrar : IModuleRegistrar
    {
        public void Register(IServiceCollection services, IConfiguration config)
            => services.AddPortfolioConstructionModule(config);
    }

    public static IServiceCollection AddPortfolioConstructionModule(
        this IServiceCollection services, IConfiguration config)
    {
        services.Configure<PortfolioConstructionOptions>(config.GetSection(PortfolioConstructionOptions.SectionName));
        services.AddMemoryCache();
        services.TryAddSingleton(TimeProvider.System);
        return services;
    }
}
