namespace FinanceSentry.Modules.BankSync.Infrastructure.Seeding;

using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Domain;
using FinanceSentry.Modules.BankSync.Domain;
using FinanceSentry.Modules.BankSync.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Gives the smoke account (seeded by the Auth module's <c>SmokeAccountSeeder</c>) obviously fake data for the
/// post-deploy live smoke to render: one <see cref="BankAccount.SeededProvider"/> account and a few transactions
/// in the month of the first seed. Written once — a user who already has any bank account is left alone — and
/// only ever for the smoke account's own user id.
/// </summary>
public static class SmokeDataSeeder
{
    private const string Currency = "EUR";
    private const decimal Balance = 2450.00m;

    private static readonly SeedTransaction[] SeedTransactions =
    [
        new(0, 3200.00m, "credit", "Example Employer Ltd salary", "Example Employer Ltd", CategoryKeys.Income),
        new(0, 950.00m, "debit", "Example Lettings rent", "Example Lettings", CategoryKeys.RentAndUtilities),
        new(1, 64.20m, "debit", "Sample Grocer", "Sample Grocer", CategoryKeys.FoodAndDrink),
        new(2, 4.50m, "debit", "Demo Coffee", "Demo Coffee", CategoryKeys.FoodAndDrink),
        new(3, 25.00m, "debit", "Test Transit top-up", "Test Transit", CategoryKeys.Transportation),
    ];

    /// <summary>Seeds the fake data for <paramref name="userId"/>; returns whether anything was written.</summary>
    public static bool Seed(BankSyncDbContext db, Guid userId, DateTime utcNow)
    {
        if (db.BankAccounts.IgnoreQueryFilters([OwnerQueryFilter.Name, BankSyncDbContext.AccountActiveFilterName]).Any(a => a.UserId == userId))
            return false;

        var account = new BankAccount(userId, $"{BankAccount.SeededProvider}-{userId:N}", "Demo Bank", "checking",
            "0000", "Smoke Test", Currency, userId, BankAccount.SeededProvider)
        {
            SyncStatus = "active",
            CurrentBalance = Balance,
        };
        db.BankAccounts.Add(account);

        // Dated within the month of the seed and never after today, so the month-to-date figures have something
        // in them right after the first deploy.
        var monthStart = new DateTime(utcNow.Year, utcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var lastOffset = utcNow.Day - 1;
        for (var i = 0; i < SeedTransactions.Length; i++)
        {
            var seed = SeedTransactions[i];
            var date = monthStart.AddDays(Math.Min(seed.DayOffset, lastOffset));
            db.Transactions.Add(new Transaction(account.Id, userId, seed.Amount, date, seed.Description,
                $"{BankAccount.SeededProvider}-{i}")
            {
                PostedDate = date,
                TransactionType = seed.Type,
                MerchantName = seed.Merchant,
                MerchantCategory = seed.Category,
            });
        }

        db.SaveChanges();
        return true;
    }

    private sealed record SeedTransaction(
        int DayOffset, decimal Amount, string Type, string Description, string Merchant, string Category);
}
