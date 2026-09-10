namespace FinanceSentry.Tests.Unit.BankSync.Application.Subscriptions;

using FinanceSentry.Modules.BankSync.Application.Services;
using FluentAssertions;
using Xunit;

/// <summary>
/// The commitment key a transaction resolves to must be the key the detector stored for the
/// commitment it belongs to. Any divergence silently misclassifies committed spend as
/// discretionary, which is exactly the bug these tests exist to prevent.
/// </summary>
public class CommitmentKeyResolverTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private static SubscriptionDetectionAlgorithm.TxRow InstallmentTx(
        string description, decimal amount, int monthsAgo, int? mcc = null) =>
        new(UserId, null, description, amount,
            new DateTime(2026, 8, 10, 0, 0, 0, DateTimeKind.Utc).AddMonths(-monthsAgo),
            null, mcc, "UAH");

    [Theory]
    [InlineData("Щомісячний платіж telemart - monomarket", 6499.84, null, "installment:telemart:6500")]
    [InlineData("Погашення наступного платежу RozetkaPay", 2339.95, null, "installment:rozetkapay:2340")]
    [InlineData("Погашення наступного платежу ТОВ Алло - monomarket", 2999.95, null, "installment:тов алло:3000")]
    [InlineData("Платіж Pandora", 1200.00, 4829, "installment:pandora:1200")]
    public void Resolve_InstallmentRepayment_ReturnsThePlanKey(
        string description, double amount, int? mcc, string expected)
    {
        CommitmentKeyResolver.Resolve(null, description, (decimal)amount, mcc)
            .Should().Be(expected);
    }

    [Fact]
    public void Resolve_NonInstallment_FallsBackToTheMerchantDetectionKey()
    {
        // Same words as the MCC-gated installment path, but an ordinary purchase MCC.
        CommitmentKeyResolver.Resolve("Netflix.com", "CARD PAYMENT 4471", 15.99m, 5815)
            .Should().Be(MerchantNameNormalizer.NormalizeDetectionKey("Netflix.com", "CARD PAYMENT 4471"));
    }

    [Fact]
    public void Resolve_FullPayoff_IsNotKeyedAsAPlan()
    {
        // The detector never stores a plan under a payoff's own amount — it uses payoffs only
        // to mark a plan completed. Keying one as a plan would invent a key nothing matches.
        CommitmentKeyResolver.Resolve(null, "Повне погашення RozetkaPay", 8420m, null)
            .Should().NotStartWith("installment:");
    }

    [Fact]
    public void Resolve_InstallmentWithNoRecoverableMerchant_FallsBackToTheMerchantKey()
    {
        // "- monomarket" alone leaves nothing behind once the marketplace tag is stripped.
        CommitmentKeyResolver.Resolve(null, "- monomarket", 500m, null)
            .Should().NotStartWith("installment:");
    }

    [Fact]
    public void Resolve_AmountJitterWithinAPlan_ResolvesToOneKey()
    {
        // telemart bills ₴6,499.84 one month and ₴6,499.85 the next; both are the same plan.
        var first = CommitmentKeyResolver.Resolve(null, "Щомісячний платіж telemart - monomarket", 6499.84m, null);
        var second = CommitmentKeyResolver.Resolve(null, "Щомісячний платіж telemart - monomarket", 6499.85m, null);

        first.Should().Be(second);
    }

    [Fact]
    public void Resolve_ConcurrentPlansAtTheSameShop_StayDistinct()
    {
        // Two Алло розстрочки run side by side; merchant-only keying would merge them.
        var cheap = CommitmentKeyResolver.Resolve(null, "Погашення наступного платежу ТОВ Алло - monomarket", 2339.95m, null);
        var dear = CommitmentKeyResolver.Resolve(null, "Погашення наступного платежу ТОВ Алло - monomarket", 2999.95m, null);

        cheap.Should().NotBe(dear);
    }

    [Fact]
    public void Resolve_ReproducesEveryKeyDetectInstallmentsStores()
    {
        // The drift guard: run the real detector over a batch of repayments, then re-derive
        // each transaction's key from the transaction alone. Every stored plan key must be
        // reachable, or committed installment spend reads as discretionary.
        var transactions = new List<SubscriptionDetectionAlgorithm.TxRow>
        {
            InstallmentTx("Щомісячний платіж telemart - monomarket", 6499.84m, 2),
            InstallmentTx("Щомісячний платіж telemart - monomarket", 6499.85m, 1),
            InstallmentTx("Погашення наступного платежу ТОВ Алло - monomarket", 2339.95m, 1),
            InstallmentTx("Погашення наступного платежу ТОВ Алло - monomarket", 2999.95m, 1),
            InstallmentTx("Платіж Pandora", 1200m, 1, mcc: 4829),
        };

        var storedKeys = SubscriptionDetectionAlgorithm.DetectInstallments(transactions)
            .Select(d => d.MerchantNameNormalized)
            .ToHashSet(StringComparer.Ordinal);

        var resolvedKeys = transactions
            .Select(t => CommitmentKeyResolver.Resolve(t.MerchantName, t.Description, t.Amount, t.Mcc))
            .ToHashSet(StringComparer.Ordinal);

        storedKeys.Should().NotBeEmpty();
        resolvedKeys.Should().BeEquivalentTo(storedKeys);
    }
}
