namespace FinanceSentry.Tests.Unit.BankSync.Infrastructure;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.BankSync.Domain;
using FinanceSentry.Modules.BankSync.Domain.Repositories;
using FinanceSentry.Modules.BankSync.Infrastructure.Jobs;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

/// <summary>
/// Unit tests for <see cref="ConsentExpiryReminderJob"/> (TrueLayer #3, part 2) — nudges the user to
/// reconnect before an open-banking consent lapses. Dedup/silence is the alert generator's job, so
/// this only verifies one alert is raised per expiring connection with the right identifiers.
/// </summary>
public class ConsentExpiryReminderJobTests
{
    private readonly Mock<ITrueLayerConnectionRepository> _connections = new();
    private readonly Mock<IAlertGeneratorService> _alerts = new();

    public ConsentExpiryReminderJobTests()
    {
        _connections.Setup(r => r.GetAllLinkedAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
    }

    [Fact]
    public async Task ExecuteAsync_RaisesOneAlertPerExpiringConnection()
    {
        var expiresAt = DateTime.UtcNow.AddDays(3);
        var conn = new TrueLayerConnection(Guid.NewGuid(), "ob-aib", "AIB", "ref-1");
        conn.MarkLinked(expiresAt);
        _connections.Setup(r => r.GetLinkedExpiringBeforeAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([conn]);

        await MakeJob().ExecuteAsync();

        _alerts.Verify(a => a.GenerateConsentExpiringAlertAsync(
            conn.UserId, conn.Id, "AIB", expiresAt, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_NoExpiringConnections_RaisesNothing()
    {
        _connections.Setup(r => r.GetLinkedExpiringBeforeAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        await MakeJob().ExecuteAsync();

        _alerts.Verify(a => a.GenerateConsentExpiringAlertAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_QueriesWithinReminderWindow()
    {
        DateTime? askedThreshold = null;
        _connections.Setup(r => r.GetLinkedExpiringBeforeAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Callback((DateTime t, CancellationToken _) => askedThreshold = t)
            .ReturnsAsync([]);

        await MakeJob().ExecuteAsync();

        Assert.NotNull(askedThreshold);
        // Threshold is ~ReminderWindowDays in the future (allow scheduling slack).
        var expected = DateTime.UtcNow.AddDays(ConsentExpiryReminderJob.ReminderWindowDays);
        Assert.True(Math.Abs((askedThreshold!.Value - expected).TotalMinutes) < 5);
    }

    [Fact]
    public async Task ExecuteAsync_ResolvesLinkedConnection_NoLongerExpiring()
    {
        var conn = new TrueLayerConnection(Guid.NewGuid(), "ob-aib", "AIB", "ref-1");
        conn.MarkLinked(DateTime.UtcNow.AddDays(60));

        _connections.Setup(r => r.GetLinkedExpiringBeforeAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _connections.Setup(r => r.GetAllLinkedAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([conn]);

        await MakeJob().ExecuteAsync();

        _alerts.Verify(a => a.ResolveConsentExpiringAlertAsync(conn.UserId, conn.Id, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_NoResolve_WhenConnectionStillExpiring()
    {
        var expiresAt = DateTime.UtcNow.AddDays(3);
        var conn = new TrueLayerConnection(Guid.NewGuid(), "ob-aib", "AIB", "ref-1");
        conn.MarkLinked(expiresAt);

        _connections.Setup(r => r.GetLinkedExpiringBeforeAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([conn]);
        _connections.Setup(r => r.GetAllLinkedAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([conn]);

        await MakeJob().ExecuteAsync();

        _alerts.Verify(a => a.ResolveConsentExpiringAlertAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private ConsentExpiryReminderJob MakeJob()
        => new(_connections.Object, _alerts.Object, Mock.Of<ILogger<ConsentExpiryReminderJob>>());
}
