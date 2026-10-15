namespace FinanceSentry.Core.Services;

using FinanceSentry.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

public static class BookFiguresServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IBookFiguresService"/> and the three original wealth sources. Sources are read in
    /// registration order, which is the order of <see cref="BookFigures.Positions"/>: crypto, then brokerage,
    /// then banking, as the figures have always been built. A new economy registers its
    /// <see cref="IWealthSource"/> after these with <c>AddScoped&lt;IWealthSource, ...&gt;()</c>; nothing else changes.
    /// Safe to call from more than one host registration: a source is added once, never twice (which would sum it twice).
    /// </summary>
    public static IServiceCollection AddBookFigures(this IServiceCollection services)
    {
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IWealthSource, CryptoWealthSource>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IWealthSource, BrokerageWealthSource>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IWealthSource, BankingWealthSource>());
        services.TryAddScoped<IBookFiguresService, BookFiguresService>();
        return services;
    }
}
