namespace FinanceSentry.Tests.Unit.Migrations;

using FinanceSentry.API.Migrations;
using FinanceSentry.Modules.Research.Infrastructure.Persistence;
using FinanceSentry.Modules.Risk.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Xunit;

/// <summary>
/// The <c>migrations</c> readiness check turns <see cref="StartupMigrationStatus"/> into the signal an
/// operator sees: Healthy when startup migrated everything, Unhealthy when the database was unreachable
/// at startup (and still is). Per-module detail is reserved for the pending-migrations list, which needs
/// a reachable database and lives in the integration suite (FreshDatabaseMigrationTests).
/// </summary>
public sealed class StartupMigrationsHealthCheckTests
{
    private const string UnreachableDatabase =
        "Host=127.0.0.1;Port=1;Database=unreachable;Username=test;Password=test;Timeout=1";

    [Fact]
    public async Task WhenNothingWasSkipped_ReportsHealthy()
    {
        var status = new StartupMigrationStatus();
        var check = new StartupMigrationsHealthCheck(status, BuildScopeFactory());

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public async Task WhenModulesWereSkippedAndTheDatabaseIsStillUnreachable_ReportsUnhealthyOnce()
    {
        var status = new StartupMigrationStatus();
        status.RecordSkipped(typeof(ResearchDbContext));
        status.RecordSkipped(typeof(RiskDbContext));
        var check = new StartupMigrationsHealthCheck(status, BuildScopeFactory());

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain("skipped at startup",
            "the operator must be told migrations never ran in this process");
        result.Description.Should().Contain("Restart the API",
            "the signal must say what to do, not just that something is wrong");
        result.Description.Should().ContainEquivalentOf("still unreachable").And.NotContain(nameof(ResearchDbContext),
            "the sibling database check already reports unreachability; module names belong to the pending list");
    }

    [Fact]
    public void RecordingTheSameContextTwice_ListsItOnce()
    {
        // Research migrates in two steps (before and after Risk), so it can be skipped twice.
        var status = new StartupMigrationStatus();
        status.RecordSkipped(typeof(ResearchDbContext));
        status.RecordSkipped(typeof(ResearchDbContext));

        status.SkippedContexts.Should().ContainSingle().Which.Should().Be(typeof(ResearchDbContext));
        status.MigrationsSkipped.Should().BeTrue();
    }

    private static IServiceScopeFactory BuildScopeFactory()
    {
        var services = new ServiceCollection();
        services.AddDbContext<ResearchDbContext>(o => o.UseNpgsql(UnreachableDatabase));
        services.AddDbContext<RiskDbContext>(o => o.UseNpgsql(UnreachableDatabase));
        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }
}
