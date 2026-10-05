namespace FinanceSentry.Tests.Unit.BankSync;

using FinanceSentry.Modules.BankSync.Domain;
using FinanceSentry.Modules.BankSync.Domain.Repositories;
using FinanceSentry.Modules.BankSync.Infrastructure.Services;
using FluentAssertions;
using Moq;
using Xunit;

/// <summary>
/// The picked transaction's charge count is how many charges under its key the person has made
/// up to and including it, so a plan started from a later payment starts at the right place.
/// </summary>
public class CommitmentTransactionReaderTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid AccountId = Guid.NewGuid();
    private const string Merchant = "Acme Hosting";

    private static Transaction Charge(DateTime date, string merchant = Merchant, Action<Transaction>? tweak = null)
    {
        var t = new Transaction(AccountId, UserId, 10m, date, merchant.ToUpperInvariant(), Guid.NewGuid().ToString())
        {
            MerchantName = merchant,
            TransactionType = "debit",
        };
        tweak?.Invoke(t);
        return t;
    }

    private static CommitmentTransactionReader Reader(Transaction picked, params Transaction[] all)
    {
        var accounts = new Mock<IBankAccountRepository>();
        accounts.Setup(r => r.GetByIdAsync(AccountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BankAccount { Currency = "EUR" });
        var transactions = new Mock<ITransactionRepository>();
        transactions.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(picked);
        transactions.Setup(r => r.GetByUserIdAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(all);
        return new CommitmentTransactionReader(accounts.Object, transactions.Object);
    }

    [Fact]
    public async Task FindAsync_CountsSameKeyChargesUpToAndIncludingThePickedOne()
    {
        var first = Charge(new DateTime(2026, 1, 5));
        var second = Charge(new DateTime(2026, 2, 5));
        var picked = Charge(new DateTime(2026, 3, 5));
        var later = Charge(new DateTime(2026, 4, 5));
        var other = Charge(new DateTime(2026, 2, 6), merchant: "Other Shop");

        var result = await Reader(picked, first, second, picked, later, other).FindAsync(UserId, Guid.NewGuid());

        result!.ChargeCount.Should().Be(3);
        result.Date.Should().Be(new DateOnly(2026, 3, 5));
        result.Currency.Should().Be("EUR");
    }

    [Fact]
    public async Task FindAsync_IgnoresPendingZeroAmountCreditAndInactiveCharges()
    {
        var picked = Charge(new DateTime(2026, 3, 5));
        var noise = new[]
        {
            Charge(new DateTime(2026, 1, 5), tweak: t => t.IsPending = true),
            Charge(new DateTime(2026, 1, 6), tweak: t => t.Amount = 0m),
            Charge(new DateTime(2026, 1, 7), tweak: t => t.TransactionType = "credit"),
            Charge(new DateTime(2026, 1, 8), tweak: t => t.SoftDelete()),
        };

        var result = await Reader(picked, [picked, .. noise]).FindAsync(UserId, Guid.NewGuid());

        result!.ChargeCount.Should().Be(1);
    }

    [Fact]
    public async Task FindAsync_CountsTwoChargesOnTheSameDayOnce()
    {
        var picked = Charge(new DateTime(2026, 3, 5, 9, 0, 0));
        var sameDay = Charge(new DateTime(2026, 3, 5, 17, 0, 0));

        var result = await Reader(picked, picked, sameDay).FindAsync(UserId, Guid.NewGuid());

        result!.ChargeCount.Should().Be(1);
    }

    [Fact]
    public async Task FindAsync_AnotherUsersTransaction_ReturnsNull()
    {
        var picked = Charge(new DateTime(2026, 3, 5), tweak: t => t.UserId = Guid.NewGuid());

        var result = await Reader(picked, picked).FindAsync(UserId, Guid.NewGuid());

        result.Should().BeNull();
    }
}
