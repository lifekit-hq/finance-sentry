namespace FinanceSentry.Tests.Unit.BankSync.Application;

using FinanceSentry.Core.Domain;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Infrastructure.Encryption;
using FinanceSentry.Modules.BankSync.Application.Services;
using FinanceSentry.Modules.BankSync.Application.Services.CategoryMapping;
using FinanceSentry.Modules.BankSync.Domain;
using FinanceSentry.Modules.BankSync.Domain.Interfaces;
using FinanceSentry.Modules.BankSync.Domain.Repositories;
using FinanceSentry.Tests.Unit.BankSync.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

public class TransactionRecategorizationServiceTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private readonly Mock<IBankAccountRepository> _accounts = new();
    private readonly Mock<ITransactionRepository> _transactions = new();
    private readonly Mock<ICredentialEncryptionService> _encryption = new();
    private readonly Mock<ITransactionDeduplicationService> _dedup = new();
    private readonly Mock<IBankProviderFactory> _providerFactory = new();
    private readonly Mock<IMonobankCredentialRepository> _monobankCredentials = new();
    private readonly Mock<FinanceSentry.Modules.BankSync.Infrastructure.Monobank.IMonobankAdapter> _monobankAdapter = new();
    private readonly Mock<ITrueLayerConnectionRepository> _truelayerConnections = new();
    private readonly Mock<FinanceSentry.Modules.BankSync.Infrastructure.TrueLayer.ITrueLayerClient> _truelayerClient = new();
    private readonly Mock<ICategoryResolver> _resolver = new();
    private IActiveSubscriptionsReader _activeSubscriptions = StubActiveSubscriptionsReader.Empty;

    private TransactionRecategorizationService BuildSut()
    {
        return new TransactionRecategorizationService(
            _accounts.Object, _transactions.Object, _encryption.Object,
            _dedup.Object, _providerFactory.Object, _monobankCredentials.Object,
            _monobankAdapter.Object, _truelayerConnections.Object, _truelayerClient.Object,
            _resolver.Object, _activeSubscriptions,
            new Mock<ILogger<TransactionRecategorizationService>>().Object);
    }

    [Fact]
    public async Task ReResolvesRowsThatAlreadyCarryAnMcc_WithoutAnyProviderCall()
    {
        var accountId = Guid.NewGuid();
        var tx = new Transaction(accountId, UserId, 12m, DateTime.UtcNow, "ATB", "h1")
        {
            Mcc = 5411,
            MerchantCategory = CategoryKeys.Uncategorized,
        };
        _accounts.Setup(r => r.GetByUserIdAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _transactions.Setup(r => r.GetByUserIdAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([tx]);
        _resolver.Setup(r => r.ResolveMcc(5411)).Returns(CategoryKeys.FoodAndDrink);

        var result = await BuildSut().RecategorizeUserAsync(UserId);

        tx.MerchantCategory.Should().Be(CategoryKeys.FoodAndDrink);
        result.ReResolved.Should().Be(1);
        result.StillUncategorized.Should().Be(0);
        _transactions.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _monobankAdapter.Verify(a => a.GetCandidatesAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Guid>(),
            It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ReFetchesFromMonobank_WindowedByOldestRow_AndUpdatesMatchedRow()
    {
        var credentialId = Guid.NewGuid();
        var account = new BankAccount(UserId, "mono_1", "Monobank", "black", "1234", "Owner", "UAH", UserId, "monobank")
        {
            MonobankCredentialId = credentialId,
        };
        // Oldest row is recent → a single ≤31-day window → no rate-limit delay in the test.
        var recent = DateTime.UtcNow.AddDays(-5);
        var tx = new Transaction(account.Id, UserId, 30m, recent, "ATB", "MHASH")
        {
            Mcc = null,
            SourceCategory = null,
            MerchantCategory = CategoryKeys.Uncategorized,
        };

        _accounts.Setup(r => r.GetByUserIdAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([account]);
        _transactions.Setup(r => r.GetByUserIdAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([tx]);
        _monobankCredentials.Setup(r => r.GetByIdAsync(credentialId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FinanceSentry.Modules.BankSync.Domain.MonobankCredential(
                UserId, new byte[32], new byte[12], new byte[16]));
        _encryption.Setup(e => e.Decrypt(It.IsAny<byte[]>(), It.IsAny<byte[]>(), It.IsAny<byte[]>(), It.IsAny<int>()))
            .Returns("mono-token");

        var candidate = new TransactionCandidate(
            AccountId: account.Id, UserId: UserId, Amount: 30m,
            TransactionDate: recent, PostedDate: recent, Description: "ATB",
            IsPending: false, TransactionType: "debit", MerchantName: "ATB",
            MerchantCategory: CategoryKeys.FoodAndDrink,
            Mcc: 5411, SourceCategory: null);

        _monobankAdapter.Setup(a => a.GetCandidatesAsync(
                "mono-token", account.ExternalAccountId, account.Id, UserId,
                It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([candidate]);
        _dedup.Setup(d => d.ComputeHash(account.Id, 30m, It.IsAny<DateTime>(), "ATB")).Returns("MHASH");

        var result = await BuildSut().RecategorizeUserAsync(UserId);

        tx.MerchantCategory.Should().Be(CategoryKeys.FoodAndDrink);
        tx.Mcc.Should().Be(5411);
        result.ReFetchedUpdated.Should().Be(1);
        result.StillUncategorized.Should().Be(0);
    }

    [Fact]
    public async Task BackfillsStoredMortgageRow_FromTransferOutToLoanPayments_WithoutAnyProviderCall()
    {
        // #553: the mortgage carries the wire-transfer MCC 4829, so re-resolving by MCC alone
        // re-buries it in TRANSFER_OUT. The active installment plan is what rescues it, and the
        // recategorization path is the backfill for history ingested before the rule existed.
        const string mortgageDescription = "516936******4992";
        const decimal mortgageAmount = 14060.96m;
        var accountId = Guid.NewGuid();
        var tx = new Transaction(accountId, UserId, mortgageAmount, DateTime.UtcNow, mortgageDescription, "h-mortgage")
        {
            TransactionType = "debit",
            Mcc = 4829,
            MerchantCategory = CategoryKeys.TransferOut,
        };
        _activeSubscriptions = new StubActiveSubscriptionsReader(new ActiveInstallmentPlan(
            CommitmentKeyResolver.Resolve(null, mortgageDescription, mortgageAmount, 4829),
            mortgageAmount));
        _accounts.Setup(r => r.GetByUserIdAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _transactions.Setup(r => r.GetByUserIdAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([tx]);

        var result = await BuildSut().RecategorizeUserAsync(UserId);

        tx.MerchantCategory.Should().Be(CategoryKeys.LoanPayments);
        result.ReResolved.Should().Be(1);
        _resolver.Verify(r => r.ResolveMcc(It.IsAny<int?>()), Times.Never);
    }

    [Fact]
    public async Task LeavesAGenuineTransferOnTheSameMcc_AsTransferOut()
    {
        var accountId = Guid.NewGuid();
        var tx = new Transaction(accountId, UserId, 5000m, DateTime.UtcNow, "Переказ на картку", "h-transfer")
        {
            TransactionType = "debit",
            Mcc = 4829,
            MerchantCategory = CategoryKeys.TransferOut,
        };
        _accounts.Setup(r => r.GetByUserIdAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _transactions.Setup(r => r.GetByUserIdAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([tx]);
        _resolver.Setup(r => r.ResolveMcc(4829)).Returns(CategoryKeys.TransferOut);

        var result = await BuildSut().RecategorizeUserAsync(UserId);

        tx.MerchantCategory.Should().Be(CategoryKeys.TransferOut);
        result.ReResolved.Should().Be(0);
    }
}
