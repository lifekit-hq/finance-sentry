namespace FinanceSentry.Tests.Unit.BankSync;

using FinanceSentry.Modules.BankSync.Domain;
using FinanceSentry.Modules.BankSync.Domain.Repositories;
using FinanceSentry.Modules.BankSync.Infrastructure.Services;
using FluentAssertions;
using Moq;
using Xunit;

/// <summary>
/// A picked transaction is read as the latest charge under its key and the number of charges made
/// up to it, so a plan started from an older payment is current and starts at the right place.
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
    public async Task FindAsync_PickedOlderCharge_AnchorsOnTheLatestSameKeyCharge()
    {
        var first = Charge(new DateTime(2026, 1, 5));
        var picked = Charge(new DateTime(2026, 2, 5));
        var third = Charge(new DateTime(2026, 3, 5));
        var latest = Charge(new DateTime(2026, 4, 5), tweak: t => t.Amount = 12.5m);
        var other = Charge(new DateTime(2026, 5, 6), merchant: "Other Shop");

        var result = await Reader(picked, first, picked, third, latest, other).FindAsync(UserId, Guid.NewGuid());

        result!.Date.Should().Be(new DateOnly(2026, 4, 5));
        result.Amount.Should().Be(12.5m);
        result.ChargeCount.Should().Be(4);
        result.Currency.Should().Be("EUR");
        result.DisplayName.Should().Be(Merchant);
    }

    [Fact]
    public async Task FindAsync_PickedLatestCharge_CountsEverySameKeyCharge()
    {
        var first = Charge(new DateTime(2026, 1, 5));
        var second = Charge(new DateTime(2026, 2, 5));
        var picked = Charge(new DateTime(2026, 3, 5));

        var result = await Reader(picked, first, second, picked).FindAsync(UserId, Guid.NewGuid());

        result!.Date.Should().Be(new DateOnly(2026, 3, 5));
        result.ChargeCount.Should().Be(3);
    }

    [Fact]
    public async Task FindAsync_LatestChargeOnAnotherAccount_UsesThatAccountsCurrency()
    {
        var otherAccount = Guid.NewGuid();
        var picked = Charge(new DateTime(2026, 2, 5));
        var latest = Charge(new DateTime(2026, 3, 5), tweak: t => t.AccountId = otherAccount);
        var accounts = new Mock<IBankAccountRepository>();
        accounts.Setup(r => r.GetByIdAsync(AccountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BankAccount { Currency = "EUR" });
        accounts.Setup(r => r.GetByIdAsync(otherAccount, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BankAccount { Currency = "USD" });
        var transactions = new Mock<ITransactionRepository>();
        transactions.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(picked);
        transactions.Setup(r => r.GetByUserIdAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync([picked, latest]);

        var result = await new CommitmentTransactionReader(accounts.Object, transactions.Object)
            .FindAsync(UserId, Guid.NewGuid());

        result!.Currency.Should().Be("USD");
    }

    [Fact]
    public async Task FindAsync_ChargesAboutAYearApart_InfersAnnualCadence()
    {
        var first = Charge(new DateTime(2024, 6, 5));
        var second = Charge(new DateTime(2025, 6, 6));
        var picked = Charge(new DateTime(2026, 6, 5));

        var result = await Reader(picked, first, second, picked).FindAsync(UserId, Guid.NewGuid());

        result!.Cadence.Should().Be("annual");
    }

    [Fact]
    public async Task FindAsync_ChargesAboutAMonthApart_InfersMonthlyCadence()
    {
        var first = Charge(new DateTime(2026, 1, 5));
        var second = Charge(new DateTime(2026, 2, 6));
        var picked = Charge(new DateTime(2026, 3, 5));

        var result = await Reader(picked, first, second, picked).FindAsync(UserId, Guid.NewGuid());

        result!.Cadence.Should().Be("monthly");
    }

    [Fact]
    public async Task FindAsync_OneOddGapAmongMonthlyCharges_StaysMonthly()
    {
        var charges = new[]
        {
            Charge(new DateTime(2025, 6, 5)),
            Charge(new DateTime(2026, 1, 5)),
            Charge(new DateTime(2026, 2, 5)),
            Charge(new DateTime(2026, 3, 5)),
        };

        var result = await Reader(charges[^1], charges).FindAsync(UserId, Guid.NewGuid());

        result!.Cadence.Should().Be("monthly");
    }

    [Fact]
    public async Task FindAsync_SingleCharge_HasNoCadence()
    {
        var picked = Charge(new DateTime(2026, 3, 5));

        var result = await Reader(picked, picked).FindAsync(UserId, Guid.NewGuid());

        result!.Cadence.Should().BeNull();
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

    [Fact]
    public async Task FindInstallmentAsync_StartsFromSameAmountChargesNotEveryPurchaseAtTheMerchant()
    {
        var earlier = new[]
        {
            Charge(new DateTime(2026, 1, 5), merchant: "Apple Store", tweak: t => t.Amount = 35m),
            Charge(new DateTime(2026, 2, 5), merchant: "Apple Store", tweak: t => t.Amount = 12m),
            Charge(new DateTime(2026, 3, 5), merchant: "Apple Store", tweak: t => t.Amount = 80m),
            Charge(new DateTime(2026, 4, 5), merchant: "Apple Store", tweak: t => t.Amount = 9m),
        };
        var picked = Charge(new DateTime(2026, 5, 5), merchant: "Apple Store", tweak: t => t.Amount = 600m);
        var unrelatedLater = Charge(new DateTime(2026, 6, 1), merchant: "Apple Store", tweak: t => t.Amount = 20m);
        var reader = Reader(picked, [.. earlier, picked, unrelatedLater]);

        var installment = await reader.FindInstallmentAsync(UserId, Guid.NewGuid());
        var subscription = await reader.FindAsync(UserId, Guid.NewGuid());

        installment!.Key.Should().Be("installment:apple store:600");
        installment.ChargeCount.Should().Be(1);
        installment.Amount.Should().Be(600m);
        installment.Date.Should().Be(new DateOnly(2026, 5, 5));
        subscription!.Key.Should().Be("apple store");
        subscription.ChargeCount.Should().Be(6);
    }

    [Fact]
    public async Task FindInstallmentAsync_CountsOnlyChargesOfTheRoundedSameAmount()
    {
        var first = Charge(new DateTime(2026, 1, 5), merchant: "Apple Store", tweak: t => t.Amount = 600.2m);
        var picked = Charge(new DateTime(2026, 2, 5), merchant: "Apple Store", tweak: t => t.Amount = 599.8m);
        var other = Charge(new DateTime(2026, 3, 5), merchant: "Apple Store", tweak: t => t.Amount = 700m);

        var result = await Reader(picked, first, picked, other).FindInstallmentAsync(UserId, Guid.NewGuid());

        result!.ChargeCount.Should().Be(2);
        result.Key.Should().Be("installment:apple store:600");
    }

    [Fact]
    public async Task FindInstallmentAsync_RecognizedPlanRepayment_KeepsItsPlanKey()
    {
        var picked = Charge(
            new DateTime(2026, 2, 5),
            merchant: "Rozetka",
            tweak: t =>
            {
                t.Description = "Погашення наступного платежу Rozetka";
                t.Amount = 1200m;
            });

        var result = await Reader(picked, picked).FindInstallmentAsync(UserId, Guid.NewGuid());

        result!.Key.Should().Be("installment:rozetka:1200");
    }
}
