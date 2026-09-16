namespace FinanceSentry.Tests.Unit.BankSync.Application;

using FinanceSentry.Infrastructure.Encryption;
using FinanceSentry.Modules.BankSync.Application.Services;
using FinanceSentry.Modules.BankSync.Domain;
using FinanceSentry.Modules.BankSync.Domain.Repositories;
using FinanceSentry.Modules.BankSync.Infrastructure.TrueLayer;
using FluentAssertions;
using Moq;
using Xunit;

/// <summary>
/// Unit tests for TrueLayerTokenRefreshService — the refresh-token exchange shared by the
/// per-account scheduled sync and the account-discovery pass (fs-494).
/// </summary>
public class TrueLayerTokenRefreshServiceTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    // Regression: TrueLayer rotates the refresh_token on every refresh. The new token MUST be
    // persisted immediately — before the caller does anything else — so a later failure can't
    // strand a consumed token and brick the connection (the invalid_grant root cause).
    [Fact]
    public async Task AcquireAccessTokenAsync_PersistsRotatedRefreshToken_BeforeReturning()
    {
        var connections = new Mock<ITrueLayerConnectionRepository>();
        var encryption = new Mock<ICredentialEncryptionService>();
        var client = new Mock<ITrueLayerClient>();

        var connection = new TrueLayerConnection(UserId, "ob-testbank", "Test Bank", $"ref-{Guid.NewGuid():N}");
        connection.SetRefreshToken([1], [2], [3], 1);
        connections.Setup(r => r.GetByIdAsync(connection.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(connection);

        encryption.Setup(e => e.Decrypt(It.IsAny<byte[]>(), It.IsAny<byte[]>(), It.IsAny<byte[]>(), It.IsAny<int>()))
            .Returns("old-refresh");
        encryption.Setup(e => e.Encrypt("new-refresh")).Returns(new EncryptionResult([9], [8], [7], 1));
        client.Setup(c => c.RefreshAccessTokenAsync("old-refresh", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TrueLayerTokenSet("AT", "new-refresh", 3600));

        var sut = new TrueLayerTokenRefreshService(connections.Object, encryption.Object, client.Object);

        var accessToken = await sut.AcquireAccessTokenAsync(connection.Id);

        accessToken.Should().Be("AT");
        connections.Verify(
            r => r.UpdateAsync(
                It.Is<TrueLayerConnection>(c => c.EncryptedRefreshToken.Length == 1 && c.EncryptedRefreshToken[0] == 9),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task AcquireAccessTokenAsync_UnknownConnection_Throws()
    {
        var connections = new Mock<ITrueLayerConnectionRepository>();
        connections.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TrueLayerConnection?)null);

        var sut = new TrueLayerTokenRefreshService(
            connections.Object, new Mock<ICredentialEncryptionService>().Object, new Mock<ITrueLayerClient>().Object);

        var act = async () => await sut.AcquireAccessTokenAsync(Guid.NewGuid());

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // Concurrent callers refreshing the SAME connection (per-account sync + discovery on the same
    // cron) must be serialized so the shared, rotating refresh_token isn't consumed twice.
    [Fact]
    public async Task AcquireAccessTokenAsync_ConcurrentCallsForSameConnection_AreSerialized()
    {
        var connections = new Mock<ITrueLayerConnectionRepository>();
        var encryption = new Mock<ICredentialEncryptionService>();
        var client = new Mock<ITrueLayerClient>();

        var connection = new TrueLayerConnection(UserId, "ob-testbank", "Test Bank", $"ref-{Guid.NewGuid():N}");
        connection.SetRefreshToken([1], [2], [3], 1);
        connections.Setup(r => r.GetByIdAsync(connection.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(connection);
        encryption.Setup(e => e.Decrypt(It.IsAny<byte[]>(), It.IsAny<byte[]>(), It.IsAny<byte[]>(), It.IsAny<int>()))
            .Returns("old-refresh");
        encryption.Setup(e => e.Encrypt(It.IsAny<string>())).Returns(new EncryptionResult([9], [8], [7], 1));

        var concurrentCalls = 0;
        var maxObservedConcurrency = 0;
        client.Setup(c => c.RefreshAccessTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                var current = Interlocked.Increment(ref concurrentCalls);
                maxObservedConcurrency = Math.Max(maxObservedConcurrency, current);
                await Task.Delay(20);
                Interlocked.Decrement(ref concurrentCalls);
                return new TrueLayerTokenSet("AT", "new-refresh", 3600);
            });

        var sut = new TrueLayerTokenRefreshService(connections.Object, encryption.Object, client.Object);

        await Task.WhenAll(
            sut.AcquireAccessTokenAsync(connection.Id),
            sut.AcquireAccessTokenAsync(connection.Id));

        maxObservedConcurrency.Should().Be(1);
    }
}
