namespace FinanceSentry.Tests.Unit.BankSync.Application;

using FinanceSentry.Modules.BankSync.Application.Commands;
using FinanceSentry.Modules.BankSync.Application.Queries;
using FinanceSentry.Modules.BankSync.Application.Services;
using FinanceSentry.Modules.BankSync.Domain.Exceptions;
using FinanceSentry.Modules.BankSync.Domain.Repositories;
using FinanceSentry.Modules.BankSync.Infrastructure.Persistence;
using FinanceSentry.Modules.BankSync.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

/// <summary>
/// Pinning a merchant as committed — rule (d) of the committed-outflow policy (spec 554). The
/// handlers run over the real repository against an in-memory context rather than a mock: what
/// these tests are actually about is the key a pin is stored under and the set the policy later
/// reads back, and a mocked repository would assert the intent instead of the round trip.
/// </summary>
public class CommittedMerchantPinsTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid OtherUserId = Guid.NewGuid();

    private static DbContextOptions<BankSyncDbContext> Options(string database) =>
        new DbContextOptionsBuilder<BankSyncDbContext>().UseInMemoryDatabase(database).Options;

    private static ICommittedMerchantPinRepository NewRepository(string? database = null) =>
        new CommittedMerchantPinRepository(
            new BankSyncDbContext(Options(database ?? $"pins-{Guid.NewGuid():N}")));

    private static Task<PinCommittedMerchantResult> Pin(
        ICommittedMerchantPinRepository repository, Guid userId, string merchant) =>
        new PinCommittedMerchantCommandHandler(repository)
            .Handle(new PinCommittedMerchantCommand(userId, merchant), CancellationToken.None);

    private static Task<bool> Unpin(
        ICommittedMerchantPinRepository repository, Guid userId, string merchant) =>
        new UnpinCommittedMerchantCommandHandler(repository)
            .Handle(new UnpinCommittedMerchantCommand(userId, merchant), CancellationToken.None);

    private static Task<IReadOnlyList<CommittedMerchantPinDto>> List(
        ICommittedMerchantPinRepository repository, Guid userId) =>
        new ListCommittedMerchantPinsQueryHandler(repository)
            .Handle(new ListCommittedMerchantPinsQuery(userId), CancellationToken.None);

    // ── The key a pin is stored under ────────────────────────────────────────

    [Theory]
    [InlineData("Anytime Fitness", "anytime fitness")]
    [InlineData("  Mario Scalas  ", "mario scalas")]
    [InlineData("NETFLIX.COM", "netflix")]
    public void Derive_UsesTheDetectorsOwnNormalization(string typed, string expectedKey)
    {
        // Same normalization as the recurrence detector, so a pin and a detected subscription
        // for one merchant cannot land on two different keys.
        CommittedMerchantKey.Derive(typed).Should().Be(expectedKey);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Derive_UnnameableMerchant_IsRejected(string? merchant)
    {
        // Everything unnameable normalizes to "unknown", which every unnamed debit also carries.
        // A pin on it would claim the whole unnamed tail of the book as committed.
        var derive = () => CommittedMerchantKey.Derive(merchant);

        derive.Should().Throw<UnpinnableMerchantException>()
              .Which.StatusCode.Should().Be(400);
    }

    // ── Pinning ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Pin_StoresTheNormalizedKeyAndKeepsWhatTheUserTyped()
    {
        var repository = NewRepository();

        var result = await Pin(repository, UserId, "  Mario Scalas  ");

        result.AlreadyPinned.Should().BeFalse();
        result.Pin.MerchantKey.Should().Be("mario scalas");
        result.Pin.DisplayName.Should().Be("Mario Scalas");

        var pinnedKeys = await repository.GetPinnedKeysAsync(UserId);
        pinnedKeys.Should().BeEquivalentTo(["mario scalas"]);
    }

    [Fact]
    public async Task Pin_TwoSpellingsOfOneMerchant_IsIdempotentAndStoresOneRow()
    {
        // "NETFLIX.COM" and "Netflix" normalize to the same key, so the second call asks for a
        // state that already holds — that is the same pin, not a conflict.
        var repository = NewRepository();
        await Pin(repository, UserId, "NETFLIX.COM");

        var second = await Pin(repository, UserId, "Netflix");

        second.AlreadyPinned.Should().BeTrue();
        second.Pin.DisplayName.Should().Be("NETFLIX.COM", because: "the first spelling is kept");
        (await List(repository, UserId)).Should().ContainSingle();
    }

    [Fact]
    public async Task Pin_UnnameableMerchant_IsRejectedBeforeAnythingIsStored()
    {
        var repository = NewRepository();

        var pin = async () => await Pin(repository, UserId, "   ");

        await pin.Should().ThrowAsync<UnpinnableMerchantException>();
        (await List(repository, UserId)).Should().BeEmpty();
    }

    [Fact]
    public async Task Pin_LosingTheRaceToAConcurrentPin_ReportsTheWinnersRow()
    {
        // Two tabs, or one double-clicked button: the second write loses the unique
        // (UserId, MerchantKey) index. The caller asked for a state that now holds, so this is
        // the documented idempotent result — not a 5xx.
        var database = $"pins-{Guid.NewGuid():N}";
        var winner = NewRepository(database);
        var loser = new CommittedMerchantPinRepository(
            new LosesItsFirstSave(Options(database), async () => await Pin(winner, UserId, "Anytime Fitness")));

        var result = await Pin(loser, UserId, "ANYTIME FITNESS");

        result.AlreadyPinned.Should().BeTrue();
        result.Pin.DisplayName.Should().Be("Anytime Fitness", because: "the winning row is the one that exists");
        (await List(winner, UserId)).Should().ContainSingle();
    }

    [Fact]
    public async Task Pin_WriteFailureThatIsNotALostRace_Surfaces()
    {
        // Nothing else wrote the row, so the failure is a real one and swallowing it would
        // report a pin the user does not hold.
        var repository = new CommittedMerchantPinRepository(
            new LosesItsFirstSave(Options($"pins-{Guid.NewGuid():N}"), competingWriter: null));

        var pin = async () => await Pin(repository, UserId, "Anytime Fitness");

        await pin.Should().ThrowAsync<DbUpdateException>();
    }

    /// <summary>
    /// A context whose first save loses a race: the competing writer commits its row, then this
    /// save fails the way the unique index fails it. Staged rather than run concurrently because
    /// the in-memory provider does not enforce unique indexes at all.
    /// </summary>
    private sealed class LosesItsFirstSave(
        DbContextOptions<BankSyncDbContext> options, Func<Task>? competingWriter)
        : BankSyncDbContext(options)
    {
        private bool _lost;

        public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (_lost)
                return await base.SaveChangesAsync(cancellationToken);

            _lost = true;
            if (competingWriter is not null)
                await competingWriter();

            throw new DbUpdateException("duplicate key value violates unique constraint");
        }
    }

    // ── Unpinning ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Unpin_MirrorsPin_AndAcceptsADifferentSpelling()
    {
        var repository = NewRepository();
        await Pin(repository, UserId, "NETFLIX.COM");

        (await Unpin(repository, UserId, "Netflix")).Should().BeTrue();
        (await repository.GetPinnedKeysAsync(UserId)).Should().BeEmpty();
    }

    [Fact]
    public async Task Unpin_MerchantThatWasNeverPinned_ReportsNothingRemoved()
    {
        // The caller turns this into a 404 rather than pretending the removal happened.
        (await Unpin(NewRepository(), UserId, "Anytime Fitness")).Should().BeFalse();
    }

    [Fact]
    public async Task Unpin_ByTheKeyTheListingAdvertised_RemovesThePin()
    {
        // List-then-unpin is the only flow an MCP caller has, so the advertised key has to
        // re-derive to itself. "*MOBI TOP-UP 0857860057" stores `mobile top-up 0057`, which used
        // to re-derive to `mobile top-up` and 404 on the pin the listing had just shown.
        var repository = NewRepository();
        await Pin(repository, UserId, "*MOBI TOP-UP 0857860057");
        var advertisedKey = (await List(repository, UserId)).Single().MerchantKey;

        advertisedKey.Should().Be("mobile top-up 0057");
        (await Unpin(repository, UserId, advertisedKey)).Should().BeTrue();
        (await repository.GetPinnedKeysAsync(UserId)).Should().BeEmpty();
    }

    // ── Listing ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task List_ReturnsTheUsersPinsOrderedByKey()
    {
        var repository = NewRepository();
        await Pin(repository, UserId, "Netflix");
        await Pin(repository, UserId, "Anytime Fitness");

        var pins = await List(repository, UserId);

        pins.Select(p => p.MerchantKey).Should().Equal("anytime fitness", "netflix");
        pins.Select(p => p.DisplayName).Should().Equal("Anytime Fitness", "Netflix");
        pins.Should().OnlyContain(p => p.Id != Guid.Empty && p.PinnedAt != default);
    }

    [Fact]
    public async Task Pins_AreScopedToTheirOwner()
    {
        // A pin is a statement about one person's obligations — it must never widen anyone
        // else's committed share, and one user must not be able to unpin another's.
        var repository = NewRepository();
        await Pin(repository, UserId, "Anytime Fitness");

        (await repository.GetPinnedKeysAsync(OtherUserId)).Should().BeEmpty();
        (await Unpin(repository, OtherUserId, "Anytime Fitness")).Should().BeFalse();
        (await repository.GetPinnedKeysAsync(UserId)).Should().BeEquivalentTo(["anytime fitness"]);
    }
}
