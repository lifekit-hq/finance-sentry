namespace FinanceSentry.Tests.Unit.BrokerageSync;

using FinanceSentry.Modules.BrokerageSync.Application.Commands;
using FinanceSentry.Modules.BrokerageSync.Domain;
using FinanceSentry.Modules.BrokerageSync.Domain.Interfaces;
using FinanceSentry.Modules.BrokerageSync.Domain.Repositories;
using FluentAssertions;
using Moq;
using Xunit;

public class SyncIBKRHoldingsReconcileTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private static IBKRCredential CredentialWithAccount()
    {
        var credential = new IBKRCredential(
            UserId,
            consumerKey: "ck",
            accessToken: "at",
            dhParam: "dh",
            encryptedAccessTokenSecret: [1],
            accessTokenSecretIv: [1],
            accessTokenSecretAuthTag: [1],
            encryptedSignatureKey: [1],
            signatureKeyIv: [1],
            signatureKeyAuthTag: [1],
            encryptedEncryptionKey: [1],
            encryptionKeyIv: [1],
            encryptionKeyAuthTag: [1],
            keyVersion: 1);
        credential.UpdateAccountId("acct-1"); // skip the GetAccountIdAsync path
        return credential;
    }

    private static BrokerageHolding Holding(string symbol, decimal quantity) =>
        new(UserId, symbol, "STK", quantity, quantity * 10m, "ibkr");

    [Fact]
    public async Task Handle_DropsZeroQuantityAndSoldOutHoldings()
    {
        var credential = CredentialWithAccount();

        var credentialRepo = new Mock<IIBKRCredentialRepository>(MockBehavior.Loose);
        credentialRepo.Setup(r => r.GetByUserIdAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(credential);

        var adapter = new Mock<IBrokerAdapter>(MockBehavior.Loose);
        // AAPL comes back at qty 0 (sold out but still reported); MSFT is a real position.
        adapter.Setup(a => a.GetPositionsAsync(credential.Id, "acct-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<BrokerPosition>
            {
                new("AAPL", "STK", 0m, 0m),
                new("MSFT", "STK", 10m, 1000m),
            });

        // Persisted holdings include TSLA, which is no longer returned at all (fully sold).
        var persisted = new List<BrokerageHolding>
        {
            Holding("MSFT", 10m),
            Holding("TSLA", 5m),
            Holding("AAPL", 3m),
        };
        var holdingRepo = new Mock<IBrokerageHoldingRepository>(MockBehavior.Loose);
        holdingRepo.Setup(r => r.GetByUserIdAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(persisted);

        List<BrokerageHolding>? upserted = null;
        holdingRepo.Setup(r => r.UpsertRangeAsync(It.IsAny<IEnumerable<BrokerageHolding>>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<BrokerageHolding>, CancellationToken>((h, _) => upserted = h.ToList())
            .Returns(Task.CompletedTask);

        List<BrokerageHolding>? removed = null;
        holdingRepo.Setup(r => r.RemoveRange(It.IsAny<IEnumerable<BrokerageHolding>>()))
            .Callback<IEnumerable<BrokerageHolding>>(h => removed = h.ToList());

        var instrumentRepo = new Mock<IBrokerageInstrumentRepository>(MockBehavior.Loose);

        var handler = new SyncIBKRHoldingsCommandHandler(
            credentialRepo.Object, holdingRepo.Object, instrumentRepo.Object, adapter.Object);

        var result = await handler.Handle(new SyncIBKRHoldingsCommand(UserId), CancellationToken.None);

        // Zero-qty AAPL is never persisted; only MSFT is upserted.
        upserted.Should().ContainSingle().Which.Symbol.Should().Be("MSFT");
        result.HoldingsCount.Should().Be(1);

        // TSLA (gone) and AAPL (now zero) are reconciled out of the DB.
        removed.Should().NotBeNull();
        removed!.Select(h => h.Symbol).Should().BeEquivalentTo(["TSLA", "AAPL"]);
    }
}
