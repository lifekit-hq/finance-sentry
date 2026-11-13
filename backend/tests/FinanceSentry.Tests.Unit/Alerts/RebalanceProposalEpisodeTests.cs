namespace FinanceSentry.Tests.Unit.Alerts;

using FinanceSentry.Modules.Alerts.Application.Services;
using FinanceSentry.Modules.Alerts.Domain;
using FinanceSentry.Modules.Alerts.Domain.Repositories;
using FinanceSentry.Core.Interfaces;
using FluentAssertions;
using Moq;
using Xunit;

/// <summary>
/// finance-sentry#1266: the daily rebalance job re-runs against a drift that persists for weeks. One drift
/// episode must wake the operator once; it speaks again only on a material change, a recurrence after the
/// drift cleared, or (for a proposal the operator acted on) when the weekly cadence has passed. Drives the real generator over a repository that
/// keeps the dedup-relevant row state, so "how many notifications" is the number of rows ever added (the
/// companion capture writes one wake per alert row).
/// </summary>
public class RebalanceProposalEpisodeTests
{
    private const string Summary = "Portfolio rebalance required: Sell Equities ≈ $15,000";

    private readonly InMemoryAlertRepository _repo = new();
    private readonly AlertGeneratorService _service;
    private readonly Guid _userId = Guid.NewGuid();

    public RebalanceProposalEpisodeTests()
    {
        var acks = new Mock<IPolicyAckReader>();
        _service = new AlertGeneratorService(_repo, acks.Object);
    }

    private int Notifications => _repo.Rows.Count(a => a.Type == AlertType.RebalanceProposal);

    private Task RunJobAsync(string signature = "Equities:Sell:1")
        => _service.GenerateRebalanceProposalAlertAsync(_userId, 1, Summary, signature);

    private Task ClearAsync() => _service.ResolveRebalanceProposalAlertAsync(_userId);

    [Fact]
    public async Task SameDriftOnConsecutiveRuns_NotifiesOnce()
    {
        await RunJobAsync();
        _repo.Age(TimeSpan.FromDays(1).Add(TimeSpan.FromMinutes(1)));
        await RunJobAsync();
        _repo.Age(TimeSpan.FromDays(1));
        await RunJobAsync();

        Notifications.Should().Be(1);
    }

    [Fact]
    public async Task AcceptedProposal_SameDriftNextDay_StaysQuiet()
    {
        await RunJobAsync();
        await _repo.AcknowledgeByReferenceAsync(
            _userId, AlertType.RebalanceProposal, _repo.Rows.Single().ReferenceId!.Value, "Accept");

        _repo.Age(TimeSpan.FromDays(1).Add(TimeSpan.FromMinutes(1)));
        await RunJobAsync();

        Notifications.Should().Be(1);
    }

    [Fact]
    public async Task DismissedProposal_SameDriftNextDay_StaysQuiet()
    {
        await RunJobAsync();
        await _repo.DismissAsync(_userId, _repo.Rows.Single().Id);

        _repo.Age(TimeSpan.FromDays(1).Add(TimeSpan.FromMinutes(1)));
        await RunJobAsync();

        Notifications.Should().Be(1);
    }

    [Fact]
    public async Task AcceptedProposal_SameDriftAfterTheWeeklyCadence_NotifiesAgain()
    {
        await RunJobAsync();
        await _repo.AcknowledgeByReferenceAsync(
            _userId, AlertType.RebalanceProposal, _repo.Rows.Single().ReferenceId!.Value, "Accept");

        _repo.Age(TimeSpan.FromDays(8));
        await RunJobAsync();

        Notifications.Should().Be(2);
    }

    [Fact]
    public async Task MaterialChangeWhileOpen_NotifiesAgainAndSupersedesTheOpenProposal()
    {
        await RunJobAsync("Equities:Sell:1");
        _repo.Age(TimeSpan.FromDays(1).Add(TimeSpan.FromMinutes(1)));
        await RunJobAsync("Equities:Sell:2");

        Notifications.Should().Be(2);
        _repo.Rows.Count(a => !a.IsResolved && !a.IsDismissed).Should().Be(1);
        _repo.Rows.Single(a => !a.IsResolved).ReferenceLabel.Should().Be("Equities:Sell:2");
    }

    [Fact]
    public async Task DriftThatClearsThenRecurs_IsANewEpisode()
    {
        await RunJobAsync();
        _repo.Age(TimeSpan.FromDays(1).Add(TimeSpan.FromMinutes(1)));
        await ClearAsync();
        _repo.Age(TimeSpan.FromDays(1));
        await RunJobAsync();

        Notifications.Should().Be(2);
    }

    [Fact]
    public async Task DriftThatClearsThenRecursWithinAnHour_IsStillANewEpisode()
    {
        await RunJobAsync();
        await ClearAsync();
        await RunJobAsync();

        Notifications.Should().Be(2);
    }

    private Task AcceptAsync()
        => _repo.AcknowledgeByReferenceAsync(
            _userId, AlertType.RebalanceProposal, _repo.Rows[0].ReferenceId!.Value, "Accept");

    private int OpenRows => _repo.Rows.Count(a => a.Type == AlertType.RebalanceProposal && !a.IsResolved && !a.IsDismissed);

    private static readonly TimeSpan OneDay = TimeSpan.FromDays(1).Add(TimeSpan.FromMinutes(1));

    /// <summary>
    /// Equities 9.9 pts, 10.1, 9.9 against a 5-point band is band multiple 1, 2, 1. After the first notification the
    /// crossing to 2 is a worsening and speaks once; the fall back to 1 is the same drift and stays quiet, however
    /// often it flips. Pre-fix the old signature's superseded row was ignored by the silence window, so it spoke
    /// on every flip.
    /// </summary>
    [Fact]
    public async Task DriftHoveringAtABandMultiple_NotifiesOnTheCrossingOnly()
    {
        await RunJobAsync("Equities:Sell:1");
        _repo.Age(OneDay);
        await RunJobAsync("Equities:Sell:2");
        _repo.Age(OneDay);
        await RunJobAsync("Equities:Sell:1");
        _repo.Age(OneDay);
        await RunJobAsync("Equities:Sell:2");
        _repo.Age(OneDay);
        await RunJobAsync("Equities:Sell:1");

        Notifications.Should().Be(2);
        OpenRows.Should().Be(1);
    }

    [Fact]
    public async Task DriftThatShrinksWhileOpen_StaysQuietAndKeepsTheOpenProposal()
    {
        await RunJobAsync("Equities:Sell:2");
        _repo.Age(OneDay);
        await RunJobAsync("Equities:Sell:1");

        Notifications.Should().Be(1);
        OpenRows.Should().Be(1);
    }

    [Fact]
    public async Task DriftOneBandFurtherOut_NotifiesAndLeavesOneOpenProposal()
    {
        await RunJobAsync("Equities:Sell:1");
        _repo.Age(OneDay);
        await RunJobAsync("Equities:Sell:2");

        Notifications.Should().Be(2);
        OpenRows.Should().Be(1);
    }

    [Fact]
    public async Task AcceptedProposal_ThenDriftClearsAndRecurs_NotifiesAtOnce()
    {
        await RunJobAsync();
        await AcceptAsync();
        _repo.Age(OneDay);
        await ClearAsync();
        _repo.Age(OneDay);
        await RunJobAsync();

        Notifications.Should().Be(2);
    }

    [Fact]
    public async Task AcceptedProposal_DailyClearRunsDoNotRewriteTheClearInstant()
    {
        await RunJobAsync();
        await AcceptAsync();
        _repo.Age(OneDay);
        await ClearAsync();
        _repo.Age(TimeSpan.FromDays(1));
        var clearedAt = _repo.Rows.Single().ResolvedAt;
        await ClearAsync();

        _repo.Rows.Single().ResolvedAt.Should().Be(clearedAt);
    }

    [Fact]
    public async Task DismissedProposal_ClearsAndRecursWithinAWeek_StaysQuiet()
    {
        await RunJobAsync();
        await _repo.DismissAsync(_userId, _repo.Rows.Single().Id);
        _repo.Age(OneDay);
        await ClearAsync();
        _repo.Age(TimeSpan.FromDays(2));
        await RunJobAsync();

        Notifications.Should().Be(1);
    }

    [Fact]
    public async Task DismissedProposal_ClearsAndRecursAfterAWeek_NotifiesAgain()
    {
        await RunJobAsync();
        await _repo.DismissAsync(_userId, _repo.Rows.Single().Id);
        _repo.Age(OneDay);
        await ClearAsync();
        _repo.Age(TimeSpan.FromDays(7));
        await RunJobAsync();

        Notifications.Should().Be(2);
    }

    [Fact]
    public async Task SleeveLeavingAndReturningAtTheSameBandMultiple_StaysQuiet()
    {
        await RunJobAsync("Bonds:Buy:1;Equities:Sell:1");
        _repo.Age(OneDay);
        await RunJobAsync("Equities:Sell:1");
        _repo.Age(OneDay);
        await RunJobAsync("Bonds:Buy:1;Equities:Sell:1");

        Notifications.Should().Be(1);
        OpenRows.Should().Be(1);
    }

    [Fact]
    public async Task SleeveNewToTheEpisode_Notifies()
    {
        await RunJobAsync("Equities:Sell:1");
        _repo.Age(OneDay);
        await RunJobAsync("Crypto:Review:0;Equities:Sell:1");

        Notifications.Should().Be(2);
        OpenRows.Should().Be(1);
    }

    [Fact]
    public async Task SleeveReturningFurtherOutThanBefore_Notifies()
    {
        await RunJobAsync("Bonds:Buy:1;Equities:Sell:1");
        _repo.Age(OneDay);
        await RunJobAsync("Equities:Sell:1");
        _repo.Age(OneDay);
        await RunJobAsync("Bonds:Buy:2;Equities:Sell:1");

        Notifications.Should().Be(2);
    }

    [Fact]
    public async Task UnplannedReviewLine_NotifiesOnceForTheEpisode()
    {
        await RunJobAsync("Crypto:Review:0");
        _repo.Age(OneDay);
        await RunJobAsync("Crypto:Review:0;Equities:Sell:1");
        _repo.Age(OneDay);
        await RunJobAsync("Equities:Sell:1");
        _repo.Age(OneDay);
        await RunJobAsync("Crypto:Review:0;Equities:Sell:1");

        Notifications.Should().Be(2);
    }

    [Fact]
    public async Task OpenProposalWearingThePreSignatureLabel_IsBumpedNotDuplicated()
    {
        await RunJobAsync("portfolio");
        _repo.Age(OneDay);
        await RunJobAsync("Equities:Sell:1");

        Notifications.Should().Be(1);
        _repo.Rows.Single().OccurrenceCount.Should().Be(2);
    }

    [Fact]
    public async Task WorseningThenClearThenRecurAtTheOldLevel_IsANewEpisode()
    {
        await RunJobAsync("Equities:Sell:1");
        _repo.Age(OneDay);
        await RunJobAsync("Equities:Sell:2");
        _repo.Age(OneDay);
        await ClearAsync();
        _repo.Age(OneDay);
        await RunJobAsync("Equities:Sell:1");

        Notifications.Should().Be(3);
    }

    /// <summary>The slice of <see cref="IAlertRepository"/> the dedup path touches, with the real filters' semantics.</summary>
    private sealed class InMemoryAlertRepository : IAlertRepository
    {
        public List<Alert> Rows { get; } = [];

        /// <summary>Moves every recorded instant into the past, as if <paramref name="by"/> had elapsed.</summary>
        public void Age(TimeSpan by)
        {
            foreach (var a in Rows)
            {
                a.CreatedAt -= by;
                a.LastOccurredAt -= by;
                a.ResolvedAt -= by;
                a.AcknowledgedAt -= by;
            }
        }

        public Task<Alert?> FindActiveAsync(Guid userId, string type, Guid? referenceId, CancellationToken ct = default)
            => Task.FromResult(Rows.FirstOrDefault(a => a.UserId == userId && a.Type == type
                && a.ReferenceId == referenceId && !a.IsResolved && !a.IsDismissed));

        public Task<bool> ExistsAsync(Guid userId, string type, Guid? referenceId, CancellationToken ct = default)
            => Task.FromResult(Rows.Any(a => a.UserId == userId && a.Type == type && a.ReferenceId == referenceId));

        public Task<bool> HasRecentAsync(
            Guid userId, string type, Guid? referenceId, string? referenceLabel, DateTimeOffset createdAfter,
            CancellationToken ct = default)
            => Task.FromResult(Rows.Any(a => a.UserId == userId && a.Type == type && a.ReferenceId == referenceId
                && a.ReferenceLabel == referenceLabel && a.CreatedAt >= createdAfter));

        public Task<IReadOnlyList<Alert>> GetByReferenceAsync(
            Guid userId, string type, Guid? referenceId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Alert>>(Rows
                .Where(a => a.UserId == userId && a.Type == type && a.ReferenceId == referenceId)
                .OrderByDescending(a => a.CreatedAt)
                .ToList());

        public Task ResolveAsync(Guid alertId, CancellationToken ct = default)
        {
            var alert = Rows.FirstOrDefault(a => a.Id == alertId);
            if (alert is null || alert.IsResolved) return Task.CompletedTask;
            alert.IsResolved = true;
            alert.ResolvedAt = DateTimeOffset.UtcNow;
            return Task.CompletedTask;
        }

        public Task ResolveAtAsync(Guid alertId, DateTimeOffset resolvedAt, CancellationToken ct = default)
        {
            var alert = Rows.FirstOrDefault(a => a.Id == alertId);
            if (alert is null || alert.IsResolved) return Task.CompletedTask;
            alert.IsResolved = true;
            alert.ResolvedAt = resolvedAt;
            return Task.CompletedTask;
        }

        public Task MarkAcceptedClearedAsync(Guid userId, string type, Guid? referenceId, CancellationToken ct = default)
        {
            foreach (var a in Rows.Where(a => a.UserId == userId && a.Type == type && a.ReferenceId == referenceId
                && a.AcknowledgementDecision == "Accept" && a.IsResolved && a.ResolvedAt <= a.AcknowledgedAt))
            {
                a.ResolvedAt = DateTimeOffset.UtcNow;
            }

            return Task.CompletedTask;
        }

        public Task BumpOccurrenceAsync(Guid alertId, CancellationToken ct = default)
        {
            var alert = Rows.First(a => a.Id == alertId);
            alert.OccurrenceCount++;
            alert.LastOccurredAt = DateTimeOffset.UtcNow;
            return Task.CompletedTask;
        }

        public Task AddAsync(Alert alert, CancellationToken ct = default)
        {
            Rows.Add(alert);
            return Task.CompletedTask;
        }

        public Task<bool> DismissAsync(Guid userId, Guid alertId, CancellationToken ct = default)
        {
            var alert = Rows.FirstOrDefault(a => a.Id == alertId && a.UserId == userId);
            if (alert is null) return Task.FromResult(false);
            alert.IsDismissed = true;
            return Task.FromResult(true);
        }

        public Task<bool> AcknowledgeByReferenceAsync(
            Guid userId, string alertType, Guid referenceId, string decision, CancellationToken ct = default)
        {
            var alert = Rows.FirstOrDefault(a => a.UserId == userId && a.Type == alertType
                && a.ReferenceId == referenceId && !a.IsResolved && !a.IsDismissed);
            if (alert is null) return Task.FromResult(false);
            alert.AcknowledgementDecision = decision;
            alert.AcknowledgedAt = DateTimeOffset.UtcNow;
            if (decision == "Accept")
            {
                alert.IsResolved = true;
                alert.ResolvedAt = alert.AcknowledgedAt;
            }

            return Task.FromResult(true);
        }

        public Task<(IReadOnlyList<Alert> Items, int TotalCount, int UnreadCount)> GetPagedAsync(
            Guid userId, string filter, int page, int pageSize, string? search = null, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<int> GetUnreadCountAsync(Guid userId, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<bool> MarkReadAsync(Guid userId, Guid alertId, CancellationToken ct = default) => throw new NotSupportedException();

        public Task MarkAllReadAsync(Guid userId, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<int> PurgeOldAsync(DateTimeOffset olderThan, CancellationToken ct = default) => throw new NotSupportedException();

        public Task DeleteByReferenceIdAsync(Guid referenceId, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<bool> AcknowledgeAsync(Guid userId, Guid alertId, string decision, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<(IReadOnlyList<Alert> Items, int TotalCount)> GetByTypesPagedAsync(
            Guid userId, IReadOnlyCollection<string> types, int page, int pageSize, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<Alert>> GetOpenAlertsByTypesAsync(
            IReadOnlyCollection<string> types, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<Alert>> GetOpenDeferredAlertsAsync(CancellationToken ct = default)
            => throw new NotSupportedException();
    }
}
