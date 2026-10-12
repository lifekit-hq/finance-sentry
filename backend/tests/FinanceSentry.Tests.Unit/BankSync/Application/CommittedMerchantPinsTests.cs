namespace FinanceSentry.Tests.Unit.BankSync.Application;

using FinanceSentry.Tests.Unit.BankSync;
using FinanceSentry.Modules.BankSync.Application.Commands;
using FinanceSentry.Modules.BankSync.Application.Queries;
using FinanceSentry.Modules.BankSync.Application.Services;
using FinanceSentry.Modules.BankSync.Domain;
using FinanceSentry.Modules.BankSync.Domain.Exceptions;
using FinanceSentry.Modules.BankSync.Domain.Repositories;
using FinanceSentry.Modules.BankSync.Infrastructure.Persistence;
using FinanceSentry.Modules.BankSync.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

/// <summary>
/// Unpinning and listing committed merchants — rule (d) of the committed-outflow policy (spec 554). The
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

    private static string NewDatabase() => $"pins-{Guid.NewGuid():N}";

    private static ICommittedMerchantPinRepository NewRepository(string? database = null) =>
        new CommittedMerchantPinRepository(
            new BankSyncDbContext(Options(database ?? NewDatabase()), AmbientCurrentUser.Instance));

    // Seeds a pin straight into the in-memory store: the repository's add is raw INSERT ... ON CONFLICT SQL,
    // which this provider cannot run (CommittedMerchantPinUpsertTests covers it on Postgres).
    private static async Task Pin(string database, Guid userId, string merchant)
    {
        var displayName = merchant.Trim();
        await using var context = new BankSyncDbContext(Options(database), AmbientCurrentUser.Instance);
        context.CommittedMerchantPins.Add(new CommittedMerchantPin
        {
            UserId = userId,
            MerchantKey = CommittedMerchantKey.Derive(displayName),
            DisplayName = displayName,
        });
        await context.SaveChangesAsync();
    }

    private static Task<bool> Unpin(
        ICommittedMerchantPinRepository repository, Guid userId, string merchant) =>
        ActingAs(userId, () => new UnpinCommittedMerchantCommandHandler(repository)
            .Handle(new UnpinCommittedMerchantCommand(userId, merchant), CancellationToken.None));

    private static Task<IReadOnlyList<CommittedMerchantPinDto>> List(
        ICommittedMerchantPinRepository repository, Guid userId) =>
        ActingAs(userId, () => new ListCommittedMerchantPinsQueryHandler(repository)
            .Handle(new ListCommittedMerchantPinsQuery(userId), CancellationToken.None));

    // The request principal is the person the call is made for; the repository's Owner query filter scopes to it.
    private static Task<T> ActingAs<T>(Guid userId, Func<Task<T>> call)
    {
        AmbientCurrentUser.ActAs(userId);
        return call();
    }

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

    // ── Unpinning ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Unpin_MirrorsPin_AndAcceptsADifferentSpelling()
    {
        var database = NewDatabase();
        var repository = NewRepository(database);
        await Pin(database, UserId, "NETFLIX.COM");

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
        var database = NewDatabase();
        var repository = NewRepository(database);
        await Pin(database, UserId, "*MOBI TOP-UP 0857860057");
        var advertisedKey = (await List(repository, UserId)).Single().MerchantKey;

        advertisedKey.Should().Be("mobile top-up 0057");
        (await Unpin(repository, UserId, advertisedKey)).Should().BeTrue();
        (await repository.GetPinnedKeysAsync(UserId)).Should().BeEmpty();
    }

    // ── Listing ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task List_ReturnsTheUsersPinsOrderedByKey()
    {
        var database = NewDatabase();
        var repository = NewRepository(database);
        await Pin(database, UserId, "Netflix");
        await Pin(database, UserId, "Anytime Fitness");

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
        var database = NewDatabase();
        var repository = NewRepository(database);
        await Pin(database, UserId, "Anytime Fitness");

        (await repository.GetPinnedKeysAsync(OtherUserId)).Should().BeEmpty();
        (await Unpin(repository, OtherUserId, "Anytime Fitness")).Should().BeFalse();
        (await repository.GetPinnedKeysAsync(UserId)).Should().BeEquivalentTo(["anytime fitness"]);
    }
}
