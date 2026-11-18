namespace FinanceSentry.Tests.Unit.MoneySemantics;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Core.Utils;
using FinanceSentry.Modules.BankSync.Application.Services;
using FinanceSentry.Modules.BankSync.Application.Services.DetectionPhrases;
using FinanceSentry.Modules.BankSync.Domain;
using FinanceSentry.Modules.BankSync.Domain.Repositories;
using FinanceSentry.Tests.Unit.BankSync.Application;
using FinanceSentry.Tests.Unit.Fx;
using FluentAssertions;
using Moq;
using Xunit;

/// <summary>
/// Issue #1470 cases 8 and 9: the monthly outflow and the small-balance rule compare USD to USD, never native to
/// USD (docs/money-semantics.md sections 2 and 3). Seed rates.
/// </summary>
public sealed class SpendAndVisibilityCasesTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private static BankAccount Account(string currency) =>
        new(UserId, $"item_{Guid.NewGuid():N}", "Bank", "checking", "1234", "Owner", currency, UserId, "truelayer");

    private static Transaction Outflow(Guid accountId, decimal amount, DateTime date) =>
        new(accountId, UserId, amount, date, "desc", Guid.NewGuid().ToString("N"), false)
        {
            TransactionType = "debit",
            PostedDate = date,
            IsActive = true,
        };

    [Fact]
    public async Task Case_08_SpendSummary_Outflows10000Uah_And50Eur_Is294Usd_NeverTheNative10050()
    {
        using var _ = FxTableScope.Seed();
        var uah = Account("UAH");
        var eur = Account("EUR");
        var date = DateTime.UtcNow.AddDays(-1);
        var transactions = new List<Transaction> { Outflow(uah.Id, 10_000m, date), Outflow(eur.Id, 50m, date) };

        var txRepo = new Mock<ITransactionRepository>();
        txRepo.Setup(r => r.GetByUserIdSinceUnscopedAsync(UserId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(transactions);
        var accountRepo = new Mock<IBankAccountRepository>();
        accountRepo.Setup(r => r.GetByUserIdUnscopedAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync([uah, eur]);
        var policy = new Mock<ICommittedOutflowPolicy>();
        policy.Setup(p => p.LoadForUserAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommittedOutflowRules
            {
                ActiveCommitmentKeys = new HashSet<string>(),
                PinnedMerchantKeys = new HashSet<string>(),
                Phrases = DetectionPhraseSet.Seed,
            });

        var sut = new MoneyFlowStatisticsService(txRepo.Object, accountRepo.Object, new TransferDetectionService(), policy.Object);

        var result = await sut.GetMonthlyFlowAsync(UserId, CounterpartyResults.None, 6);

        result.Sum(r => r.OutflowUsd).Should().Be(294.00m);
    }

    [Fact]
    public async Task Case_09_SmallBalanceRule_Threshold10Eur_Becomes10_80Usd_1000Uah_IsNotSmall_100Uah_IsSmall()
    {
        using var _ = FxTableScope.Seed();
        var reader = new Mock<IUserAssetVisibilityReader>();
        reader.Setup(r => r.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserAssetVisibility(true, true, 10m));
        var currency = new Mock<IUserBaseCurrencyReader>();
        currency.Setup(r => r.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync("EUR");

        var prefs = await AssetVisibility.ResolveInUsdAsync(reader.Object, currency.Object, UserId, CancellationToken.None);

        prefs.SmallBalanceThreshold.Should().Be(10.80m);
        AssetVisibility.Classify(24.0m, false, prefs).Should().BeNull("1,000 UAH is 24 USD, above 10.80 USD");
        AssetVisibility.Classify(2.4m, false, prefs).Should().Be("small", "100 UAH is 2.4 USD, below 10.80 USD");
    }
}
