using System.Net;
using System.Net.Http.Json;
using FinanceSentry.Modules.BrokerageSync.Domain;
using FluentAssertions;
using Moq;
using Xunit;

namespace FinanceSentry.Tests.Integration.BrokerageSync;

public class BrokerageControllerDisconnectContractTests : IClassFixture<BrokerageApiFactory>
{
    private readonly HttpClient _client;
    private readonly BrokerageApiFactory _factory;

    public BrokerageControllerDisconnectContractTests(BrokerageApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateAuthenticatedClient();
    }

    [Fact]
    public async Task Disconnect_NoAuth_Returns401()
    {
        var anonClient = _factory.CreateClient();
        var response = await anonClient.DeleteAsync("/api/v1/brokerage/ibkr/disconnect");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Disconnect_NoAccount_Returns404()
    {
        _factory.CredentialRepoMock
            .Setup(r => r.GetByUserIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IBKRCredential?)null);
        _factory.HoldingRepoMock
            .Setup(r => r.GetByUserIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var response = await _client.DeleteAsync("/api/v1/brokerage/ibkr/disconnect");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await response.Content.ReadFromJsonAsync<BrokerageErrorShape>();
        body!.ErrorCode.Should().Be("NOT_CONNECTED");
    }

    [Fact]
    public async Task Disconnect_ValidAccount_Returns204()
    {
        var credential = new IBKRCredential(
            _factory.TestUserId, "FINSENTRY", "access-token", "dh-pem",
            [1], [2], [3], [4], [5], [6], [7], [8], [9], keyVersion: 1);
        credential.UpdateAccountId("U1234567");

        _factory.CredentialRepoMock
            .Setup(r => r.GetByUserIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(credential);
        _factory.CredentialRepoMock
            .Setup(r => r.Update(It.IsAny<IBKRCredential>()));
        _factory.CredentialRepoMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _factory.HoldingRepoMock
            .Setup(r => r.GetByUserIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _factory.HoldingRepoMock
            .Setup(r => r.DeleteByUserIdAndProviderAsync(It.IsAny<Guid>(), "ibkr", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _factory.HoldingRepoMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var response = await _client.DeleteAsync("/api/v1/brokerage/ibkr/disconnect");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}
