namespace FinanceSentry.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

public static class UpdatedAtStampingExtensions
{
    /// <summary>Registers <see cref="UpdatedAtSaveChangesInterceptor"/> and the clock it reads.</summary>
    public static IServiceCollection AddUpdatedAtStamping(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<UpdatedAtSaveChangesInterceptor>();
        return services;
    }

    /// <summary>Attaches the shared interceptor to a DbContext; call it from every <c>AddDbContext</c> factory.</summary>
    public static DbContextOptionsBuilder UseUpdatedAtStamping(
        this DbContextOptionsBuilder options, IServiceProvider serviceProvider)
        => options.AddInterceptors(serviceProvider.GetRequiredService<UpdatedAtSaveChangesInterceptor>());
}
