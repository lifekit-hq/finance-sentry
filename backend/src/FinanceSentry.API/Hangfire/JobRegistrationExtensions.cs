namespace FinanceSentry.API.Hangfire;

using FinanceSentry.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;

public static class JobRegistrationExtensions
{
    public static WebApplication RegisterAllModuleJobs(this WebApplication app)
    {
        RegisterAllModuleJobs(app.Services);

        return app;
    }

    public static void RegisterAllModuleJobs(IServiceProvider services)
    {
        foreach (var registrar in services.GetServices<IJobRegistrar>())
            registrar.RegisterJobs(services);
    }
}
