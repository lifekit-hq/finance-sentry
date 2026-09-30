namespace FinanceSentry.Tests.Unit.JobRegistration;

using FinanceSentry.API.Hangfire;
using FluentAssertions;
using global::Hangfire.PostgreSql;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

public class StartupJobRegistrationTests
{
    private static readonly TimeSpan[] NoDelay = [TimeSpan.Zero, TimeSpan.Zero];

    private static PostgreSqlDistributedLockException LockTimeout() => new("hangfire:lock:recurring-job:x");

    [Fact]
    public void Succeeds_FirstAttempt_ReturnsTrue()
    {
        var calls = 0;

        StartupJobRegistration.RegisterWithRetry(() => calls++, NullLogger.Instance, NoDelay).Should().BeTrue();

        calls.Should().Be(1);
    }

    [Fact]
    public void LockTimeoutThenSuccess_Retries()
    {
        var calls = 0;

        var ok = StartupJobRegistration.RegisterWithRetry(
            () => { if (++calls < 3) throw LockTimeout(); }, NullLogger.Instance, NoDelay);

        ok.Should().BeTrue();
        calls.Should().Be(3);
    }

    [Fact]
    public void LockTimeoutAlways_GivesUpWithErrorLogAndDoesNotThrow()
    {
        var logger = new Mock<ILogger>();
        var calls = 0;

        var ok = StartupJobRegistration.RegisterWithRetry(
            () => { calls++; throw LockTimeout(); }, logger.Object, NoDelay);

        ok.Should().BeFalse();
        calls.Should().Be(3);
        logger.Verify(
            l => l.Log(LogLevel.Error, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<Exception?>(),
                (Func<It.IsAnyType, Exception?, string>)It.IsAny<object>()),
            Times.Once);
    }

    [Fact]
    public void OtherExceptions_Propagate()
    {
        var act = () => StartupJobRegistration.RegisterWithRetry(
            () => throw new InvalidOperationException("boom"), NullLogger.Instance, NoDelay);

        act.Should().Throw<InvalidOperationException>();
    }
}
