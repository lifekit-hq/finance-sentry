namespace FinanceSentry.Tests.Unit.Connections;

using FinanceSentry.Core.Connections;
using FinanceSentry.Infrastructure.Connections;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

/// <summary>
/// Shadow mode (Option B, S1): every attempt is evaluated, logged as one structured line carrying the would-be
/// outcome, and stored when the health changed. A failure inside the recorder never reaches the sync.
/// </summary>
public class ConnectionHealthShadowRecorderTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);
    private static readonly ConnectionHealthSubject Subject = new("monobank", "BankAccount", Guid.NewGuid(), Guid.NewGuid());

    private readonly CapturingLogger _logger = new();

    private ConnectionHealthShadowRecorder CreateRecorder(ConnectionHealthOptions? options = null) =>
        ShadowRecorders.Create(new FixedTimeProvider(Now), options, _logger);

    [Fact]
    public async Task Failure_LogsOneStructuredLineWithTheWouldBeOutcome_AndPersistsTheNewHealth()
    {
        ConnectionHealth? persisted = null;

        var evaluation = await CreateRecorder().RecordFailureAsync(
            Subject,
            new ConnectionHealth(),
            ProviderFailure.CredentialDefinitive("MONOBANK_TOKEN_INVALID"),
            (health, _) => { persisted = health; return Task.CompletedTask; });

        evaluation!.Outcome.Should().Be(ConnectionHealthOutcome.NotifyActionRequired);
        persisted.Should().Be(evaluation.Health);
        persisted!.State.Should().Be(ConnectionHealthState.ActionRequired);
        persisted.LastFailureAt.Should().Be(Now);

        var entry = _logger.Entries.Should().ContainSingle().Subject;
        entry.Level.Should().Be(LogLevel.Information);
        entry.Values.Should().Contain(new Dictionary<string, object?>
        {
            ["Provider"] = "monobank",
            ["SubjectKind"] = "BankAccount",
            ["SubjectId"] = Subject.Id,
            ["UserId"] = Subject.UserId,
            ["PreviousHealthState"] = ConnectionHealthState.Healthy,
            ["HealthState"] = ConnectionHealthState.ActionRequired,
            ["HealthOutcome"] = ConnectionHealthOutcome.NotifyActionRequired,
            ["Attempt"] = "failure",
            ["FailureClass"] = FailureClass.Credential,
            ["FailureStrength"] = FailureStrength.Definitive,
            ["FailureCode"] = "MONOBANK_TOKEN_INVALID",
            ["ConsecutiveFailures"] = 1,
        });
    }

    [Fact]
    public async Task Success_LogsTheResolve()
    {
        var failing = new ConnectionHealth { State = ConnectionHealthState.Failing, ConsecutiveFailures = 4, FirstFailureAt = Now.AddHours(-5) };

        var evaluation = await CreateRecorder().RecordSuccessAsync(Subject, failing, (_, _) => Task.CompletedTask);

        evaluation!.Outcome.Should().Be(ConnectionHealthOutcome.Resolve);
        var entry = _logger.Entries.Should().ContainSingle().Subject;
        entry.Values["Attempt"].Should().Be("success");
        entry.Values["HealthOutcome"].Should().Be(ConnectionHealthOutcome.Resolve);
        entry.Values["FailureClass"].Should().BeNull();
    }

    [Fact]
    public async Task InternalFailure_LeavesTheHealthAlone_AndDoesNotPersist()
    {
        var persists = 0;

        var evaluation = await CreateRecorder().RecordFailureAsync(
            Subject,
            new ConnectionHealth(),
            ProviderFailure.Internal("MONOBANK_PARSE_ERROR"),
            (_, _) => { persists++; return Task.CompletedTask; });

        evaluation!.Health.State.Should().Be(ConnectionHealthState.Healthy);
        persists.Should().Be(0);
        _logger.Entries.Should().ContainSingle();
    }

    [Fact]
    public async Task PersistThrowing_IsSwallowedAndLoggedAsAWarning()
    {
        var evaluation = await CreateRecorder().RecordFailureAsync(
            Subject,
            new ConnectionHealth(),
            ProviderFailure.Transient("HTTP_503"),
            (_, _) => throw new InvalidOperationException("db down"));

        evaluation.Should().BeNull();
        _logger.Entries.Should().Contain(e => e.Level == LogLevel.Warning && e.Exception is InvalidOperationException);
    }

    [Fact]
    public async Task Thresholds_AreReadFromOptions()
    {
        var options = new ConnectionHealthOptions { FailingMinConsecutiveFailures = 1, FailingMinTimeWithoutSuccess = TimeSpan.Zero };

        var evaluation = await CreateRecorder(options).RecordFailureAsync(
            Subject, new ConnectionHealth(), ProviderFailure.Transient("HTTP_503"), (_, _) => Task.CompletedTask);

        evaluation!.Outcome.Should().Be(ConnectionHealthOutcome.NotifyFailing);
    }

    [Fact]
    public void Registration_BindsTheConnectionHealthSection_AndIsIdempotent()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionHealth:FailingMinConsecutiveFailures"] = "5",
                ["ConnectionHealth:FailingMinTimeWithoutSuccess"] = "04:00:00",
                ["ConnectionHealth:DormantAfter"] = "14.00:00:00",
            })
            .Build();
        var services = new ServiceCollection().AddLogging();

        services.AddConnectionHealthShadow(config);
        services.AddConnectionHealthShadow(config);

        services.Count(d => d.ServiceType == typeof(IConnectionHealthShadow)).Should().Be(1);
        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IConnectionHealthShadow>().Should().BeOfType<ConnectionHealthShadowRecorder>();
        var bound = provider.GetRequiredService<IOptionsMonitor<ConnectionHealthOptions>>().CurrentValue;
        bound.FailingMinConsecutiveFailures.Should().Be(5);
        bound.FailingMinTimeWithoutSuccess.Should().Be(TimeSpan.FromHours(4));
        bound.DormantAfter.Should().Be(TimeSpan.FromDays(14));
        bound.SuspectConfirmationWindow.Should().Be(TimeSpan.FromMinutes(30));
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed record LogEntry(LogLevel Level, Exception? Exception, IReadOnlyDictionary<string, object?> Values);

    private sealed class CapturingLogger : ILogger<ConnectionHealthShadowRecorder>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var values = state is IEnumerable<KeyValuePair<string, object?>> pairs
                ? pairs.ToDictionary(p => p.Key, p => p.Value)
                : [];
            Entries.Add(new LogEntry(logLevel, exception, values));
        }
    }
}
