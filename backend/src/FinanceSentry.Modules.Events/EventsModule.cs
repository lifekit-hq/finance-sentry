namespace FinanceSentry.Modules.Events;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Events.Domain.Repositories;
using FinanceSentry.Modules.Events.Infrastructure.Persistence;
using FinanceSentry.Modules.Events.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Events (feature 049): the calendar of upcoming events, the fired-events feed and the reader's
/// verdicts. Owns one table; every other read goes through the ports in <c>Domain/Ports</c>, whose
/// adapters live in <c>FinanceSentry.Integration</c>. No jobs - the calendar is computed on read.
/// </summary>
public static class EventsModule
{
    public const string MigrationsHistoryTable = "__ef_migrations_history_events";

    internal sealed class ModuleRegistrar : IModuleRegistrar
    {
        public void Register(IServiceCollection services, IConfiguration config)
            => services.AddEventsModule(config);
    }

    public static IServiceCollection AddEventsModule(
        this IServiceCollection services, IConfiguration config)
    {
        services.AddDbContext<EventsDbContext>(
            o => o.UseNpgsql(
                config.GetConnectionString("Default")!,
                b => b.MigrationsHistoryTable(MigrationsHistoryTable, "public")));

        services.AddScoped<IEventVerdictRepository, EventVerdictRepository>();

        return services;
    }
}
