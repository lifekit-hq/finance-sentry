namespace FinanceSentry.Tests.Unit.Alerts;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Alerts;
using FinanceSentry.Modules.Alerts.Infrastructure.Jobs;
using Hangfire;
using Hangfire.Common;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

/// <summary>
/// The cash-shortfall sentinel moved from the retired Liquidity module into Alerts (#1531):
/// it must still be registered as the same daily recurring job, under the same job id.
/// </summary>
public class LiquiditySentinelRegistrationTests
{
    [Fact]
    public void AlertsModule_RegistersLiquiditySentinelJob_UnderItsOriginalDailyJobId()
    {
        var services = new ServiceCollection();
        services.AddAlertsModule(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Default"] = "Host=x" })
            .Build());

        Assert.Contains(services, d => d.ServiceType == typeof(LiquiditySentinelJob));

        var manager = new Mock<IRecurringJobManager>();
        services.AddSingleton(manager.Object);
        using var sp = services.BuildServiceProvider();

        foreach (var registrar in sp.GetServices<IJobRegistrar>())
        {
            registrar.RegisterJobs(sp);
        }

        manager.Verify(m => m.AddOrUpdate(
            "liquidity-sentinel",
            It.Is<Job>(j => j.Type == typeof(LiquiditySentinelJob)),
            Cron.Daily(),
            It.IsAny<RecurringJobOptions>()), Times.Once);
    }
}
