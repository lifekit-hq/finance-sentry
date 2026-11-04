namespace FinanceSentry.Tests.Unit.Subscriptions;

using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Subscriptions.Application.Services;
using FinanceSentry.Modules.Subscriptions.Domain;
using FinanceSentry.Modules.Subscriptions.Infrastructure.Persistence;
using FinanceSentry.Modules.Subscriptions.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

/// <summary>
/// The cross-module commitments reader over the real repository on the in-memory provider, read with no principal the
/// way the budget breach job reads it: which statuses count as active and which as ended, and whose rows. Synthetic rows only.
/// </summary>
public sealed class ActiveCommitmentsReaderTests : IAsyncDisposable
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid OtherUserId = Guid.NewGuid();

    private readonly SubscriptionsDbContext _db = new(
        new DbContextOptionsBuilder<SubscriptionsDbContext>().UseInMemoryDatabase($"commitments-{Guid.NewGuid():N}").Options,
        NoCurrentUser.Instance);

    private ActiveCommitmentsReader Reader() => new(new DetectedSubscriptionRepository(_db));

    private static DetectedSubscription Make(Guid owner, string key, Action<DetectedSubscription>? status = null)
    {
        var row = DetectedSubscription.Create(
            owner.ToString(), key, key, "monthly", 200m, 200m, "USD",
            new DateOnly(2027, 1, 1), new DateOnly(2027, 2, 1), occurrenceCount: 6, confidenceScore: 3,
            category: null, kind: SubscriptionKinds.Installment);
        status?.Invoke(row);
        return row;
    }

    private async Task SeedAsync(params DetectedSubscription[] rows)
    {
        _db.DetectedSubscriptions.AddRange(rows);
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task EndedCommitments_AreTheCompletedAndLapsedRows_OfTheNamedUserOnly()
    {
        await SeedAsync(
            Make(UserId, "active"),
            Make(UserId, "paid-off", r => r.MarkCompleted()),
            Make(UserId, "lapsed", r => r.MarkPotentiallyCancelled()),
            Make(UserId, "dismissed", r => r.MarkDismissed()),
            Make(OtherUserId, "someone-elses", r => r.MarkCompleted()));

        var ended = await Reader().GetEndedCommitmentsAsync(UserId);

        ended.Select(c => c.Key).Should().BeEquivalentTo("paid-off", "lapsed");
    }

    [Fact]
    public async Task ActiveCommitments_DoNotIncludeEndedRows()
    {
        await SeedAsync(
            Make(UserId, "active"),
            Make(UserId, "paid-off", r => r.MarkCompleted()),
            Make(UserId, "lapsed", r => r.MarkPotentiallyCancelled()));

        var active = await Reader().GetActiveCommitmentsAsync(UserId);

        active.Select(c => c.Key).Should().BeEquivalentTo("active");
    }

    [Fact]
    public async Task AnEndedCommitment_CarriesItsAmountCurrencyAndEndDate()
    {
        var row = Make(UserId, "paid-off", r =>
        {
            r.SetTerm(6, new DateOnly(2027, 1, 1), new DateOnly(2026, 8, 1));
            r.MarkCompleted();
        });
        await SeedAsync(row);

        var ended = Assert.Single(await Reader().GetEndedCommitmentsAsync(UserId));

        ended.Amount.Should().Be(200m);
        ended.Currency.Should().Be("USD");
        ended.Kind.Should().Be(SubscriptionKinds.Installment);
        ended.EndDate.Should().Be(new DateOnly(2027, 1, 1));
    }

    public async ValueTask DisposeAsync() => await _db.DisposeAsync();
}
