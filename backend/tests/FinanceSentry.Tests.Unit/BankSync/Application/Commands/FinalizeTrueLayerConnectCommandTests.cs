namespace FinanceSentry.Tests.Unit.BankSync.Application.Commands;

using FinanceSentry.Infrastructure.Encryption;
using FinanceSentry.Modules.BankSync.Application.Commands;
using FinanceSentry.Modules.BankSync.Application.Services;
using FinanceSentry.Modules.BankSync.Domain;
using FinanceSentry.Modules.BankSync.Domain.Repositories;
using FinanceSentry.Modules.BankSync.Infrastructure.TrueLayer;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

/// <summary>
/// Regression coverage for issue #473: a provider account reference ending in an alphanumeric
/// block (e.g. Revolut's "GB29REVO...AB12") must not crash finalize and strand the connection
/// LINKED with zero accounts.
/// </summary>
public class FinalizeTrueLayerConnectCommandTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private sealed record Harness(
        FinalizeTrueLayerConnectCommandHandler Sut,
        Mock<ITrueLayerClient> Client,
        Mock<ITrueLayerConnectionRepository> Connections,
        Mock<IBankAccountRepository> Accounts,
        Mock<IScheduledSyncService> SyncService,
        TrueLayerConnection Connection);

    private static Harness BuildSut()
    {
        var client = new Mock<ITrueLayerClient>();
        var connections = new Mock<ITrueLayerConnectionRepository>();
        var encryption = new Mock<ICredentialEncryptionService>();
        var accounts = new Mock<IBankAccountRepository>();
        var syncService = new Mock<IScheduledSyncService>();
        var configuration = new Mock<IConfiguration>();
        var logger = new Mock<ILogger<FinalizeTrueLayerConnectCommandHandler>>();

        var connection = new TrueLayerConnection(UserId, "ob-revolut", "Revolut", "ref-473");

        connections.Setup(c => c.GetByReferenceAsync("ref-473", It.IsAny<CancellationToken>()))
                   .ReturnsAsync(connection);
        connections.Setup(c => c.UpdateAsync(It.IsAny<TrueLayerConnection>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync((TrueLayerConnection c, CancellationToken _) => c);

        client.Setup(c => c.ExchangeCodeAsync("auth-code", It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(new TrueLayerTokenSet("access-token", "refresh-token", 3600));

        encryption.Setup(e => e.Encrypt("refresh-token"))
                  .Returns(new EncryptionResult([1], [2], [3], 1));

        client.Setup(c => c.ListCardsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(Array.Empty<TrueLayerAccountInfo>());

        accounts.Setup(a => a.GetByExternalAccountIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((BankAccount?)null);
        accounts.Setup(a => a.AddAsync(It.IsAny<BankAccount>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((BankAccount a, CancellationToken _) => a);

        syncService.Setup(s => s.PerformFullSyncAsync(
                It.IsAny<Guid>(), It.IsAny<CancellationToken>(), It.IsAny<string?>()))
            .ThrowsAsync(new InvalidOperationException("sync not under test"));

        var sut = new FinalizeTrueLayerConnectCommandHandler(
            client.Object, connections.Object, encryption.Object, accounts.Object,
            syncService.Object, configuration.Object, logger.Object);

        return new Harness(sut, client, connections, accounts, syncService, connection);
    }

    [Theory]
    [InlineData("GB29REVO00997977ZZAB", "7977")]
    [InlineData("AB", "0000")]
    public void ExtractLast4_KeepsDigitsOnly(string iban, string expected)
        => TrueLayerHttpClient.ExtractLast4(iban, number: null).Should().Be(expected);

    [Fact]
    public async Task Handle_AlphanumericAccountReference_CreatesAccountWithDigitsOnlyLast4AndLinksConnection()
    {
        var h = BuildSut();

        // Revolut-style IBAN ending in an alphanumeric block rather than four digits — the
        // provider account carries whatever a correctly-fixed ExtractLast4 would derive from it.
        const string iban = "GB29REVO00997977ZZAB";
        var providerAccount = new TrueLayerAccountInfo(
            AccountId: "tl-acc-473",
            DisplayName: "Revolut Current Account",
            Currency: "EUR",
            ProviderName: "Revolut",
            AccountType: "checking",
            Iban: iban,
            AccountNumberLast4: TrueLayerHttpClient.ExtractLast4(iban, number: null));

        h.Client.Setup(c => c.ListAccountsAsync("access-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync([providerAccount]);
        h.Client.Setup(c => c.GetBalanceAsync("access-token", "tl-acc-473", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TrueLayerAccountBalance(1000m, 1000m, "EUR"));

        var result = await h.Sut.Handle(
            new FinalizeTrueLayerConnectCommand("ref-473", "auth-code"), CancellationToken.None);

        result.AccountsConnected.Should().Be(1);
        result.CreatedAccountIds.Should().HaveCount(1);
        h.Connection.Status.Should().Be("LINKED");

        h.Accounts.Verify(a => a.AddAsync(
            It.Is<BankAccount>(acc =>
                acc.AccountNumberLast4.Length == 4 &&
                acc.AccountNumberLast4.All(char.IsDigit) &&
                acc.AccountNumberLast4 == "7977"),
            It.IsAny<CancellationToken>()), Times.Once);

        h.Connections.Verify(c => c.UpdateAsync(
            It.Is<TrueLayerConnection>(conn => conn.Status == "LINKED"),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
