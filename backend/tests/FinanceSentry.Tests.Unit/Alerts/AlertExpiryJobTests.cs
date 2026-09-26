namespace FinanceSentry.Tests.Unit.Alerts;

using FinanceSentry.Modules.Alerts.Domain;
using FinanceSentry.Modules.Alerts.Domain.Repositories;
using FinanceSentry.Modules.Alerts.Infrastructure.Jobs;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

/// <summary>
/// Unit tests for <see cref="AlertExpiryJob"/> (finance-sentry#419 S4) — one case per TTL boundary
/// (just inside, just past), explicit never-expire cases for the Error-severity types and the
/// freshness flavour of <see cref="AlertType.MarketStructure"/>, and an already-resolved alert left
/// untouched.
/// </summary>
public class AlertExpiryJobTests
{
    private readonly Mock<IAlertRepository> _repo = new();

    private AlertExpiryJob MakeJob()
        => MakeJob(new FixedClock(DateTimeOffset.UtcNow));

    private AlertExpiryJob MakeJob(TimeProvider clock)
        => new(_repo.Object, clock, Mock.Of<ILogger<AlertExpiryJob>>());

    private static Alert MakeAlert(string type, DateTimeOffset createdAt, string severity = AlertSeverity.Warning, string? referenceLabel = null)
        => new()
        {
            Id = Guid.NewGuid(),
            Type = type,
            Severity = severity,
            ReferenceLabel = referenceLabel,
            CreatedAt = createdAt,
            IsResolved = false,
            IsDismissed = false,
        };

    private void SetCandidates(params Alert[] alerts)
        => _repo.Setup(r => r.GetOpenAlertsByTypesAsync(It.IsAny<IReadOnlyCollection<string>>(), default))
            .ReturnsAsync(alerts);

    private void SetDeferredCandidates(params Alert[] alerts)
        => _repo.Setup(r => r.GetOpenDeferredAlertsAsync(default))
            .ReturnsAsync(alerts);

    [Theory]
    [InlineData(AlertType.NewsCluster, 3)]
    [InlineData(AlertType.FilingLanded, 14)]
    [InlineData(AlertType.FxSpread, 30)]
    [InlineData(AlertType.DuplicateCharge, 30)]
    [InlineData(AlertType.PriceHike, 60)]
    [InlineData(AlertType.PerformanceBrief, 14)]
    [InlineData(AlertType.EarningsAhead, 2)]
    public async Task JustInsideTtl_LeavesAlertOpen(string type, int ttlDays)
    {
        var alert = MakeAlert(type, DateTimeOffset.UtcNow.AddDays(-ttlDays).AddMinutes(1));
        SetCandidates(alert);

        await MakeJob().ExecuteAsync();

        _repo.Verify(r => r.ResolveAsync(It.IsAny<Guid>(), default), Times.Never);
    }

    [Theory]
    [InlineData(AlertType.NewsCluster, 3)]
    [InlineData(AlertType.FilingLanded, 14)]
    [InlineData(AlertType.FxSpread, 30)]
    [InlineData(AlertType.DuplicateCharge, 30)]
    [InlineData(AlertType.PriceHike, 60)]
    [InlineData(AlertType.PerformanceBrief, 14)]
    [InlineData(AlertType.EarningsAhead, 2)]
    public async Task JustPastTtl_ResolvesAlert(string type, int ttlDays)
    {
        var alert = MakeAlert(type, DateTimeOffset.UtcNow.AddDays(-ttlDays).AddMinutes(-1));
        SetCandidates(alert);

        await MakeJob().ExecuteAsync();

        _repo.Verify(r => r.ResolveAsync(alert.Id, default), Times.Once);
    }

    [Fact]
    public async Task MarketStructureTickerMove_JustInsideTtl_LeavesAlertOpen()
    {
        var alert = MakeAlert(
            AlertType.MarketStructure, DateTimeOffset.UtcNow.AddDays(-7).AddMinutes(1),
            AlertSeverity.Warning, referenceLabel: "AAPL");
        SetCandidates(alert);

        await MakeJob().ExecuteAsync();

        _repo.Verify(r => r.ResolveAsync(It.IsAny<Guid>(), default), Times.Never);
    }

    [Fact]
    public async Task MarketStructureTickerMove_JustPastTtl_ResolvesAlert()
    {
        var alert = MakeAlert(
            AlertType.MarketStructure, DateTimeOffset.UtcNow.AddDays(-7).AddMinutes(-1),
            AlertSeverity.Warning, referenceLabel: "AAPL");
        SetCandidates(alert);

        await MakeJob().ExecuteAsync();

        _repo.Verify(r => r.ResolveAsync(alert.Id, default), Times.Once);
    }

    [Fact]
    public async Task BudgetBreach_JustInsideEndOfFollowingMonth_LeavesAlertOpen()
    {
        var createdAt = new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero);
        var alert = MakeAlert(AlertType.BudgetBreach, createdAt);
        SetCandidates(alert);

        var clock = new FixedClock(new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero).AddMinutes(-1));
        await MakeJob(clock).ExecuteAsync();

        _repo.Verify(r => r.ResolveAsync(It.IsAny<Guid>(), default), Times.Never);
    }

    [Fact]
    public async Task BudgetBreach_JustPastEndOfFollowingMonth_ResolvesAlert()
    {
        var createdAt = new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero);
        var alert = MakeAlert(AlertType.BudgetBreach, createdAt);
        SetCandidates(alert);

        var clock = new FixedClock(new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero).AddMinutes(1));
        await MakeJob(clock).ExecuteAsync();

        _repo.Verify(r => r.ResolveAsync(alert.Id, default), Times.Once);
    }

    [Theory]
    [InlineData(AlertType.SyncFailure)]
    [InlineData(AlertType.JobFailure)]
    public async Task ErrorSeverityType_NeverExpires(string type)
    {
        var alert = MakeAlert(type, DateTimeOffset.UtcNow.AddYears(-5), AlertSeverity.Error);
        SetCandidates(alert);

        await MakeJob().ExecuteAsync();

        _repo.Verify(r => r.ResolveAsync(It.IsAny<Guid>(), default), Times.Never);
    }

    [Fact]
    public async Task MarketStructureFreshness_NeverExpires()
    {
        var alert = MakeAlert(
            AlertType.MarketStructure, DateTimeOffset.UtcNow.AddYears(-5),
            AlertSeverity.Error, referenceLabel: "freshness");
        SetCandidates(alert);

        await MakeJob().ExecuteAsync();

        _repo.Verify(r => r.ResolveAsync(It.IsAny<Guid>(), default), Times.Never);
    }

    [Theory]
    [InlineData(AlertType.CashShortfall)]
    [InlineData(AlertType.LowBalance)]
    [InlineData(AlertType.ThesisBroken)]
    [InlineData(AlertType.PolicyViolation)]
    public async Task NoResolvePathTypes_AreNotEvenQueried(string type)
    {
        IReadOnlyCollection<string>? queriedTypes = null;
        _repo.Setup(r => r.GetOpenAlertsByTypesAsync(It.IsAny<IReadOnlyCollection<string>>(), default))
            .Callback((IReadOnlyCollection<string> types, CancellationToken _) => queriedTypes = types)
            .ReturnsAsync([]);

        await MakeJob().ExecuteAsync();

        Assert.DoesNotContain(type, queriedTypes!);
    }

    [Fact]
    public async Task Deferred_JustInsideSevenDays_LeavesAlertOpen()
    {
        var alert = MakeAlert(AlertType.LowBalance, DateTimeOffset.UtcNow);
        alert.AcknowledgementDecision = "Defer";
        alert.AcknowledgedAt = DateTimeOffset.UtcNow.AddDays(-7).AddMinutes(1);
        SetDeferredCandidates(alert);

        await MakeJob().ExecuteAsync();

        _repo.Verify(r => r.ResolveAsync(It.IsAny<Guid>(), default), Times.Never);
    }

    [Fact]
    public async Task Deferred_JustPastSevenDays_ResolvesAlertRegardlessOfType()
    {
        // LowBalance has no per-type TTL and normally never expires by age — the Defer path
        // overrides that and expires it 7 days after the user's decision (#419 S5).
        var alert = MakeAlert(AlertType.LowBalance, DateTimeOffset.UtcNow);
        alert.AcknowledgementDecision = "Defer";
        alert.AcknowledgedAt = DateTimeOffset.UtcNow.AddDays(-7).AddMinutes(-1);
        SetDeferredCandidates(alert);

        await MakeJob().ExecuteAsync();

        _repo.Verify(r => r.ResolveAsync(alert.Id, default), Times.Once);
    }

    [Fact]
    public async Task Deferred_AlsoReturnedByTypeQuery_IsResolvedOnce()
    {
        var alert = MakeAlert(AlertType.PriceHike, DateTimeOffset.UtcNow.AddDays(-1));
        alert.AcknowledgementDecision = "Defer";
        alert.AcknowledgedAt = DateTimeOffset.UtcNow.AddDays(-7).AddMinutes(-1);
        SetCandidates(alert);
        SetDeferredCandidates(alert);

        await MakeJob().ExecuteAsync();

        _repo.Verify(r => r.ResolveAsync(alert.Id, default), Times.Once);
    }

    [Fact]
    public async Task AlreadyResolvedAlert_ReturnedByRepositoryAnyway_IsLeftUntouched()
    {
        // The repository query already excludes resolved rows; this guards the job's own logic in
        // case a resolved row ever reaches it (e.g. a race with a concurrent resolve-on-clear call).
        var alert = MakeAlert(AlertType.NewsCluster, DateTimeOffset.UtcNow.AddDays(-30));
        alert.IsResolved = true;
        SetCandidates(alert);

        await MakeJob().ExecuteAsync();

        _repo.Verify(r => r.ResolveAsync(It.IsAny<Guid>(), default), Times.Never);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
