namespace FinanceSentry.API.Modules;

using System.Reflection;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

public static class ModuleRegistrationExtensions
{
    public static IServiceCollection AddAllModules(
        this IServiceCollection services, IConfiguration config)
    {
        // Force-load every FinanceSentry.Modules.* assembly before scanning. Referenced module
        // assemblies are otherwise loaded lazily; MVC modules happen to be force-loaded early via
        // generated ApplicationPart attributes, but MCP-only modules (no controllers, e.g. Radar)
        // are not — so without this they would be missing from AppDomain.GetAssemblies() here and
        // their IModuleRegistrar would never be discovered.
        EnsureModuleAssembliesLoaded();

        var registrars = Discover<IModuleRegistrar>();

        var moduleAssemblies = registrars.Select(r => r.GetType().Assembly).Distinct().ToArray();
        services.AddCqrs(moduleAssemblies);

        foreach (var registrar in registrars)
            registrar.Register(services, config);

        // The API is the worker host: it alone registers what only the worker may do — hosted
        // services, key rotation, the credential key (issue #613). McpServiceRegistration loads
        // IModuleRegistrar only, so the MCP host never inherits these.
        foreach (var registrar in Discover<IWorkerRegistrar>())
            registrar.Register(services, config);

        return services;
    }

    private static List<T> Discover<T>() where T : class =>
        AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a =>
            {
                try { return a.GetTypes(); }
                catch (ReflectionTypeLoadException ex) { return ex.Types.OfType<Type>(); }
            })
            .Where(t => typeof(T).IsAssignableFrom(t) && t is { IsInterface: false, IsAbstract: false })
            .Select(t => (T)Activator.CreateInstance(t)!)
            .ToList();

    private static void EnsureModuleAssembliesLoaded()
    {
        var moduleDlls = Directory.GetFiles(
            AppContext.BaseDirectory, "FinanceSentry.Modules.*.dll", SearchOption.TopDirectoryOnly);

        foreach (var dll in moduleDlls)
        {
            var name = AssemblyName.GetAssemblyName(dll);
            var alreadyLoaded = AppDomain.CurrentDomain.GetAssemblies()
                .Any(a => a.GetName().Name == name.Name);

            if (!alreadyLoaded)
                Assembly.Load(name);
        }
    }
}
