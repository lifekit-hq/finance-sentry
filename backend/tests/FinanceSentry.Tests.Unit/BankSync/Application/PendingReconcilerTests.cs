namespace FinanceSentry.Tests.Unit.BankSync.Application;

using FinanceSentry.Modules.BankSync.Application.Services;
using FinanceSentry.Modules.BankSync.Domain;
using FluentAssertions;
using Xunit;

public class PendingReconcilerTests
{
    private static readonly Guid Account = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid User = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001");

    private static Transaction Tx(decimal amount, string description, bool pending, string hash) =>
        new(Account, User, amount, new DateTime(2026, 7, 5, 0, 0, 0, DateTimeKind.Utc), description, hash, pending);

    private static readonly DateTime Day = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);

    private static Transaction Pending(DateTime date, string hash, decimal amount = -900m) =>
        new(Account, User, amount, date, "Rent", hash, isPending: true);

    private static Transaction Posted(DateTime date, string hash, DateTime? postedDate = null, decimal amount = -900m) =>
        new(Account, User, amount, date, "Rent", hash, isPending: false) { PostedDate = postedDate ?? date };

    [Fact]
    public void RetiresPending_WhenPostedTwinExistsAmongExisting()
    {
        var pending = Tx(15.38m, "Lidl Ireland Ltd", pending: true, "h-pending");
        var posted = Tx(15.38m, "Lidl Ireland Ltd", pending: false, "h-posted");

        var stale = PendingReconciler.SelectStalePending([pending, posted], []);

        stale.Should().ContainSingle().Which.Should().BeSameAs(pending);
    }

    [Fact]
    public void RetiresPending_WhenPostedTwinArrivesInNewBatch()
    {
        var pending = Tx(15.38m, "Lidl Ireland Ltd", pending: true, "h-pending");
        var postedNew = Tx(15.38m, "Lidl Ireland Ltd", pending: false, "h-posted-new");

        var stale = PendingReconciler.SelectStalePending([pending], [postedNew]);

        stale.Should().ContainSingle().Which.Should().BeSameAs(pending);
    }

    [Fact]
    public void KeepsPending_WhenNoPostedTwin()
    {
        var pending = Tx(15.38m, "Lidl Ireland Ltd", pending: true, "h-pending");
        var otherPosted = Tx(9.99m, "Tesco Stores", pending: false, "h-other");

        var stale = PendingReconciler.SelectStalePending([pending, otherPosted], []);

        stale.Should().BeEmpty();
    }

    [Fact]
    public void KeepsPending_WhenOnlyTwinIsAlsoPending()
    {
        var pendingA = Tx(15.38m, "Lidl Ireland Ltd", pending: true, "h-a");
        var pendingB = Tx(15.38m, "Lidl Ireland Ltd", pending: true, "h-b");

        var stale = PendingReconciler.SelectStalePending([pendingA, pendingB], []);

        stale.Should().BeEmpty();
    }

    [Fact]
    public void MatchIsCaseInsensitiveOnDescription()
    {
        var pending = Tx(20m, "AMAZON.IE", pending: true, "h-pending");
        var posted = Tx(20m, "amazon.ie", pending: false, "h-posted");

        var stale = PendingReconciler.SelectStalePending([pending, posted], []);

        stale.Should().ContainSingle();
    }

    [Fact]
    public void DoesNotRetireFreshlyInsertedPending()
    {
        // A pending row only in the new batch is current, never retired here.
        var newPending = Tx(15.38m, "Lidl Ireland Ltd", pending: true, "h-new-pending");
        var posted = Tx(15.38m, "Lidl Ireland Ltd", pending: false, "h-posted");

        var stale = PendingReconciler.SelectStalePending([], [newPending, posted]);

        stale.Should().BeEmpty();
    }

    [Fact]
    public void RetiresPending_WhenProviderSplicesSettlementStampIntoDescription()
    {
        // AIB rewrites the description when the transaction settles: the booked feed carries a
        // "TxnDate:" stamp the pending feed never sent, and uppercases the name. Before the
        // descriptions were normalized the twin was missed and the payment counted twice.
        var pending = Tx(239m, "*MOBI Denys Sychov IE26090266947650 *MOBI Denys Sychov", pending: true, "h-pending");
        var posted = Tx(239m, "*MOBI DENYS SYCHOV IE26090266947650 TxnDate: 02Sep2026 *MOBI DENYS SYCHOV", pending: false, "h-posted");

        var stale = PendingReconciler.SelectStalePending([pending, posted], []);

        stale.Should().ContainSingle().Which.Should().BeSameAs(pending);
    }

    [Fact]
    public void KeepsPending_WhenOnlyTheSettlementStampWouldHaveMatchedThem()
    {
        // Stripping the stamp must not merge two genuinely different payments: same account,
        // same amount, different merchant.
        var pending = Tx(20m, "DENYS SYCHOV IE26082062813884 Sent from Revolut", pending: true, "h-pending");
        var posted = Tx(20m, "*MOBI TOP-UP 0857860057 TxnDate: 20Aug2026", pending: false, "h-posted");

        var stale = PendingReconciler.SelectStalePending([pending, posted], []);

        stale.Should().BeEmpty();
    }

    // ── #1229: the match is date-aware and one-to-one ────────────────────────

    [Fact]
    public void KeepsPending_WhenTheOnlyPostedTwinIsOlderThanIt()
    {
        // Last month's rent posted; this month's identical rent is pending. The old posted row is no settlement of it.
        var lastMonth = Posted(Day.AddDays(-30), "h-old");
        var pending = Pending(Day, "h-pending");

        PendingReconciler.SelectStalePending([lastMonth, pending], []).Should().BeEmpty();
    }

    [Fact]
    public void RetiresPending_WhenPostedTwinSettlesWithinTheWindowAfterIt()
    {
        var pending = Pending(Day, "h-pending");
        var posted = Posted(Day, "h-posted", postedDate: Day.AddDays(PendingReconciler.SettlementWindowDays));

        PendingReconciler.SelectStalePending([pending, posted], []).Should().ContainSingle().Which.Should().BeSameAs(pending);
    }

    [Fact]
    public void KeepsPending_WhenPostedTwinSettlesPastTheWindow()
    {
        var pending = Pending(Day, "h-pending");
        var posted = Posted(Day, "h-posted", postedDate: Day.AddDays(PendingReconciler.SettlementWindowDays + 1));

        PendingReconciler.SelectStalePending([pending, posted], []).Should().BeEmpty();
    }

    [Fact]
    public void OnePostedRow_RetiresOnlyOnePendingRow()
    {
        // Two identical pending payments, one settlement so far: the second stays pending.
        var first = Pending(Day, "h-1");
        var second = Pending(Day.AddDays(1), "h-2");
        var posted = Posted(Day.AddDays(1), "h-posted");

        PendingReconciler.SelectStalePending([first, second, posted], [])
            .Should().ContainSingle().Which.Should().BeSameAs(first);
    }

    [Fact]
    public void TwoPostedRows_RetireTwoPendingRows()
    {
        var first = Pending(Day, "h-1");
        var second = Pending(Day.AddDays(1), "h-2");
        var postedA = Posted(Day.AddDays(1), "h-a");
        var postedB = Posted(Day.AddDays(2), "h-b");

        var stale = PendingReconciler.SelectStalePending([first, second, postedA, postedB], []);

        stale.Should().HaveCount(2);
        stale.Should().Contain(first).And.Contain(second);
    }

    [Fact]
    public void IgnoresARowThatWasJustSettledInPlace()
    {
        // A pending row settled by hash this run is no twin of another pending row.
        var settledInPlace = Posted(Day.AddDays(1), "h-settled");
        var other = Pending(Day, "h-other");

        PendingReconciler.SelectStalePending([other, settledInPlace], [], [settledInPlace]).Should().BeEmpty();
    }
}
