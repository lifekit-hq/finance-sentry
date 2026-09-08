namespace FinanceSentry.Tests.Unit.BankSync.Application;

using FinanceSentry.Modules.BankSync.API.Responses;
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

    private static ICommittedMerchantPinRepository NewRepository() =>
        new CommittedMerchantPinRepository(new BankSyncDbContext(
            new DbContextOptionsBuilder<BankSyncDbContext>()
                .UseInMemoryDatabase($"pins-{Guid.NewGuid():N}").Options));

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
