namespace FinanceSentry.Modules.Companion.Tests;

using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Core.Utils;
using FinanceSentry.Modules.Companion.Application.Services;
using FinanceSentry.Modules.Companion.Domain;
using FinanceSentry.Modules.Companion.Domain.Repositories;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

/// <summary>
/// A thesis- or candidate-review notice reaches Ledger as a companion event of its own kind: the event carries the subject id
/// (as its reference id), the source and the evidence references, and its lane follows the source (filing, break and unbreak
/// by the mode; news, earnings and catalyst held for the digest).
/// </summary>
public sealed class ReviewDueCaptureTests
{
    private static readonly Guid User = Guid.Parse("77777777-7777-7777-7777-777777777777");
    private static readonly Guid SubjectId = Guid.Parse("88888888-8888-8888-8888-888888888888");

    private sealed class Settings(NotificationMode mode) : INotificationSettingRepository
    {
        public Task<CompanionNotificationSetting> GetOrDefaultAsync(Guid userId, CancellationToken ct = default)
            => Task.FromResult(new CompanionNotificationSetting { UserId = userId, Mode = mode });

        public Task<CompanionNotificationSetting> GetOrDefaultUnscopedAsync(Guid userId, CancellationToken ct = default)
            => GetOrDefaultAsync(userId, ct);

        public Task UpsertAsync(CompanionNotificationSetting setting, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class OneAlert(MaterialAlertRecord alert) : IMaterialAlertReader
    {
        public Task<IReadOnlyList<MaterialAlertRecord>> GetNewSinceAsync(DateTimeOffset watermark, int limit, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<MaterialAlertRecord>>([alert]);

        public Task<IReadOnlySet<Guid>> GetOpenIdsAsync(IReadOnlyCollection<Guid> alertIds, CancellationToken ct = default)
            => Task.FromResult<IReadOnlySet<Guid>>(alertIds.ToHashSet());
    }

    private sealed class NoAnalystActions : IAnalystActionFeedReader
    {
        public Task<IReadOnlyList<AnalystActionFeedRecord>> GetNewSinceAsync(DateTimeOffset watermark, int limit, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<AnalystActionFeedRecord>>([]);
    }

    private sealed class NoHoldings : IBrokerageHoldingsReader
    {
        public Task<IReadOnlyList<BrokerageHoldingSummary>> GetHoldingsAsync(Guid userId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<BrokerageHoldingSummary>>([]);
    }

    private sealed class NoBankingTotals : IBankingTotalsReader
    {
        public Task<IReadOnlyList<Guid>> GetActiveUserIdsAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Guid>>([]);

        public Task<decimal> GetTotalUsdAsync(Guid userId, CancellationToken ct = default) => Task.FromResult(0m);

        public Task<DateTime?> GetLatestSuccessfulSyncAsync(Guid userId, CancellationToken ct = default)
            => Task.FromResult<DateTime?>(null);
    }

    private sealed class NoAccounts : IBankingAccountsReader
    {
        public Task<IReadOnlyList<BankingAccountSummary>> GetAccountSummariesAsync(Guid userId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<BankingAccountSummary>>([]);

        public Task<IReadOnlyList<Guid>> GetAllActiveUserIdsAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Guid>>([User]);

        public Task<IReadOnlyList<AccountBalanceSnapshot>> GetActiveAccountSnapshotsAsync(Guid userId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<AccountBalanceSnapshot>>([]);
    }

    private static ReviewDue Review(ReviewSubjectKind kind, string source, params string[] evidence)
        => new(kind, SubjectId, "NVDA", source, "ref-1", evidence, $"NVDA: {source} happened");

    private static MaterialAlertRecord Alert(ReviewDue review, string? message = null)
        => new(
            Guid.NewGuid(), User, review.Subject == ReviewSubjectKind.Thesis ? "ThesisReviewDue" : "CandidateReviewDue",
            "Info", "Thesis review due: NVDA", Guid.NewGuid(), "NVDA", DateTimeOffset.UtcNow,
            AlertAppPath.ForSymbol("NVDA"), null, message ?? ReviewDueMessage.Compose(review));

    private static async Task<CompanionEvent> CaptureAsync(MaterialAlertRecord alert, NotificationMode mode)
    {
        await using var store = new InMemoryCompanionStore($"review-due-{Guid.NewGuid():N}", NoCurrentUser.Instance);
        var capture = new CompanionEventCapture(
            new OneAlert(alert), new NoAnalystActions(), new NoHoldings(), new NoBankingTotals(), new NoAccounts(),
            new Settings(mode), store, store, new MaterialityPolicy(), Options.Create(new CompanionOptions()),
            NullLogger<CompanionEventCapture>.Instance);

        (await capture.CaptureAsync()).Should().Be(1);
        return (await store.AllEventsAsync()).Single();
    }

    [Theory]
    [InlineData(ReviewSubjectKind.Thesis, CompanionEventKind.ThesisReviewDue)]
    [InlineData(ReviewSubjectKind.Candidate, CompanionEventKind.CandidateReviewDue)]
    public async Task The_event_has_its_own_kind_and_hands_Ledger_the_subject_source_and_evidence(
        ReviewSubjectKind subject, CompanionEventKind expectedKind)
    {
        var review = Review(subject, ReviewDueSource.Filing, "filing:0001-26-000123", "url:https://www.sec.gov/x");

        var evt = await CaptureAsync(Alert(review), NotificationMode.Realtime);

        evt.Kind.Should().Be(expectedKind);
        evt.Subject.Should().Be("NVDA");
        evt.ReferenceId.Should().Be(SubjectId, "Ledger resolves the thesis or candidate from it");
        evt.AppPath.Should().Be("/assets/NVDA");
        evt.Summary.Should().Contain($"subject={ReviewDueMessage.SubjectKey(subject)}:{SubjectId}")
            .And.Contain("source=filing").And.Contain("ref=ref-1")
            .And.Contain("filing:0001-26-000123");
        evt.Summary.Length.Should().BeLessThanOrEqualTo(500);
    }

    [Theory]
    [InlineData(ReviewDueSource.Filing, NotificationMode.Realtime, EventDisposition.Pending)]
    [InlineData(ReviewDueSource.Break, NotificationMode.Realtime, EventDisposition.Pending)]
    [InlineData(ReviewDueSource.Unbreak, NotificationMode.Scan, EventDisposition.Pending)]
    [InlineData(ReviewDueSource.Filing, NotificationMode.Quiet, EventDisposition.SuppressedByMode)]
    [InlineData(ReviewDueSource.News, NotificationMode.Realtime, EventDisposition.HeldForDigest)]
    [InlineData(ReviewDueSource.Earnings, NotificationMode.Realtime, EventDisposition.HeldForDigest)]
    [InlineData(ReviewDueSource.Catalyst, NotificationMode.Scan, EventDisposition.HeldForDigest)]
    [InlineData(ReviewDueSource.News, NotificationMode.Quiet, EventDisposition.SuppressedByMode)]
    public async Task The_lane_follows_the_source(string source, NotificationMode mode, EventDisposition expected)
    {
        var evt = await CaptureAsync(Alert(Review(ReviewSubjectKind.Thesis, source)), mode);

        evt.Disposition.Should().Be(expected);
    }

    [Fact]
    public async Task The_candidate_variant_follows_the_same_lanes()
    {
        (await CaptureAsync(Alert(Review(ReviewSubjectKind.Candidate, ReviewDueSource.Filing)), NotificationMode.Realtime))
            .Disposition.Should().Be(EventDisposition.Pending);
        (await CaptureAsync(Alert(Review(ReviewSubjectKind.Candidate, ReviewDueSource.News)), NotificationMode.Realtime))
            .Disposition.Should().Be(EventDisposition.HeldForDigest);
    }

    [Fact]
    public async Task A_notice_whose_text_carries_no_payload_is_held_for_the_digest_with_its_title()
    {
        var review = Review(ReviewSubjectKind.Thesis, ReviewDueSource.Filing);

        var evt = await CaptureAsync(Alert(review, message: "no machine tail here"), NotificationMode.Realtime);

        evt.Disposition.Should().Be(EventDisposition.HeldForDigest);
        evt.Summary.Should().Be("Thesis review due: NVDA");
        evt.Kind.Should().Be(CompanionEventKind.ThesisReviewDue);
    }
}
