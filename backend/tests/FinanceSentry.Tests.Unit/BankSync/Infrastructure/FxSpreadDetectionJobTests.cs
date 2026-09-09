namespace FinanceSentry.Tests.Unit.BankSync.Infrastructure;

using FinanceSentry.Core.Domain;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Core.Utils;
using FinanceSentry.Modules.BankSync.Application.Services;
using FinanceSentry.Modules.BankSync.Domain;
using FinanceSentry.Modules.BankSync.Infrastructure.Jobs;
using FinanceSentry.Modules.BankSync.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

/// <summary>
/// Unit tests for <see cref="FxSpreadDetectionJob"/> (044/US4).
/// The sentinel pairs specific debit+credit transfer legs via <see cref="TransferDetectionService"/>
/// and alerts per matched pair whose implied conversion rate trails the market rate beyond the
/// threshold. Crucially, unrelated same-day flows (salary in, rent out) must never pair — the
/// old day-bucket aggregate design alerted on exactly that coincidence.
/// </summary>
public sealed class FxSpreadDetectionJobTests : IDisposable
{
    private readonly Mock<IAlertGeneratorService> _alerts = new();

    // Live rates deliberately different from CurrencyConverter's offline seed (EUR 1.08 /
    // UAH 0.024, which would imply 45): the sentinel must measure against the refreshed table,
    // so every figure below is pinned to 1.20 / 0.030 = 40 and would not match under the seed.
    private const decimal LiveEurUsd = 1.20m;
    private const decimal LiveUahUsd = 0.030m;
    private const decimal EurUahMarketRate = 40m;

    public FxSpreadDetectionJobTests() => InstallLiveRates();

    public void Dispose() => CurrencyConverter.UpdateRates(CurrencyConverter.FallbackRates);

    private static void InstallLiveRates() =>
        CurrencyConverter.UpdateRates(new Dictionary<string, decimal>
        {
            ["EUR"] = LiveEurUsd,
            ["UAH"] = LiveUahUsd,
        });

    private static BankSyncDbContext NewDb() => new(
        new DbContextOptionsBuilder<BankSyncDbContext>()
            .UseInMemoryDatabase($"fxspread-{Guid.NewGuid():N}").Options);

    private static IOptions<HygieneSentinelsOptions> OptionsWith(
        int lookbackDays, decimal threshold, int? maxRateAgeHours = null)
    {
        var options = new HygieneSentinelsOptions
        {
            FxSpreadLookbackDays = lookbackDays,
            FxSpreadThreshold = threshold,
        };
        if (maxRateAgeHours is not null) options.FxSpreadMaxRateAgeHours = maxRateAgeHours.Value;

        return Options.Create(options);
    }

    private FxSpreadDetectionJob MakeJob(
        BankSyncDbContext db, IOptions<HygieneSentinelsOptions>? options = null) =>
        new(db, new TransferDetectionService(), _alerts.Object,
            options ?? Options.Create(new HygieneSentinelsOptions()),
            Mock.Of<ILogger<FxSpreadDetectionJob>>());

    private static BankAccount MakeAccount(Guid userId, string currency)
    {
        var account = new BankAccount(userId, Guid.NewGuid().ToString(), "Test Bank",
            "current", "1234", "Owner", currency, Guid.NewGuid(), "monobank");
        return account;
    }

    /// <summary>
    /// Adapter convention: positive amount, direction in <c>TransactionType</c> ("debit"/"credit").
    /// </summary>
    private static Transaction MakeTx(BankAccount account, decimal amount, string type,
        DateTime? date = null, string description = "tx", string? category = null,
        bool isPending = false, DateTime? postedDate = null)
    {
        var tx = new Transaction(account.Id, account.UserId, amount,
            date ?? DateTime.UtcNow, description, Guid.NewGuid().ToString(), isPending)
        {
            TransactionType = type,
            MerchantCategory = category,
            IsActive = true,
            PostedDate = postedDate,
        };
        return tx;
    }

    /// <summary>A debit+credit conversion pair carrying a transfer category on both legs.</summary>
    private static (Transaction Debit, Transaction Credit) MakeConversion(
        BankAccount fromAccount, decimal fromAmount, BankAccount toAccount, decimal toAmount,
        DateTime date, bool isPending = false, DateTime? postedDate = null)
    {
        var debit = MakeTx(fromAccount, fromAmount, "debit", date,
            description: "Currency exchange", category: CategoryKeys.TransferOut,
            isPending: isPending, postedDate: postedDate);
        var credit = MakeTx(toAccount, toAmount, "credit", date,
            description: "Currency exchange", category: CategoryKeys.TransferIn,
            isPending: isPending, postedDate: postedDate);
        return (debit, credit);
    }

    [Fact]
    public async Task ExecuteAsync_AlertFired_WhenMatchedPairImpliedRateIsBelowMarketByMoreThanThreshold()
    {
        await using var db = NewDb();
        var userId = Guid.NewGuid();
        var eurAccount = MakeAccount(userId, "EUR");
        var uahAccount = MakeAccount(userId, "UAH");
        db.BankAccounts.AddRange(eurAccount, uahAccount);

        // 100 EUR debit → 3600 UAH credit (implied rate 36, market 40)
        // Spread = (40 - 36) / 40 = 10% > default 3% threshold
        var (debit, credit) = MakeConversion(eurAccount, 100m, uahAccount, 3600m, DateTime.UtcNow);
        db.Transactions.AddRange(debit, credit);
        await db.SaveChangesAsync();

        await MakeJob(db).ExecuteAsync();

        _alerts.Verify(a => a.GenerateFxSpreadAlertAsync(
            userId, debit.Id, "EUR", "UAH", 36m, EurUahMarketRate,
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// The reference rate has to be one somebody published. CurrencyConverter seeds itself with
    /// hardcoded constants that hold until the refresh job first ticks and survive any feed
    /// outage afterwards; against a drifted seed a fair conversion reads as a multi-percent loss.
    /// The sentinel stands down instead of accusing the bank.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_NoAlert_WhenFxRatesAreOlderThanTheConfiguredMaxAge()
    {
        await using var db = NewDb();
        var userId = Guid.NewGuid();
        var eurAccount = MakeAccount(userId, "EUR");
        var uahAccount = MakeAccount(userId, "UAH");
        db.BankAccounts.AddRange(eurAccount, uahAccount);

        // The same conversion the firing case alerts on.
        var (debit, credit) = MakeConversion(eurAccount, 100m, uahAccount, 3600m, DateTime.UtcNow);
        db.Transactions.AddRange(debit, credit);
        await db.SaveChangesAsync();

        // Zero tolerance is stale by definition (freshness is strict), so the table installed in
        // the constructor cannot satisfy it — no dependence on how much time has elapsed.
        await MakeJob(db, OptionsWith(30, 0.03m, maxRateAgeHours: 0)).ExecuteAsync();

        _alerts.Verify(a => a.GenerateFxSpreadAlertAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// THE regression test for the day-bucket aggregate design: two unrelated same-day flows —
    /// a UAH rent debit and a USD salary credit — whose amounts happen to imply a "rate" inside
    /// the old 3× plausibility band (400 / 18000 ≈ 0.0222 vs market UAH→USD 0.024, a 7% "spread")
    /// must NOT pair and must NOT alert. They carry no transfer signal (no transfer type or
    /// category, dissimilar descriptions), so the transfer matcher rejects them; the old design
    /// summed day buckets and alerted.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_NoAlert_WhenUnrelatedSameDayFlowsCoincideWithinOldPlausibilityBand()
    {
        await using var db = NewDb();
        var userId = Guid.NewGuid();
        var uahAccount = MakeAccount(userId, "UAH");
        var usdAccount = MakeAccount(userId, "USD");
        db.BankAccounts.AddRange(uahAccount, usdAccount);

        var today = DateTime.UtcNow;
        // Rent stored signed-negative (legacy convention) so the old design's Amount<0 outflow
        // bucket sees it; the salary credit is a plain positive inflow. Neither leg is a transfer.
        var rentDebit = new Transaction(uahAccount.Id, userId, -18000m, today,
            "Monthly rent payment", Guid.NewGuid().ToString())
        {
            TransactionType = "debit",
            MerchantCategory = "RENT_AND_UTILITIES",
            IsActive = true,
        };
        var salaryCredit = MakeTx(usdAccount, 400m, "credit", today,
            description: "ACME Corp payroll", category: "INCOME");
        db.Transactions.AddRange(rentDebit, salaryCredit);
        await db.SaveChangesAsync();

        await MakeJob(db).ExecuteAsync();

        _alerts.Verify(a => a.GenerateFxSpreadAlertAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// Amount coincidence alone is not a pair: two same-day flows whose USD values are within the
    /// pairing tolerance still must not match without a transfer type/category/description signal.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_NoAlert_WhenAmountsCoincideButNoTransferSignal()
    {
        await using var db = NewDb();
        var userId = Guid.NewGuid();
        var eurAccount = MakeAccount(userId, "EUR");
        var uahAccount = MakeAccount(userId, "UAH");
        db.BankAccounts.AddRange(eurAccount, uahAccount);

        var today = DateTime.UtcNow;
        // 100 EUR grocery debit and 4050 UAH bonus credit — USD values within the pairing
        // tolerance, but no transfer signal on either leg.
        db.Transactions.AddRange(
            MakeTx(eurAccount, 100m, "debit", today,
                description: "Grocery store purchase", category: "FOOD_AND_DRINK"),
            MakeTx(uahAccount, 4050m, "credit", today,
                description: "Employer bonus", category: "INCOME"));
        await db.SaveChangesAsync();

        await MakeJob(db).ExecuteAsync();

        _alerts.Verify(a => a.GenerateFxSpreadAlertAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_NoAlert_WhenImpliedRateIsWithinThreshold()
    {
        await using var db = NewDb();
        var userId = Guid.NewGuid();
        var eurAccount = MakeAccount(userId, "EUR");
        var uahAccount = MakeAccount(userId, "UAH");
        db.BankAccounts.AddRange(eurAccount, uahAccount);

        // Implied 39.5 vs market 40 — spread = 1.25% < 3% threshold
        var (debit, credit) = MakeConversion(eurAccount, 100m, uahAccount, 3950m, DateTime.UtcNow);
        db.Transactions.AddRange(debit, credit);
        await db.SaveChangesAsync();

        await MakeJob(db).ExecuteAsync();

        _alerts.Verify(a => a.GenerateFxSpreadAlertAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_NoAlert_WhenLegsAreTooFarApartToPair()
    {
        await using var db = NewDb();
        var userId = Guid.NewGuid();
        var eurAccount = MakeAccount(userId, "EUR");
        var uahAccount = MakeAccount(userId, "UAH");
        db.BankAccounts.AddRange(eurAccount, uahAccount);

        // Legs 10 days apart — beyond the transfer matcher's 2-day window, so no pair forms
        // even though both carry a transfer category.
        db.Transactions.AddRange(
            MakeTx(eurAccount, 100m, "debit", DateTime.UtcNow.AddDays(-2),
                description: "Currency exchange", category: CategoryKeys.TransferOut),
            MakeTx(uahAccount, 4050m, "credit", DateTime.UtcNow.AddDays(-12),
                description: "Currency exchange", category: CategoryKeys.TransferIn));
        await db.SaveChangesAsync();

        await MakeJob(db, OptionsWith(30, 0.03m)).ExecuteAsync();

        _alerts.Verify(a => a.GenerateFxSpreadAlertAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_NoAlert_WhenTransferIsSameCurrency()
    {
        await using var db = NewDb();
        var userId = Guid.NewGuid();
        var eurAccount1 = MakeAccount(userId, "EUR");
        var eurAccount2 = MakeAccount(userId, "EUR");
        var uahAccount = MakeAccount(userId, "UAH"); // second currency so the user qualifies
        db.BankAccounts.AddRange(eurAccount1, eurAccount2, uahAccount);

        // A EUR→EUR internal transfer pairs, but carries no FX conversion to measure.
        var (debit, credit) = MakeConversion(eurAccount1, 100m, eurAccount2, 100m, DateTime.UtcNow);
        db.Transactions.AddRange(debit, credit);
        await db.SaveChangesAsync();

        await MakeJob(db).ExecuteAsync();

        _alerts.Verify(a => a.GenerateFxSpreadAlertAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_UsesConfigurableThreshold()
    {
        await using var db = NewDb();
        var userId = Guid.NewGuid();
        var eurAccount = MakeAccount(userId, "EUR");
        var uahAccount = MakeAccount(userId, "UAH");
        db.BankAccounts.AddRange(eurAccount, uahAccount);

        // Implied 39.0 (spread = 2.5%): below the 3% default but above the 2% custom threshold
        var (debit, credit) = MakeConversion(eurAccount, 100m, uahAccount, 3900m, DateTime.UtcNow);
        db.Transactions.AddRange(debit, credit);
        await db.SaveChangesAsync();

        await MakeJob(db, OptionsWith(30, 0.02m)).ExecuteAsync();

        _alerts.Verify(a => a.GenerateFxSpreadAlertAsync(
            userId, debit.Id, "EUR", "UAH", 39m, EurUahMarketRate,
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_NoAlert_WhenTransactionOutsideLookbackWindow()
    {
        await using var db = NewDb();
        var userId = Guid.NewGuid();
        var eurAccount = MakeAccount(userId, "EUR");
        var uahAccount = MakeAccount(userId, "UAH");
        db.BankAccounts.AddRange(eurAccount, uahAccount);

        // A genuine costly conversion, but 40 days old — outside a 30-day lookback window
        var old = DateTime.UtcNow.AddDays(-40);
        var (debit, credit) = MakeConversion(eurAccount, 100m, uahAccount, 4050m, old);
        db.Transactions.AddRange(debit, credit);
        await db.SaveChangesAsync();

        await MakeJob(db, OptionsWith(30, 0.03m)).ExecuteAsync();

        _alerts.Verify(a => a.GenerateFxSpreadAlertAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_NoAlert_WhenAccountIsInactive()
    {
        await using var db = NewDb();
        var userId = Guid.NewGuid();
        var eurAccount = MakeAccount(userId, "EUR");
        eurAccount.IsActive = false;
        var uahAccount = MakeAccount(userId, "UAH");
        db.BankAccounts.AddRange(eurAccount, uahAccount);

        // Same figures as the firing case, but the debit side's account is disconnected.
        var (debit, credit) = MakeConversion(eurAccount, 100m, uahAccount, 3600m, DateTime.UtcNow);
        db.Transactions.AddRange(debit, credit);
        await db.SaveChangesAsync();

        await MakeJob(db).ExecuteAsync();

        _alerts.Verify(a => a.GenerateFxSpreadAlertAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_AlertsPerConversion_EachKeyedToItsOwnDebitLeg()
    {
        await using var db = NewDb();
        var userId = Guid.NewGuid();
        var eurAccount = MakeAccount(userId, "EUR");
        var uahAccount = MakeAccount(userId, "UAH");
        db.BankAccounts.AddRange(eurAccount, uahAccount);

        // Two distinct costly conversions on adjacent days — each must alert with its own
        // debit transaction id (the dedup key), not collapse into one currency-pair alert.
        var (debit1, credit1) = MakeConversion(
            eurAccount, 100m, uahAccount, 3600m, DateTime.UtcNow.AddDays(-1));
        var (debit2, credit2) = MakeConversion(
            eurAccount, 200m, uahAccount, 7200m, DateTime.UtcNow);
        db.Transactions.AddRange(debit1, credit1, debit2, credit2);
        await db.SaveChangesAsync();

        await MakeJob(db).ExecuteAsync();

        _alerts.Verify(a => a.GenerateFxSpreadAlertAsync(
            userId, debit1.Id, "EUR", "UAH", 36m, EurUahMarketRate, It.IsAny<CancellationToken>()),
            Times.Once);
        _alerts.Verify(a => a.GenerateFxSpreadAlertAsync(
            userId, debit2.Id, "EUR", "UAH", 36m, EurUahMarketRate, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// A hold's amount is provisional, and a cross-currency conversion is where the bank revises
    /// it on settlement — so the implied rate divided out of a hold is a rate nobody was charged.
    /// The sentinel waits for the settled figures rather than accusing on the provisional ones.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_NoAlert_WhenTheConversionIsStillOnHold()
    {
        await using var db = NewDb();
        var userId = Guid.NewGuid();
        var eurAccount = MakeAccount(userId, "EUR");
        var uahAccount = MakeAccount(userId, "UAH");
        db.BankAccounts.AddRange(eurAccount, uahAccount);

        // The same figures the firing case alerts on, still pending on both legs.
        var (debit, credit) = MakeConversion(
            eurAccount, 100m, uahAccount, 3600m, DateTime.UtcNow, isPending: true);
        db.Transactions.AddRange(debit, credit);
        await db.SaveChangesAsync();

        await MakeJob(db).ExecuteAsync();

        _alerts.Verify(a => a.GenerateFxSpreadAlertAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// The hold outlives the settled conversion it became: <c>PendingReconciler</c> retires a hold
    /// by matching it to a posted row on (account, amount, description), and an FX hold settles at
    /// a different amount — so both rows stay active. Both pair, and the dedup key is the debit
    /// transaction id, so one conversion produced two alerts under two ids nothing could join.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_AlertsOnce_WhenAHoldCoexistsWithTheSettledConversionItBecame()
    {
        await using var db = NewDb();
        var userId = Guid.NewGuid();
        var eurAccount = MakeAccount(userId, "EUR");
        var uahAccount = MakeAccount(userId, "UAH");
        db.BankAccounts.AddRange(eurAccount, uahAccount);

        var date = DateTime.UtcNow;
        // The hold quotes 100 EUR → 3600 UAH; it settles at 102 → 3672 (the same implied 36).
        // Both legs moved, so neither matches its twin on amount and the hold is never retired.
        var (heldDebit, heldCredit) = MakeConversion(
            eurAccount, 100m, uahAccount, 3600m, date, isPending: true);
        var (settledDebit, settledCredit) = MakeConversion(
            eurAccount, 102m, uahAccount, 3672m, date, postedDate: date);
        db.Transactions.AddRange(heldDebit, heldCredit, settledDebit, settledCredit);
        await db.SaveChangesAsync();

        await MakeJob(db).ExecuteAsync();

        _alerts.Verify(a => a.GenerateFxSpreadAlertAsync(
            userId, settledDebit.Id, "EUR", "UAH", 36m, EurUahMarketRate,
            It.IsAny<CancellationToken>()),
            Times.Once);
        // …and that is the only alert: the hold contributed no second one.
        _alerts.Verify(a => a.GenerateFxSpreadAlertAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Waiting for settlement must not silently drop a slow one. The lookback window follows the
    /// same date the transfer matcher pairs on — <c>PostedDate ?? TransactionDate</c> — so a
    /// conversion that settles days after it was made is measured when it settles rather than
    /// having aged out of the window while it was still ineligible.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_AlertFired_WhenAConversionSettledLongAfterItWasMade()
    {
        await using var db = NewDb();
        var userId = Guid.NewGuid();
        var eurAccount = MakeAccount(userId, "EUR");
        var uahAccount = MakeAccount(userId, "UAH");
        db.BankAccounts.AddRange(eurAccount, uahAccount);

        // Made 10 days ago, settled today — outside a 3-day window on the transaction date,
        // inside it on the settled date.
        var (debit, credit) = MakeConversion(
            eurAccount, 100m, uahAccount, 3600m, DateTime.UtcNow.AddDays(-10),
            postedDate: DateTime.UtcNow);
        db.Transactions.AddRange(debit, credit);
        await db.SaveChangesAsync();

        await MakeJob(db, OptionsWith(3, 0.03m)).ExecuteAsync();

        _alerts.Verify(a => a.GenerateFxSpreadAlertAsync(
            userId, debit.Id, "EUR", "UAH", 36m, EurUahMarketRate, It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
