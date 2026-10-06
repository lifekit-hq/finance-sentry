namespace FinanceSentry.Tests.Unit.BankSync.Application;

using FinanceSentry.Modules.BankSync.Application.Services;
using FinanceSentry.Modules.BankSync.Domain;
using FinanceSentry.Modules.BankSync.Domain.Repositories;
using FluentAssertions;
using Moq;
using Xunit;

/// <summary>
/// Unit tests for FlowBreakdownService (#576): the per-transaction audit view must apply
/// the exact classification MoneyFlowStatisticsService applies, so a bucket's USD sum
/// reproduces the tile it explains.
/// </summary>
public class FlowBreakdownServiceTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private const string Month = "2026-05";
    private static readonly DateTime InMonth = new(2026, 5, 10, 0, 0, 0, DateTimeKind.Utc);

    private static (BankAccount account, Guid accountId) MakeAccount(string currency)
    {
        var a = new BankAccount(UserId, $"item_{Guid.NewGuid():N}", "Bank", "checking", "1234", "Owner", currency, UserId, "truelayer");
        return (a, a.Id);
    }

    private static Transaction MakeTx(
        Guid accountId, decimal amount, string type, DateTime date,
        string description = "desc", string? category = null)
    {
        return new Transaction(accountId, UserId, amount, date, description, Guid.NewGuid().ToString("N"), isPending: false)
        {
            TransactionType = type,
            PostedDate = date,
            IsActive = true,
            MerchantCategory = category
        };
    }

    private static FlowBreakdownService Sut(
        IReadOnlyList<Transaction> transactions,
        BankAccount account,
        CounterpartyClassificationResult classification)
    {
        var txRepo = new Mock<ITransactionRepository>();
        txRepo.Setup(r => r.GetByUserIdSinceAsync(UserId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(transactions);

        var accountRepo = new Mock<IBankAccountRepository>();
        accountRepo.Setup(r => r.GetByUserIdAsync(UserId, It.IsAny<CancellationToken>()))
                   .ReturnsAsync([account]);

        var counterparty = new Mock<ICounterpartyClassificationService>();
        counterparty.Setup(c => c.ClassifyForWindowAsync(UserId, It.IsAny<int>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(classification);

        return new FlowBreakdownService(
            txRepo.Object, accountRepo.Object, new TransferDetectionService(), counterparty.Object);
    }

    private static CounterpartyClassificationResult WithMatches(params (Guid Id, string Name, string Role)[] matches)
        => new(
            matches.Select(m => m.Id).ToHashSet(),
            [],
            matches.ToDictionary(m => m.Id, m => new CounterpartyMatch(m.Name, m.Role)));

    [Fact]
    public async Task NormalCreditsAndDebits_LandInIncomeAndSpending()
    {
        var (account, accountId) = MakeAccount("USD");
        var salary = MakeTx(accountId, 1000m, "credit", InMonth, "Salary");
        var groceries = MakeTx(accountId, 400m, "debit", InMonth, "Groceries");

        var result = await Sut([salary, groceries], account, CounterpartyResults.None)
            .GetBreakdownAsync(UserId, Month);

        result.Items.Should().HaveCount(2);
        result.Items.Single(i => i.TransactionId == salary.Id).Bucket.Should().Be(FlowBuckets.Income);
        var spend = result.Items.Single(i => i.TransactionId == groceries.Id);
        spend.Bucket.Should().Be(FlowBuckets.Spending);
        spend.Direction.Should().Be("out");
        spend.CounterpartyName.Should().BeNull();
    }

    [Fact]
    public async Task CounterpartyRoles_DecideTheBucket()
    {
        var (account, accountId) = MakeAccount("USD");
        var rent = MakeTx(accountId, 430m, "credit", InMonth, "From Mom");
        var support = MakeTx(accountId, 300m, "debit", InMonth, "To Mom");
        var mortgage = MakeTx(accountId, 315m, "debit", InMonth, "To 516936");
        var invested = MakeTx(accountId, 800m, "debit", InMonth, "To IBKR");
        var withdrawn = MakeTx(accountId, 100m, "credit", InMonth, "From IBKR");

        var classification = WithMatches(
            (rent.Id, "Mom", FlowRoles.FamilySupport),
            (support.Id, "Mom", FlowRoles.FamilySupport),
            (mortgage.Id, "Mortgage", FlowRoles.Household),
            (invested.Id, "Investment routing", FlowRoles.Investment),
            (withdrawn.Id, "Investment routing", FlowRoles.Investment));

        var result = await Sut([rent, support, mortgage, invested, withdrawn], account, classification)
            .GetBreakdownAsync(UserId, Month);

        result.Items.Single(i => i.TransactionId == rent.Id).Bucket.Should().Be(FlowBuckets.Income);
        result.Items.Single(i => i.TransactionId == support.Id).Bucket.Should().Be(FlowBuckets.Spending);
        var mortgageItem = result.Items.Single(i => i.TransactionId == mortgage.Id);
        mortgageItem.Bucket.Should().Be(FlowBuckets.Spending);
        mortgageItem.FlowRole.Should().Be(FlowRoles.Household);
        mortgageItem.CounterpartyName.Should().Be("Mortgage");
        result.Items.Single(i => i.TransactionId == invested.Id).Bucket.Should().Be(FlowBuckets.Invested);
        result.Items.Single(i => i.TransactionId == withdrawn.Id).Bucket.Should().Be(FlowBuckets.InvestmentReturn);
    }

    [Fact]
    public async Task SelfRoutingLegs_LandInExcludedRouting_BothDirections()
    {
        var (account, accountId) = MakeAccount("EUR");
        var outLeg = MakeTx(accountId, 1200m, "debit", InMonth, "Liudmyla Sychova");
        var backLeg = MakeTx(accountId, 1200m, "credit", InMonth, "Від: Людмила Сичова");

        var classification = WithMatches(
            (outLeg.Id, "Routing via mom (EUR)", FlowRoles.SelfRouting),
            (backLeg.Id, "Routing via mom (EUR)", FlowRoles.SelfRouting));

        var result = await Sut([outLeg, backLeg], account, classification)
            .GetBreakdownAsync(UserId, Month);

        result.Items.Should().HaveCount(2);
        result.Items.Should().OnlyContain(i => i.Bucket == FlowBuckets.ExcludedRouting);
        result.Items.Should().OnlyContain(i => i.CounterpartyName == "Routing via mom (EUR)");
    }

    [Fact]
    public async Task TransferCategory_IsExcluded_UnlessACounterpartyClaimedIt()
    {
        var (account, accountId) = MakeAccount("USD");
        var repayment = MakeTx(accountId, 720m, "credit", InMonth, "Card top-up", CategoryKeysTransferIn());
        var claimed = MakeTx(accountId, 315m, "debit", InMonth, "To 516936", "TRANSFER_OUT");

        var classification = WithMatches((claimed.Id, "Mortgage", FlowRoles.Household));

        var result = await Sut([repayment, claimed], account, classification)
            .GetBreakdownAsync(UserId, Month);

        result.Items.Single(i => i.TransactionId == repayment.Id).Bucket.Should().Be(FlowBuckets.ExcludedTransfer);
        result.Items.Single(i => i.TransactionId == claimed.Id).Bucket.Should().Be(FlowBuckets.Spending);
    }

    [Fact]
    public async Task DetectedTransferPair_BothLegsExcluded()
    {
        var (account, accountId) = MakeAccount("USD");
        var (other, otherId) = MakeAccount("USD");

        // Same amount, same day, different accounts, shared description words — the
        // pair-detection confirming signal.
        var debit = MakeTx(accountId, 500m, "debit", InMonth, "internal move alpha");
        var credit = MakeTx(otherId, 500m, "credit", InMonth, "internal move alpha");

        var txRepo = new Mock<ITransactionRepository>();
        txRepo.Setup(r => r.GetByUserIdSinceAsync(UserId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync([debit, credit]);
        var accountRepo = new Mock<IBankAccountRepository>();
        accountRepo.Setup(r => r.GetByUserIdAsync(UserId, It.IsAny<CancellationToken>()))
                   .ReturnsAsync([account, other]);
        var counterparty = new Mock<ICounterpartyClassificationService>();
        counterparty.Setup(c => c.ClassifyForWindowAsync(UserId, It.IsAny<int>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(CounterpartyResults.None);

        var sut = new FlowBreakdownService(
            txRepo.Object, accountRepo.Object, new TransferDetectionService(), counterparty.Object);

        var result = await sut.GetBreakdownAsync(UserId, Month);

        result.Items.Should().HaveCount(2);
        result.Items.Should().OnlyContain(i => i.Bucket == FlowBuckets.ExcludedPair);
    }

    [Fact]
    public async Task OtherMonths_AreFilteredOut_ButStillInformPairDetection()
    {
        var (account, accountId) = MakeAccount("USD");
        var inMay = MakeTx(accountId, 100m, "debit", InMonth, "May spend");
        var inApril = MakeTx(accountId, 999m, "debit", new DateTime(2026, 4, 10, 0, 0, 0, DateTimeKind.Utc), "April spend");

        var result = await Sut([inMay, inApril], account, CounterpartyResults.None)
            .GetBreakdownAsync(UserId, Month);

        result.Month.Should().Be(Month);
        result.Items.Should().ContainSingle(i => i.TransactionId == inMay.Id);
    }

    [Fact]
    public async Task AmountUsd_ConvertsFromTheAccountCurrency()
    {
        var (account, accountId) = MakeAccount("EUR");
        var tx = MakeTx(accountId, 100m, "debit", InMonth, "EUR spend");

        var result = await Sut([tx], account, CounterpartyResults.None)
            .GetBreakdownAsync(UserId, Month);

        var item = result.Items.Single();
        item.Currency.Should().Be("EUR");
        item.Amount.Should().Be(100m);
        // Assert against the converter itself, not a literal — the process-wide rate table
        // may have been swapped by another test.
        item.AmountUsd.Should().Be(FinanceSentry.Core.Utils.CurrencyConverter.ToUsd(100m, "EUR"));
    }

    private static string CategoryKeysTransferIn() => "TRANSFER_IN";

    [Fact]
    public async Task RangeBreakdown_IncludesBothEdgeDaysAndNothingOutsideThem()
    {
        var (account, accountId) = MakeAccount("USD");
        var before = MakeTx(accountId, 1m, "debit", new DateTime(2026, 5, 9, 23, 59, 59, DateTimeKind.Utc));
        var firstDay = MakeTx(accountId, 2m, "debit", new DateTime(2026, 5, 10, 0, 0, 0, DateTimeKind.Utc));
        var lastDay = MakeTx(accountId, 4m, "credit", new DateTime(2026, 5, 12, 23, 59, 59, DateTimeKind.Utc));
        var after = MakeTx(accountId, 8m, "debit", new DateTime(2026, 5, 13, 0, 0, 0, DateTimeKind.Utc));

        var result = await Sut([before, firstDay, lastDay, after], account, CounterpartyResults.None)
            .GetRangeBreakdownAsync(UserId, new DateOnly(2026, 5, 10), new DateOnly(2026, 5, 12));

        result.Items.Select(i => i.TransactionId).Should().BeEquivalentTo([firstDay.Id, lastDay.Id]);
        result.Month.Should().BeEmpty();
        result.From.Should().Be(new DateOnly(2026, 5, 10));
        result.To.Should().Be(new DateOnly(2026, 5, 12));
    }

    [Fact]
    public async Task RangeBreakdown_CrossesAMonthBoundary()
    {
        var (account, accountId) = MakeAccount("USD");
        var april = MakeTx(accountId, 3m, "debit", new DateTime(2026, 4, 30, 12, 0, 0, DateTimeKind.Utc));
        var may = MakeTx(accountId, 5m, "debit", new DateTime(2026, 5, 1, 12, 0, 0, DateTimeKind.Utc));

        var result = await Sut([april, may], account, CounterpartyResults.None)
            .GetRangeBreakdownAsync(UserId, new DateOnly(2026, 4, 28), new DateOnly(2026, 5, 2));

        result.Items.Should().HaveCount(2);
    }
}
