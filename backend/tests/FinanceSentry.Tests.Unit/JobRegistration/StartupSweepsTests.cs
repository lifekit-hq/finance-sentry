namespace FinanceSentry.Tests.Unit.JobRegistration;

using FinanceSentry.API.Hangfire;
using FinanceSentry.API.Migrations;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Infrastructure.Fx;
using FluentAssertions;
using global::Hangfire;
using global::Hangfire.Common;
using global::Hangfire.States;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

public class StartupSweepsTests
{
    private readonly StartupMigrationStatus _migrations = new();
    private readonly List<string> _enqueued = [];

    private ServiceProvider BuildServices()
    {
        var client = new Mock<IBackgroundJobClient>();
        client.Setup(c => c.Create(It.IsAny<Job>(), It.IsAny<IState>()))
            .Callback<Job, IState>((job, _) => _enqueued.Add(job.Type.Name))
            .Returns("1");

        return new ServiceCollection()
            .AddSingleton(_migrations)
            .AddSingleton(client.Object)
            .AddSingleton<IStartupSweep>(new RecordingSweep(_enqueued))
            .BuildServiceProvider();
    }

    [Fact]
    public void Enqueue_RunsModuleSweepsThenFxRefresh_OncePerCall()
    {
        StartupSweeps.Enqueue(BuildServices());

        _enqueued.Should().Equal("module-sweep", nameof(ExchangeRateRefreshJob));
    }

    [Fact]
    public void MigrationsSkipped_EnqueuesNothing()
    {
        _migrations.RecordSkipped(typeof(object));

        StartupSweeps.Enqueue(BuildServices());

        _enqueued.Should().BeEmpty();
    }

    private sealed class RecordingSweep(List<string> log) : IStartupSweep
    {
        public void Enqueue(IServiceProvider services) => log.Add("module-sweep");
    }
}
