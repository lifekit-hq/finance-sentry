using FinanceSentry.Modules.BrokerageSync.Application.Services;
using FinanceSentry.Modules.BrokerageSync.Domain;
using FinanceSentry.Modules.BrokerageSync.Domain.Repositories;
using FinanceSentry.Modules.BrokerageSync.Infrastructure.IBKR.Flex;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace FinanceSentry.Tests.Unit.BrokerageSync.Flex;

public class IbkrFlexStatementFetcherTests
{
    private readonly Mock<IIBKRFlexCredentialRepository> _credentialRepo = new(MockBehavior.Strict);
    private readonly Mock<IIbkrFlexCredentialResolver> _resolver = new(MockBehavior.Strict);
    private readonly Mock<IIbkrFlexClient> _flexClient = new(MockBehavior.Strict);

    private IbkrFlexStatementFetcher CreateFetcher() => new(
        _credentialRepo.Object,
        _resolver.Object,
        _flexClient.Object,
        NullLogger<IbkrFlexStatementFetcher>.Instance);

    [Fact]
    public async Task FetchAsync_NoCredentialConfigured_ReturnsNull_AndMakesNoFlexCall()
    {
        var userId = Guid.NewGuid();
        _credentialRepo
            .Setup(r => r.GetByUserIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IBKRFlexCredential?)null);

        var result = await CreateFetcher().FetchAsync(userId, CancellationToken.None);

        result.Should().BeNull();
        _resolver.Verify(r => r.Resolve(It.IsAny<IBKRFlexCredential>()), Times.Never);
        _flexClient.Verify(
            c => c.FetchStatementAsync(It.IsAny<IbkrFlexCredentials>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task FetchAsync_DeactivatedCredential_ReturnsNull_AndMakesNoFlexCall()
    {
        var userId = Guid.NewGuid();
        var credential = new IBKRFlexCredential(userId, "999999", [1], [2], [3], 1);
        credential.Deactivate();
        _credentialRepo
            .Setup(r => r.GetByUserIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(credential);

        var result = await CreateFetcher().FetchAsync(userId, CancellationToken.None);

        result.Should().BeNull();
        _flexClient.Verify(
            c => c.FetchStatementAsync(It.IsAny<IbkrFlexCredentials>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task FetchAsync_ActiveCredential_ResolvesAndFetchesStatement_RecordsSuccess()
    {
        var userId = Guid.NewGuid();
        var credential = new IBKRFlexCredential(userId, "999999", [1], [2], [3], 1);
        var resolved = new IbkrFlexCredentials(userId, "synthetic-token", "999999");
        var statement = new FlexStatementXml { AccountId = "U0000001" };

        _credentialRepo
            .Setup(r => r.GetByUserIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(credential);
        _resolver.Setup(r => r.Resolve(credential)).Returns(resolved);
        _flexClient
            .Setup(c => c.FetchStatementAsync(resolved, It.IsAny<CancellationToken>()))
            .ReturnsAsync(statement);
        _credentialRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var result = await CreateFetcher().FetchAsync(userId, CancellationToken.None);

        result.Should().BeSameAs(statement);
        credential.LastUsedAt.Should().NotBeNull();
    }
}
