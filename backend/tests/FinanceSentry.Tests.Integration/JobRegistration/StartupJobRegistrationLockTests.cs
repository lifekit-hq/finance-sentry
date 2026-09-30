namespace FinanceSentry.Tests.Integration.JobRegistration;

using FinanceSentry.API.Hangfire;
using FinanceSentry.API.Migrations;
using FinanceSentry.Modules.Companion;
using FinanceSentry.Tests.Integration.Shared;
using FluentAssertions;
using global::Hangfire;
using global::Hangfire.PostgreSql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Testcontainers.PostgreSql;
using Xunit;

/// <summary>
/// Reproduces the 2026-09-30 crash loop against real Hangfire PostgreSQL storage: a lock row on
/// <c>hangfire:lock:recurring-job:companion-capture</c> that nobody releases (what a SIGKILLed holder
/// leaves behind) makes registration throw <see cref="PostgreSqlDistributedLockException"/> after
/// Hangfire's fixed 15s wait. The host must come up and serve regardless, and registration must
/// complete by itself once the lock is gone. Requires Docker (skipped where no daemon is reachable).
/// </summary>
[Trait("Category", "Integration")]
public sealed class StartupJobRegistrationLockTests : IAsyncLifetime
{
    private const long ContainerMemoryBytes = 2L * 1024 * 1024 * 1024;
    private const string CaptureJobLock = "lock:recurring-job:companion-capture";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(90);

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

    private IHost BuildHost(RecurringJobRegistrationOptions options)
    {
        var connectionString = _postgres!.GetConnectionString();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Default"] = connectionString })
            .Build();

        return Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddHangfire(cfg => cfg.UsePostgreSqlStorage(
                    o => o.UseNpgsqlConnection(connectionString),
                    new PostgreSqlStorageOptions { SchemaName = "hangfire", PrepareSchemaIfNecessary = true }));
                services.AddCompanionModule(config);
                services.AddSingleton<StartupMigrationStatus>();
                services.AddSingleton<RecurringJobRegistrationStatus>();
                services.AddSingleton(options);
                services.AddHostedService<RecurringJobRegistrationService>();
            })
            .Build();
    }

    private static async Task<bool> WaitFor(Func<bool> condition)
    {
        using var cts = new CancellationTokenSource(Timeout);
        while (!condition())
        {
            if (cts.IsCancellationRequested)
                return false;
            await Task.Delay(TimeSpan.FromMilliseconds(200));
        }

        return true;
    }

    [DockerRequiredFact]
    public async Task HeldLock_HostStartsWhileRegistrationRetries_ThenCompletesOnItsOwnAfterRelease()
    {
        using var host = BuildHost(new(TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)));
        var storage = host.Services.GetRequiredService<JobStorage>();
        var status = host.Services.GetRequiredService<RecurringJobRegistrationStatus>();
        var holder = storage.GetConnection();
        var heldLock = holder.AcquireDistributedLock(CaptureJobLock, TimeSpan.FromSeconds(5));

        // Registration blocks on the held lock (15s per attempt) — the host must still start.
        await host.StartAsync().WaitAsync(Timeout);
        status.State.Should().Be(RecurringJobRegistrationState.Pending);

        // Outlast one full lock wait so the failure path (not just a slow first attempt) is exercised.
        await Task.Delay(TimeSpan.FromSeconds(20));
        status.State.Should().Be(RecurringJobRegistrationState.Pending);

        heldLock.Dispose();
        holder.Dispose();

        (await WaitFor(() => status.State == RecurringJobRegistrationState.Registered)).Should().BeTrue();
        using var read = storage.GetConnection();
        read.GetAllItemsFromSet("recurring-jobs").Should().Contain("companion-capture");
        await host.StopAsync();
    }

    [DockerRequiredFact]
    public async Task HeldLock_BudgetSpent_HostStaysUpAndRegistrationIsFailed()
    {
        using var host = BuildHost(new(TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)));
        var storage = host.Services.GetRequiredService<JobStorage>();
        var status = host.Services.GetRequiredService<RecurringJobRegistrationStatus>();
        using var holder = storage.GetConnection();
        using var heldLock = holder.AcquireDistributedLock(CaptureJobLock, TimeSpan.FromSeconds(5));

        await host.StartAsync().WaitAsync(Timeout);

        (await WaitFor(() => status.State == RecurringJobRegistrationState.Failed)).Should().BeTrue();
        await host.StopAsync();
    }
}
