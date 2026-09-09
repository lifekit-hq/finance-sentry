namespace FinanceSentry.Tests.Unit.BankSync;

using FinanceSentry.Modules.BankSync.Application.Services;
using FluentAssertions;
using Xunit;

/// <summary>
/// Unit tests for TransactionDeduplicationService (T110).
///
/// Validates FR-006 (deduplication) and SC-005 (95%+ accuracy).
/// </summary>
public class TransactionDeduplicationTests
{
    // 32-byte key base64-encoded — for tests only
    private const string TestKey = "dGVzdC1vbmx5LWtleS1ub3QtdGhlLWxlYWtlZC1vbmU=";

    private readonly TransactionDeduplicationService _sut = new(TestKey);

    private static readonly Guid AccountId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid UserId = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001");

    // ── Hash determinism ────────────────────────────────────────────────────

    [Fact]
    public void ComputeHash_IsDeterministic_SameInputsSameHash()
    {
        var date = new DateTime(2026, 1, 15);
        var h1 = _sut.ComputeHash(AccountId, 99.99m, date, "Tesco Metro Dublin");
        var h2 = _sut.ComputeHash(AccountId, 99.99m, date, "Tesco Metro Dublin");

        h1.Should().Be(h2);
    }

    [Fact]
    public void ComputeHash_ProducesHex64Chars()
    {
        var hash = _sut.ComputeHash(AccountId, 10m, DateTime.Today, "test");
        hash.Should().HaveLength(64).And.MatchRegex("^[0-9a-f]{64}$");
    }

    [Fact]
    public void ComputeHash_DifferentAmounts_DifferentHashes()
    {
        var date = new DateTime(2026, 1, 1);
        var h1 = _sut.ComputeHash(AccountId, 10.00m, date, "same");
        var h2 = _sut.ComputeHash(AccountId, 10.01m, date, "same");

        h1.Should().NotBe(h2);
    }

    [Fact]
    public void ComputeHash_DifferentDates_DifferentHashes()
    {
        var h1 = _sut.ComputeHash(AccountId, 50m, new DateTime(2026, 1, 1), "same");
        var h2 = _sut.ComputeHash(AccountId, 50m, new DateTime(2026, 1, 2), "same");

        h1.Should().NotBe(h2);
    }

    [Fact]
    public void ComputeHash_DifferentDescriptions_DifferentHashes()
    {
        var date = new DateTime(2026, 3, 1);
        var h1 = _sut.ComputeHash(AccountId, 100m, date, "Lidl");
        var h2 = _sut.ComputeHash(AccountId, 100m, date, "Aldi");

        h1.Should().NotBe(h2);
    }

    [Fact]
    public void ComputeHash_CaseInsensitive_SameHash()
    {
        var date = new DateTime(2026, 2, 1);
        var h1 = _sut.ComputeHash(AccountId, 25m, date, "AMAZON PRIME");
        var h2 = _sut.ComputeHash(AccountId, 25m, date, "amazon prime");

        h1.Should().Be(h2, "hash must be case-insensitive for descriptions");
    }

    [Fact]
    public void ComputeHash_DescriptionTrimmed_SameHash()
    {
        var date = new DateTime(2026, 2, 1);
        var h1 = _sut.ComputeHash(AccountId, 25m, date, "  Revolut  ");
        var h2 = _sut.ComputeHash(AccountId, 25m, date, "Revolut");

        h1.Should().Be(h2, "hash must trim whitespace from descriptions");
    }

    // ── Pending vs Posted distinction ────────────────────────────────────────

    [Fact]
    public void FilterDuplicates_PendingAndPostedVersions_TreatedAsDifferent()
    {
        // Pending: uses TransactionDate (authorization date)
        var pending = MakeCandidate(AccountId, 50m, new DateTime(2026, 1, 10), "Netflix",
            isPending: true, transactionDate: new DateTime(2026, 1, 10), postedDate: null);

        // Posted: uses PostedDate
        var posted = MakeCandidate(AccountId, 50m, new DateTime(2026, 1, 10), "Netflix",
            isPending: false, transactionDate: new DateTime(2026, 1, 10), postedDate: new DateTime(2026, 1, 11));

        var pendingHash = _sut.ComputeHash(AccountId, 50m, pending.HashDate, "Netflix");
        var postedHash = _sut.ComputeHash(AccountId, 50m, posted.HashDate, "Netflix");

        pendingHash.Should().NotBe(postedHash,
            "pending (authorization date) and posted (settled date) must produce different hashes");
    }

    // ── FilterDuplicates bulk behaviour ─────────────────────────────────────

    [Fact]
    public void FilterDuplicates_EmptyIncoming_ReturnsEmpty()
    {
        var result = _sut.FilterDuplicates([], new HashSet<string>());
        result.Should().BeEmpty();
    }

    [Fact]
    public void FilterDuplicates_AllNew_ReturnsAll()
    {
        var incoming = MakeCandidates(50);
        var result = _sut.FilterDuplicates(incoming, new HashSet<string>());

        result.Should().HaveCount(50);
    }

    [Fact]
    public void FilterDuplicates_AllExisting_ReturnsNone()
    {
        var incoming = MakeCandidates(50);
        var existingHashes = incoming
            .Select(c => _sut.ComputeHash(c.AccountId, c.Amount, c.HashDate, c.Description))
            .ToHashSet();

        var result = _sut.FilterDuplicates(incoming, existingHashes);

        result.Should().BeEmpty();
    }

    /// <summary>SC-005: Deduplication must filter 95%+ of duplicates.</summary>
    [Fact]
    public void FilterDuplicates_100Transactions_50Duplicates_FiltersCorrectly_SC005()
    {
        // 50 unique transactions already in DB (hashes present)
        var uniqueInDb = MakeCandidates(50, startIndex: 0);
        var existingHashes = uniqueInDb
            .Select(c => _sut.ComputeHash(c.AccountId, c.Amount, c.HashDate, c.Description))
            .ToHashSet();

        // 50 new + 50 duplicates of the existing ones = 100 incoming
        var newOnes = MakeCandidates(50, startIndex: 50);
        var incoming = uniqueInDb.Concat(newOnes).ToList(); // 100 total

        var result = _sut.FilterDuplicates(incoming, existingHashes);

        result.Should().HaveCount(50, "exactly the 50 new transactions should pass through");

        // Verify accuracy: 50 duplicates filtered / 50 that should be filtered = 100% > 95%
        var duplicateCount = 100 - result.Count;
        var deduplicationAccuracy = (double)duplicateCount / 50 * 100;
        deduplicationAccuracy.Should().BeGreaterThanOrEqualTo(95,
            $"SC-005 requires 95%+ deduplication accuracy. Got {deduplicationAccuracy}%");
    }

    [Fact]
    public void FilterDuplicates_WithinBatchDuplicates_FilteredToo()
    {
        var candidate = MakeCandidate(AccountId, 100m, new DateTime(2026, 1, 1), "Duplicate in batch");
        var same = MakeCandidate(AccountId, 100m, new DateTime(2026, 1, 1), "Duplicate in batch");

        var result = _sut.FilterDuplicates([candidate, same], new HashSet<string>());

        result.Should().HaveCount(1, "within-batch duplicates should be filtered");
    }

    // ── ToEntity helper ─────────────────────────────────────────────────────

    [Fact]
    public void ToEntity_SetsUniqueHashCorrectly()
    {
        var candidate = MakeCandidate(AccountId, 77m, new DateTime(2026, 2, 14), "Valentine");
        var entity = _sut.ToEntity(candidate);

        var expectedHash = _sut.ComputeHash(candidate.AccountId, candidate.Amount,
            candidate.HashDate, candidate.Description);

        entity.UniqueHash.Should().Be(expectedHash);
        entity.Amount.Should().Be(77m);
        entity.Description.Should().Be("Valentine");
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static TransactionCandidate MakeCandidate(
        Guid accountId, decimal amount, DateTime date, string description,
        bool isPending = false, DateTime? transactionDate = null, DateTime? postedDate = null)
    {
        var txDate = transactionDate ?? date;
        return new TransactionCandidate(
            AccountId: accountId,
            UserId: UserId,
            Amount: amount,
            TransactionDate: txDate,
            PostedDate: postedDate ?? (isPending ? null : date),
            Description: description,
            IsPending: isPending,
            TransactionType: "debit",
            MerchantName: null,
            MerchantCategory: null);
    }

    private static List<TransactionCandidate> MakeCandidates(int count, int startIndex = 0)
    {
        return Enumerable.Range(startIndex, count)
            .Select(i => MakeCandidate(
                AccountId,
                amount: (i + 1) * 10.00m,
                date: new DateTime(2026, 1, 1).AddDays(i),
                description: $"Merchant_{i}"))
            .ToList();
    }
}
