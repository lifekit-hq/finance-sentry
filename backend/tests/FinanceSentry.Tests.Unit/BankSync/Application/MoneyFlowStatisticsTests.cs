namespace FinanceSentry.Tests.Unit.BankSync.Application;

using FinanceSentry.Core.Domain;
using FinanceSentry.Core.Utils;
using FinanceSentry.Modules.BankSync.Application.Services;
using FinanceSentry.Modules.BankSync.Domain;
using FinanceSentry.Modules.BankSync.Domain.Repositories;
using FluentAssertions;
using Moq;
using Xunit;

/// <summary>
/// Unit tests for MoneyFlowStatisticsService (T413).
/// All repository dependencies are mocked; no database required.
/// </summary>
public class MoneyFlowStatisticsTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a BankAccount and returns both the entity and its auto-generated Id.
    /// </summary>
    private static (BankAccount account, Guid accountId) MakeAccount(string currency)
    {
        var a = new BankAccount(UserId, $"item_{Guid.NewGuid():N}", "Bank", "checking", "1234", "Owner", currency, UserId, "truelayer");
        return (a, a.Id);
    }

    private static Transaction MakeTx(
        Guid accountId, decimal amount, string type, DateTime date, bool isPending = false,
        string? merchantName = null, string description = "desc", int? mcc = null,
        string? category = null)
    {
        var hash = Guid.NewGuid().ToString("N");
        var tx = new Transaction(accountId, UserId, amount, date, description, hash, isPending)
        {
            TransactionType = type,
            PostedDate = isPending ? null : date,
            IsActive = true,
            MerchantName = merchantName,
            Mcc = mcc,
            MerchantCategory = category
        };
        return tx;
    }

    private static Mock<ITransactionRepository> TxRepo(IReadOnlyList<Transaction> transactions)
    {
        var mock = new Mock<ITransactionRepository>();
        mock.Setup(r => r.GetByUserIdSinceAsync(UserId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(transactions);
        return mock;
    }

    private static Mock<IBankAccountRepository> AccountRepo(params BankAccount[] accounts)
    {
        var mock = new Mock<IBankAccountRepository>();
        mock.Setup(r => r.GetByUserIdAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(accounts);
        return mock;
    }

    /// <summary>
    /// The real <see cref="CommittedOutflowRules"/> behind a stubbed loader: these tests are
    /// about what the reader does with a verdict, so stubbing the verdict itself would let the
    /// two drift. Only the per-user data the rules read is faked.
    /// </summary>
    private static ICommittedOutflowPolicy CommittedPolicy(params string[] activeCommitmentKeys) =>
        Policy(activeCommitmentKeys, []);

    /// <summary>A user who has pinned merchants (rule (d)) but has no detected commitments.</summary>
    private static ICommittedOutflowPolicy CommittedPolicyWithPins(params string[] pinnedMerchantKeys) =>
        Policy([], pinnedMerchantKeys);

    private static ICommittedOutflowPolicy Policy(string[] activeCommitmentKeys, string[] pinnedMerchantKeys)
    {
        var mock = new Mock<ICommittedOutflowPolicy>();
        mock.Setup(p => p.LoadForUserAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommittedOutflowRules
            {
                ActiveCommitmentKeys = activeCommitmentKeys.ToHashSet(StringComparer.Ordinal),
                PinnedMerchantKeys = pinnedMerchantKeys.ToHashSet(StringComparer.Ordinal),
            });
        return mock.Object;
    }

    // ── T413 Test 1: Six months of monthly flow ───────────────────────────────

    [Fact]
    public async Task GetMonthlyFlow_SixMonths_ReturnsCorrectInOutNet()
    {
        // Arrange: one credit and one debit per month for 6 months
        var (account, accountId) = MakeAccount("EUR");
        var now = DateTime.UtcNow;
        var transactions = new List<Transaction>();

        for (int i = 0; i < 6; i++)
        {
            var month = now.AddMonths(-i);
            var monthDate = new DateTime(month.Year, month.Month, 15, 0, 0, 0, DateTimeKind.Utc);
            transactions.Add(MakeTx(accountId, 1000m, "credit", monthDate));
            transactions.Add(MakeTx(accountId, 600m, "debit", monthDate));
        }

        var txRepoMock = new Mock<ITransactionRepository>();
        txRepoMock.Setup(r => r.GetByUserIdSinceAsync(UserId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(transactions);

        var accountRepoMock = new Mock<IBankAccountRepository>();
        accountRepoMock.Setup(r => r.GetByUserIdAsync(UserId, It.IsAny<CancellationToken>()))
                       .ReturnsAsync([account]);

        var sut = new MoneyFlowStatisticsService(
            txRepoMock.Object, accountRepoMock.Object, new TransferDetectionService(), CommittedPolicy());

        // Act
        var result = await sut.GetMonthlyFlowAsync(UserId, CounterpartyResults.None, 6);

        // Assert: 6 months, each with 1000 inflow, 600 outflow, 400 net
        result.Should().HaveCount(6);
        result.Should().AllSatisfy(mf =>
        {
            mf.Inflow.Should().Be(1000m);
            mf.Outflow.Should().Be(600m);
            mf.Net.Should().Be(400m);
            mf.Currency.Should().Be("EUR");
        });

        // Results should be sorted by month DESC
        var months = result.Select(mf => mf.Month).ToList();
        months.Should().BeInDescendingOrder();
    }

    // ── T413 Test 2: Pending transactions counted ─────────────────────────────

    [Fact]
    public async Task GetMonthlyFlow_IncludesPendingTransactions()
    {
        // A card hold is committed money — excluding pending debits made the current
        // month's outflow a fraction of real spending (the "$688 outflow" bug).
        var (account, accountId) = MakeAccount("EUR");
        var date = new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc);

        var transactions = new List<Transaction>
        {
            MakeTx(accountId, 500m, "credit", date, isPending: false),
            MakeTx(accountId, 100m, "debit",  date, isPending: false),
            MakeTx(accountId, 40m,  "debit",  date, isPending: true)
        };

        var txRepoMock = new Mock<ITransactionRepository>();
        txRepoMock.Setup(r => r.GetByUserIdSinceAsync(UserId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(transactions);

        var accountRepoMock = new Mock<IBankAccountRepository>();
        accountRepoMock.Setup(r => r.GetByUserIdAsync(UserId, It.IsAny<CancellationToken>()))
                       .ReturnsAsync([account]);

        var sut = new MoneyFlowStatisticsService(
            txRepoMock.Object, accountRepoMock.Object, new TransferDetectionService(), CommittedPolicy());

        // Act
        var result = await sut.GetMonthlyFlowAsync(UserId, CounterpartyResults.None, 6);

        // Assert: the pending debit counts → outflow = 100 + 40
        result.Should().HaveCount(1);
        result[0].Inflow.Should().Be(500m);
        result[0].Outflow.Should().Be(140m);
        result[0].Net.Should().Be(360m);
    }

    // ── T413 Test 3: Multi-currency separate stats ────────────────────────────

    [Fact]
    public async Task GetMonthlyFlow_MultiCurrency_SeparateStatsPerCurrency()
    {
        // Arrange
        var (eurAccount, eurAccountId) = MakeAccount("EUR");
        var (usdAccount, usdAccountId) = MakeAccount("USD");
        var date = new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc);

        var transactions = new List<Transaction>
        {
            MakeTx(eurAccountId, 1000m, "credit", date),
            MakeTx(eurAccountId, 400m,  "debit",  date),
            MakeTx(usdAccountId, 800m,  "credit", date),
            MakeTx(usdAccountId, 300m,  "debit",  date)
        };

        var txRepoMock = new Mock<ITransactionRepository>();
        txRepoMock.Setup(r => r.GetByUserIdSinceAsync(UserId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(transactions);

        var accountRepoMock = new Mock<IBankAccountRepository>();
        accountRepoMock.Setup(r => r.GetByUserIdAsync(UserId, It.IsAny<CancellationToken>()))
                       .ReturnsAsync([eurAccount, usdAccount]);

        var sut = new MoneyFlowStatisticsService(
            txRepoMock.Object, accountRepoMock.Object, new TransferDetectionService(), CommittedPolicy());

        // Act
        var result = await sut.GetMonthlyFlowAsync(UserId, CounterpartyResults.None, 6);

        // Assert: 2 rows — one for EUR, one for USD (same month)
        result.Should().HaveCount(2);

        var eurFlow = result.First(mf => mf.Currency == "EUR");
        eurFlow.Inflow.Should().Be(1000m);
        eurFlow.Outflow.Should().Be(400m);
        eurFlow.Net.Should().Be(600m);

        var usdFlow = result.First(mf => mf.Currency == "USD");
        usdFlow.Inflow.Should().Be(800m);
        usdFlow.Outflow.Should().Be(300m);
        usdFlow.Net.Should().Be(500m);
    }

    // ── Transfer exclusion: internal transfer pair must not inflate inflow/outflow ─

    [Fact]
    public async Task GetMonthlyFlow_ExcludesInternalTransferPair()
    {
        var (accountA, accountAId) = MakeAccount("USD");
        var (accountB, accountBId) = MakeAccount("USD");
        var date = new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc);

        var transferDebit  = MakeTx(accountAId, 500m, "debit",  date);
        var transferCredit = MakeTx(accountBId, 500m, "credit", date);
        transferDebit.Description  = "Transfer to savings";
        transferCredit.Description = "Transfer to savings";

        var transactions = new List<Transaction>
        {
            transferDebit,
            transferCredit,
            MakeTx(accountAId, 100m, "debit",  date),  // real spending — kept
            MakeTx(accountAId, 300m, "credit", date),  // real income   — kept
        };

        var txRepoMock = new Mock<ITransactionRepository>();
        txRepoMock.Setup(r => r.GetByUserIdSinceAsync(UserId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(transactions);

        var accountRepoMock = new Mock<IBankAccountRepository>();
        accountRepoMock.Setup(r => r.GetByUserIdAsync(UserId, It.IsAny<CancellationToken>()))
                       .ReturnsAsync([accountA, accountB]);

        var sut = new MoneyFlowStatisticsService(
            txRepoMock.Object, accountRepoMock.Object, new TransferDetectionService(), CommittedPolicy());

        var result = await sut.GetMonthlyFlowAsync(UserId, CounterpartyResults.None, 6);

        result.Should().HaveCount(1);
        result[0].Inflow.Should().Be(300m);   // transfer credit excluded
        result[0].Outflow.Should().Be(100m);  // transfer debit  excluded
        result[0].Net.Should().Be(200m);
    }

    // ── T044 Test: FamilySupportOutflowUsd populated from counterparty expense ──

    [Fact]
    public async Task GetMonthlyFlow_WithCounterpartyExpense_FamilySupportOutflowUsdPopulated()
    {
        // Arrange: one credit and one debit in a single month; the counterparty stub
        // reports a net expense of 50 USD for that month so the family-support split
        // can be verified independently from the normal outflow.
        var (account, accountId) = MakeAccount("USD");
        var date = new DateTime(2026, 5, 10, 0, 0, 0, DateTimeKind.Utc);

        var transactions = new List<Transaction>
        {
            MakeTx(accountId, 1000m, "credit", date),
            MakeTx(accountId, 400m,  "debit",  date),
        };

        var txRepoMock = new Mock<ITransactionRepository>();
        txRepoMock.Setup(r => r.GetByUserIdSinceAsync(UserId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(transactions);

        var accountRepoMock = new Mock<IBankAccountRepository>();
        accountRepoMock.Setup(r => r.GetByUserIdAsync(UserId, It.IsAny<CancellationToken>()))
                       .ReturnsAsync([account]);

        // A 50 USD family-support expense for May 2026.
        var classification = CounterpartyResults.WithFlows(
            new CounterpartyMonthlyFlow("2026-05", "Mom", FlowRoles.FamilySupport, 0m, 50m));

        var sut = new MoneyFlowStatisticsService(
            txRepoMock.Object, accountRepoMock.Object, new TransferDetectionService(), CommittedPolicy());

        // Act
        var result = await sut.GetMonthlyFlowAsync(UserId, classification, 6);

        // Assert: the counterparty adjustment lands on its OWN synthetic USD row; the normal
        // row's figures are untouched. Month totals stay honest: 400 + 50 of outflow.
        result.Should().HaveCount(2);
        var normal = result.Single(r => r.FamilySupportOutflowUsd == 0m);
        normal.Outflow.Should().Be(400m);
        normal.OutflowUsd.Should().Be(400m);

        var synthetic = result.Single(r => r.FamilySupportOutflowUsd == 50m);
        synthetic.Currency.Should().Be("USD");
        synthetic.OutflowUsd.Should().Be(50m);
        result.Sum(r => r.OutflowUsd).Should().Be(400m + 50m);
    }

    // ── T044 Test: rent in and support out in the same month both land ────────

    [Fact]
    public async Task GetMonthlyFlow_CounterpartyRentAndSupportSameMonth_BothDirectionsLandGross()
    {
        // The white-card month: ₴18k-equivalent rent arrives from Mom and $500 of support
        // goes back to her. Both sides count in full — netting them to $220 of income would
        // report a month with no family spending at all, which is the savings-rate lie.
        var (account, accountId) = MakeAccount("USD");
        var date = new DateTime(2026, 5, 10, 0, 0, 0, DateTimeKind.Utc);

        var transactions = new List<Transaction>
        {
            MakeTx(accountId, 1000m, "credit", date),
            MakeTx(accountId, 400m,  "debit",  date),
        };

        var txRepoMock = new Mock<ITransactionRepository>();
        txRepoMock.Setup(r => r.GetByUserIdSinceAsync(UserId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(transactions);

        var accountRepoMock = new Mock<IBankAccountRepository>();
        accountRepoMock.Setup(r => r.GetByUserIdAsync(UserId, It.IsAny<CancellationToken>()))
                       .ReturnsAsync([account]);

        var classification = CounterpartyResults.WithFlows(
            new CounterpartyMonthlyFlow("2026-05", "Mom", FlowRoles.FamilySupport, 720m, 500m));

        var sut = new MoneyFlowStatisticsService(
            txRepoMock.Object, accountRepoMock.Object, new TransferDetectionService(), CommittedPolicy());

        var result = await sut.GetMonthlyFlowAsync(UserId, classification, 6);

        // Both directions land gross on the month's synthetic USD row; the totals a consumer
        // sums per month carry the full picture.
        result.Should().HaveCount(2);
        var synthetic = result.Single(r => r.FamilySupportOutflowUsd == 500m);
        synthetic.InflowUsd.Should().Be(720m);
        synthetic.OutflowUsd.Should().Be(500m);
        result.Sum(r => r.InflowUsd).Should().Be(1000m + 720m);
        result.Sum(r => r.OutflowUsd).Should().Be(400m + 500m);
        // Savings rate reads off these two: 820/1720 ≈ 48%, not the 1320/1500 = 88% netting gave.
        result.Sum(r => r.NetUsd).Should().Be(1720m - 900m);
    }

    // ── T044 Test: investment routing is carved out of "kept", never out of spend ──

    [Fact]
    public async Task GetMonthlyFlow_WithInvestmentRouting_ReportsInvestedWithoutInflatingOutflow()
    {
        // Arrange: 1000 in, 400 spent. On top of that the user routed 300 to a brokerage.
        // That 300 is not spending — it must not touch OutflowUsd (and so must not move the
        // savings rate), it must only surface as InvestedOutflowUsd.
        var (account, accountId) = MakeAccount("USD");
        var date = new DateTime(2026, 5, 10, 0, 0, 0, DateTimeKind.Utc);

        var transactions = new List<Transaction>
        {
            MakeTx(accountId, 1000m, "credit", date),
            MakeTx(accountId, 400m,  "debit",  date),
        };

        var txRepoMock = new Mock<ITransactionRepository>();
        txRepoMock.Setup(r => r.GetByUserIdSinceAsync(UserId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(transactions);

        var accountRepoMock = new Mock<IBankAccountRepository>();
        accountRepoMock.Setup(r => r.GetByUserIdAsync(UserId, It.IsAny<CancellationToken>()))
                       .ReturnsAsync([account]);

        var classification = CounterpartyResults.WithFlows(
            new CounterpartyMonthlyFlow("2026-05", "Investment routing", FlowRoles.Investment, 0m, 300m),
            new CounterpartyMonthlyFlow("2026-05", "Mom", FlowRoles.FamilySupport, 0m, 50m));

        var sut = new MoneyFlowStatisticsService(
            txRepoMock.Object, accountRepoMock.Object, new TransferDetectionService(), CommittedPolicy());

        // Act
        var result = await sut.GetMonthlyFlowAsync(UserId, classification, 6);

        // Assert: the invested 300 surfaces only on the synthetic USD row and never joins
        // outflow; the family-support 50 joins outflow on that same row.
        result.Should().HaveCount(2);
        var synthetic = result.Single(r => r.InvestedOutflowUsd == 300m);
        synthetic.FamilySupportOutflowUsd.Should().Be(50m);
        synthetic.OutflowUsd.Should().Be(50m);
        result.Sum(r => r.OutflowUsd).Should().Be(400m + 50m);
        result.Sum(r => r.NetUsd).Should().Be(1000m - 450m);
    }

    [Fact]
    public async Task GetMonthlyFlow_HouseholdOutflow_CountsAsSpendingButNotAsFamilySupport()
    {
        // Arrange: 1000 in, 400 spent normally, plus a 315 mortgage payment that leaves as a
        // card-to-card transfer (household counterparty). It is real spending — it must join
        // OutflowUsd and pull the savings rate down — but it is a bill, not support, so it
        // must not surface under FamilySupportOutflowUsd (nor as InvestedOutflowUsd).
        var (account, accountId) = MakeAccount("USD");
        var date = new DateTime(2026, 5, 10, 0, 0, 0, DateTimeKind.Utc);

        var transactions = new List<Transaction>
        {
            MakeTx(accountId, 1000m, "credit", date),
            MakeTx(accountId, 400m,  "debit",  date),
        };

        var txRepoMock = new Mock<ITransactionRepository>();
        txRepoMock.Setup(r => r.GetByUserIdSinceAsync(UserId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(transactions);

        var accountRepoMock = new Mock<IBankAccountRepository>();
        accountRepoMock.Setup(r => r.GetByUserIdAsync(UserId, It.IsAny<CancellationToken>()))
                       .ReturnsAsync([account]);

        var classification = CounterpartyResults.WithFlows(
            new CounterpartyMonthlyFlow("2026-05", "Mortgage", FlowRoles.Household, 0m, 315m));

        var sut = new MoneyFlowStatisticsService(
            txRepoMock.Object, accountRepoMock.Object, new TransferDetectionService(), CommittedPolicy());

        // Act
        var result = await sut.GetMonthlyFlowAsync(UserId, classification, 6);

        // Assert
        result.Should().HaveCount(2);
        var synthetic = result.Single(r => r.Currency == "USD" && r.Inflow == 0m && r.OutflowUsd == 315m);
        synthetic.FamilySupportOutflowUsd.Should().Be(0m);
        synthetic.InvestedOutflowUsd.Should().Be(0m);
        result.Sum(r => r.OutflowUsd).Should().Be(400m + 315m);
        result.Sum(r => r.NetUsd).Should().Be(1000m - 715m);
    }

    [Fact]
    public async Task GetMonthlyFlow_SelfRoutingFlows_TouchNothing()
    {
        // 1000 in, 400 out normally; on top, 1200 routed out and 1200 routed back via a
        // self-routing counterparty. Neither direction may reach income, outflow, family
        // support, or invested — the money never left or entered the user's estate.
        var (account, accountId) = MakeAccount("USD");
        var date = new DateTime(2026, 5, 10, 0, 0, 0, DateTimeKind.Utc);

        var transactions = new List<Transaction>
        {
            MakeTx(accountId, 1000m, "credit", date),
            MakeTx(accountId, 400m,  "debit",  date),
        };

        var txRepoMock = new Mock<ITransactionRepository>();
        txRepoMock.Setup(r => r.GetByUserIdSinceAsync(UserId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(transactions);

        var accountRepoMock = new Mock<IBankAccountRepository>();
        accountRepoMock.Setup(r => r.GetByUserIdAsync(UserId, It.IsAny<CancellationToken>()))
                       .ReturnsAsync([account]);

        var classification = CounterpartyResults.WithFlows(
            new CounterpartyMonthlyFlow("2026-05", "Routing via mom (EUR)", FlowRoles.SelfRouting, 1200m, 1200m));

        var sut = new MoneyFlowStatisticsService(
            txRepoMock.Object, accountRepoMock.Object, new TransferDetectionService(), CommittedPolicy());

        // Act
        var result = await sut.GetMonthlyFlowAsync(UserId, classification, 6);

        // Assert: the synthetic row exists but carries zeroes everywhere that counts.
        result.Sum(r => r.InflowUsd).Should().Be(1000m);
        result.Sum(r => r.OutflowUsd).Should().Be(400m);
        result.Sum(r => r.FamilySupportOutflowUsd).Should().Be(0m);
        result.Sum(r => r.InvestedOutflowUsd).Should().Be(0m);
    }

    [Fact]
    public async Task GetMonthlyFlow_InvestmentCreditsAreNotIncome()
    {
        // Arrange: no bank transactions at all in the window, only a net INBOUND investment
        // movement (a withdrawal back from the venue). Money coming out of an investment
        // sleeve is not earnings — it must not inflate inflow and thus the savings rate.
        var (account, _) = MakeAccount("USD");

        var txRepoMock = new Mock<ITransactionRepository>();
        txRepoMock.Setup(r => r.GetByUserIdSinceAsync(UserId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync([]);

        var accountRepoMock = new Mock<IBankAccountRepository>();
        accountRepoMock.Setup(r => r.GetByUserIdAsync(UserId, It.IsAny<CancellationToken>()))
                       .ReturnsAsync([account]);

        var classification = CounterpartyResults.WithFlows(
            new CounterpartyMonthlyFlow("2026-05", "Investment routing", FlowRoles.Investment, 700m, 0m));

        var sut = new MoneyFlowStatisticsService(
            txRepoMock.Object, accountRepoMock.Object, new TransferDetectionService(), CommittedPolicy());

        // Act
        var result = await sut.GetMonthlyFlowAsync(UserId, classification, 6);

        // Assert
        result.Should().HaveCount(1);
        result[0].InflowUsd.Should().Be(0m);
        result[0].OutflowUsd.Should().Be(0m);
        result[0].InvestedOutflowUsd.Should().Be(0m);
    }

    // ── PR #547 review: the adjustment always gets its own USD row ────────────

    [Fact]
    public async Task GetMonthlyFlow_MultiCurrencyMonthWithCounterpartyFlow_AdjustmentSitsInItsOwnUsdRow()
    {
        // A month with UAH and EUR buckets plus counterparty traffic. Which bucket happens to
        // be "first" is dictionary-iteration order — folding the USD adjustment into it made
        // that row's native and USD columns tell different stories. The adjustment must land
        // on its own synthetic USD row and leave every native figure untouched.
        var (uahAccount, uahAccountId) = MakeAccount("UAH");
        var (eurAccount, eurAccountId) = MakeAccount("EUR");
        var date = new DateTime(2026, 5, 10, 0, 0, 0, DateTimeKind.Utc);

        var transactions = new List<Transaction>
        {
            MakeTx(uahAccountId, 20000m, "credit", date),
            MakeTx(uahAccountId, 8000m,  "debit",  date),
            MakeTx(eurAccountId, 1000m,  "credit", date),
            MakeTx(eurAccountId, 400m,   "debit",  date),
        };

        var classification = CounterpartyResults.WithFlows(
            new CounterpartyMonthlyFlow("2026-05", "Mom", FlowRoles.FamilySupport, 100m, 50m));

        var sut = new MoneyFlowStatisticsService(
            TxRepo(transactions).Object, AccountRepo(uahAccount, eurAccount).Object,
            new TransferDetectionService(), CommittedPolicy());

        var result = await sut.GetMonthlyFlowAsync(UserId, classification, 6);

        result.Should().HaveCount(3);

        // Native amounts of the currency rows are untouched by the adjustment.
        var uah = result.Single(r => r.Currency == "UAH");
        uah.Inflow.Should().Be(20000m);
        uah.Outflow.Should().Be(8000m);
        uah.InflowUsd.Should().Be(CurrencyConverter.ToUsd(20000m, "UAH"));
        uah.OutflowUsd.Should().Be(CurrencyConverter.ToUsd(8000m, "UAH"));
        uah.FamilySupportOutflowUsd.Should().Be(0m);

        var eur = result.Single(r => r.Currency == "EUR");
        eur.Inflow.Should().Be(1000m);
        eur.Outflow.Should().Be(400m);
        eur.InflowUsd.Should().Be(CurrencyConverter.ToUsd(1000m, "EUR"));
        eur.OutflowUsd.Should().Be(CurrencyConverter.ToUsd(400m, "EUR"));
        eur.FamilySupportOutflowUsd.Should().Be(0m);

        // The adjustment is its own USD row with zero native amounts.
        var synthetic = result.Single(r => r.FamilySupportOutflowUsd == 50m);
        synthetic.Currency.Should().Be("USD");
        synthetic.Month.Should().Be("2026-05");
        synthetic.Inflow.Should().Be(0m);
        synthetic.Outflow.Should().Be(0m);
        synthetic.InflowUsd.Should().Be(100m);
        synthetic.OutflowUsd.Should().Be(50m);
        // The committed/discretionary partition stays exact on the synthetic row too.
        (synthetic.CommittedOutflowUsd + synthetic.DiscretionaryOutflowUsd).Should().Be(synthetic.OutflowUsd);

        // Month totals a consumer sums are correct.
        result.Sum(r => r.InflowUsd).Should().Be(
            CurrencyConverter.ToUsd(20000m, "UAH") + CurrencyConverter.ToUsd(1000m, "EUR") + 100m);
        result.Sum(r => r.OutflowUsd).Should().Be(
            CurrencyConverter.ToUsd(8000m, "UAH") + CurrencyConverter.ToUsd(400m, "EUR") + 50m);
    }

    // ── T413 Test 4: Debit/credit classification ──────────────────────────────

    [Fact]
    public async Task GetMonthlyFlow_DebitCreditClassification()
    {
        // Arrange — credit = inflow, debit = outflow
        var (account, accountId) = MakeAccount("EUR");
        var date = new DateTime(2026, 2, 10, 0, 0, 0, DateTimeKind.Utc);

        var transactions = new List<Transaction>
        {
            MakeTx(accountId, 750m, "credit", date),  // → inflow
            MakeTx(accountId, 250m, "debit",  date),  // → outflow
            MakeTx(accountId, 150m, "credit", date),  // → inflow
        };

        var txRepoMock = new Mock<ITransactionRepository>();
        txRepoMock.Setup(r => r.GetByUserIdSinceAsync(UserId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(transactions);

        var accountRepoMock = new Mock<IBankAccountRepository>();
        accountRepoMock.Setup(r => r.GetByUserIdAsync(UserId, It.IsAny<CancellationToken>()))
                       .ReturnsAsync([account]);

        var sut = new MoneyFlowStatisticsService(
            txRepoMock.Object, accountRepoMock.Object, new TransferDetectionService(), CommittedPolicy());

        // Act
        var result = await sut.GetMonthlyFlowAsync(UserId, CounterpartyResults.None, 6);

        // Assert
        result.Should().HaveCount(1);
        result[0].Inflow.Should().Be(900m);   // 750 + 150
        result[0].Outflow.Should().Be(250m);
        result[0].Net.Should().Be(650m);
    }

    // ── #538 Committed vs discretionary split ─────────────────────────────────

    [Fact]
    public async Task GetMonthlyFlow_SplitsOutflowByActiveCommitmentMerchantKey()
    {
        var (account, accountId) = MakeAccount("USD");
        var date = new DateTime(2026, 4, 12, 0, 0, 0, DateTimeKind.Utc);

        var transactions = new List<Transaction>
        {
            MakeTx(accountId, 15m,  "debit",  date, merchantName: "Netflix"),
            MakeTx(accountId, 85m,  "debit",  date, merchantName: "Silpo"),
            MakeTx(accountId, 500m, "credit", date, merchantName: "Employer"),
        };

        var sut = new MoneyFlowStatisticsService(
            TxRepo(transactions).Object, AccountRepo(account).Object, new TransferDetectionService(),
            CommittedPolicy("netflix"));

        var result = await sut.GetMonthlyFlowAsync(UserId, CounterpartyResults.None, 6);

        result.Should().HaveCount(1);
        result[0].OutflowUsd.Should().Be(100m);
        result[0].CommittedOutflowUsd.Should().Be(15m);
        result[0].DiscretionaryOutflowUsd.Should().Be(85m);
    }

    [Fact]
    public async Task GetMonthlyFlow_PinnedMerchant_MovesSpendFromDiscretionaryToCommitted()
    {
        // Rule (d) end to end: the gym is uncategorized and was never detected as recurring, so
        // without the pin it is discretionary. The user pinning it is the only thing that moves
        // the money — which is the whole point of the escape hatch.
        var (account, accountId) = MakeAccount("USD");
        var date = new DateTime(2026, 4, 12, 0, 0, 0, DateTimeKind.Utc);

        var transactions = new List<Transaction>
        {
            MakeTx(accountId, 60m, "debit", date, merchantName: "Anytime Fitness"),
            MakeTx(accountId, 40m, "debit", date, merchantName: "Silpo"),
        };

        var unpinned = await new MoneyFlowStatisticsService(
            TxRepo(transactions).Object, AccountRepo(account).Object, new TransferDetectionService(),
            CommittedPolicy()).GetMonthlyFlowAsync(UserId, CounterpartyResults.None, 6);

        var pinned = await new MoneyFlowStatisticsService(
            TxRepo(transactions).Object, AccountRepo(account).Object, new TransferDetectionService(),
            CommittedPolicyWithPins("anytime fitness")).GetMonthlyFlowAsync(UserId, CounterpartyResults.None, 6);

        unpinned[0].CommittedOutflowUsd.Should().Be(0m);
        unpinned[0].DiscretionaryOutflowUsd.Should().Be(100m);

        pinned[0].CommittedOutflowUsd.Should().Be(60m);
        pinned[0].DiscretionaryOutflowUsd.Should().Be(40m);
        (pinned[0].CommittedOutflowUsd + pinned[0].DiscretionaryOutflowUsd)
            .Should().Be(pinned[0].OutflowUsd);
    }

    [Fact]
    public async Task GetMonthlyFlow_SplitAlwaysPartitionsOutflow()
    {
        // The split may never invent or drop spend: whatever the match rule decides,
        // the two buckets must add back to OutflowUsd.
        var (account, accountId) = MakeAccount("UAH");
        var date = new DateTime(2026, 4, 12, 0, 0, 0, DateTimeKind.Utc);

        var transactions = new List<Transaction>
        {
            MakeTx(accountId, 333.33m, "debit", date, merchantName: "Netflix"),
            MakeTx(accountId, 777.77m, "debit", date, merchantName: "Silpo"),
        };

        var sut = new MoneyFlowStatisticsService(
            TxRepo(transactions).Object, AccountRepo(account).Object, new TransferDetectionService(),
            CommittedPolicy("netflix"));

        var result = await sut.GetMonthlyFlowAsync(UserId, CounterpartyResults.None, 6);

        result[0].CommittedOutflowUsd.Should().Be(CurrencyConverter.ToUsd(333.33m, "UAH"));
        result[0].DiscretionaryOutflowUsd.Should().BeGreaterThan(0m);
        (result[0].CommittedOutflowUsd + result[0].DiscretionaryOutflowUsd)
            .Should().Be(result[0].OutflowUsd);
    }

    [Fact]
    public async Task GetMonthlyFlow_CommittedOutflow_IsConvertedPerCurrencyAtTheReaderBoundary()
    {
        // Two commitments billed in different currencies in the same month. Adding the native
        // amounts (14,000 ₴ + 100 €) produces a number that is neither hryvnia nor euro; only
        // the USD figures may be summed across rows.
        var (uahAccount, uahAccountId) = MakeAccount("UAH");
        var (eurAccount, eurAccountId) = MakeAccount("EUR");
        var date = new DateTime(2026, 4, 12, 0, 0, 0, DateTimeKind.Utc);

        var transactions = new List<Transaction>
        {
            MakeTx(uahAccountId, 14000m, "debit", date, merchantName: "Kredobank"),
            MakeTx(eurAccountId, 100m,   "debit", date, merchantName: "Netflix"),
        };

        var sut = new MoneyFlowStatisticsService(
            TxRepo(transactions).Object, AccountRepo(uahAccount, eurAccount).Object,
            new TransferDetectionService(),
            CommittedPolicy("kredobank", "netflix"));

        var result = await sut.GetMonthlyFlowAsync(UserId, CounterpartyResults.None, 6);

        var expectedUah = CurrencyConverter.ToUsd(14000m, "UAH");
        var expectedEur = CurrencyConverter.ToUsd(100m, "EUR");

        result.Should().HaveCount(2);
        result.First(r => r.Currency == "UAH").CommittedOutflowUsd.Should().Be(expectedUah);
        result.First(r => r.Currency == "EUR").CommittedOutflowUsd.Should().Be(expectedEur);
        result.Sum(r => r.CommittedOutflowUsd).Should().Be(expectedUah + expectedEur);

        // The figure a native `.Sum(x => x.Amount)` would have produced.
        result.Sum(r => r.CommittedOutflowUsd).Should().NotBe(14100m);
    }

    [Fact]
    public async Task GetMonthlyFlow_CommittedMatch_UsesTheDetectorsMerchantKey()
    {
        // The detector stores "claude" for every Anthropic spelling. Matching on the raw
        // merchant name would miss the charge the detector itself grouped under that key.
        var (account, accountId) = MakeAccount("USD");
        var date = new DateTime(2026, 4, 12, 0, 0, 0, DateTimeKind.Utc);

        var transactions = new List<Transaction>
        {
            MakeTx(accountId, 22m, "debit", date, merchantName: "Anthropic* Claude Sub 4471"),
            // Merchant name absent — the key falls back to the description, and mobile
            // top-ups collapse to a per-number key.
            MakeTx(accountId, 10m, "debit", date, description: "*MOBI TOP-UP 0857860057"),
        };

        var sut = new MoneyFlowStatisticsService(
            TxRepo(transactions).Object, AccountRepo(account).Object, new TransferDetectionService(),
            CommittedPolicy("claude", "mobile top-up 0057"));

        var result = await sut.GetMonthlyFlowAsync(UserId, CounterpartyResults.None, 6);

        result[0].CommittedOutflowUsd.Should().Be(32m);
        result[0].DiscretionaryOutflowUsd.Should().Be(0m);
    }

    [Fact]
    public async Task GetMonthlyFlow_TransferToACommittedMerchant_CountsInNeitherBucket()
    {
        // Transfers are already out of Outflow; the split must inherit that exclusion rather
        // than re-deriving it, or committed spend would exceed the outflow it partitions.
        var (account, accountId) = MakeAccount("USD");
        var date = new DateTime(2026, 4, 12, 0, 0, 0, DateTimeKind.Utc);

        var transfer = MakeTx(accountId, 700m, "debit", date, merchantName: "Netflix");
        transfer.MerchantCategory = CategoryKeys.TransferOut;

        var transactions = new List<Transaction>
        {
            transfer,
            MakeTx(accountId, 15m, "debit", date, merchantName: "Netflix"),
        };

        var sut = new MoneyFlowStatisticsService(
            TxRepo(transactions).Object, AccountRepo(account).Object, new TransferDetectionService(),
            CommittedPolicy("netflix"));

        var result = await sut.GetMonthlyFlowAsync(UserId, CounterpartyResults.None, 6);

        result[0].OutflowUsd.Should().Be(15m);
        result[0].CommittedOutflowUsd.Should().Be(15m);
        result[0].DiscretionaryOutflowUsd.Should().Be(0m);
    }

    [Fact]
    public async Task GetMonthlyFlow_NoActiveCommitments_TreatsAllOutflowAsDiscretionary()
    {
        // Before the detector has found anything (or after everything is dismissed) the split
        // must not guess — it reports the whole outflow as discretionary.
        var (account, accountId) = MakeAccount("USD");
        var date = new DateTime(2026, 4, 12, 0, 0, 0, DateTimeKind.Utc);

        var transactions = new List<Transaction>
        {
            MakeTx(accountId, 15m, "debit", date, merchantName: "Netflix"),
        };

        var sut = new MoneyFlowStatisticsService(
            TxRepo(transactions).Object, AccountRepo(account).Object, new TransferDetectionService(),
            CommittedPolicy());

        var result = await sut.GetMonthlyFlowAsync(UserId, CounterpartyResults.None, 6);

        result[0].CommittedOutflowUsd.Should().Be(0m);
        result[0].DiscretionaryOutflowUsd.Should().Be(15m);
    }

    [Fact]
    public async Task GetMonthlyFlow_InstallmentRepayment_CountsAsCommitted()
    {
        // A розстрочка repayment is the most committed spend there is, but the detector keys
        // its plans as installment:{merchant}:{amount} — a form no merchant key ever takes, so
        // matching on the merchant key alone booked every repayment as discretionary.
        var (account, accountId) = MakeAccount("UAH");
        var date = new DateTime(2026, 4, 12, 0, 0, 0, DateTimeKind.Utc);

        var transactions = new List<Transaction>
        {
            MakeTx(accountId, 6499.84m, "debit", date, description: "Щомісячний платіж telemart - monomarket"),
            MakeTx(accountId, 900m, "debit", date, merchantName: "Silpo"),
        };

        var sut = new MoneyFlowStatisticsService(
            TxRepo(transactions).Object, AccountRepo(account).Object, new TransferDetectionService(),
            CommittedPolicy("installment:telemart:6500"));

        var result = await sut.GetMonthlyFlowAsync(UserId, CounterpartyResults.None, 6);

        result[0].CommittedOutflowUsd.Should().Be(CurrencyConverter.ToUsd(6499.84m, "UAH"));
        result[0].DiscretionaryOutflowUsd.Should().Be(CurrencyConverter.ToUsd(900m, "UAH"));
        (result[0].CommittedOutflowUsd + result[0].DiscretionaryOutflowUsd)
            .Should().Be(result[0].OutflowUsd);
    }

    [Fact]
    public async Task GetMonthlyFlow_InstallmentAtASecondPlanAmount_IsNotClaimedByTheFirstPlan()
    {
        // The same shop can carry concurrent розстрочки; only the plan the user actually has
        // an active row for is committed. Merchant-level matching would claim both.
        var (account, accountId) = MakeAccount("UAH");
        var date = new DateTime(2026, 4, 12, 0, 0, 0, DateTimeKind.Utc);

        var transactions = new List<Transaction>
        {
            MakeTx(accountId, 2339.95m, "debit", date, description: "Погашення наступного платежу ТОВ Алло - monomarket"),
            MakeTx(accountId, 2999.95m, "debit", date, description: "Погашення наступного платежу ТОВ Алло - monomarket"),
        };

        var sut = new MoneyFlowStatisticsService(
            TxRepo(transactions).Object, AccountRepo(account).Object, new TransferDetectionService(),
            CommittedPolicy("installment:тов алло:2340"));

        var result = await sut.GetMonthlyFlowAsync(UserId, CounterpartyResults.None, 6);

        result[0].CommittedOutflowUsd.Should().Be(CurrencyConverter.ToUsd(2339.95m, "UAH"));
        result[0].DiscretionaryOutflowUsd.Should().Be(CurrencyConverter.ToUsd(2999.95m, "UAH"));
    }

    [Fact]
    public async Task GetMonthlyFlow_InstallmentsInTwoCurrencies_AreConvertedPerBucket()
    {
        // Installment plans are billed in the account's currency; summing ₴6,499.84 with
        // €120 natively would produce a number that is neither.
        var (uahAccount, uahAccountId) = MakeAccount("UAH");
        var (eurAccount, eurAccountId) = MakeAccount("EUR");
        var date = new DateTime(2026, 4, 12, 0, 0, 0, DateTimeKind.Utc);

        var transactions = new List<Transaction>
        {
            MakeTx(uahAccountId, 6499.84m, "debit", date, description: "Щомісячний платіж telemart - monomarket"),
            MakeTx(eurAccountId, 120m, "debit", date, description: "Платіж Pandora", mcc: 4829),
        };

        var sut = new MoneyFlowStatisticsService(
            TxRepo(transactions).Object, AccountRepo(uahAccount, eurAccount).Object,
            new TransferDetectionService(),
            CommittedPolicy("installment:telemart:6500", "installment:pandora:120"));

        var result = await sut.GetMonthlyFlowAsync(UserId, CounterpartyResults.None, 6);

        var expectedUah = CurrencyConverter.ToUsd(6499.84m, "UAH");
        var expectedEur = CurrencyConverter.ToUsd(120m, "EUR");

        result.Should().HaveCount(2);
        result.Sum(r => r.CommittedOutflowUsd).Should().Be(expectedUah + expectedEur);
        result.Sum(r => r.DiscretionaryOutflowUsd).Should().Be(0m);

        // The figure a native `.Sum(x => x.Amount)` would have produced.
        result.Sum(r => r.CommittedOutflowUsd).Should().NotBe(6619.84m);
    }

    [Fact]
    public async Task GetMonthlyFlow_CompletedInstallmentPlan_IsDiscretionary()
    {
        // A plan the detector marked completed leaves the active key set, so its repayments
        // stop counting as committed — the same "active only" rule subscriptions follow.
        var (account, accountId) = MakeAccount("UAH");
        var date = new DateTime(2026, 4, 12, 0, 0, 0, DateTimeKind.Utc);

        var transactions = new List<Transaction>
        {
            MakeTx(accountId, 6499.84m, "debit", date, description: "Щомісячний платіж telemart - monomarket"),
        };

        var sut = new MoneyFlowStatisticsService(
            TxRepo(transactions).Object, AccountRepo(account).Object, new TransferDetectionService(),
            CommittedPolicy("installment:rozetkapay:2340"));

        var result = await sut.GetMonthlyFlowAsync(UserId, CounterpartyResults.None, 6);

        result[0].CommittedOutflowUsd.Should().Be(0m);
        result[0].DiscretionaryOutflowUsd.Should().Be(result[0].OutflowUsd);
    }

    [Fact]
    public async Task GetMonthlyFlow_RepaymentsOnTheWireTransferMcc_AreOutflowAndCommitted()
    {
        // #553: the mortgage and the monomarket repayments all carry MCC 4829, and while they
        // were categorized TRANSFER_OUT the transfer exclusion dropped them from outflow
        // entirely — hiding the largest fixed commitment in the book. Categorized
        // LOAN_PAYMENTS they are spending, and their commitment keys make them committed.
        // The genuine card-to-card transfer on the same MCC must still be excluded.
        const string mortgageDescription = "516936******4992";
        const decimal mortgageAmount = 14060.96m;
        var (account, accountId) = MakeAccount("UAH");
        var date = new DateTime(2026, 4, 12, 0, 0, 0, DateTimeKind.Utc);

        var transactions = new List<Transaction>
        {
            MakeTx(accountId, mortgageAmount, "debit", date, description: mortgageDescription,
                mcc: 4829, category: CategoryKeys.LoanPayments),
            MakeTx(accountId, 2999.95m, "debit", date, description: "Платіж ТОВ Алло - monomarket",
                mcc: 4829, category: CategoryKeys.LoanPayments),
            MakeTx(accountId, 5000m, "debit", date, description: "Переказ на картку",
                mcc: 4829, category: CategoryKeys.TransferOut),
        };

        var sut = new MoneyFlowStatisticsService(
            TxRepo(transactions).Object, AccountRepo(account).Object, new TransferDetectionService(),
            CommittedPolicy(
                CommitmentKeyResolver.Resolve(null, mortgageDescription, mortgageAmount, 4829),
                "installment:тов алло:3000"));

        var result = await sut.GetMonthlyFlowAsync(UserId, CounterpartyResults.None, 6);

        result[0].Outflow.Should().Be(mortgageAmount + 2999.95m);
        result[0].OutflowUsd.Should().Be(CurrencyConverter.ToUsd(mortgageAmount + 2999.95m, "UAH"));
        result[0].CommittedOutflowUsd.Should().Be(result[0].OutflowUsd);
        result[0].DiscretionaryOutflowUsd.Should().Be(0m);
    }

    // ── #554 The widened definition: rent, loans, counterparty obligations ────

    [Fact]
    public async Task GetMonthlyFlow_Rent_IsCommittedWithoutADetectedSubscription()
    {
        // The 1,275 EUR/mo rent to the same payee is the largest fixed outflow in the book and
        // has no recurrence signature the detector can see. Under the subscription-only
        // definition it read as discretionary, which is what stopped #538 on its own gate.
        var (account, accountId) = MakeAccount("EUR");
        var date = new DateTime(2026, 6, 3, 0, 0, 0, DateTimeKind.Utc);

        var transactions = new List<Transaction>
        {
            MakeTx(accountId, 1275m, "debit", date, merchantName: "To Mario Scalas",
                description: "Rent June", category: CategoryKeys.RentAndUtilities),
            MakeTx(accountId, 62.40m, "debit", date, merchantName: "Zara",
                description: "ZARA MILANO", category: CategoryKeys.GeneralMerchandise),
        };

        var sut = new MoneyFlowStatisticsService(
            TxRepo(transactions).Object, AccountRepo(account).Object, new TransferDetectionService(),
            CommittedPolicy());

        var result = await sut.GetMonthlyFlowAsync(UserId, CounterpartyResults.None, 6);

        result[0].CommittedOutflowUsd.Should().Be(CurrencyConverter.ToUsd(1275m, "EUR"));
        result[0].DiscretionaryOutflowUsd.Should().Be(CurrencyConverter.ToUsd(62.40m, "EUR"));
        (result[0].CommittedOutflowUsd + result[0].DiscretionaryOutflowUsd)
            .Should().Be(result[0].OutflowUsd);
    }

    [Fact]
    public async Task GetMonthlyFlow_TheRealBookShapes_LandOnTheRightSideAcrossCurrencies()
    {
        // Every shape the widened definition has to get right, in one month, across two
        // currencies: rent (category), the mortgage behind a masked card and a monomarket
        // installment (category + plan key), a Netflix-style subscription (merchant key), and
        // two ordinary shops.
        const decimal rent = 1275m;
        const decimal shopEur = 62.40m;
        const decimal mortgage = 14060.96m;
        const decimal installment = 2999.95m;
        const decimal netflix = 439m;
        const decimal shopUah = 900m;

        var (eurAccount, eurAccountId) = MakeAccount("EUR");
        var (uahAccount, uahAccountId) = MakeAccount("UAH");
        var date = new DateTime(2026, 6, 12, 0, 0, 0, DateTimeKind.Utc);

        var transactions = new List<Transaction>
        {
            MakeTx(eurAccountId, rent, "debit", date, merchantName: "To Mario Scalas",
                description: "Rent June", category: CategoryKeys.RentAndUtilities),
            MakeTx(eurAccountId, shopEur, "debit", date, merchantName: "Zara",
                description: "ZARA MILANO", category: CategoryKeys.GeneralMerchandise),
            MakeTx(uahAccountId, mortgage, "debit", date, description: "516936******4992",
                mcc: 4829, category: CategoryKeys.LoanPayments),
            MakeTx(uahAccountId, installment, "debit", date,
                description: "Платіж ТОВ Алло - monomarket", mcc: 4829,
                category: CategoryKeys.LoanPayments),
            MakeTx(uahAccountId, netflix, "debit", date, merchantName: "Netflix.com",
                description: "CARD PAYMENT 4471", mcc: 5815),
            MakeTx(uahAccountId, shopUah, "debit", date, merchantName: "Сільпо",
                description: "СІЛЬПО 148", category: CategoryKeys.FoodAndDrink),
        };

        var sut = new MoneyFlowStatisticsService(
            TxRepo(transactions).Object, AccountRepo(eurAccount, uahAccount).Object,
            new TransferDetectionService(), CommittedPolicy("netflix"));

        var result = await sut.GetMonthlyFlowAsync(UserId, CounterpartyResults.None, 6);

        var eur = result.Single(r => r.Currency == "EUR");
        eur.CommittedOutflowUsd.Should().Be(CurrencyConverter.ToUsd(rent, "EUR"));
        eur.DiscretionaryOutflowUsd.Should().Be(CurrencyConverter.ToUsd(shopEur, "EUR"));

        var uah = result.Single(r => r.Currency == "UAH");
        var committedUah = CurrencyConverter.ToUsd(mortgage + installment + netflix, "UAH");
        uah.CommittedOutflowUsd.Should().Be(committedUah);
        uah.DiscretionaryOutflowUsd.Should().Be(CurrencyConverter.ToUsd(shopUah, "UAH"));

        // The cross-currency total is only meaningful in USD: the committed NATIVE sum adds
        // hryvnia to euros and is wrong by roughly the whole rent.
        result.Sum(r => r.CommittedOutflowUsd)
            .Should().Be(CurrencyConverter.ToUsd(rent, "EUR") + committedUah);
        result.Sum(r => r.CommittedOutflowUsd)
            .Should().NotBe(rent + mortgage + installment + netflix);
    }

    [Fact]
    public async Task GetMonthlyFlow_CounterpartyObligations_AreCommitted_UnroledSpendIsNot()
    {
        // Rule (c) reads spec 044's roles directly rather than re-deciding who counts as
        // family. Support and the household bill are obligations; a counterparty saved with no
        // role is spending that names none, and investment routing is not spending at all.
        var (account, accountId) = MakeAccount("EUR");
        var date = new DateTime(2026, 5, 10, 0, 0, 0, DateTimeKind.Utc);

        var classification = CounterpartyResults.WithFlows(
            new CounterpartyMonthlyFlow("2026-05", "мама", FlowRoles.FamilySupport, 0m, 50m),
            new CounterpartyMonthlyFlow("2026-05", "Mortgage", FlowRoles.Household, 0m, 315m),
            new CounterpartyMonthlyFlow("2026-05", "Unnamed payee", string.Empty, 0m, 40m),
            new CounterpartyMonthlyFlow("2026-05", "Binance top-up", FlowRoles.Investment, 0m, 300m));

        var sut = new MoneyFlowStatisticsService(
            TxRepo([MakeTx(accountId, 100m, "debit", date)]).Object, AccountRepo(account).Object,
            new TransferDetectionService(), CommittedPolicy());

        var result = await sut.GetMonthlyFlowAsync(UserId, classification, 6);

        var synthetic = result.Single(r => r.InvestedOutflowUsd == 300m);
        synthetic.OutflowUsd.Should().Be(405m);              // 50 + 315 + 40; investment is not spending
        synthetic.CommittedOutflowUsd.Should().Be(365m);     // support + household
        synthetic.DiscretionaryOutflowUsd.Should().Be(40m);  // the unroled counterparty
        (synthetic.CommittedOutflowUsd + synthetic.DiscretionaryOutflowUsd)
            .Should().Be(synthetic.OutflowUsd);
    }
}
