namespace FinanceSentry.Tests.Unit.BankSync.Application;

using FinanceSentry.Core.Domain;
using FinanceSentry.Core.Utils;
using FinanceSentry.Modules.BankSync.Application.Services;
using FinanceSentry.Modules.BankSync.Domain;
using FluentAssertions;
using Xunit;

public class TransferDetectionTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private static Transaction MakeTx(
        Guid accountId, decimal amount, string type, DateTime date,
        string description = "Transfer to savings", bool isPending = false, bool isActive = true,
        string? category = null)
    {
        var hash = Guid.NewGuid().ToString("N");
        var tx = new Transaction(accountId, UserId, amount, date, description, hash, isPending)
        {
            TransactionType = type,
            PostedDate = isPending ? null : date,
            IsActive = isActive,
            MerchantCategory = category,
        };
        return tx;
    }

    /// <summary>The credit-side amount that exactly mirrors a debit across currencies at current rates.</summary>
    private static decimal ConvertedAmount(decimal amount, string fromCurrency, string toCurrency) =>
        Math.Round(CurrencyConverter.ToUsd(amount, fromCurrency) / CurrencyConverter.ToUsd(1m, toCurrency), 2);

    [Fact]
    public void DetectTransferTransactionIds_EmptyInput_ReturnsEmptySet()
    {
        var sut = new TransferDetectionService();

        var result = sut.DetectTransferTransactionIds([]);

        result.Should().BeEmpty();
    }

    [Fact]
    public void DetectTransferTransactionIds_PairMatchedByAmountAndDescription_ReturnsBothIds()
    {
        var accountA = Guid.NewGuid();
        var accountB = Guid.NewGuid();
        var date = new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc);

        var debit  = MakeTx(accountA, 500m, "debit",  date);
        var credit = MakeTx(accountB, 500m, "credit", date);

        var sut = new TransferDetectionService();

        var result = sut.DetectTransferTransactionIds([debit, credit]);

        result.Should().BeEquivalentTo(new[] { debit.Id, credit.Id });
    }

    [Fact]
    public void DetectTransferTransactionIds_UnrelatedDebitAndCredit_NotMatched()
    {
        var accountA = Guid.NewGuid();
        var accountB = Guid.NewGuid();
        var date = new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc);

        // Different amounts + different descriptions → not a transfer
        var debit  = MakeTx(accountA, 500m, "debit",  date, description: "Grocery Store");
        var credit = MakeTx(accountB, 500m, "credit", date, description: "Employer Payroll");

        var sut = new TransferDetectionService();

        var result = sut.DetectTransferTransactionIds([debit, credit]);

        result.Should().BeEmpty();
    }

    [Fact]
    public void DetectTransferTransactionIds_AmbiguousCredit_ConsumedByFirstDebitOnly()
    {
        // Two debits both match the same credit. The credit must be consumed once, so
        // the second debit stays unmatched (and is counted as real spending).
        var accountA = Guid.NewGuid();
        var accountB = Guid.NewGuid();
        var date = new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc);

        var debit1 = MakeTx(accountA, 500m, "debit",  date);
        var debit2 = MakeTx(accountA, 500m, "debit",  date);
        var credit = MakeTx(accountB, 500m, "credit", date);

        var sut = new TransferDetectionService();

        var result = sut.DetectTransferTransactionIds([debit1, debit2, credit]);

        result.Should().HaveCount(2);
        result.Should().Contain(credit.Id);
        (result.Contains(debit1.Id) ^ result.Contains(debit2.Id)).Should().BeTrue();
    }

    [Fact]
    public void DetectTransferTransactionIds_InactiveRows_Ignored()
    {
        var accountA = Guid.NewGuid();
        var accountB = Guid.NewGuid();
        var date = new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc);

        var debit  = MakeTx(accountA, 500m, "debit",  date, isActive: false);
        var credit = MakeTx(accountB, 500m, "credit", date, isActive: false);

        var sut = new TransferDetectionService();

        var result = sut.DetectTransferTransactionIds([debit, credit]);

        result.Should().BeEmpty();
    }

    [Fact]
    public void DetectTransferTransactionIds_PendingLeg_StillPairs()
    {
        // Cash-flow statistics count pending money, so a transfer whose receiving leg is
        // still pending must be excluded like any other — otherwise the pending credit
        // leaks into income until it posts (the Revolut→AIB €265 case).
        var accountA = Guid.NewGuid();
        var accountB = Guid.NewGuid();
        var date = new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc);

        var debit  = MakeTx(accountA, 500m, "debit",  date);
        var credit = MakeTx(accountB, 500m, "credit", date, isPending: true);

        var sut = new TransferDetectionService();

        var result = sut.DetectTransferTransactionIds([debit, credit]);

        result.Should().BeEquivalentTo(new[] { debit.Id, credit.Id });
    }

    [Theory]
    [InlineData("Debit",  "Credit")]
    [InlineData("DEBIT",  "CREDIT")]
    [InlineData("debit",  "CREDIT")]
    [InlineData("DEBIT",  "credit")]
    [InlineData(" debit", " credit")]   // leading whitespace should be trimmed
    [InlineData("debit ", "credit ")]   // trailing whitespace
    public void DetectTransferTransactionIds_MixedCaseAndWhitespaceTypes_AreMatchedCorrectly(
        string debitType, string creditType)
    {
        var accountA = Guid.NewGuid();
        var accountB = Guid.NewGuid();
        var date = new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc);

        var debit  = MakeTx(accountA, 500m, debitType,  date);
        var credit = MakeTx(accountB, 500m, creditType, date);

        var sut = new TransferDetectionService();

        var result = sut.DetectTransferTransactionIds([debit, credit]);

        result.Should().BeEquivalentTo(new[] { debit.Id, credit.Id },
            because: $"debit type '{debitType}' and credit type '{creditType}' should be bucketed case-insensitively");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("unknown")]
    [InlineData("transfer")]
    public void DetectTransferTransactionIds_NonDebitCreditType_NotBucketed(string? txType)
    {
        // Rows with a type that is neither "debit" nor "credit" (regardless of case)
        // must not end up in either bucket and therefore never produce a matched pair.
        var accountA = Guid.NewGuid();
        var accountB = Guid.NewGuid();
        var date = new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc);

        // Two rows with the same non-debit/credit type — no valid debit+credit pair exists.
        var tx1 = MakeTx(accountA, 500m, txType!, date);
        var tx2 = MakeTx(accountB, 500m, txType!, date);

        var sut = new TransferDetectionService();

        var result = sut.DetectTransferTransactionIds([tx1, tx2]);

        result.Should().BeEmpty(because: $"type '{txType}' is not a debit or credit bucket");
    }

    // ── Cross-currency pairing ────────────────────────────────────────────────

    [Fact]
    public void DetectTransferTransactionIds_CrossCurrencyPairWithTransferCategory_Matched()
    {
        // Revolut EUR → Monobank UAH: amounts differ in native currency, the Monobank leg
        // carries a transfer category (MCC 4829), descriptions share no words.
        var eurAccount = Guid.NewGuid();
        var uahAccount = Guid.NewGuid();
        var date = new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc);

        var debit = MakeTx(eurAccount, 500m, "debit", date, description: "To UAH account");
        var credit = MakeTx(uahAccount, ConvertedAmount(500m, "EUR", "UAH"), "credit", date,
            description: "Від: Олекса Морган", category: CategoryKeys.TransferIn);

        var currencies = new Dictionary<Guid, string> { [eurAccount] = "EUR", [uahAccount] = "UAH" };

        var sut = new TransferDetectionService();

        var result = sut.DetectTransferTransactionIds([debit, credit], currencies);

        result.Should().BeEquivalentTo(new[] { debit.Id, credit.Id });
    }

    [Fact]
    public void DetectTransferTransactionIds_CrossCurrencyWithinFxTolerance_Matched()
    {
        // The receiving bank's own FX rate differs slightly from our rate table.
        var eurAccount = Guid.NewGuid();
        var uahAccount = Guid.NewGuid();
        var date = new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc);

        var mirrored = ConvertedAmount(500m, "EUR", "UAH");
        var debit = MakeTx(eurAccount, 500m, "debit", date, category: CategoryKeys.TransferOut);
        var credit = MakeTx(uahAccount, Math.Round(mirrored * 1.03m, 2), "credit", date,
            description: "Від: Олекса Морган");

        var currencies = new Dictionary<Guid, string> { [eurAccount] = "EUR", [uahAccount] = "UAH" };

        var sut = new TransferDetectionService();

        var result = sut.DetectTransferTransactionIds([debit, credit], currencies);

        result.Should().BeEquivalentTo(new[] { debit.Id, credit.Id });
    }

    [Fact]
    public void DetectTransferTransactionIds_CrossCurrencyOutsideFxTolerance_NotMatched()
    {
        var eurAccount = Guid.NewGuid();
        var uahAccount = Guid.NewGuid();
        var date = new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc);

        var mirrored = ConvertedAmount(500m, "EUR", "UAH");
        var debit = MakeTx(eurAccount, 500m, "debit", date, category: CategoryKeys.TransferOut);
        var credit = MakeTx(uahAccount, Math.Round(mirrored * 1.10m, 2), "credit", date,
            description: "Від: Олекса Морган");

        var currencies = new Dictionary<Guid, string> { [eurAccount] = "EUR", [uahAccount] = "UAH" };

        var sut = new TransferDetectionService();

        var result = sut.DetectTransferTransactionIds([debit, credit], currencies);

        result.Should().BeEmpty(because: "a 10% amount mismatch exceeds the FX tolerance");
    }

    [Fact]
    public void DetectTransferTransactionIds_CrossCurrencyWithoutAnySignal_NotMatched()
    {
        // Amounts align at current rates but nothing marks either leg as a transfer:
        // no transfer type, no transfer category, no shared description words.
        var eurAccount = Guid.NewGuid();
        var uahAccount = Guid.NewGuid();
        var date = new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc);

        var debit = MakeTx(eurAccount, 50m, "debit", date, description: "Grocery Store");
        var credit = MakeTx(uahAccount, ConvertedAmount(50m, "EUR", "UAH"), "credit", date,
            description: "Refund from marketplace");

        var currencies = new Dictionary<Guid, string> { [eurAccount] = "EUR", [uahAccount] = "UAH" };

        var sut = new TransferDetectionService();

        var result = sut.DetectTransferTransactionIds([debit, credit], currencies);

        result.Should().BeEmpty(because: "coincidentally aligned amounts alone must not pair");
    }

    [Fact]
    public void DetectTransferTransactionIds_UnknownCurrency_NotMatchedAcrossCurrencies()
    {
        // CurrencyConverter falls back to 1:1 for unknown currencies; matching on that
        // would compare unrelated magnitudes, so unknown-currency legs never pair.
        var eurAccount = Guid.NewGuid();
        var otherAccount = Guid.NewGuid();
        var date = new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc);

        var debit = MakeTx(eurAccount, 500m, "debit", date, category: CategoryKeys.TransferOut);
        var credit = MakeTx(otherAccount, 500m, "credit", date, category: CategoryKeys.TransferIn);

        var currencies = new Dictionary<Guid, string> { [eurAccount] = "EUR", [otherAccount] = "XYZ" };

        var sut = new TransferDetectionService();

        var result = sut.DetectTransferTransactionIds([debit, credit], currencies);

        result.Should().BeEmpty(because: "no trustworthy rate exists for 'XYZ'");
    }

    [Fact]
    public void DetectTransferTransactionIds_MissingCurrencyInfo_KeepsLegacySameAmountMatching()
    {
        // Accounts absent from the currency map fall back to exact-amount matching.
        var accountA = Guid.NewGuid();
        var accountB = Guid.NewGuid();
        var date = new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc);

        var debit = MakeTx(accountA, 500m, "debit", date);
        var credit = MakeTx(accountB, 500m, "credit", date);

        var sut = new TransferDetectionService();

        var result = sut.DetectTransferTransactionIds([debit, credit], new Dictionary<Guid, string>());

        result.Should().BeEquivalentTo(new[] { debit.Id, credit.Id });
    }

    [Fact]
    public void DetectTransferTransactionIds_SameCurrency_TransferCategoryAloneDoesNotMatch()
    {
        // The category shortcut is deliberately cross-currency-only: within one currency a
        // jar top-up (TRANSFER_OUT) must not consume an unrelated equal-amount credit.
        var accountA = Guid.NewGuid();
        var accountB = Guid.NewGuid();
        var date = new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc);

        var debit = MakeTx(accountA, 1000m, "debit", date,
            description: "Поповнення «Банка»", category: CategoryKeys.TransferOut);
        var credit = MakeTx(accountB, 1000m, "credit", date, description: "Дяка від друга");

        var currencies = new Dictionary<Guid, string> { [accountA] = "UAH", [accountB] = "UAH" };

        var sut = new TransferDetectionService();

        var result = sut.DetectTransferTransactionIds([debit, credit], currencies);

        result.Should().BeEmpty(because: "same-currency pairs still require a type or description signal");
    }
}
