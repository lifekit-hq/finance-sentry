namespace FinanceSentry.Integration;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Core.Services;
using FinanceSentry.Modules.Events.Domain.Ports;
using FinanceSentry.Modules.Radar.Domain.Ports;
using FinanceSentry.Modules.Research.Domain.Ports;
using FinanceSentry.Modules.Risk.Domain.Ports;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Wires cross-module and cross-cutting service registrations whose consumers span more than one
/// module. Risk, Research, and Mcp all consume IBookFiguresService; registering it here rather
/// than in any single module prevents an undeclared coupling. See also 039: cross-module read ports.
/// </summary>
public static class CrossModulePortRegistration
{
    public static IServiceCollection AddCrossModulePorts(this IServiceCollection services)
    {
        services.AddScoped<IBookFiguresService, BookFiguresService>();

        services.AddScoped<IAllocationPolicySource, IpsAllocationPolicySource>();
        services.AddScoped<IPositionCapSource, RiskPositionCapSource>();

        // 049: the Events module reads calendars, alerts and the companion outbox through these ports.
        services.AddScoped<IUpcomingCorporateEventReader, EventsCorporateCalendarAdapter>();
        services.AddScoped<IMacroEventReader, EventsMacroEventAdapter>();
        services.AddScoped<IThesisCatalystReader, EventsThesisCatalystAdapter>();
        services.AddScoped<IPeriodicFilingReader, EventsPeriodicFilingAdapter>();
        services.AddScoped<IFiredAlertReader, EventsFiredAlertAdapter>();
        services.AddScoped<IEventDeliveryReader, EventsDeliveryAdapter>();

        // 412: portfolio value source for book-vs-benchmark TWR; bridges Wealth snapshots → Radar.
        services.AddScoped<IPortfolioValueSource, RadarPortfolioValueSource>();

        // 413: portfolio scan data — bridges Research IPS + Risk rules → Radar portfolio scanner.
        services.AddScoped<IPortfolioScanDataReader, PortfolioScanDataReader>();

        // 421: asset dossier — bridges BrokerageSync tax lots and Radar signals → Research dossier query.
        services.AddScoped<IHoldingTaxLotsReader, HoldingTaxLotsAdapter>();
        services.AddScoped<IAssetSignalReader, AssetSignalAdapter>();
        // 414: thesis track record — bridges Research → Radar's weekly performance brief.
        services.AddScoped<ITrackRecordSource, ResearchTrackRecordSource>();
        return services;
    }
}
