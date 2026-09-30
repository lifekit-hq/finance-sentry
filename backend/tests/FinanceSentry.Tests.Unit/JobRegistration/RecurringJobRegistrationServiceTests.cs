namespace FinanceSentry.Tests.Unit.JobRegistration;

using FinanceSentry.API.Hangfire;
using FinanceSentry.API.Migrations;
using FinanceSentry.Core.Interfaces;
using FluentAssertions;
using global::Hangfire;
using global::Hangfire.PostgreSql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

public class RecurringJobRegistrationServiceTests
{
    private static readonly RecurringJobRegistrationOptions FastOptions =
        new(TimeSpan.FromSeconds(30), TimeSpan.Zero, TimeSpan.Zero);

    private readonly RecurringJobRegistrationStatus _status = new();
    private readonly StartupMigrationStatus _migrations = new();
    private readonly ScriptedRegistrar _registrar = new();
    private readonly CapturingLogger _logger = new();

    private RecurringJobRegistrationService CreateService(RecurringJobRegistrationOptions? options = null)
    {
        var services = new ServiceCollection()
            .AddSingleton<IJobRegistrar>(_registrar)
            .AddSingleton(Mock.Of<IRecurringJobManager>())
            .AddSingleton(Mock.Of<IBackgroundJobClient>())
            .BuildServiceProvider();

        return new RecurringJobRegistrationService(
            services, Mock.Of<IHostApplicationLifetime>(), _status, _migrations, options ?? FastOptions, _logger);
    }

    private static PostgreSqlDistributedLockException LockTimeout() => new("hangfire:lock:recurring-job:x");

    [Fact]
    public async Task Registers_OnFirstAttempt()
    {
        await CreateService().RunAsync(CancellationToken.None);

        _registrar.Calls.Should().Be(1);
        _status.State.Should().Be(RecurringJobRegistrationState.Registered);
    }

    [Fact]
    public async Task LockTimeoutThenSuccess_RetriesAndStaysPendingInBetween()
    {
        var stateDuringRetry = new List<RecurringJobRegistrationState>();
        _registrar.OnCall = call =>
        {
            stateDuringRetry.Add(_status.State);
            if (call < 3) throw LockTimeout();
        };

        await CreateService().RunAsync(CancellationToken.None);

        _registrar.Calls.Should().Be(3);
        stateDuringRetry.Should().AllBeEquivalentTo(RecurringJobRegistrationState.Pending);
        _status.State.Should().Be(RecurringJobRegistrationState.Registered);
    }

    [Fact]
    public async Task LockTimeoutPastBudget_LogsErrorAndFails()
    {
        _registrar.OnCall = _ => throw LockTimeout();

        await CreateService(new(TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(10)))
            .RunAsync(CancellationToken.None);

        _status.State.Should().Be(RecurringJobRegistrationState.Failed);
        _logger.Levels.Should().Contain(LogLevel.Error);
        _registrar.Calls.Should().BeGreaterThan(1);
    }

    [Fact]
    public async Task OtherException_Propagates()
    {
        _registrar.OnCall = _ => throw new InvalidOperationException("boom");

        var act = () => CreateService().RunAsync(CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        _registrar.Calls.Should().Be(1);
        _status.State.Should().Be(RecurringJobRegistrationState.Pending);
    }

    [Fact]
    public async Task MigrationsSkipped_DoesNotRegister()
    {
        _migrations.RecordSkipped(typeof(object));

        await CreateService().RunAsync(CancellationToken.None);

        _registrar.Calls.Should().Be(0);
        _status.State.Should().Be(RecurringJobRegistrationState.Skipped);
    }

    [Fact]
    public void Budget_ExceedsConfiguredStorageLockTimeout()
    {
        var lockTimeout = TimeSpan.FromMinutes(7);
        var services = new ServiceCollection()
            .AddSingleton(new PostgreSqlStorageOptions { DistributedLockTimeout = lockTimeout })
            .AddRecurringJobRegistration()
            .BuildServiceProvider();

        services.GetRequiredService<RecurringJobRegistrationOptions>().Budget.Should().BeGreaterThan(lockTimeout);
    }

    [Theory]
    [InlineData(RecurringJobRegistrationState.Pending, HealthStatus.Healthy)]
    [InlineData(RecurringJobRegistrationState.Registered, HealthStatus.Healthy)]
    [InlineData(RecurringJobRegistrationState.Skipped, HealthStatus.Healthy)]
    [InlineData(RecurringJobRegistrationState.Failed, HealthStatus.Unhealthy)]
    public async Task HealthCheck_FailsOnlyOnceBudgetIsSpent(RecurringJobRegistrationState state, HealthStatus expected)
    {
        _status.Set(state);

        var result = await new RecurringJobRegistrationHealthCheck(_status).CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(expected);
    }

    private sealed class ScriptedRegistrar : IJobRegistrar
    {
        public int Calls { get; private set; }

        public Action<int>? OnCall { get; set; }

        public void RegisterJobs(IServiceProvider services)
        {
            Calls++;
            OnCall?.Invoke(Calls);
        }
    }

    private sealed class CapturingLogger : ILogger<RecurringJobRegistrationService>
    {
        public List<LogLevel> Levels { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Levels.Add(logLevel);
    }
}
