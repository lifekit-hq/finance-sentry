namespace FinanceSentry.Tests.Unit.JobRegistration;

using FinanceSentry.Modules.BankSync.Infrastructure.Jobs;
using FluentAssertions;
using global::Hangfire.PostgreSql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Xunit;

public sealed class HangfireStorageOptionsTests
{
    // Issue 1554: without the sliding invisibility timeout a job still running after InvisibilityTimeout (30 min) is fetched
    // again by the second worker, and two copies run at once (the theme attention refresh doubled its GDELT requests).
    [Fact]
    public void PostgreSqlStorage_KeepsALongRunningJobInvisible_SoItIsNotFetchedASecondTime()
    {
        var provider = new ServiceCollection()
            .AddHangfireServices(new ConfigurationBuilder().Build(), new StubHostEnvironment("Production"))
            .BuildServiceProvider();

        provider.GetRequiredService<PostgreSqlStorageOptions>().UseSlidingInvisibilityTimeout.Should().BeTrue();
    }

    private sealed class StubHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "FinanceSentry.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
