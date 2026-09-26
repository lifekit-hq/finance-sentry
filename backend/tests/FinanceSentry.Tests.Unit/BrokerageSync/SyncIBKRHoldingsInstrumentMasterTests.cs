namespace FinanceSentry.Tests.Unit.BrokerageSync;

using FinanceSentry.Modules.BrokerageSync.Application.Commands;
using FinanceSentry.Modules.BrokerageSync.Domain;
using FinanceSentry.Modules.BrokerageSync.Domain.Interfaces;
using FinanceSentry.Modules.BrokerageSync.Domain.Repositories;
using FluentAssertions;
using Moq;
using Xunit;

/// <summary>
/// fs-435 S4 — the instrument master must survive a re-sync (idempotent, no
/// duplicate rows), must never lose its human-set classification to a sync, and
/// its row must outlive the position it backs once that position sells out.
/// </summary>
public class SyncIBKRHoldingsInstrumentMasterTests
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
        credential.UpdateAccountId("acct-1");
        return credential;
    }

    private static (
        Mock<IIBKRCredentialRepository> CredentialRepo,
        Mock<IBrokerageHoldingRepository> HoldingRepo,
        Mock<IBrokerAdapter> Adapter,
        IBKRCredential Credential) Wiring()
    {
        var credential = CredentialWithAccount();

        var credentialRepo = new Mock<IIBKRCredentialRepository>(MockBehavior.Loose);
        credentialRepo.Setup(r => r.GetByUserIdAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(credential);

        var holdingRepo = new Mock<IBrokerageHoldingRepository>(MockBehavior.Loose);
        holdingRepo.Setup(r => r.GetByUserIdAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<BrokerageHolding>());

        var adapter = new Mock<IBrokerAdapter>(MockBehavior.Loose);

        return (credentialRepo, holdingRepo, adapter, credential);
    }

    /// <summary>Backs an in-memory instrument store so tests can assert on what actually got persisted.</summary>
    private sealed class FakeInstrumentRepository : IBrokerageInstrumentRepository
    {
        public readonly List<BrokerageInstrument> Store = [];

        public Task<BrokerageInstrument?> GetByConidAsync(
            Guid userId, string provider, long conid, CancellationToken ct = default) =>
            Task.FromResult(Store.FirstOrDefault(
                i => i.UserId == userId && i.Provider == provider && i.Conid == conid));

        public Task<BrokerageInstrument?> GetByIdAsync(Guid userId, Guid id, CancellationToken ct = default) =>
            Task.FromResult(Store.FirstOrDefault(i => i.UserId == userId && i.Id == id));

        public Task<IReadOnlyList<BrokerageInstrument>> GetByUserIdAsync(Guid userId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<BrokerageInstrument>>(Store.Where(i => i.UserId == userId).ToList());

        public Task AddAsync(BrokerageInstrument instrument, CancellationToken ct = default)
        {
            Store.Add(instrument);
            return Task.CompletedTask;
        }

        public void Update(BrokerageInstrument instrument)
        {
            // In-memory: mutations are already applied to the tracked instance.
        }

        public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    [Fact]
    public async Task Handle_ConidSurvivesTheAdapterIntoTheInstrumentRecord()
    {
        var (credentialRepo, holdingRepo, adapter, credential) = Wiring();
        adapter.Setup(a => a.GetPositionsAsync(credential.Id, "acct-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<BrokerPosition>
            {
                new("AAPL", "STK", 10m, 1000m, Conid: 265598),
            });

        var instrumentRepo = new FakeInstrumentRepository();
        var handler = new SyncIBKRHoldingsCommandHandler(
            credentialRepo.Object, holdingRepo.Object, instrumentRepo, adapter.Object);

        await handler.Handle(new SyncIBKRHoldingsCommand(UserId), CancellationToken.None);

        instrumentRepo.Store.Should().ContainSingle();
        var instrument = instrumentRepo.Store[0];
        instrument.Conid.Should().Be(265598);
        instrument.Symbol.Should().Be("AAPL");
        instrument.Provider.Should().Be("ibkr");
        instrument.Classification.Should().BeNull();
    }

    [Fact]
    public async Task Handle_ReSyncIsIdempotent_NoDuplicateInstrumentRows()
    {
        var (credentialRepo, holdingRepo, adapter, credential) = Wiring();
        adapter.Setup(a => a.GetPositionsAsync(credential.Id, "acct-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<BrokerPosition>
            {
                new("AAPL", "STK", 10m, 1000m, Conid: 265598),
            });

        var instrumentRepo = new FakeInstrumentRepository();
        var handler = new SyncIBKRHoldingsCommandHandler(
            credentialRepo.Object, holdingRepo.Object, instrumentRepo, adapter.Object);

        await handler.Handle(new SyncIBKRHoldingsCommand(UserId), CancellationToken.None);
        await handler.Handle(new SyncIBKRHoldingsCommand(UserId), CancellationToken.None);

        instrumentRepo.Store.Should().ContainSingle("re-syncing the same conid must upsert, not duplicate");
    }

    [Fact]
    public async Task Handle_SoldOutInstrumentRowSurvives()
    {
        var (credentialRepo, holdingRepo, adapter, credential) = Wiring();

        // First sync: AAPL held.
        adapter.Setup(a => a.GetPositionsAsync(credential.Id, "acct-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<BrokerPosition>
            {
                new("AAPL", "STK", 10m, 1000m, Conid: 265598),
            });

        var instrumentRepo = new FakeInstrumentRepository();
        var handler = new SyncIBKRHoldingsCommandHandler(
            credentialRepo.Object, holdingRepo.Object, instrumentRepo, adapter.Object);
        await handler.Handle(new SyncIBKRHoldingsCommand(UserId), CancellationToken.None);

        // Second sync: AAPL fully sold out — the broker stops returning it at all.
        adapter.Setup(a => a.GetPositionsAsync(credential.Id, "acct-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<BrokerPosition>());
        await handler.Handle(new SyncIBKRHoldingsCommand(UserId), CancellationToken.None);

        instrumentRepo.Store.Should().ContainSingle(
            "the instrument row must outlive the holding it backed, so a later tax schedule can still find it");
        instrumentRepo.Store[0].Conid.Should().Be(265598);
    }

    [Fact]
    public async Task Handle_ClassificationStaysNullAfterSync_AndIsNeverOverwrittenBySync()
    {
        var (credentialRepo, holdingRepo, adapter, credential) = Wiring();
        adapter.Setup(a => a.GetPositionsAsync(credential.Id, "acct-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<BrokerPosition>
            {
                new("VWCE", "ETF", 5m, 500m, Conid: 99887766),
            });

        var instrumentRepo = new FakeInstrumentRepository();
        var handler = new SyncIBKRHoldingsCommandHandler(
            credentialRepo.Object, holdingRepo.Object, instrumentRepo, adapter.Object);

        await handler.Handle(new SyncIBKRHoldingsCommand(UserId), CancellationToken.None);
        instrumentRepo.Store.Single().Classification.Should().BeNull();

        // A human classifies it as an offshore fund.
        instrumentRepo.Store.Single().SetClassification(InstrumentClassification.OffshoreFund);

        // A later sync (e.g. the description changes slightly) must not disturb it.
        adapter.Setup(a => a.GetPositionsAsync(credential.Id, "acct-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<BrokerPosition>
            {
                new("VWCE ETF", "ETF", 5m, 520m, Conid: 99887766),
            });
        await handler.Handle(new SyncIBKRHoldingsCommand(UserId), CancellationToken.None);

        var instrument = instrumentRepo.Store.Single();
        instrument.Classification.Should().Be(InstrumentClassification.OffshoreFund);
        instrument.Symbol.Should().Be("VWCE ETF");
    }
}
