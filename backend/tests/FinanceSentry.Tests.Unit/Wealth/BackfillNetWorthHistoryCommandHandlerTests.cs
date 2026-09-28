namespace FinanceSentry.Tests.Unit.Wealth;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Wealth.Application.Commands;
using FinanceSentry.Modules.Wealth.Domain;
using FinanceSentry.Modules.Wealth.Domain.Repositories;
using FluentAssertions;
using Moq;
using Xunit;

public class BackfillNetWorthHistoryCommandHandlerTests
{
    private static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid CheckingAccountId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid CreditAccountId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);
    private static readonly DateOnly AnchorDate = Today.AddDays(-2);

    [Fact]
    public async Task Handle_WalksCheckingBalanceBackwardThroughTransactions()
    {
        // Checking account: current balance 1000, a $200 debit posted on the anchor date means
        // the balance the day before the anchor must have been 1200 (before that debit landed).
        var anchor = new NetWorthSnapshot { SnapshotDate = AnchorDate };
        var account = new AccountBalanceSnapshot(
            CheckingAccountId, "Test Bank", "checking", "1234", "USD", CurrentBalance: 1000m);
        var transactions = new[]
        {
            // Establishes an earlier floor so the debit's own day is included in the output.
            new BankingTransactionSummary(
                CheckingAccountId, "monobank", "debit", 0m, "USD", 0m,
                AnchorDate.AddDays(-10).ToDateTime(TimeOnly.MinValue), IsPending: false),
            new BankingTransactionSummary(
                CheckingAccountId, "monobank", "debit", 200m, "USD", 200m,
                AnchorDate.ToDateTime(TimeOnly.MinValue), IsPending: false),
        };

        var rows = await CaptureInsertedRows(anchor, [account], transactions);

        var anchorMinusOne = rows.Should().ContainSingle(r => r.SnapshotDate == AnchorDate.AddDays(-1)).Subject;
        anchorMinusOne.BankingTotal.Should().Be(1200m);
        anchorMinusOne.TotalNetWorth.Should().Be(1200m);
    }

    [Fact]
    public async Task Handle_ExcludesPendingTransactionsFromTheWalk()
    {
        // A pending debit dated "today" reflects an unsettled hold, not part of the
        // provider-stored CurrentBalance the walk anchors on. If it were included, the
        // reconstructed balance for every earlier day would be silently wrong.
        var anchor = new NetWorthSnapshot { SnapshotDate = AnchorDate };
        var account = new AccountBalanceSnapshot(
            CheckingAccountId, "Test Bank", "checking", "1234", "USD", CurrentBalance: 1000m);
        var transactions = new[]
        {
            // Establishes an earlier floor so the anchor day is included in the output.
            new BankingTransactionSummary(
                CheckingAccountId, "monobank", "debit", 0m, "USD", 0m,
                AnchorDate.AddDays(-10).ToDateTime(TimeOnly.MinValue), IsPending: false),
            new BankingTransactionSummary(
                CheckingAccountId, "monobank", "debit", 200m, "USD", 200m,
                AnchorDate.ToDateTime(TimeOnly.MinValue), IsPending: true),
        };

        var rows = await CaptureInsertedRows(anchor, [account], transactions);

        var anchorMinusOne = rows.Should().ContainSingle(r => r.SnapshotDate == AnchorDate.AddDays(-1)).Subject;
        anchorMinusOne.BankingTotal.Should().Be(1000m);
    }

    [Fact]
    public async Task Handle_ConvertsNativeCurrencyToUsdAtEachDay()
    {
        // EUR account, current balance 100 EUR, no transactions in the walked window — every
        // backfilled day should carry the same native balance converted via CurrencyConverter.
        var anchor = new NetWorthSnapshot { SnapshotDate = AnchorDate };
        var account = new AccountBalanceSnapshot(
            CheckingAccountId, "Test Bank", "checking", "1234", "EUR", CurrentBalance: 100m);
        var transactions = new[]
        {
            // A transaction far enough back to establish an earliest-tx floor before the anchor,
            // but with no amount effect on the walked days themselves.
            new BankingTransactionSummary(
                CheckingAccountId, "monobank", "debit", 0m, "EUR", 0m,
                AnchorDate.AddDays(-5).ToDateTime(TimeOnly.MinValue), IsPending: false),
        };

        var rows = await CaptureInsertedRows(anchor, [account], transactions);

        var row = rows.Should().ContainSingle(r => r.SnapshotDate == AnchorDate.AddDays(-1)).Subject;
        row.BankingTotal.Should().Be(108m); // 100 EUR * 1.08 fallback rate
    }

    [Fact]
    public async Task Handle_InvertsSignForCreditAccountDebit()
    {
        // Credit (liability) account: CurrentBalance is the amount owed. A debit (purchase)
        // grows the debt rather than shrinking it, so walking backward must SUBTRACT less
        // debt for the earlier day, not add it back the checking-account way.
        var anchor = new NetWorthSnapshot { SnapshotDate = AnchorDate };
        var account = new AccountBalanceSnapshot(
            CreditAccountId, "Test Bank", "credit", "5678", "USD", CurrentBalance: 300m);
        var transactions = new[]
        {
            // Establishes an earlier floor so the purchase's own day is included in the output.
            new BankingTransactionSummary(
                CreditAccountId, "monobank", "debit", 0m, "USD", 0m,
                AnchorDate.AddDays(-10).ToDateTime(TimeOnly.MinValue), IsPending: false),
            new BankingTransactionSummary(
                CreditAccountId, "monobank", "debit", 50m, "USD", 50m,
                AnchorDate.ToDateTime(TimeOnly.MinValue), IsPending: false),
        };

        var rows = await CaptureInsertedRows(anchor, [account], transactions);

        var anchorMinusOne = rows.Should().ContainSingle(r => r.SnapshotDate == AnchorDate.AddDays(-1)).Subject;
        // Owed amount the day before the $50 purchase was 300 - 50 = 250, and it is a liability
        // so it enters the net total negated.
        anchorMinusOne.BankingTotal.Should().Be(-250m);
    }

    [Fact]
    public async Task Handle_NeverInsertsARowOnOrAfterTheAnchorSnapshotDate()
    {
        var anchor = new NetWorthSnapshot { SnapshotDate = AnchorDate };
        var account = new AccountBalanceSnapshot(
            CheckingAccountId, "Test Bank", "checking", "1234", "USD", CurrentBalance: 1000m);
        var transactions = new[]
        {
            new BankingTransactionSummary(
                CheckingAccountId, "monobank", "debit", 10m, "USD", 10m,
                AnchorDate.AddDays(-3).ToDateTime(TimeOnly.MinValue), IsPending: false),
        };

        var rows = await CaptureInsertedRows(anchor, [account], transactions);

        rows.Should().OnlyContain(r => r.SnapshotDate < AnchorDate);
    }

    [Fact]
    public async Task Handle_MarksEveryRowApproximateWithZeroedNonBankingSleeves()
    {
        var anchor = new NetWorthSnapshot { SnapshotDate = AnchorDate };
        var account = new AccountBalanceSnapshot(
            CheckingAccountId, "Test Bank", "checking", "1234", "USD", CurrentBalance: 1000m);
        var transactions = new[]
        {
            new BankingTransactionSummary(
                CheckingAccountId, "monobank", "debit", 10m, "USD", 10m,
                AnchorDate.AddDays(-3).ToDateTime(TimeOnly.MinValue), IsPending: false),
        };

        var rows = await CaptureInsertedRows(anchor, [account], transactions);

        rows.Should().NotBeEmpty();
        rows.Should().OnlyContain(r => r.IsApproximate && r.BrokerageTotal == 0m && r.CryptoTotal == 0m);
    }

    [Fact]
    public async Task Handle_ExcludesAccountFromDatesBeforeItsEarliestTransaction()
    {
        // Regression: an account connected after the gap started (e.g. mid-May) must not have
        // its current balance fabricated backward past its own first synced transaction.
        var earlyAccountId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var lateAccountId = Guid.Parse("55555555-5555-5555-5555-555555555555");
        var anchor = new NetWorthSnapshot { SnapshotDate = AnchorDate };
        var accounts = new[]
        {
            new AccountBalanceSnapshot(earlyAccountId, "Old Bank", "checking", "1111", "USD", CurrentBalance: 500m),
            new AccountBalanceSnapshot(lateAccountId, "New Bank", "checking", "2222", "USD", CurrentBalance: 200m),
        };
        var earlyFloor = AnchorDate.AddDays(-10);
        var lateFloor = AnchorDate.AddDays(-1); // connected the day before the anchor
        var transactions = new[]
        {
            new BankingTransactionSummary(
                earlyAccountId, "monobank", "debit", 0m, "USD", 0m,
                earlyFloor.ToDateTime(TimeOnly.MinValue), IsPending: false),
            new BankingTransactionSummary(
                lateAccountId, "truelayer", "debit", 0m, "USD", 0m,
                lateFloor.ToDateTime(TimeOnly.MinValue), IsPending: false),
        };

        var rows = await CaptureInsertedRows(anchor, accounts, transactions);

        // Before the late account's own floor, only the early account contributes.
        var beforeLateFloor = rows.Should()
            .ContainSingle(r => r.SnapshotDate == lateFloor.AddDays(-1)).Subject;
        beforeLateFloor.BankingTotal.Should().Be(500m);

        // On/after the late account's floor, both accounts contribute.
        var onLateFloor = rows.Should().ContainSingle(r => r.SnapshotDate == lateFloor).Subject;
        onLateFloor.BankingTotal.Should().Be(700m);
    }

    [Fact]
    public async Task Handle_WhenNoRealSnapshotExists_ReturnsEmptyResultWithoutTouchingRepository()
    {
        var snapshotRepo = new Mock<INetWorthSnapshotRepository>();
        snapshotRepo
            .Setup(r => r.GetEarliestByUserIdAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((NetWorthSnapshot?)null);
        var accountsReader = new Mock<IBankingAccountsReader>();
        var transactionReader = new Mock<IBankingTransactionReader>();

        var handler = new BackfillNetWorthHistoryCommandHandler(
            snapshotRepo.Object, accountsReader.Object, transactionReader.Object);
        var result = await handler.Handle(new BackfillNetWorthHistoryCommand(UserId), CancellationToken.None);

        result.InsertedCount.Should().Be(0);
        result.AnchorSnapshotDate.Should().BeNull();
        snapshotRepo.Verify(
            r => r.InsertMissingAsync(It.IsAny<IReadOnlyCollection<NetWorthSnapshot>>(), It.IsAny<CancellationToken>()),
            Times.Never);
        accountsReader.Verify(
            r => r.GetActiveAccountSnapshotsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_PassesReconstructedRowsToInsertMissingForIdempotentPersistence()
    {
        // The handler itself never checks for existing rows — idempotency and the
        // never-overwrite-a-real-snapshot guarantee live in InsertMissingAsync, which the
        // repository test covers directly. Here we verify the handler defers to it rather
        // than e.g. calling PersistAsync/UpsertAsync, which would bypass that guarantee.
        var anchor = new NetWorthSnapshot { SnapshotDate = AnchorDate };
        var account = new AccountBalanceSnapshot(
            CheckingAccountId, "Test Bank", "checking", "1234", "USD", CurrentBalance: 1000m);
        var transactions = new[]
        {
            new BankingTransactionSummary(
                CheckingAccountId, "monobank", "debit", 10m, "USD", 10m,
                AnchorDate.AddDays(-3).ToDateTime(TimeOnly.MinValue), IsPending: false),
        };
        var snapshotRepo = SetupSnapshotRepo(anchor, out var capturedRows, insertedCountOverride: 0);
        var handler = BuildHandler(snapshotRepo, [account], transactions);

        var result = await handler.Handle(new BackfillNetWorthHistoryCommand(UserId), CancellationToken.None);

        capturedRows().Should().NotBeEmpty();
        result.InsertedCount.Should().Be(0); // repository reported everything already occupied
        snapshotRepo.Verify(
            r => r.PersistAsync(It.IsAny<NetWorthSnapshot>(), It.IsAny<CancellationToken>()), Times.Never);
        snapshotRepo.Verify(
            r => r.UpsertAsync(It.IsAny<NetWorthSnapshot>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static async Task<IReadOnlyList<NetWorthSnapshot>> CaptureInsertedRows(
        NetWorthSnapshot anchor,
        IReadOnlyList<AccountBalanceSnapshot> accounts,
        IReadOnlyList<BankingTransactionSummary> transactions)
    {
        var snapshotRepo = SetupSnapshotRepo(anchor, out var capturedRows);
        var handler = BuildHandler(snapshotRepo, accounts, transactions);

        await handler.Handle(new BackfillNetWorthHistoryCommand(UserId), CancellationToken.None);

        return capturedRows();
    }

    private static BackfillNetWorthHistoryCommandHandler BuildHandler(
        Mock<INetWorthSnapshotRepository> snapshotRepo,
        IReadOnlyList<AccountBalanceSnapshot> accounts,
        IReadOnlyList<BankingTransactionSummary> transactions)
    {
        var accountsReader = new Mock<IBankingAccountsReader>();
        accountsReader
            .Setup(r => r.GetActiveAccountSnapshotsAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(accounts);

        var transactionReader = new Mock<IBankingTransactionReader>();
        transactionReader
            .Setup(r => r.GetTransactionsAsync(UserId, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(transactions);

        return new BackfillNetWorthHistoryCommandHandler(snapshotRepo.Object, accountsReader.Object, transactionReader.Object);
    }

    private static Mock<INetWorthSnapshotRepository> SetupSnapshotRepo(
        NetWorthSnapshot anchor, out Func<IReadOnlyList<NetWorthSnapshot>> captured, int? insertedCountOverride = null)
    {
        IReadOnlyList<NetWorthSnapshot> rows = [];
        var snapshotRepo = new Mock<INetWorthSnapshotRepository>();
        snapshotRepo
            .Setup(r => r.GetEarliestByUserIdAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(anchor);
        snapshotRepo
            .Setup(r => r.InsertMissingAsync(It.IsAny<IReadOnlyCollection<NetWorthSnapshot>>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyCollection<NetWorthSnapshot>, CancellationToken>((s, _) => rows = s.ToList())
            .ReturnsAsync((IReadOnlyCollection<NetWorthSnapshot> s, CancellationToken _) => insertedCountOverride ?? s.Count);

        captured = () => rows;
        return snapshotRepo;
    }
}
