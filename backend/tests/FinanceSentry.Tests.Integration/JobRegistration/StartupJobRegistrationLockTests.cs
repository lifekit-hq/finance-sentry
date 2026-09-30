namespace FinanceSentry.Tests.Integration.JobRegistration;

using FinanceSentry.API.Hangfire;
using FinanceSentry.Modules.Companion;
using FinanceSentry.Tests.Integration.Shared;
using FluentAssertions;
using global::Hangfire;
using global::Hangfire.PostgreSql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.PostgreSql;
using Xunit;

/// <summary>
/// Reproduces the 2026-09-30 crash loop against real Hangfire PostgreSQL storage: another holder of
/// <c>hangfire:lock:recurring-job:companion-capture</c> makes startup job registration throw
/// <see cref="PostgreSqlDistributedLockException"/> after Hangfire's fixed 15s lock timeout.
/// Requires Docker (reported as skipped where no daemon is reachable).
/// </summary>
[Trait("Category", "Integration")]
public sealed class StartupJobRegistrationLockTests : IAsyncLifetime
{
    private const long ContainerMemoryBytes = 2L * 1024 * 1024 * 1024;
    private const string CaptureJobLock = "lock:recurring-job:companion-capture";
    private const int LockHoldPastTimeoutSeconds = 20;

    private PostgreSqlContainer? _postgres;

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder("postgres:16-alpine")
            .WithCreateParameterModifier(p => p.HostConfig?.Memory = ContainerMemoryBytes)
            .Build();
        await _postgres.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_postgres is not null)
            await _postgres.DisposeAsync();
    }

    private ServiceProvider BuildServices()
    {
        var connectionString = _postgres!.GetConnectionString();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Default"] = connectionString })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHangfire(cfg => cfg.UsePostgreSqlStorage(
            o => o.UseNpgsqlConnection(connectionString),
            new PostgreSqlStorageOptions { SchemaName = "hangfire", PrepareSchemaIfNecessary = true }));
        services.AddCompanionModule(config);

        return services.BuildServiceProvider();
    }

    [DockerRequiredFact]
    public async Task HeldRecurringJobLock_UnprotectedRegistration_ThrowsLockTimeout()
    {
        await using var sp = BuildServices();
        var storage = sp.GetRequiredService<JobStorage>();
        using var connection = storage.GetConnection();
        using var heldLock = connection.AcquireDistributedLock(CaptureJobLock, TimeSpan.FromSeconds(5));

        var act = () => JobRegistrationExtensions.RegisterAllModuleJobs(sp);

        act.Should().Throw<PostgreSqlDistributedLockException>();
    }

    [DockerRequiredFact]
    public async Task HeldRecurringJobLock_ReleasedAfterTimeout_RegistrationRetriesAndCompletes()
    {
        await using var sp = BuildServices();
        var storage = sp.GetRequiredService<JobStorage>();
        var connection = storage.GetConnection();
        var heldLock = connection.AcquireDistributedLock(CaptureJobLock, TimeSpan.FromSeconds(5));
        var release = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(LockHoldPastTimeoutSeconds));
            heldLock.Dispose();
            connection.Dispose();
        });

        var completed = StartupJobRegistration.RegisterWithRetry(
            () => JobRegistrationExtensions.RegisterAllModuleJobs(sp),
            NullLogger.Instance,
            [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)]);
        await release;

        completed.Should().BeTrue();
        using var read = storage.GetConnection();
        read.GetAllItemsFromSet("recurring-jobs").Should().Contain("companion-capture");
    }
}
